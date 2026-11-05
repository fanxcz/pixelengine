using System.Collections.ObjectModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelEngine.Graphics;

public readonly record struct SpriteAtlasInput(string Name, Texture2D Texture);
public readonly record struct SpriteAtlasRegion(string Name, Rectangle SourceRectangle);

/// <summary>Owns a packed texture atlas and its named source rectangles.</summary>
public sealed class SpriteAtlas : IDisposable
{
    private readonly Dictionary<string, SpriteAtlasRegion> _regions;
    private readonly ReadOnlyDictionary<string, SpriteAtlasRegion> _regionsView;
    private bool _disposed;

    public Texture2D Texture { get; }
    /// <summary>Read-only region map; callers cannot corrupt atlas source rectangles.</summary>
    public IReadOnlyDictionary<string, SpriteAtlasRegion> Regions => _regionsView;

    internal SpriteAtlas(Texture2D texture, Dictionary<string, SpriteAtlasRegion> regions)
    {
        Texture = texture;
        _regions = regions;
        _regionsView = new ReadOnlyDictionary<string, SpriteAtlasRegion>(_regions);
    }

    public bool TryGetRegion(string name, out SpriteAtlasRegion region)
        => _regions.TryGetValue(name ?? string.Empty, out region);

    public SpriteAtlasRegion GetRegion(string name)
        => _regions.TryGetValue(name ?? string.Empty, out var region)
            ? region : throw new KeyNotFoundException($"Sprite atlas region '{name}' was not found.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Texture.Dispose();
    }
}

/// <summary>
/// Builds deterministic, non-rotated shelf-packed atlases from runtime textures.
/// Input textures remain owned by their original asset store; only the returned atlas is owned here.
/// </summary>
public static class SpriteAtlasBuilder
{
    private sealed record PackedItem(SpriteAtlasInput Input, int Width, int Height, int X, int Y);

    public static SpriteAtlas Build(GraphicsDevice device, IReadOnlyList<SpriteAtlasInput> sprites,
        int maximumSize = 2048, int padding = 2)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(sprites);
        if (sprites.Count is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(sprites), "Atlas inputs must contain 1–512 sprites.");
        if (maximumSize is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumSize), "Atlas size must be between 64 and 4096.");
        if (padding is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(padding));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<SpriteAtlasInput>(sprites.Count);
        foreach (var item in sprites)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) throw new ArgumentException("Every sprite needs a non-empty unique name.", nameof(sprites));
            if (item.Texture is null) throw new ArgumentException($"Sprite '{item.Name}' has no texture.", nameof(sprites));
            if (!names.Add(item.Name)) throw new ArgumentException($"Duplicate sprite atlas name '{item.Name}'.", nameof(sprites));
            if (item.Texture.Width <= 0 || item.Texture.Height <= 0 ||
                item.Texture.Width + padding * 2 > maximumSize || item.Texture.Height + padding * 2 > maximumSize)
                throw new InvalidOperationException($"Sprite '{item.Name}' is too large for a {maximumSize}x{maximumSize} atlas.");
            ordered.Add(item);
        }

        var sorted = ordered.OrderByDescending(x => x.Texture.Height)
            .ThenByDescending(x => x.Texture.Width)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var packed = TryPack(sorted, maximumSize, padding)
            ?? throw new InvalidOperationException($"The {sprites.Count} sprites do not fit into a {maximumSize}x{maximumSize} atlas.");
        // MonoGame supports non-power-of-two textures; use the exact occupied rectangle to avoid wasting memory.
        var atlasWidth = Math.Max(1, packed.Max(x => x.X + x.Width + padding));
        var atlasHeight = Math.Max(1, packed.Max(x => x.Y + x.Height + padding));
        if (atlasWidth > maximumSize || atlasHeight > maximumSize)
            throw new InvalidOperationException("Packed atlas dimensions exceed the configured maximum.");

        var atlasPixels = new Color[checked(atlasWidth * atlasHeight)];
        var regions = new Dictionary<string, SpriteAtlasRegion>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in packed)
        {
            var source = new Color[checked(item.Width * item.Height)];
            try { item.Input.Texture.GetData(source); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not read sprite '{item.Input.Name}' into an RGBA atlas. Ensure it uses a readable Color texture format.", ex);
            }
            // Extrude edge texels into the transparent gutter to avoid linear-sampling seams.
            for (var py = -padding; py < item.Height + padding; py++)
            for (var px = -padding; px < item.Width + padding; px++)
            {
                var sourceX = Math.Clamp(px, 0, item.Width - 1);
                var sourceY = Math.Clamp(py, 0, item.Height - 1);
                atlasPixels[(item.Y + py) * atlasWidth + item.X + px] = source[sourceY * item.Width + sourceX];
            }
            regions.Add(item.Input.Name, new SpriteAtlasRegion(item.Input.Name,
                new Rectangle(item.X, item.Y, item.Width, item.Height)));
        }

        var atlasTexture = new Texture2D(device, atlasWidth, atlasHeight, false, SurfaceFormat.Color);
        try
        {
            atlasTexture.SetData(atlasPixels);
            return new SpriteAtlas(atlasTexture, regions);
        }
        catch
        {
            atlasTexture.Dispose();
            throw;
        }
    }

    private static List<PackedItem>? TryPack(IReadOnlyList<SpriteAtlasInput> items, int maximumSize, int padding)
    {
        var minimumWidth = items.Max(x => x.Texture.Width + padding * 2);
        var totalArea = items.Sum(x => (long)(x.Texture.Width + padding * 2) * (x.Texture.Height + padding * 2));
        var startWidth = Math.Max(NextPowerOfTwo(minimumWidth), NextPowerOfTwo((int)Math.Ceiling(Math.Sqrt(totalArea))));
        var atlasWidth = Math.Min(startWidth, maximumSize);
        while (true)
        {
            var result = new List<PackedItem>(items.Count);
            var x = padding;
            var y = padding;
            var rowHeight = 0;
            var failed = false;
            foreach (var item in items)
            {
                var width = item.Texture.Width;
                var height = item.Texture.Height;
                if (x + width + padding > atlasWidth)
                {
                    x = padding;
                    y += rowHeight + padding * 2;
                    rowHeight = 0;
                }
                if (x + width + padding > atlasWidth || y + height + padding > maximumSize)
                {
                    failed = true;
                    break;
                }
                result.Add(new PackedItem(item, width, height, x, y));
                // Both neighboring sprites own a gutter, so reserve padding on both sides.
                x += width + padding * 2;
                rowHeight = Math.Max(rowHeight, height);
            }
            if (!failed) return result;
            if (atlasWidth == maximumSize) break;
            atlasWidth = Math.Min(maximumSize, atlasWidth * 2);
        }
        return null;
    }

    private static int NextPowerOfTwo(int value)
    {
        value = Math.Max(1, value);
        var result = 1;
        while (result < value && result < 8192) result <<= 1;
        return result;
    }
}
