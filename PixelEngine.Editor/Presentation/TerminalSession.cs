using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PixelEngine.Editor;

/// <summary>Shell dialect hosted by a <see cref="TerminalSession"/>.</summary>
internal enum TerminalShell
{
    Cmd,
    PowerShell
}

/// <summary>Raised when a shell process cannot be started or its streams fail.</summary>
internal sealed class TerminalSessionException : Exception
{
    public TerminalSessionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// A real, long-lived Windows shell with redirected stdin/stdout/stderr. Unlike one-shot
/// <c>cmd /c</c> runs this keeps shell state (working directory, environment, variables),
/// streams both stdout and stderr with their real text, supports interactive programs, and
/// reports the exit code of each tracked command by appending unique echo sentinels.
/// Reads are fully asynchronous, so the UI thread is never blocked by a running command.
/// </summary>
internal sealed class TerminalSession : IDisposable
{
    private const string Sentinels = "__PIXELENGINE__";
    private readonly object _sync = new();
    private Process? _process;
    private int _sentinelSequence;
    private int _activeToken;
    private string? _activeCommand;
    private bool _disposed;

    public TerminalShell Shell { get; }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                try { return _process is { HasExited: false }; }
                catch (InvalidOperationException) { return false; }
            }
        }
    }

    public bool HasCommandInFlight => Volatile.Read(ref _activeCommand) is not null;

    /// <summary>Working directory reported by the shell itself (kept in sync by parsed `cd`).</summary>
    public string? WorkingDirectory { get; private set; }

    /// <summary>Raw output line; <c>isError</c> marks stderr and shell-level failures.</summary>
    public event Action<string, bool>? OutputLine;
    /// <summary>Raised when a tracked command line completes with its real exit code.</summary>
    public event Action<string, int>? CommandCompleted;
    public event Action<TerminalSession>? Exited;

    public TerminalSession(TerminalShell shell) => Shell = shell;

    public string ShellName => Shell == TerminalShell.PowerShell ? "PowerShell" : "Command Prompt";

    public string Prompt => Shell == TerminalShell.PowerShell ? "PS" : ">";

    /// <summary>Starts the shell process. Throws <see cref="TerminalSessionException"/> with a user-readable reason.</summary>
    public void Start(string workingDirectory)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is not null) throw new InvalidOperationException("The terminal session is already running.");
            var startInfo = BuildStartInfo(workingDirectory, out var executable);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += OnOutputData;
            process.ErrorDataReceived += OnErrorData;
            process.Exited += OnProcessExited;
            try
            {
                if (!process.Start()) throw new TerminalSessionException($"{ShellName} did not start.");
                process.StandardInput.AutoFlush = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or PlatformNotSupportedException)
            {
                process.Dispose();
                throw new TerminalSessionException(
                    $"{ShellName} could not be started ({executable}). {ex.Message}", ex);
            }
            _process = process;
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory;
            if (Shell == TerminalShell.PowerShell)
            {
                // Progress records are drawn with carriage returns and would flood the reader.
                WriteRaw("$ProgressPreference = 'SilentlyContinue'\r\n");
            }
        }
    }

    private ProcessStartInfo BuildStartInfo(string workingDirectory, out string executable)
    {
        if (!OperatingSystem.IsWindows())
            throw new TerminalSessionException("The integrated terminal hosts cmd.exe and PowerShell, so it requires Windows.");
        var resolvedDirectory = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.CurrentDirectory;
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = resolvedDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["TERM"] = "dumb";

        if (Shell == TerminalShell.PowerShell)
        {
            executable = ResolvePowerShell();
            info.FileName = executable;
            // UTF-8 console output plus a stable, non-themed prompt for the redirected console.
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NoExit");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("-");
        }
        else
        {
            executable = Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comSpec && File.Exists(comSpec)
                ? comSpec
                : Path.Combine(Environment.SystemDirectory, "cmd.exe");
            info.FileName = executable;
            info.Arguments = "/d /k \"chcp 65001>nul & prompt $P$G\"";
        }
        return info;
    }

    private static string ResolvePowerShell()
    {
        var windowsPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(windowsPowerShell)) return windowsPowerShell;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var candidate in new[] { "powershell.exe", "pwsh.exe" })
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var full = Path.Combine(directory.Trim('"'), candidate);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException) { /* Malformed PATH entry. */ }
                catch (IOException) { }
            }
        }
        throw new TerminalSessionException("PowerShell was not found. Install Windows PowerShell 5.1 or PowerShell 7, or switch the terminal to Command Prompt.");
    }

    /// <summary>
    /// Sends a command line to the shell and tracks its exit code. While another tracked command
    /// is still running the text is forwarded as raw standard input instead (so interactive
    /// prompts can be answered) and no completion event is reported for it.
    /// </summary>
    public void SendCommand(string commandLine)
    {
        var command = commandLine?.TrimEnd() ?? string.Empty;
        if (command.Length == 0) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRunning) throw new TerminalSessionException("The terminal session is not running.");

        var tracked = !HasCommandInFlight;
        if (tracked)
        {
            var token = Interlocked.Increment(ref _sentinelSequence);
            Interlocked.Exchange(ref _activeToken, token);
            Volatile.Write(ref _activeCommand, command);
            // A blank line first flushes any half-written prompt in the child console.
            WriteRaw($"\r\n{command}\r\n{CurrentDirectoryProbe(token)}\r\n{ExitCodeProbe(token)}\r\n");
        }
        else
        {
            WriteRaw($"\r\n{command}\r\n");
        }
    }

    private string CurrentDirectoryProbe(int token) => Shell == TerminalShell.PowerShell
        ? $"Write-Output \"{Sentinels}CWD:{token}:$($PWD.Path)\""
        : $"echo {Sentinels}CWD:{token}:%CD%";

    private string ExitCodeProbe(int token) => Shell == TerminalShell.PowerShell
        ? $"Write-Output \"{Sentinels}EXIT:{token}:$(if ($?) {{ if ($LASTEXITCODE -ne $null) {{ $LASTEXITCODE }} else {{ 0 }} }} else {{ 1 }})\""
        : $"echo {Sentinels}EXIT:{token}:%ERRORLEVEL%";

    private void WriteRaw(string text)
    {
        Process? process;
        lock (_sync) process = _process;
        if (process is null) return;
        try
        {
            process.StandardInput.Write(text);
            process.StandardInput.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Raise($"Could not write to {ShellName}: {ex.Message}", isError: true);
        }
    }

    /// <summary>Sends a bare Enter (used by interactive prompts).</summary>
    public void SendEmptyLine() => WriteRaw("\r\n");

    /// <summary>Writes raw text without a trailing newline (interactive input support).</summary>
    public void SendRawInput(string text)
    {
        if (text is null) return;
        WriteRaw(text);
    }

    /// <summary>Terminates the shell process tree. Queued work in the session is lost.</summary>
    public void Kill()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
        }
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Raise($"Could not stop {ShellName}: {ex.Message}", isError: true);
        }
        finally
        {
            try { process.WaitForExit(2000); } catch (InvalidOperationException) { }
            process.Dispose();
        }
        Volatile.Write(ref _activeCommand, null);
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        HandleLine(e.Data, isError: false);
    }

    private void OnErrorData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        HandleLine(e.Data, isError: true);
    }

    private void HandleLine(string line, bool isError)
    {
        if (line.Contains(Sentinels, StringComparison.Ordinal) && TryParseSentinel(line, out var token, out var payload))
        {
            if (payload.Kind == SentinelKind.Directory)
            {
                if (payload.Value.Length > 0 && Directory.Exists(payload.Value)) WorkingDirectory = payload.Value;
                return;
            }
            var command = Volatile.Read(ref _activeCommand);
            if (Interlocked.CompareExchange(ref _activeToken, 0, token) == token)
            {
                Volatile.Write(ref _activeCommand, null);
                if (command is not null) CommandCompleted?.Invoke(command, payload.Value.Length > 0 && int.TryParse(payload.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode) ? exitCode : -1);
            }
            return;
        }
        // cmd.exe echoes the command it is about to run; suppressing that duplicate keeps the
        // transcript readable because the editor already prints the prompt line itself.
        Raise(line, isError);
    }

    private enum SentinelKind { Directory, Exit }

    private readonly record struct SentinelPayload(SentinelKind Kind, string Value);

    private static bool TryParseSentinel(string line, out int token, out SentinelPayload payload)
    {
        token = -1;
        payload = default;
        var trimmed = line.Trim();
        var start = trimmed.IndexOf(Sentinels, StringComparison.Ordinal) + Sentinels.Length;
        var parts = trimmed[start..].Split(':', 3);
        if (parts.Length < 3) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out token)) return false;
        var kind = parts[0] switch
        {
            "CWD" => SentinelKind.Directory,
            "EXIT" => SentinelKind.Exit,
            _ => (SentinelKind?)null
        };
        if (kind is null) return false;
        payload = new SentinelPayload(kind.Value, parts[2].Trim());
        return true;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        int exitCode;
        try { exitCode = (sender as Process)?.ExitCode ?? -1; }
        catch (InvalidOperationException) { exitCode = -1; }
        Volatile.Write(ref _activeCommand, null);
        lock (_sync)
        {
            if (ReferenceEquals(sender, _process)) _process = null;
        }
        Exited?.Invoke(this);
        Raise($"{ShellName} exited with code {exitCode}.", isError: exitCode != 0);
    }

    private void Raise(string line, bool isError) => OutputLine?.Invoke(line, isError);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kill();
    }
}
