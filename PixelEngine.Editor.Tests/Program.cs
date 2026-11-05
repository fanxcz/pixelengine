using System.Text.Json;
using PixelEngine.Editor;
using PixelEngine.Serialization;

// Editor-side smoke tests. These run headless: they never create a window, they exercise the
// non-visual logic the Studio shell depends on (persisted preferences, dock layout
// serialization, build-diagnostic parsing and MSBuild output shapes).
//
// Run with:  dotnet run --project .\PixelEngine.Editor.Tests -c Debug

var failures = new List<string>();
var passes = 0;

void Check(string name, Action body)
{
    try
    {
        body();
        passes++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures.Add(name + ": " + ex.Message);
        Console.WriteLine("FAIL " + name + ": " + ex.Message);
    }
}

var scratch = Path.Combine(Path.GetTempPath(), "PixelEngineEditorTests_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);

try
{
    Check("settings round-trip preserves every preference", () =>
    {
        var path = Path.Combine(scratch, "settings.json");
        var settings = new EditorSettings
        {
            AutoCheckScripts = false,
            SaveBeforeRun = false,
            ConfirmDiscardChanges = false,
            GroupRepeatedLogs = false,
            MaxOutputEntries = 4321,
            DefaultShell = "Command Prompt",
            TerminalFontSize = 12.5f,
            TerminalScrollback = 9000,
            ScriptFontSize = 14f,
            ScriptWordWrap = true,
            SceneGridSize = 32f,
            SceneSnap = false,
            SceneShowGrid = false,
            SceneShowColliders = false,
            SceneShowLabels = true
        };
        settings.RememberProject(@"C:\Games\Alpha\alpha.lic");
        if (!settings.TrySave(path, out var saveError)) throw new InvalidOperationException("Save failed: " + saveError);

        var loaded = EditorSettings.Load(path, out var warning);
        if (warning is not null) throw new InvalidOperationException("Unexpected load warning: " + warning);
        if (loaded.AutoCheckScripts || loaded.SaveBeforeRun || loaded.ConfirmDiscardChanges || loaded.GroupRepeatedLogs)
            throw new InvalidOperationException("Boolean preferences were not restored.");
        if (loaded.MaxOutputEntries != 4321 || loaded.TerminalScrollback != 9000) throw new InvalidOperationException("Numeric preferences were not restored.");
        if (loaded.DefaultShell != "Command Prompt") throw new InvalidOperationException("Shell preference was not restored.");
        if (Math.Abs(loaded.TerminalFontSize - 12.5f) > .001f || Math.Abs(loaded.ScriptFontSize - 14f) > .001f)
            throw new InvalidOperationException("Font preferences were not restored.");
        if (Math.Abs(loaded.SceneGridSize - 32f) > .001f || loaded.SceneSnap || loaded.SceneShowGrid || loaded.SceneShowColliders || !loaded.SceneShowLabels)
            throw new InvalidOperationException("Scene view preferences were not restored.");
        if (loaded.RecentProjects.Count != 1) throw new InvalidOperationException("Recent projects were not persisted.");
        if (!File.Exists(path)) throw new InvalidOperationException("Settings file was not written.");
        if (File.Exists(path + ".tmp")) throw new InvalidOperationException("Temporary settings file was left behind.");
    });

    Check("settings clamp hostile values instead of trusting the file", () =>
    {
        var path = Path.Combine(scratch, "hostile.json");
        File.WriteAllText(path, """
        {"maxOutputEntries":-5,"terminalFontSize":900,"terminalScrollback":1,"scriptFontSize":0,
         "sceneGridSize":0,"defaultShell":"bash","recentProjects":["","  ","a.lic","a.lic","b.lic"]}
        """);
        var loaded = EditorSettings.Load(path, out _);
        if (loaded.MaxOutputEntries != 250) throw new InvalidOperationException($"maxOutputEntries={loaded.MaxOutputEntries}");
        if (loaded.TerminalFontSize > 24f || loaded.TerminalFontSize < 7f) throw new InvalidOperationException("font not clamped");
        if (loaded.TerminalScrollback != 500) throw new InvalidOperationException("scrollback not clamped");
        if (loaded.SceneGridSize < 1f) throw new InvalidOperationException("grid size not clamped");
        if (loaded.DefaultShell != "PowerShell") throw new InvalidOperationException("unknown shell was not normalized");
        if (loaded.RecentProjects.Count != 2) throw new InvalidOperationException($"recent list not de-duplicated/cleaned: {loaded.RecentProjects.Count}");
    });

    Check("corrupt settings fall back to defaults with a warning", () =>
    {
        var path = Path.Combine(scratch, "corrupt.json");
        File.WriteAllText(path, "{ this is not json");
        var loaded = EditorSettings.Load(path, out var warning);
        if (warning is null) throw new InvalidOperationException("A corrupt settings file produced no warning.");
        if (loaded.MaxOutputEntries != 2000 || loaded.DefaultShell != "PowerShell")
            throw new InvalidOperationException("Defaults were not applied.");
    });

    Check("missing settings file yields defaults silently", () =>
    {
        var loaded = EditorSettings.Load(Path.Combine(scratch, "does-not-exist.json"), out var warning);
        if (warning is not null) throw new InvalidOperationException("A missing file should not warn.");
        if (!loaded.SceneSnap || !loaded.SceneShowGrid || loaded.SceneShowLabels)
            throw new InvalidOperationException("Booleans do not match the documented defaults.");
    });

    Check("recent projects keep newest first and stay bounded", () =>
    {
        var settings = new EditorSettings();
        for (var i = 0; i < 20; i++) settings.RememberProject($@"C:\p{i}.lic");
        if (settings.RecentProjects.Count > 12) throw new InvalidOperationException("Recent list is unbounded.");
        if (settings.RecentProjects[0] != @"C:\p19.lic") throw new InvalidOperationException("Newest project is not first.");
        settings.RememberProject(@"C:\p10.lic");
        if (settings.RecentProjects[0] != @"C:\p10.lic" || settings.RecentProjects.Count(x => x == @"C:\p10.lic") != 1)
            throw new InvalidOperationException("Re-remembering a project did not move it to the top exactly once.");
        if (settings.ExistingRecentProjects().Any()) throw new InvalidOperationException("Non-existent paths were reported as existing.");
    });

    Check("dock layout persists every splitter and collapse flag", () =>
    {
        var path = Path.Combine(scratch, "layout.json");
        var layout = new EditorDockLayout
        {
            RootDistance = 801, HierarchyDistance = 260, CenterDistance = 940, FileSystemDistance = 240,
            HierarchyHidden = true, FileSystemHidden = true, InspectorHidden = false, BottomDockHidden = true
        };
        File.WriteAllText(path, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        var restored = JsonSerializer.Deserialize<EditorDockLayout>(File.ReadAllText(path));
        if (restored is null) throw new InvalidOperationException("Layout did not deserialize.");
        if (restored.RootDistance != 801 || restored.HierarchyDistance != 260 || restored.CenterDistance != 940 || restored.FileSystemDistance != 240)
            throw new InvalidOperationException("Splitter distances were not preserved.");
        if (!restored.HierarchyHidden || !restored.FileSystemHidden || restored.InspectorHidden || !restored.BottomDockHidden)
            throw new InvalidOperationException("Collapse flags were not preserved.");
    });

    Check("build diagnostic parser understands both MSBuild error shapes", () =>
    {
        CheckParse(@"C:\src\Game\MainForm.cs(120,15): error CS0118: 'Graphics' is a namespace but is used like a type [C:\src\Game\Game.csproj]",
            "C:\\src\\Game\\MainForm.cs", 120, 15, "error", "CS0118");
        CheckParse(@"D:\a\pixelengine\pixelengine\PixelEngine.Editor\CodeEditorControl.cs:42:9: warning CS8602: Dereference of a possibly null reference.",
            "D:\\a\\pixelengine\\pixelengine\\PixelEngine.Editor\\CodeEditorControl.cs", 42, 9, "warning", "CS8602");
        CheckParse("Assets/Game.cs(7,1): error CS1002: ; expected", "Assets/Game.cs", 7, 1, "error", "CS1002");

        foreach (var nonDiagnostic in new[]
                 {
                     "Build succeeded.",
                     "    0 Warning(s)",
                     "  Game -> C:\\src\\Game\\bin\\Debug\\Game.dll",
                     "  Determining projects to restore...",
                     "MSBUILD : error MSB1009: Project file does not exist."
                 })
        {
            if (BuildDiagnosticLineParser.TryParse(nonDiagnostic, out _, out _, out _, out _, out _, out _))
                throw new InvalidOperationException("Non-diagnostic line was parsed as a compiler diagnostic: " + nonDiagnostic);
        }
    });

    Check("build diagnostic parser rejects truncated records", () =>
    {
        foreach (var broken in new[]
                 {
                     "(12,4): error CS0001: missing file",
                     "Game.cs(abc,4): error CS0001: bad line number",
                     "Game.cs(12,4): error CS0001",
                     "Game.cs(12,4): fatal CS0001: not an error/warning"
                 })
            if (BuildDiagnosticLineParser.TryParse(broken, out _, out _, out _, out _, out _, out _))
                throw new InvalidOperationException("Malformed diagnostic was accepted: " + broken);
    });

    Check("project fixtures still load through the editor's project reader", () =>
    {
        var root = FindRepositoryRoot();
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var licDirectory = Path.Combine(root, "Lic");
        var fixtures = Directory.GetFiles(licDirectory, "*.lic").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (fixtures.Length == 0) throw new InvalidOperationException("No .lic fixtures found.");
        foreach (var fixture in fixtures)
        {
            var project = LicProjectReader.Load(fixture);
            if (project.Scenes.Count == 0) throw new InvalidOperationException($"'{Path.GetFileName(fixture)}' has no scenes.");
            if (project.Scenes.Any(scene => scene.Objects is null)) throw new InvalidOperationException($"'{Path.GetFileName(fixture)}' has a null object list.");
        }
    });
}
finally
{
    try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
}

Console.WriteLine($"\nEditor tests: {passes} passed, {failures.Count} failed.");
if (failures.Count > 0)
{
    Console.Error.WriteLine("\nEditor test failures:");
    foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
    return 1;
}
return 0;

void CheckParse(string line, string expectedFile, int expectedLine, int expectedColumn, string expectedSeverity, string expectedCode)
{
    if (!BuildDiagnosticLineParser.TryParse(line, out var file, out var lineNumber, out var column, out var severity, out var code, out _))
        throw new InvalidOperationException("Diagnostic was not parsed: " + line);
    if (file != expectedFile || lineNumber != expectedLine || column != expectedColumn ||
        !string.Equals(severity, expectedSeverity, StringComparison.OrdinalIgnoreCase) || code != expectedCode)
        throw new InvalidOperationException(
            $"Parsed '{line}' as {file}:{lineNumber}:{column} {severity} {code}; expected {expectedFile}:{expectedLine}:{expectedColumn} {expectedSeverity} {expectedCode}.");
}

static string? FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    return null;
}
