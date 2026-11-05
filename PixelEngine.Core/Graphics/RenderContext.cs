using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using PixelEngine.Graphics;

namespace PixelEngine.Rendering;

public sealed class RenderContext
{
    public SpriteBatch SpriteBatch { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public Camera2D Camera { get; }
    public Texture2D WhitePixel { get; }
    private readonly List<LightDrawCommand> _lights = new(16);
    private readonly List<ShadowSegment> _shadowSegments = new(64);
    public IReadOnlyList<LightDrawCommand> Lights => _lights;
    public IReadOnlyList<ShadowSegment> ShadowSegments => _shadowSegments;

    /// <summary>Clears per-frame draw commands while retaining the list capacity.</summary>
    public void ResetFrame()
    {
        _lights.Clear();
        _shadowSegments.Clear();
    }

    public void EnqueueLight(Vector2 position, float radius, Color color, float intensity, float falloff = 2f)
    {
        if (_lights.Count >= 16) return;
        _lights.Add(new LightDrawCommand(position, radius, color, intensity, falloff));
    }

    public void EnqueueShadowSegment(Vector2 start, Vector2 end)
    {
        if (_shadowSegments.Count >= 4096 || !float.IsFinite(start.X) || !float.IsFinite(start.Y) ||
            !float.IsFinite(end.X) || !float.IsFinite(end.Y) || Vector2.DistanceSquared(start, end) < .0001f) return;
        _shadowSegments.Add(new ShadowSegment(start, end));
    }
    internal RenderContext(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, Camera2D camera, Texture2D whitePixel)
        => (SpriteBatch, GraphicsDevice, Camera, WhitePixel) = (spriteBatch, graphicsDevice, camera, whitePixel);
}
