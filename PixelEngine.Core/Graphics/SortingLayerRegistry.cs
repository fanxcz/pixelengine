namespace PixelEngine.Graphics;

/// <summary>Named 2D draw layers. Higher layer indices are drawn in front.</summary>
public static class SortingLayerRegistry
{
    public const int MaximumLayers = 256;
    private const float LayerStep = .90f / MaximumLayers;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> Layers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Background"] = 0,
        ["Default"] = 1,
        ["Foreground"] = 2,
        ["UI"] = 3
    };

    public static IReadOnlyDictionary<string, int> Snapshot
    {
        get { lock (Sync) return new Dictionary<string, int>(Layers, StringComparer.OrdinalIgnoreCase); }
    }

    public static int Register(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Sorting layer names cannot be empty or whitespace.", nameof(name));
        lock (Sync)
        {
            if (Layers.TryGetValue(name, out var existing)) return existing;
            if (Layers.Count >= MaximumLayers)
                throw new InvalidOperationException($"A project may register at most {MaximumLayers} sorting layers.");
            var index = Layers.Count == 0 ? 0 : Layers.Values.Max() + 1;
            Layers.Add(name, index);
            return index;
        }
    }

    /// <summary>Returns a SpriteBatch BackToFront depth; smaller values are nearer.</summary>
    public static float GetDepth(string? layer, int order)
    {
        var index = 1;
        if (!string.IsNullOrWhiteSpace(layer))
        {
            try { index = Register(layer.Trim()); }
            catch (InvalidOperationException)
            {
                // Keep rendering alive for projects that exceed the registry cap; Diagnostics
                // reports the configuration problem and overflow layers share the last band.
                index = MaximumLayers - 1;
            }
        }
        // The spacing is fixed: registering another layer must never reorder existing
        // objects that already computed a depth earlier in this frame or previous frames.
        // Keep the order range strictly inside its layer band.
        var orderOffset = Math.Clamp(order, -30000, 30000) / 30000f * LayerStep * .4f;
        return Math.Clamp(.98f - index * LayerStep - orderOffset, .02f, .98f);
    }
}
