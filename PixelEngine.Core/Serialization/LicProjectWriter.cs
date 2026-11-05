using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Serialization;

/// <summary>Writes the legacy .lic v1 JSON shape without changing its field names.</summary>
public static class LicProjectWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        MaxDepth = 128
    };

    public static void Save(string path, LicProjectData project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, Serialize(project));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { /* A stale temporary file must not replace the primary save result. */ }
        }
    }

    public static string Serialize(LicProjectData project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var json = JsonSerializer.Serialize(project, Options);
        if (Encoding.UTF8.GetByteCount(json) > LicProjectReader.MaximumProjectBytes)
            throw new InvalidDataException($"LIC project exceeds the {LicProjectReader.MaximumProjectBytes / (1024 * 1024)} MiB document limit.");
        return json;
    }
}
