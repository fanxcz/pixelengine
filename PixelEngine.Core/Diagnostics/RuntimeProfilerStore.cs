using System.Text.Json;

namespace PixelEngine.Diagnostics;

public sealed record RuntimeProfilerDocument(DateTime TimestampUtc, EngineProfilerSnapshot Snapshot);

/// <summary>Small per-process file bridge so the editor can inspect a separately running player.</summary>
public static class RuntimeProfilerStore
{
    public const long MaximumSnapshotBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PixelEngine", "Logs");

    public static string GetSnapshotPath(string? directory = null, int? processId = null)
    {
        var targetDirectory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        var id = processId ?? Environment.ProcessId;
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(processId), "Process ID must be positive.");
        return Path.Combine(targetDirectory, $"runtime-profiler-{id}.json");
    }

    public static void WriteSnapshot(EngineProfilerSnapshot snapshot, string? directory = null, int? processId = null)
    {
        ValidateSnapshot(snapshot);
        var path = Path.GetFullPath(GetSnapshotPath(directory, processId));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new RuntimeProfilerDocument(DateTime.UtcNow, snapshot);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumSnapshotBytes)
            throw new InvalidDataException("Runtime profiler snapshot exceeds the supported 1 MiB limit.");

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { /* A stale temporary file must not hide the primary write result. */ }
        }
    }

    public static bool TryReadLatest(TimeSpan maxAge, out RuntimeProfilerDocument? document, string? directory = null)
    {
        document = null;
        if (maxAge < TimeSpan.Zero) return false;
        var targetDirectory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        string[] paths;
        try
        {
            if (!Directory.Exists(targetDirectory)) return false;
            paths = Directory.GetFiles(targetDirectory, "runtime-profiler-*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }

        foreach (var path in paths.OrderByDescending(SafeLastWriteTimeUtc))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumSnapshotBytes) continue;
                var candidate = JsonSerializer.Deserialize<RuntimeProfilerDocument>(ReadSnapshotText(path), JsonOptions);
                if (candidate is null || candidate.TimestampUtc == default || !IsValidSnapshot(candidate.Snapshot)) continue;
                var timestampUtc = candidate.TimestampUtc.Kind == DateTimeKind.Utc
                    ? candidate.TimestampUtc : candidate.TimestampUtc.ToUniversalTime();
                var age = DateTime.UtcNow - timestampUtc;
                if (age < TimeSpan.FromSeconds(-5) || age > maxAge) continue;
                document = candidate with { TimestampUtc = timestampUtc };
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException or NotSupportedException or InvalidDataException
                or System.Security.SecurityException)
            {
                // A player may be replacing its snapshot while the editor polls it.
            }
        }
        return false;
    }

    private static string ReadSnapshotText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= 0 || stream.Length > MaximumSnapshotBytes)
            throw new InvalidDataException("Runtime profiler snapshot is empty or exceeds the 1 MiB limit.");

        using var buffer = new MemoryStream((int)Math.Min(stream.Length, MaximumSnapshotBytes));
        var chunk = new byte[16_384];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > MaximumSnapshotBytes)
                throw new InvalidDataException("Runtime profiler snapshot exceeds the 1 MiB limit.");
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void ValidateSnapshot(EngineProfilerSnapshot snapshot)
    {
        if (!IsValidSnapshot(snapshot))
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Profiler metrics must be finite and counts must be non-negative.");
    }

    private static bool IsValidSnapshot(EngineProfilerSnapshot snapshot)
        => double.IsFinite(snapshot.FramesPerSecond) && snapshot.FramesPerSecond >= 0d
            && double.IsFinite(snapshot.FrameMilliseconds) && snapshot.FrameMilliseconds >= 0d
            && double.IsFinite(snapshot.UpdateMilliseconds) && snapshot.UpdateMilliseconds >= 0d
            && double.IsFinite(snapshot.DrawMilliseconds) && snapshot.DrawMilliseconds >= 0d
            && snapshot.ManagedMemoryBytes >= 0 && snapshot.ObjectCount >= 0
            && snapshot.ComponentCount >= 0 && snapshot.FrameCount >= 0;

    private static DateTime SafeLastWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException) { return DateTime.MinValue; }
    }
}
