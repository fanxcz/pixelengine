using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PixelEngine.Editor;

internal static class Program
{
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixelEngine", "Logs", "editor-startup.log");

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            ConfigureProcessForHighDpi();
            WriteStartupLog($"Starting PixelEngine 2.0 Studio ({Environment.Version}, {RuntimeInformation.ProcessArchitecture}).");
            StudioControlStyler.EnableApplicationDarkMode();
            ApplicationConfiguration.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetDefaultFont(Theme.Body(Theme.FontBody));
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) => ReportUnexpected("Unhandled UI exception:", eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                WriteStartupLog("Unhandled application exception:", eventArgs.ExceptionObject as Exception, showDialog: true);
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                WriteStartupLog("Unobserved background task exception:", eventArgs.Exception);
                eventArgs.SetObserved();
            };

            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot is not null)
            {
                Environment.CurrentDirectory = repositoryRoot;
                WriteStartupLog($"Repository: {repositoryRoot}");
            }

            var projectPath = ResolveInitialProject(args, repositoryRoot);
            WriteStartupLog(projectPath is null
                ? "No default LIC project found; editor will open without a project."
                : $"Opening project: {projectPath}",
                encodeDebug: true);

            using var form = new MainForm(projectPath);
            form.Shown += (_, _) => WriteStartupLog("MainForm is visible.", encodeDebug: true);
            Application.Run(form);
            WriteStartupLog("Editor closed normally.");
        }
        catch (Exception ex)
        {
            WriteStartupLog("Fatal startup error:", ex);
            MessageBox.Show(
                $"PixelEngine Studio could not start.\n\n{ex.Message}\n\nStartup log:\n{StartupLogPath}",
                "PixelEngine startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Declares per-monitor DPI awareness before any window exists. Without this the OS
    /// bitmap-stretches the whole editor on scaled displays, which blurs text and clips
    /// dock content. Exceptions are ignored on older Windows versions that lack the API.
    /// </summary>
    private static void ConfigureProcessForHighDpi()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }

        if (DebugOutputEnabled)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch (IOException) { } // No console attached (WinExe).
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    private static bool DebugOutputEnabled
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    private static void ReportUnexpected(string context, Exception? exception)
    {
        WriteStartupLog(context, exception);
        MessageBox.Show(
            $"The editor encountered an unexpected error.\n\n{exception?.Message}\n\nFull details were saved to:\n{StartupLogPath}",
            "PixelEngine Studio error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string? ResolveInitialProject(string[] args, string? repositoryRoot)
    {
        var requested = args.FirstOrDefault(value => value.EndsWith(".lic", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var fullPath = Path.GetFullPath(requested);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("The requested LIC project does not exist.", fullPath);
            return fullPath;
        }

        if (repositoryRoot is null) return null;
        var demoPath = Path.Combine(repositoryRoot, "Lic", "demo.lic");
        return File.Exists(demoPath) ? demoPath : null;
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }

    private static void WriteStartupLog(string message, Exception? exception = null, bool encodeDebug = false, bool showDialog = false)
    {
        try
        {
            var folder = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(folder);
            var text = string.Create(CultureInfo.InvariantCulture, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
            if (exception is not null) text += Environment.NewLine + exception;
            var builder = new StringBuilder(text).AppendLine().AppendLine();
            // Keep the log bounded so a long-lived session cannot grow without limit.
            var existing = File.Exists(StartupLogPath) ? new FileInfo(StartupLogPath).Length : 0;
            if (existing > 4 * 1024 * 1024) File.WriteAllText(StartupLogPath, string.Empty);
            File.AppendAllText(StartupLogPath, builder.ToString(), Encoding.UTF8);
            if (encodeDebug && DebugOutputEnabled) Console.WriteLine("[PixelEngine] " + message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Diagnostics must never prevent the editor from starting.
        }
        if (showDialog)
        {
            try
            {
                MessageBox.Show($"{message}\n\n{exception?.Message}\n\nLog: {StartupLogPath}",
                    "PixelEngine", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }
}
