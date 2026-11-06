namespace PixelEngine.Editor;

internal static class EditorLaunchPaths
{
    public static string? ResolveInitialProject(
        IReadOnlyList<string> arguments,
        string? repositoryRoot,
        string applicationDirectory,
        string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var requested = arguments.FirstOrDefault(value =>
            value.EndsWith(".lic", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var fullPath = Path.GetFullPath(requested, currentDirectory);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("The requested LIC project does not exist.", fullPath);
            return fullPath;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
            candidates.Add(Path.Combine(repositoryRoot, "Lic", "demo.lic"));
        candidates.Add(Path.Combine(applicationDirectory, "Lic", "demo.lic"));
        candidates.Add(Path.Combine(currentDirectory, "Lic", "demo.lic"));

        var demoPath = candidates.FirstOrDefault(File.Exists);
        return demoPath is null ? null : Path.GetFullPath(demoPath);
    }

    public static string? FindBundledPlayer(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        var names = OperatingSystem.IsWindows()
            ? new[] { "PixelEngine.Sandbox.exe", "PixelEngine.Sandbox" }
            : new[] { "PixelEngine.Sandbox", "PixelEngine.Sandbox.exe" };

        // Current releases keep the complete, non-single-file MonoGame publish output in
        // Player/. Older preview archives placed a single-file executable beside Studio.
        foreach (var name in names)
        {
            var legacyCandidate = Path.Combine(applicationDirectory, name);
            if (File.Exists(legacyCandidate)) return legacyCandidate;

            var packagedCandidate = Path.Combine(applicationDirectory, "Player", name);
            if (File.Exists(packagedCandidate)) return packagedCandidate;
        }

        return null;
    }
}
