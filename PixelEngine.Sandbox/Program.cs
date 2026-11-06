using System.Runtime.InteropServices;
using PixelEngine.Compatibility;
using PixelEngine.Core;
using PixelEngine.Serialization;
using PixelEngine.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace PixelEngine.Sandbox;

internal static class Program
{
    private static readonly string PlayerLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixelEngine", "Logs", "player-startup.log");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            WritePlayerLog($"Starting MonoGame player ({RuntimeInformation.ProcessArchitecture}); base directory: {AppContext.BaseDirectory}");
            var projectPath = ResolveProjectPath(args);
            WritePlayerLog($"Project: {projectPath}");
            using var game = new LicPreviewGame(projectPath);
            game.Run();
            WritePlayerLog("Game window closed normally.");
            return 0;
        }
        catch (Exception ex)
        {
            WritePlayerLog("Player startup/runtime failure:", ex);
            var rootCause = ex.GetBaseException();
            var message = $"The game could not start.\n\n{rootCause.Message}\n\nFull details were saved to:\n{PlayerLogPath}";
            if (OperatingSystem.IsWindows())
                NativeMessageBox(IntPtr.Zero, message, "PixelEngine Player error", 0x10 | 0x0);
            else
                Console.Error.WriteLine(message + Environment.NewLine + ex);
            // A failed startup must be visible to Studio's process monitor. Main previously
            // swallowed the exception and returned exit code 0, hiding the actual failure.
            return 1;
        }
    }

    private static string ResolveProjectPath(string[] args)
    {
        var requested = args.FirstOrDefault(x => x.EndsWith(".lic", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var full = Path.GetFullPath(requested);
            if (!File.Exists(full)) throw new FileNotFoundException("Requested .lic project was not found.", full);
            return full;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Lic", "demo.lic"),
            Path.Combine(Environment.CurrentDirectory, "Lic", "demo.lic"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lic", "demo.lic"))
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not locate Lic/demo.lic. Pass a .lic path as the first argument.");
    }

    private static void WritePlayerLog(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerLogPath)!);
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            if (exception is not null) text += Environment.NewLine + exception;
            File.AppendAllText(PlayerLogPath, text + Environment.NewLine + Environment.NewLine);
        }
        catch { /* A logging failure must not mask the game error. */ }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NativeMessageBox(IntPtr window, string text, string caption, uint type);
}

internal sealed class LicPreviewGame : EngineGame
{
    private readonly string _projectPath;
    private LicProjectData? _project;
    private double _profilerLogAccumulator;

    public LicPreviewGame(string projectPath) : base(1280, 720)
    {
        _projectPath = projectPath;
        Window.Title = "PixelEngine 2.0 — LIC Preview";
    }

    protected override void OnEngineInitialize()
    {
        _project = LicProjectReader.Load(_projectPath);
        Window.Title = $"PixelEngine 2.0 — {_project.Name} [{_project.Scenes[_project.StartScene].Name}]";
        var scene = LicSceneFactory.Create(_project, _project.StartScene, Assets, Camera);
        SetScene(scene);

        var scriptCount = _project.Scripts.Count;
        var luaCount = _project.Scripts.Count(x => string.Equals(x.Language, "lua", StringComparison.OrdinalIgnoreCase));
        var csharpCount = scriptCount - luaCount;
        var assetCount = _project.Assets.Count;
        Console.WriteLine($"Loaded '{_project.Name}' from {_projectPath}");
        Console.WriteLine($"Scenes: {_project.Scenes.Count}; Objects in active scene: {_project.Scenes[_project.StartScene].Objects.Count}; Lua: {luaCount}; C# retained/not executed: {csharpCount}; Assets: {assetCount}");
        Console.WriteLine("Compatibility preview: A/D or arrows move the follow object; W/Up/Space jumps; Esc exits.");
        Console.WriteLine("Lua scripts run in MoonSharp SoftSandbox. Legacy C# scripts are deliberately not executed.");
    }

    protected override void Update(GameTime gameTime)
    {
        if (Input.WasKeyPressed(Keys.Escape)) Exit();
        base.Update(gameTime);
        _profilerLogAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
        if (_profilerLogAccumulator >= 2.0)
        {
            _profilerLogAccumulator = 0;
            var sample = Profiler.Snapshot;
            Console.WriteLine($"[Profiler] FPS={sample.FramesPerSecond:0.0}; Frame={sample.FrameMilliseconds:0.00}ms; Update={sample.UpdateMilliseconds:0.00}ms; Draw={sample.DrawMilliseconds:0.00}ms; Objects={sample.ObjectCount}; Components={sample.ComponentCount}; Memory={sample.ManagedMemoryBytes / 1024d / 1024d:0.0}MB");
            try { RuntimeProfilerStore.WriteSnapshot(sample); }
            catch (Exception ex) { Console.Error.WriteLine($"[Profiler] Telemetry write failed: {ex.Message}"); }
        }
    }
}
