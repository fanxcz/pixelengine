namespace PixelEngine.Serialization;

/// <summary>Imports small project-local assets into the embedded LIC asset store.</summary>
public static class LicAssetOperations
{
    public const long DefaultMaximumAssetBytes = 16 * 1024 * 1024;

    public static LicAssetData ImportFile(LicProjectData project, string path, long maximumBytes = DefaultMaximumAssetBytes)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Maximum asset size must be positive.");
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Asset file was not found.", path);
        if (info.Length <= 0 || info.Length > maximumBytes || info.Length > int.MaxValue)
            throw new InvalidDataException($"Asset size must be between 1 byte and {Math.Min(maximumBytes, int.MaxValue)} bytes.");

        var extension = info.Extension.ToLowerInvariant();
        var type = extension switch
        {
            ".png" => "png",
            ".wav" => "wav",
            _ => throw new NotSupportedException("Only PNG and WAV assets are supported in this importer.")
        };

        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var buffer = new MemoryStream((int)Math.Min(Math.Min(stream.Length, maximumBytes), 1024 * 1024)))
        {
            var chunk = new byte[81_920];
            long total = 0;
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > maximumBytes || total > int.MaxValue)
                    throw new InvalidDataException($"Asset exceeds the configured {maximumBytes} byte import limit.");
                buffer.Write(chunk, 0, read);
            }
            if (total == 0) throw new InvalidDataException("Asset file is empty.");
            bytes = buffer.ToArray();
        }
        ValidateSignature(bytes, type, path);
        var baseName = Path.GetFileName(path);
        var uniqueName = MakeUniqueName(project, baseName);
        var asset = new LicAssetData
        {
            Name = uniqueName,
            Type = type,
            Base64 = Convert.ToBase64String(bytes)
        };
        project.Assets.Add(asset);
        return asset;
    }

    /// <summary>Renames an embedded asset and updates references in scene objects and prefab templates.</summary>
    public static void RenameAsset(LicProjectData project, LicAssetData asset, string newName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(asset);
        if (!project.Assets.Contains(asset)) throw new InvalidOperationException("Asset does not belong to this project.");

        var name = (newName ?? string.Empty).Trim();
        if (name.Length == 0 || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl))
            throw new ArgumentException("Asset name must be a valid file name without directory paths or invalid characters.", nameof(newName));

        var expectedExtension = string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase) ? ".png"
            : string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase) ? ".wav" : string.Empty;
        if (expectedExtension.Length == 0 || !string.Equals(Path.GetExtension(name), expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Asset name must use the {expectedExtension} extension.", nameof(newName));
        if (project.Assets.Any(x => !ReferenceEquals(x, asset) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Asset '{name}' already exists.");

        var oldName = asset.Name;
        asset.Name = name;
        foreach (var item in project.Scenes.SelectMany(scene => scene.Objects)
                     .Concat(project.Prefabs.Select(prefab => prefab.Object)))
        {
            if (string.Equals(item.Sprite, oldName, StringComparison.OrdinalIgnoreCase)) item.Sprite = name;
            foreach (var component in item.Components ?? new List<LicComponentData>())
            {
                if (string.Equals(component.Texture, oldName, StringComparison.OrdinalIgnoreCase)) component.Texture = name;
                if (string.Equals(component.Clip, oldName, StringComparison.OrdinalIgnoreCase)) component.Clip = name;
            }
        }
    }

    /// <summary>Removes an embedded asset and, by default, clears all scene/prefab references to it.</summary>
    public static int RemoveAsset(LicProjectData project, LicAssetData asset, bool clearReferences = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(asset);
        if (!project.Assets.Contains(asset)) throw new InvalidOperationException("Asset does not belong to this project.");
        var references = 0;
        foreach (var item in project.Scenes.SelectMany(scene => scene.Objects)
                     .Concat(project.Prefabs.Select(prefab => prefab.Object)))
        {
            if (string.Equals(item.Sprite, asset.Name, StringComparison.OrdinalIgnoreCase))
            {
                references++;
                if (clearReferences) item.Sprite = string.Empty;
            }
            foreach (var component in item.Components ?? new List<LicComponentData>())
            {
                if (string.Equals(component.Texture, asset.Name, StringComparison.OrdinalIgnoreCase))
                {
                    references++;
                    if (clearReferences) component.Texture = string.Empty;
                }
                if (string.Equals(component.Clip, asset.Name, StringComparison.OrdinalIgnoreCase))
                {
                    references++;
                    if (clearReferences) component.Clip = string.Empty;
                }
            }
        }
        project.Assets.Remove(asset);
        return references;
    }

    public static byte[] GetBytes(LicAssetData asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        try { return Convert.FromBase64String(asset.Base64 ?? string.Empty); }
        catch (FormatException ex) { throw new InvalidDataException($"Asset '{asset.Name}' contains invalid Base64 data.", ex); }
    }

    private static string MakeUniqueName(LicProjectData project, string fileName)
    {
        var candidate = fileName;
        var existing = project.Assets.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(candidate)) return candidate;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            candidate = $"{name}_{i}{extension}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static void ValidateSignature(byte[] bytes, string type, string path)
    {
        if (type == "png")
        {
            byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (bytes.Length < signature.Length || !bytes.AsSpan(0, signature.Length).SequenceEqual(signature))
                throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a valid PNG file.");
        }
        else if (type == "wav")
        {
            if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a valid WAV file.");
        }
    }
}
