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
            WriteStartupLog("Starting PixelEngine 2.0 Studio.");
#if DEBUG
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("[PixelEngine] Starting editor...");
            Console.WriteLine($"[PixelEngine] Startup log: {StartupLogPath}");
#endif
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) =>
            {
                WriteStartupLog("Unhandled UI exception:", eventArgs.Exception);
                MessageBox.Show(
                    $"The editor encountered an unexpected error.\n\n{eventArgs.Exception.Message}\n\nFull details were saved to:\n{StartupLogPath}",
                    "PixelEngine Studio error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                WriteStartupLog("Unhandled application exception:", eventArgs.ExceptionObject as Exception);
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
#if DEBUG
                Console.WriteLine($"[PixelEngine] Repository: {repositoryRoot}");
#endif
            }

            var projectPath = ResolveInitialProject(args, repositoryRoot);
            WriteStartupLog(projectPath is null
                ? "No default LIC project found; editor will open without a project."
                : $"Opening project: {projectPath}");
#if DEBUG
            Console.WriteLine(projectPath is null
                ? "[PixelEngine] No default project found; opening empty editor."
                : $"[PixelEngine] Opening project: {projectPath}");
#endif
            using var form = new MainForm(projectPath);
            form.Shown += (_, _) =>
            {
                WriteStartupLog("MainForm is visible.");
#if DEBUG
                Console.WriteLine("[PixelEngine] Editor window is visible.");
#endif
            };
            Application.Run(form);
            WriteStartupLog("Editor closed normally.");
#if DEBUG
            Console.WriteLine("[PixelEngine] Editor closed normally.");
#endif
        }
        catch (Exception ex)
        {
            WriteStartupLog("Fatal startup error:", ex);
#if DEBUG
            Console.Error.WriteLine("[PixelEngine] Fatal startup error:");
            Console.Error.WriteLine(ex);
#endif
            MessageBox.Show(
                $"PixelEngine Studio could not start.\n\n{ex}\n\nStartup log:\n{StartupLogPath}",
                "PixelEngine startup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private static void WriteStartupLog(string message, Exception? exception = null)
    {
        try
        {
            var folder = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(folder);
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            if (exception is not null) text += Environment.NewLine + exception;
            File.AppendAllText(StartupLogPath, text + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never prevent the editor from starting.
        }
    }
}
