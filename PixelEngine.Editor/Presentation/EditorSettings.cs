using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Editor;

/// <summary>
/// Persisted editor preferences. Stored as JSON next to the dock layout under
/// <c>%LOCALAPPDATA%\PixelEngine</c>, so settings survive restarts without touching the
/// project file. Every property has a safe default: a corrupt or partial file falls back to
/// defaults instead of blocking startup.
/// </summary>
internal sealed class EditorSettings
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")] public int Version { get; set; } = CurrentVersion;

    // ── Behaviour ────────────────────────────────────────────────────────────────
    /// <summary>Automatically run the Lua syntax check after a project loads.</summary>
    [JsonPropertyName("autoCheckScripts")] public bool AutoCheckScripts { get; set; } = true;
    /// <summary>Save the project before launching the player (required for a real preview).</summary>
    [JsonPropertyName("saveBeforeRun")] public bool SaveBeforeRun { get; set; } = true;
    /// <summary>Ask for confirmation before replacing the project with a new/other one.</summary>
    [JsonPropertyName("confirmDiscardChanges")] public bool ConfirmDiscardChanges { get; set; } = true;
    /// <summary>Collapse repeated identical log lines instead of printing them all.</summary>
    [JsonPropertyName("groupRepeatedLogs")] public bool GroupRepeatedLogs { get; set; } = true;
    /// <summary>Keep at most this many Output entries in memory (250–20 000).</summary>
    [JsonPropertyName("maxOutputEntries")] public int MaxOutputEntries { get; set; } = 2000;

    // ── Terminal ─────────────────────────────────────────────────────────────────
    /// <summary>Shell started when the terminal first opens.</summary>
    [JsonPropertyName("defaultShell")] public string DefaultShell { get; set; } = "PowerShell";
    /// <summary>Font size (points) for the terminal transcript (7–24).</summary>
    [JsonPropertyName("terminalFontSize")] public float TerminalFontSize { get; set; } = 9.5f;
    /// <summary>Maximum transcript lines retained by the terminal (500–50 000).</summary>
    [JsonPropertyName("terminalScrollback")] public int TerminalScrollback { get; set; } = 6000;

    // ── Script editor ────────────────────────────────────────────────────────────
    [JsonPropertyName("scriptFontSize")] public float ScriptFontSize { get; set; } = 10f;
    [JsonPropertyName("scriptWordWrap")] public bool ScriptWordWrap { get; set; }
    [JsonPropertyName("scriptShowLineNumbers")] public bool ScriptShowLineNumbers { get; set; } = true;

    // ── Scene view defaults ──────────────────────────────────────────────────────
    [JsonPropertyName("sceneGridSize")] public float SceneGridSize { get; set; } = 16f;
    [JsonPropertyName("sceneSnap")] public bool SceneSnap { get; set; } = true;
    [JsonPropertyName("sceneShowGrid")] public bool SceneShowGrid { get; set; } = true;
    [JsonPropertyName("sceneShowColliders")] public bool SceneShowColliders { get; set; } = true;
    [JsonPropertyName("sceneShowLabels")] public bool SceneShowLabels { get; set; }

    // ── Recent projects ─────────────────────────────────────────────────────────
    [JsonPropertyName("recentProjects")] public List<string> RecentProjects { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixelEngine", "EditorSettings.json");

    /// <summary>Loads settings, repairing out-of-range values and ignoring unreadable files.</summary>
    public static EditorSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(SettingsFile)) return new EditorSettings();
            var json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<EditorSettings>(json, JsonOptions) ?? new EditorSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            warning = $"Editor settings could not be read and were reset to defaults: {ex.Message}";
            return new EditorSettings();
        }
    }

    /// <summary>Saves atomically. Returns false and reports the reason instead of throwing.</summary>
    public bool TrySave(out string? error)
    {
        error = null;
        try
        {
            Normalize();
            var folder = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(folder);
            var temporary = SettingsFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            // Write-then-replace keeps the previous file intact if the process dies mid-write.
            File.Move(temporary, SettingsFile, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Clamps every numeric value into its supported range.</summary>
    public void Normalize()
    {
        MaxOutputEntries = Math.Clamp(MaxOutputEntries, 250, 20_000);
        TerminalFontSize = float.IsFinite(TerminalFontSize) ? Math.Clamp(TerminalFontSize, 7f, 24f) : 9.5f;
        TerminalScrollback = Math.Clamp(TerminalScrollback, 500, 50_000);
        ScriptFontSize = float.IsFinite(ScriptFontSize) ? Math.Clamp(ScriptFontSize, 7f, 32f) : 10f;
        SceneGridSize = float.IsFinite(SceneGridSize) ? Math.Clamp(SceneGridSize, 1f, 512f) : 16f;
        DefaultShell = string.Equals(DefaultShell, "Command Prompt", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(DefaultShell, "cmd", StringComparison.OrdinalIgnoreCase)
            ? "Command Prompt"
            : "PowerShell";
        RecentProjects ??= new List<string>();
        RecentProjects = RecentProjects
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        Version = CurrentVersion;
    }

    /// <summary>Records a project path at the top of the recent list (bounded, de-duplicated).</summary>
    public void RememberProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        RecentProjects.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, path);
        while (RecentProjects.Count > 12) RecentProjects.RemoveAt(RecentProjects.Count - 1);
    }

    /// <summary>Recent entries that still exist on disk.</summary>
    public IEnumerable<string> ExistingRecentProjects()
    {
        foreach (var path in RecentProjects)
        {
            var exists = false;
            try { exists = File.Exists(path); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
            if (exists) yield return path;
        }
    }
}
