using System.Text.Json;

namespace PixelEngine.Storage;

/// <summary>
/// Bounded project-scoped key/value storage. Writes are atomic and stay under the PixelEngine
/// save folder. If the OS denies storage access, the store degrades to an in-memory session store.
/// </summary>
public sealed class PersistentSaveStore
{
    public const int MaximumKeys = 256;
    public const int MaximumKeyLength = 128;
    public const int MaximumValueLength = 64 * 1024;
    public const int MaximumFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private bool _persistent;

    public string FilePath { get; private set; } = string.Empty;
    public bool IsPersistent => _persistent;
    public string? LoadWarning { get; private set; }

    public PersistentSaveStore(string projectName, string? rootDirectory = null)
    {
        var safeName = MakeSafeFileName(projectName);
        var root = rootDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(AppContext.BaseDirectory, ".pixelengine");
            root = Path.Combine(root, "PixelEngine", "Saves");
        }

        try
        {
            var fullRoot = Path.GetFullPath(root);
            Directory.CreateDirectory(fullRoot);
            FilePath = Path.Combine(fullRoot, safeName + ".json");
            _persistent = true;
            LoadFromDisk();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            FilePath = string.Empty;
            _persistent = false;
            LoadWarning = $"Persistent save storage is unavailable; saves will last for this session only: {ex.Message}";
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot
    {
        get { lock (_sync) return new Dictionary<string, string>(_values, StringComparer.Ordinal); }
    }

    public bool TryGet(string key, out string value)
    {
        ValidateKey(key);
        lock (_sync) return _values.TryGetValue(key, out value!);
    }

    public string Get(string key, string fallback = "")
        => TryGet(key, out var value) ? value : fallback;

    public void Set(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumValueLength) throw new ArgumentOutOfRangeException(nameof(value), $"Save values are limited to {MaximumValueLength} characters.");
        lock (_sync)
        {
            if (!_values.ContainsKey(key) && _values.Count >= MaximumKeys)
                throw new InvalidOperationException($"A project may store at most {MaximumKeys} save keys.");
            var previous = _values.TryGetValue(key, out var oldValue) ? oldValue : null;
            _values[key] = value;
            try { Persist(); }
            catch
            {
                if (previous is null) _values.Remove(key);
                else _values[key] = previous;
                throw;
            }
        }
    }

    public bool Remove(string key)
    {
        ValidateKey(key);
        lock (_sync)
        {
            if (!_values.Remove(key, out var previous)) return false;
            try { Persist(); }
            catch { _values[key] = previous; throw; }
            return true;
        }
    }

    private void LoadFromDisk()
    {
        if (!_persistent || !File.Exists(FilePath)) return;
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Length > MaximumFileBytes) throw new InvalidDataException("Save file exceeds the supported size limit.");
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            if (loaded.Count > MaximumKeys) throw new InvalidDataException("Save file contains too many keys.");
            var validated = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in loaded)
            {
                ValidateKey(pair.Key);
                if (pair.Value is null || pair.Value.Length > MaximumValueLength)
                    throw new InvalidDataException($"Save entry '{pair.Key}' is invalid or too large.");
                validated.Add(pair.Key, pair.Value);
            }
            foreach (var pair in validated) _values[pair.Key] = pair.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or InvalidDataException or ArgumentException or System.Security.SecurityException)
        {
            LoadWarning = $"An invalid save file was ignored: {ex.Message}";
            try
            {
                var backup = FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
                File.Move(FilePath, backup, overwrite: false);
            }
            catch { /* Preserve startup even when the damaged file cannot be quarantined. */ }
        }
    }

    private void Persist()
    {
        if (!_persistent) return;
        var json = JsonSerializer.Serialize(_values, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumFileBytes)
            throw new InvalidOperationException("Project save data exceeds the 1 MiB storage limit.");
        var temporary = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) { Console.Error.WriteLine($"[PersistentSaveStore] Could not delete '{temporary}': {ex.Message}"); }
        }
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaximumKeyLength) throw new ArgumentOutOfRangeException(nameof(key), $"Save keys are limited to {MaximumKeyLength} characters.");
    }

    private static string MakeSafeFileName(string? name)
    {
        var input = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var output = new string(input.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).Take(80).ToArray());
        output = output.Trim().Trim('.');
        return string.IsNullOrWhiteSpace(output) ? "Untitled" : output;
    }
}
