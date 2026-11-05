using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelEngine.Rendering;
using PixelEngine.Scene;

namespace PixelEngine.Graphics;

/// <summary>Textured quad with named sorting layers and per-layer order.</summary>
public sealed class SpriteRenderer : Component
{
    public Texture2D? Texture { get; set; }
    public Color Tint { get; set; } = Color.White;
    public Rectangle? SourceRectangle { get; set; }
    public Vector2 Size { get; set; } = new(32, 32);
    public string SortingLayer { get; set; } = "Default";
    public int SortingOrder { get; set; }
    /// <summary>Optional explicit SpriteBatch depth. NaN uses SortingLayer/SortingOrder.</summary>
    public float LayerDepth { get; set; } = float.NaN;

    /// <summary>Assigns this renderer to a named region of a built SpriteAtlas.</summary>
    public void SetAtlasRegion(SpriteAtlas atlas, string regionName)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        var region = atlas.GetRegion(regionName);
        Texture = atlas.Texture;
        SourceRectangle = region.SourceRectangle;
    }

    public override void Draw(RenderContext context)
    {
        if (Texture is null) return;
        var source = SourceRectangle.HasValue
            ? Rectangle.Intersect(SourceRectangle.Value, new Rectangle(0, 0, Texture.Width, Texture.Height))
            : new Rectangle(0, 0, Texture.Width, Texture.Height);
        if (source.Width <= 0 || source.Height <= 0) return;
        var transform = Transform;
        var sourceSize = new Vector2(source.Width, source.Height);
        var origin = transform.Origin == Vector2.Zero ? sourceSize * .5f : transform.Origin;
        var scale = transform.Scale * new Vector2(Size.X / Math.Max(1, sourceSize.X), Size.Y / Math.Max(1, sourceSize.Y));
        var depth = float.IsFinite(LayerDepth) ? Math.Clamp(LayerDepth, 0f, 1f)
            : SortingLayerRegistry.GetDepth(SortingLayer, SortingOrder);
        context.SpriteBatch.Draw(Texture, transform.Position, source, Tint,
            transform.Rotation, origin, scale, SpriteEffects.None, depth);
    }
}
