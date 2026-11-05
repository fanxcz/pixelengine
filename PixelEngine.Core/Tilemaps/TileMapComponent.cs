using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelEngine.Graphics;
using PixelEngine.Rendering;
using PixelEngine.Scene;
using PixelEngine.Serialization;

namespace PixelEngine.Tilemaps;

/// <summary>Palette tilemap renderer. Tile value zero is empty; positive values index the palette.</summary>
public sealed class TileMapComponent : Component
{
    private string[] _cachedPaletteStrings = Array.Empty<string>();
    private Color[] _cachedPaletteColors = Array.Empty<Color>();

    public LicTileMapData Data { get; }

    public TileMapComponent(LicTileMapData data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        // Keep runtime work bounded even if the project was not opened in the Diagnostics tab.
        Data.Width = Math.Clamp(Data.Width, 1, 512);
        Data.Height = Math.Clamp(Data.Height, 1, 512);
        Data.TileSize = Math.Clamp(Data.TileSize, 1, 256);
        Data.EnsureCellStorage();
    }

    public override void Draw(RenderContext context)
    {
        if (!float.IsFinite(Transform.Position.X) || !float.IsFinite(Transform.Position.Y)) return;
        var tileSize = Math.Clamp(Data.TileSize, 1, 256);
        var cells = Data.Cells;
        EnsurePaletteCache();

        // Convert the four viewport corners back to world space and draw only the
        // intersecting tile range. This keeps large maps practical at runtime.
        var viewport = context.GraphicsDevice.Viewport;
        var view = context.Camera.GetViewMatrix(viewport);
        Matrix.Invert(ref view, out var inverse);
        var c0 = Vector2.Transform(Vector2.Zero, inverse);
        var c1 = Vector2.Transform(new Vector2(viewport.Width, 0), inverse);
        var c2 = Vector2.Transform(new Vector2(0, viewport.Height), inverse);
        var c3 = Vector2.Transform(new Vector2(viewport.Width, viewport.Height), inverse);
        var minX = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
        var maxX = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
        var minY = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
        var maxY = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));
        var localMinX = (minX - Transform.Position.X) / tileSize;
        var localMaxX = (maxX - Transform.Position.X) / tileSize;
        var localMinY = (minY - Transform.Position.Y) / tileSize;
        var localMaxY = (maxY - Transform.Position.Y) / tileSize;
        var startX = SafeTileIndex(localMinX, Data.Width, useCeiling: false);
        var endX = SafeTileIndex(localMaxX, Data.Width, useCeiling: true);
        var startY = SafeTileIndex(localMinY, Data.Height, useCeiling: false);
        var endY = SafeTileIndex(localMaxY, Data.Height, useCeiling: true);

        for (var y = startY; y < endY; y++)
        for (var x = startX; x < endX; x++)
        {
            var index = y * Data.Width + x;
            if (index >= cells.Count) continue;
            var paletteIndex = cells[index];
            if (paletteIndex <= 0 || paletteIndex >= Data.Palette.Count) continue;
            var color = _cachedPaletteColors[paletteIndex];
            var position = Transform.Position + new Vector2(x * tileSize + tileSize * .5f, y * tileSize + tileSize * .5f);
            context.SpriteBatch.Draw(context.WhitePixel, position, null, color, 0f,
                new Vector2(.5f), new Vector2(tileSize), SpriteEffects.None, .99f);
        }
    }

    private void EnsurePaletteCache()
    {
        var palette = Data.Palette;
        var unchanged = palette.Count == _cachedPaletteStrings.Length;
        if (unchanged)
        {
            for (var i = 0; i < palette.Count; i++)
            {
                if (!string.Equals(palette[i], _cachedPaletteStrings[i], StringComparison.Ordinal))
                {
                    unchanged = false;
                    break;
                }
            }
        }
        if (unchanged) return;

        _cachedPaletteStrings = palette.ToArray();
        _cachedPaletteColors = new Color[_cachedPaletteStrings.Length];
        for (var i = 0; i < _cachedPaletteStrings.Length; i++)
            _cachedPaletteColors[i] = ColorParser.Parse(_cachedPaletteStrings[i], Color.White);
    }

    private static int SafeTileIndex(float coordinate, int limit, bool useCeiling)
    {
        if (!float.IsFinite(coordinate)) return coordinate < 0f ? 0 : limit;
        var value = useCeiling ? MathF.Ceiling(coordinate) + 1f : MathF.Floor(coordinate) - 1f;
        if (value <= 0f) return 0;
        if (value >= limit) return limit;
        return (int)value;
    }
}
