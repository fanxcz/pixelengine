using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Integrated task terminal for editor workflows. Each command runs in a bounded
/// cmd.exe process so a stuck build can be stopped without hanging the editor.
/// </summary>
internal sealed class EditorTerminalControl : UserControl
{
    private readonly RichTextBox _output = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Terminal,
        ForeColor = Theme.Text,
        Font = new Font("Cascadia Mono", 9.5f),
        DetectUrls = false,
        WordWrap = false,
        HideSelection = false
    };
    private readonly TextBox _command = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Viewport,
        ForeColor = Theme.Text,
        Font = new Font("Cascadia Mono", 9.5f),
        PlaceholderText = "Enter command (e.g. dotnet build .\\PixelEngine.sln -c Debug)"
    };
    private readonly Button _run = new() { Text = "Run", Width = 68, Height = 28, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat };
    private readonly Button _stop = new() { Text = "Stop", Width = 68, Height = 28, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat, Enabled = false };
    private readonly Button _clear = new() { Text = "Clear", Width = 68, Height = 28, Dock = DockStyle.Right, FlatStyle = FlatStyle.Flat };
    private readonly Label _workingDirectory = new() { AutoSize = false, Width = 240, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted };
    private readonly List<string> _history = new();
    private readonly List<(string Text, bool IsError)> _pendingOutput = new();
    private readonly object _pendingOutputLock = new();
    private int _historyIndex;
    private Process? _process;
    private bool _disposed;

    public Func<string>? WorkingDirectoryProvider { get; set; }
    public event Action<string>? CommandStarted;
    public event Action<string, bool>? OutputLineReceived;
    public event Action<string, int>? CommandCompleted;

    public EditorTerminalControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Terminal;

        var header = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.Toolbar, Padding = new Padding(6, 3, 6, 3) };
        var title = new Label { Dock = DockStyle.Left, Width = 104, Text = "TERMINAL", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8.5f) };
        _workingDirectory.Dock = DockStyle.Fill;
        _workingDirectory.AutoEllipsis = true;
        StyleButton(_run, Theme.Accent);
        StyleButton(_stop, Theme.Danger);
        StyleButton(_clear, Theme.Toolbar);
        _clear.Click += (_, _) => _output.Clear();
        _stop.Click += (_, _) => StopCommand();
        _run.Click += (_, _) => Execute(_command.Text);
        header.Controls.Add(_workingDirectory);
        header.Controls.Add(_clear);
        header.Controls.Add(_stop);
        header.Controls.Add(_run);
        header.Controls.Add(title);

        var prompt = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Toolbar, Padding = new Padding(7, 6, 7, 6) };
        var promptLabel = new Label { Text = ">", Dock = DockStyle.Left, Width = 18, ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Cascadia Mono", 11f, FontStyle.Bold) };
        prompt.Controls.Add(_command);
        prompt.Controls.Add(promptLabel);
        _command.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Execute(_command.Text);
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.SuppressKeyPress = true;
                MoveHistory(-1);
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                MoveHistory(1);
            }
        };

        Controls.Add(_output);
        Controls.Add(prompt);
        Controls.Add(header);
        UpdateWorkingDirectoryLabel();
        AppendLine("PixelEngine integrated terminal · command tasks run in their own process.");
        AppendLine("Tip: use the Build / Tests quick actions or enter dotnet commands below.");
    }

    public void FocusCommandInput() => _command.Focus();

    public void Execute(string? command)
    {
        command = command?.Trim();
        if (string.IsNullOrWhiteSpace(command) || _disposed) return;
        if (_process is { HasExited: false } runningProcess)
        {
            try
            {
                runningProcess.StandardInput.WriteLine(command);
                runningProcess.StandardInput.Flush();
                AppendLine("> " + command);
                _command.Clear();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                AppendLine("The running command does not accept input: " + ex.Message, isError: true);
            }
            return;
        }

        _command.Text = string.Empty;
        _history.Add(command);
        if (_history.Count > 100) _history.RemoveAt(0);
        _historyIndex = _history.Count;
        AppendLine($"{Environment.NewLine}> {command}");
        try
        {
            UpdateWorkingDirectoryLabel();
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec ? comSpec : "cmd.exe",
                WorkingDirectory = GetWorkingDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/s");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(command);
            start.Environment["DOTNET_NOLOGO"] = "1";
            start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLine(e.Data, isError: true); };
            if (!process.Start())
            {
                process.Dispose();
                AppendLine("The command process could not be started.", isError: true);
                return;
            }
            _process = process;
            _run.Enabled = false;
            _stop.Enabled = true;
            CommandStarted?.Invoke(command);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = ObserveProcessAsync(process, command);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppendLine("Command launch failed: " + ex.Message, isError: true);
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
            _process?.Dispose();
            _process = null;
            _run.Enabled = true;
            _stop.Enabled = false;
        }
    }

    private async Task ObserveProcessAsync(Process process, string command)
    {
        int exitCode;
        try
        {
            await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            exitCode = -1;
            AppendLine("Could not read process exit status: " + ex.Message, isError: true);
        }

        PostToUi(() =>
        {
            AppendLine($"Process finished with exit code {exitCode}.", isError: exitCode != 0);
            var isCurrentProcess = ReferenceEquals(_process, process);
            if (isCurrentProcess)
            {
                _process = null;
                _run.Enabled = true;
                _stop.Enabled = false;
            }
            process.Dispose();
            CommandCompleted?.Invoke(command, exitCode);
        });
    }

    public void StopCommand()
    {
        var process = _process;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                AppendLine("Stop requested…");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            AppendLine("Could not stop process: " + ex.Message, isError: true);
        }
    }

    public void AppendLine(string text, bool isError = false)
    {
        if (_disposed || IsDisposed) return;
        if (!IsHandleCreated)
        {
            lock (_pendingOutputLock) _pendingOutput.Add((text, isError));
            return;
        }
        PostToUi(() =>
        {
            if (_disposed || IsDisposed) return;
            _output.SelectionStart = _output.TextLength;
            _output.SelectionLength = 0;
            _output.SelectionColor = isError ? Theme.Danger : Theme.Text;
            _output.AppendText(text + Environment.NewLine);
            _output.SelectionColor = Theme.Text;
            OutputLineReceived?.Invoke(text, isError);
            if (_output.Lines.Length > 8000)
            {
                var remove = _output.Lines.Length - 6000;
                var textToKeep = string.Join(Environment.NewLine, _output.Lines.Skip(remove));
                _output.Text = textToKeep + Environment.NewLine;
            }
            _output.SelectionStart = _output.TextLength;
            _output.ScrollToCaret();
        });
    }

    private void PostToUi(Action action)
    {
        if (_disposed || IsDisposed || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch (InvalidOperationException) { /* The editor may close while a process is exiting. */ }
    }

    private string GetWorkingDirectory()
    {
        var candidate = WorkingDirectoryProvider?.Invoke();
        return !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)
            ? candidate
            : Environment.CurrentDirectory;
    }

    private void UpdateWorkingDirectoryLabel()
    {
        var path = GetWorkingDirectory();
        _workingDirectory.Text = path;
    }

    private void MoveHistory(int offset)
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + offset, 0, _history.Count);
        _command.Text = _historyIndex == _history.Count ? string.Empty : _history[_historyIndex];
        _command.SelectionStart = _command.TextLength;
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
        foreach (var line in pending) AppendLine(line.Text, line.IsError);
    }

    private static void StyleButton(Button button, Color background)
    {
        button.BackColor = background;
        button.ForeColor = Theme.Text;
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = Theme.Selection;
        button.FlatAppearance.MouseDownBackColor = Theme.AccentDark;
        button.FlatAppearance.BorderSize = 1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try { _process?.Kill(entireProcessTree: true); } catch { }
            _process?.Dispose();
            _process = null;
        }
        base.Dispose(disposing);
    }
}
