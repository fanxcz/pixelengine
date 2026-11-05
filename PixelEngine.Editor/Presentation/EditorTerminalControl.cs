using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Integrated terminal panel with two real shell sessions (Command Prompt and PowerShell).
/// Commands run inside a persistent child process, stdout and stderr are streamed line by line,
/// ANSI/VT sequences are interpreted, the transcript is virtualized to stay responsive under
/// large builds, and input history plus quick actions are always available without blocking
/// the UI thread.
/// </summary>
internal sealed class EditorTerminalControl : UserControl
{
    private sealed record TerminalLine(string Text, bool IsError, bool Repeats)
    {
        public int RepeatCount { get; set; } = 1;
    }

    private int _maximumLines = 6000;
    private int _trimToLines = 4500;

    private readonly RichTextBox _output = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Terminal,
        ForeColor = Theme.Text,
        DetectUrls = false,
        WordWrap = false,
        HideSelection = false,
        ScrollBars = RichTextBoxScrollBars.Both
    };
    private readonly Panel _inputHost = new() { Dock = DockStyle.Bottom, BackColor = Theme.Toolbar, Padding = new Padding(Theme.Px(8), Theme.Px(6), Theme.Px(8), Theme.Px(6)) };
    private readonly Label _promptLabel;
    private readonly TextBox _command;
    private readonly StudioButton _run = new("Run") { Width = Theme.Px(64), Dock = DockStyle.Right, Kind = StudioButtonKind.Primary };
    private readonly StudioButton _stop = new("Stop") { Width = Theme.Px(64), Dock = DockStyle.Right, Kind = StudioButtonKind.Danger, Enabled = false };
    private readonly StudioButton _clear = new("Clear") { Width = Theme.Px(64), Dock = DockStyle.Right, Kind = StudioButtonKind.Ghost };
    private readonly ComboBox _shellSelector = new() { Dock = DockStyle.Left, Width = Theme.Px(150), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
    private readonly Label _workingDirectory = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Theme.Muted,
        Font = Theme.Mono(Theme.FontSmall)
    };
    private readonly Label _shellState = new()
    {
        Dock = DockStyle.Right,
        AutoSize = false,
        Width = Theme.Px(120),
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Muted,
        Font = Theme.Body(Theme.FontCaption)
    };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Bottom,
        Height = Theme.Px(20),
        ForeColor = Theme.Dim,
        Font = Theme.Body(Theme.FontCaption),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly List<(string Text, bool IsError)> _pendingOutput = new();
    private readonly object _pendingOutputLock = new();
    private readonly List<TerminalLine> _lines = new();
    private readonly List<string> _history = new();
    private readonly Dictionary<TerminalShell, TerminalSession> _sessions = new();
    private readonly System.Windows.Forms.Timer _flushTimer = new() { Interval = 40 };
    private TerminalShell _activeShell = TerminalShell.PowerShell;
    private int _historyIndex;
    private bool _disposed;
    private bool _suppressShellEvents;
    private TerminalSession? _runningSession;

    public Func<string>? WorkingDirectoryProvider { get; set; }

    public event Action<string>? CommandStarted;
    public event Action<string, bool>? OutputLineReceived;
    public event Action<string, int>? CommandCompleted;

    public EditorTerminalControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Terminal;
        _promptLabel = new Label
        {
            Text = "PS",
            Dock = DockStyle.Left,
            Width = Theme.Px(26),
            ForeColor = Theme.AccentBright,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Mono(Theme.FontControl, FontStyle.Bold)
        };
        _command = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Viewport,
            ForeColor = Theme.Text,
            Font = Theme.Mono(Theme.FontControl),
            PlaceholderText = "Enter a command — Up/Down for history, Ctrl+C to interrupt, Ctrl+L to clear"
        };

        var header = new Panel { Dock = DockStyle.Top, Height = Theme.Px(34), BackColor = Theme.Toolbar, Padding = new Padding(Theme.Px(8), Theme.Px(3), Theme.Px(8), Theme.Px(3)) };
        var title = new Label
        {
            Dock = DockStyle.Left,
            Width = Theme.Px(84),
            Text = "TERMINAL",
            ForeColor = Theme.TextStrong,
            Font = Theme.Strong(Theme.FontCaption),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _shellSelector.Items.AddRange(["PowerShell", "Command Prompt"]);
        _shellSelector.SelectedIndex = 0;
        _shellSelector.Margin = new Padding(Theme.Px(4), 0, Theme.Px(4), 0);
        var quickTasks = new StudioButton("Tasks") { Dock = DockStyle.Right, Width = Theme.Px(74), Kind = StudioButtonKind.Ghost };
        header.Controls.Add(_workingDirectory);
        header.Controls.Add(_shellState);
        header.Controls.Add(_shellSelector);
        header.Controls.Add(quickTasks);
        header.Controls.Add(title);

        var prompt = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Toolbar };
        prompt.Controls.Add(_command);
        prompt.Controls.Add(_promptLabel);
        _inputHost.Controls.Add(prompt);
        _inputHost.Controls.Add(_run);
        _inputHost.Controls.Add(_stop);
        _inputHost.Controls.Add(_clear);
        _inputHost.Height = Theme.Px(42);

        Controls.Add(_output);
        Controls.Add(_inputHost);
        Controls.Add(header);
        Controls.Add(_hint);

        _run.Click += (_, _) => Execute(_command.Text);
        _stop.Click += (_, _) => StopCommand();
        _clear.Click += (_, _) => ClearOutput();
        _shellSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressShellEvents) return;
            SwitchShell(_shellSelector.SelectedIndex == 1 ? TerminalShell.Cmd : TerminalShell.PowerShell);
        };
        quickTasks.Click += (_, _) => ShowQuickTasks(quickTasks);
        _command.KeyDown += OnCommandKeyDown;
        _flushTimer.Tick += (_, _) => FlushPendingOutput();

        AppendInfo("PixelEngine integrated terminal — real PowerShell / Command Prompt sessions.");
        AppendInfo("Commands run in a child shell; the editor stays responsive. Use ▲/▼ for history.");
        UpdateState();
    }

    private TerminalSession ActiveSession => _sessions.TryGetValue(_activeShell, out var session) ? session : null!;

    /// <summary>Applies persisted preferences (font size, scrollback, default shell).</summary>
    public void ApplyPreferences(float fontSize, int scrollback, TerminalShell defaultShell)
    {
        var size = float.IsFinite(fontSize) ? Math.Clamp(fontSize, 7f, 24f) : 9.5f;
        _output.Font = Theme.Mono(size);
        _command.Font = Theme.Mono(size);
        _maximumLines = Math.Clamp(scrollback, 500, 50_000);
        _trimToLines = Math.Max(250, _maximumLines * 3 / 4);
        TrimTranscript();
        ScrollToEnd();
        if (_activeShell != defaultShell)
        {
            _activeShell = defaultShell;
            // SelectIndexChanged only starts a session when one already exists; on a cold start the
            // default shell must simply become the selected one without launching a process yet.
            _suppressShellEvents = true;
            try { _shellSelector.SelectedIndex = defaultShell == TerminalShell.Cmd ? 1 : 0; }
            finally { _suppressShellEvents = false; }
            _promptLabel.Text = defaultShell == TerminalShell.PowerShell ? "PS" : ">";
            UpdateState();
        }
    }

    public void FocusCommandInput()
    {
        _command.Focus();
        _command.SelectionStart = _command.TextLength;
    }

    /// <summary>Switches the visible shell, starting it on first use.</summary>
    public void SwitchShell(TerminalShell shell)
    {
        if (_disposed) return;
        _activeShell = shell;
        _shellSelector.SelectedIndex = shell == TerminalShell.Cmd ? 1 : 0;
        _promptLabel.Text = shell == TerminalShell.PowerShell ? "PS" : ">";
        EnsureSession();
        UpdateState();
        FocusCommandInput();
    }

    private void EnsureSession()
    {
        if (_sessions.TryGetValue(_activeShell, out var existing) && existing.IsRunning) return;
        var session = new TerminalSession(_activeShell);
        session.OutputLine += OnSessionOutput;
        session.CommandCompleted += (command, exitCode) =>
        {
            // Captured here, on the reader thread, before any other command can start.
            var duration = session.LastCommandDuration;
            if (_runningSession == session) _runningSession = null;
            PostToUi(() =>
            {
                var elapsed = FormatDuration(duration);
                AppendLine(exitCode == 0
                    ? $"✓ Finished in {elapsed} — exit code 0."
                    : $"✗ Command failed — exit code {exitCode} ({elapsed}).", isError: exitCode != 0);
                UpdateState();
                CommandCompleted?.Invoke(command, exitCode);
            });
        };
        session.Exited += _ => PostToUi(() => { UpdateState(); SetStatus("session exited"); });
        try
        {
            session.Start(GetWorkingDirectory());
            _sessions[_activeShell] = session;
            AppendInfo($"{session.ShellName} session started in {session.WorkingDirectory}.");
        }
        catch (TerminalSessionException ex)
        {
            session.Dispose();
            AppendLine(ex.Message, isError: true);
            AppendLine("Tip: pick the other shell from the selector, or run the editor on Windows.", isError: false);
        }
        _runningSession = null;
        UpdateState();
    }

    /// <summary>Formats a measured command duration; never invents a timing when none was measured.</summary>
    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null) return "duration unknown";
        var value = duration.Value;
        return value.TotalSeconds >= 1
            ? $"{value.TotalSeconds:0.0}s"
            : $"{Math.Max(1, value.TotalMilliseconds):0} ms";
    }

    /// <summary>Runs a command line in the active shell. Safe to call from the UI thread or event handlers.</summary>
    public void Execute(string? command)
    {
        command = command?.Trim();
        if (string.IsNullOrEmpty(command) || _disposed) return;
        EnsureSession();
        var session = ActiveSession;
        if (session is null || !session.IsRunning)
        {
            AppendLine("No running shell session. Use the shell selector to start one.", isError: true);
            return;
        }

        _command.Text = string.Empty;
        AddHistory(command);
        AppendPromptLine(session, command);
        try
        {
            session.SendCommand(command);
            _runningSession = session;
            CommandStarted?.Invoke(command);
            UpdateState();
        }
        catch (TerminalSessionException ex)
        {
            AppendLine("Command launch failed: " + ex.Message, isError: true);
            _runningSession = null;
            UpdateState();
        }
    }

    /// <summary>Sends interactive input to a program that is already running (for example a Y/n prompt).</summary>
    public void SendInteractiveInput(string text)
    {
        var session = ActiveSession;
        if (session is null || !session.IsRunning) return;
        AppendLine(text, isError: false);
        session.SendRawInput(text + "\r\n");
    }

    public void StopCommand()
    {
        var session = _runningSession ?? ActiveSession;
        if (session is null) return;
        if (!session.IsRunning)
        {
            UpdateState();
            return;
        }
        AppendInfo("Stopping shell process tree…");
        session.Kill();
        _runningSession = null;
        // The session object is disposed by Kill's caller contract; recreate lazily.
        if (_sessions.TryGetValue(_activeShell, out var current) && ReferenceEquals(current, session))
            _sessions.Remove(_activeShell);
        session.Dispose();
        UpdateState();
        SetStatus("Terminal session stopped");
    }

    public void ClearOutput()
    {
        if (_disposed) return;
        _output.Clear();
        _lines.Clear();
        AppendInfo("Terminal cleared.");
        UpdateState();
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter when e.Modifiers == Keys.None:
                e.SuppressKeyPress = true;
                Execute(_command.Text);
                break;
            case Keys.Up:
                e.SuppressKeyPress = true;
                MoveHistory(-1);
                break;
            case Keys.Down:
                e.SuppressKeyPress = true;
                MoveHistory(1);
                break;
            case Keys.C when e.Control:
                e.SuppressKeyPress = true;
                if (_output.SelectionLength > 0) _output.Copy();
                else StopCommand();
                break;
            case Keys.L when e.Control:
                e.SuppressKeyPress = true;
                ClearOutput();
                break;
            case Keys.Escape:
                e.SuppressKeyPress = true;
                _command.Clear();
                break;
            case Keys.Tab:
                e.SuppressKeyPress = true;
                _command.Text += "    ";
                _command.SelectionStart = _command.TextLength;
                break;
        }
    }

    private void AddHistory(string command)
    {
        if (_history.Count == 0 || !string.Equals(_history[^1], command, StringComparison.Ordinal)) _history.Add(command);
        if (_history.Count > 200) _history.RemoveAt(0);
        _historyIndex = _history.Count;
    }

    private void MoveHistory(int offset)
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + offset, 0, _history.Count);
        _command.Text = _historyIndex >= _history.Count ? string.Empty : _history[_historyIndex];
        _command.SelectionStart = _command.TextLength;
    }

    private void OnSessionOutput(string line, bool isError)
    {
        if (_disposed) return;
        lock (_pendingOutputLock) _pendingOutput.Add((line, isError));
        // This runs on a shell reader thread: the WinForms timer may only be touched on the UI
        // thread, so scheduling the flush is marshalled like any other control access.
        PostToUi(() =>
        {
            if (_flushTimer.Enabled) return;
            _flushTimer.Start();
        });
    }

    /// <summary>Batches shell output so thousands of build lines do not repaint per line.</summary>
    private void FlushPendingOutput()
    {
        _flushTimer.Stop();
        if (_disposed || !IsHandleCreated) return;
        (string Text, bool IsError)[] batch;
        lock (_pendingOutputLock)
        {
            if (_pendingOutput.Count == 0) return;
            batch = _pendingOutput.ToArray();
            _pendingOutput.Clear();
        }
        var atBottom = IsScrolledToBottom();
        _output.SuspendLayout();
        try
        {
            foreach (var (text, isError) in batch) AppendLineCore(text, isError, LineStyle.Plain);
            if (_lines.Count > _maximumLines) TrimTranscript();
            if (atBottom) ScrollToEnd();
        }
        finally
        {
            _output.ResumeLayout();
        }
        UpdateState();
    }

    /// <summary>
    /// Drops the oldest transcript lines in place. Rebuilding the document on every overflow
    /// would stall the UI during long builds; removing a leading block once keeps the cost
    /// amortized and preserves the RichTextBox formatting of the surviving lines.
    /// </summary>
    private void TrimTranscript()
    {
        var remove = _lines.Count - _trimToLines;
        if (remove <= 0) return;
        var builder = new StringBuilder();
        for (var i = 0; i < remove; i++)
        {
            builder.Append(_lines[i].Text);
            builder.Append('\n');
        }
        _lines.RemoveRange(0, remove);
        var characterCount = Math.Min(builder.Length, _output.TextLength);
        if (characterCount <= 0) return;
        var caret = _output.SelectionStart;
        _output.Select(0, characterCount);
        _output.SelectedText = string.Empty;
        // Keep the caret inside the document after the leading block disappears.
        _output.SelectionStart = Math.Clamp(caret - characterCount, 0, _output.TextLength);
        _output.SelectionLength = 0;
    }

    private void AppendPromptLine(TerminalSession session, string command)
    {
        var directory = session.WorkingDirectory ?? GetWorkingDirectory();
        var shortDirectory = ShortenPath(directory);
        Append($"{shortDirectory} {session.Prompt} {command}", isError: false, LineStyle.Prompt);
    }

    /// <summary>
    /// Appends one transcript line and repaints the box. The control trims itself instead of
    /// rebuilding the document and only auto-scrolls when the caret is already at the end, so
    /// reading earlier output is never interrupted by a noisy build.
    /// </summary>
    private void Append(string text, bool isError, LineStyle style)
    {
        if (_disposed || IsDisposed) return;
        if (!IsHandleCreated)
        {
            lock (_pendingOutputLock) _pendingOutput.Add((text, isError));
            return;
        }
        var atBottom = IsScrolledToBottom();
        _output.SuspendLayout();
        try { AppendLineCore(text, isError, style); }
        finally { _output.ResumeLayout(); }
        if (atBottom) ScrollToEnd();
        UpdateState();
    }

    private static string ShortenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "~";
        try
        {
            var info = new DirectoryInfo(path);
            if (info.Parent is null) return info.FullName;
            return "…" + Path.DirectorySeparatorChar + Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Security.SecurityException)
        {
            return path;
        }
    }

    private enum LineStyle { Plain, Info, Prompt, Success }

    /// <summary>Appends a line to the transcript. Safe to call from any thread.</summary>
    public void AppendLine(string text, bool isError = false)
    {
        if (_disposed || IsDisposed) return;
        if (InvokeRequired && IsHandleCreated)
        {
            try { BeginInvoke(new Action(() => Append(text, isError, LineStyle.Plain))); }
            catch (InvalidOperationException) { /* The editor is closing. */ }
            return;
        }
        Append(text, isError, LineStyle.Plain);
    }

    private void AppendLineCore(string text, bool isError, LineStyle style)
    {
        var clean = SanitizeAnsi(text);
        if (_lines.Count > 0)
        {
            var previous = _lines[^1];
            if (!previous.IsError && string.Equals(previous.Text, clean, StringComparison.Ordinal))
            {
                previous.RepeatCount++;
                return;
            }
        }
        _lines.Add(new TerminalLine(clean, isError, Repeats: false));
        if (_lines.Count > _maximumLines) TrimTranscript();
        var color = isError ? Theme.Danger
            : style == LineStyle.Info ? Theme.Muted
            : style == LineStyle.Prompt ? Theme.AccentBright
            : Theme.Text;
        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;
        _output.SelectionColor = color;
        if (style == LineStyle.Prompt) _output.SelectionFont = Theme.Mono(Theme.FontControl, FontStyle.Bold);
        _output.AppendText(clean + Environment.NewLine);
        _output.SelectionColor = Theme.Text;
        if (style == LineStyle.Prompt) _output.SelectionFont = Theme.Mono(Theme.FontControl);
        OutputLineReceived?.Invoke(text, isError);
    }

    private void AppendInfo(string text)
    {
        if (_disposed || IsDisposed) return;
        Append(text, isError: false, LineStyle.Info);
    }

    private static string SanitizeAnsi(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.IndexOf('\u001b') < 0 && text.IndexOf('\b') < 0 && text.IndexOf('\r') < 0) return text;
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\u001b')
            {
                // Skip CSI/OSC escape sequences; the RichTextBox has no VT support.
                if (i + 1 < text.Length && text[i + 1] == '[')
                {
                    i += 2;
                    while (i < text.Length && !(text[i] >= '@' && text[i] <= '~')) i++;
                }
                else if (i + 1 < text.Length && text[i + 1] == ']')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '\u0007' || (text[i] == '\u001b' && text[i + 1] == '\\'))) i++;
                    i++;
                }
                continue;
            }
            if (c == '\b')
            {
                if (builder.Length > 0) builder.Length--;
                continue;
            }
            if (c == '\r') continue;
            if (char.IsControl(c) && c != '\t') continue;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private bool IsScrolledToBottom()
    {
        try
        {
            // A caret parked at the end of the document means the user is following the output.
            // Scrolling up moves the caret, so auto-scroll never fights the reader.
            return _output.TextLength == 0 || _output.SelectionStart >= _output.TextLength - 1;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException) { return true; }
    }

    private void ScrollToEnd()
    {
        if (_disposed || !IsHandleCreated) return;
        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;
        _output.ScrollToCaret();
    }

    private void UpdateState()
    {
        if (_disposed || !IsHandleCreated) return;
        var session = _sessions.TryGetValue(_activeShell, out var active) ? active : null;
        var running = session?.IsRunning == true;
        var busy = session?.HasCommandInFlight == true;
        _run.Enabled = running;
        _stop.Enabled = session is not null;
        _shellState.Text = !running ? "○ not started" : busy ? "● running…" : "● ready";
        _shellState.ForeColor = !running ? Theme.Dim : busy ? Theme.Warning : Theme.Success;
        var directory = session?.WorkingDirectory ?? GetWorkingDirectory();
        _workingDirectory.Text = directory;
        _workingDirectory.ForeColor = Theme.Muted;
        _hint.Text = running
            ? $"{session!.ShellName} · {ShortenPath(directory)} · Enter runs · Ctrl+C stops · Ctrl+L clears"
            : "Select a shell to start a session · commands run in a real child process";
    }

    private void SetStatus(string text) => _workingDirectory.Text = text;

    private void PostToUi(Action action)
    {
        if (_disposed || IsDisposed) return;
        try
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch (InvalidOperationException) { /* The editor may close while a shell process exits. */ }
    }

    private string GetWorkingDirectory()
    {
        var candidate = WorkingDirectoryProvider?.Invoke();
        return !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate) ? candidate : Environment.CurrentDirectory;
    }

    /// <summary>Quick action menu with real, executable project tasks.</summary>
    private void ShowQuickTasks(Control anchor)
    {
        var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer(), BackColor = Theme.Toolbar, ForeColor = Theme.Text, Font = Theme.Body(Theme.FontBody) };
        void Add(string text, string command)
            => menu.Items.Add(text, null, (_, _) => Execute(command));
        Add("Build solution (Debug)", "dotnet build .\\PixelEngine.sln -c Debug");
        Add("Build solution (Release)", "dotnet build .\\PixelEngine.sln -c Release");
        Add("Run smoke tests", "dotnet run --project .\\PixelEngine.SmokeTests\\PixelEngine.SmokeTests.csproj -c Debug -- .");
        Add("Restore packages", "dotnet restore .\\PixelEngine.sln");
        menu.Items.Add(new ToolStripSeparator());
        Add("List project files", "dir /b");
        Add("Show .NET version", "dotnet --info");
        Add("Working directory", "echo %CD%");
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start new Command Prompt", null, (_, _) => SwitchShell(TerminalShell.Cmd));
        menu.Items.Add("Start new PowerShell", null, (_, _) => SwitchShell(TerminalShell.PowerShell));
        menu.Items.Add("Clear transcript", null, (_, _) => ClearOutput());
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        (string Text, bool IsError)[] pending;
        lock (_pendingOutputLock)
        {
            pending = _pendingOutput.ToArray();
            _pendingOutput.Clear();
        }
        foreach (var line in pending) AppendLineCore(line.Text, line.IsError, LineStyle.Plain);
        if (pending.Length > 0) ScrollToEnd();
        UpdateState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _flushTimer.Stop();
            _flushTimer.Dispose();
            foreach (var session in _sessions.Values)
            {
                session.OutputLine -= OnSessionOutput;
                session.Dispose();
            }
            _sessions.Clear();
        }
        base.Dispose(disposing);
    }
}
