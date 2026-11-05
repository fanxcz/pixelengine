using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelEngine.Graphics;

/// <summary>2D camera with safe zoom, world/screen conversion and optional world bounds.</summary>
public sealed class Camera2D
{
    private Vector2 _position;
    private Vector2 _boundsMin;
    private Vector2 _boundsMax;
    private float _zoom = 1f;
    private float _rotation;

    public Vector2 Position { get => _position; set => _position = Safe(value); }
    public float Zoom { get => _zoom; set => _zoom = float.IsFinite(value) ? Math.Clamp(value, .05f, 128f) : 1f; }
    public float Rotation { get => _rotation; set => _rotation = float.IsFinite(value) ? value : 0f; }
    public bool ClampToBounds { get; set; }
    public Vector2 BoundsMin { get => _boundsMin; set => _boundsMin = Safe(value); }
    public Vector2 BoundsMax { get => _boundsMax; set => _boundsMax = Safe(value); }

    public Matrix GetViewMatrix(Viewport viewport)
    {
        var center = new Vector2(Math.Max(0, viewport.Width) * .5f, Math.Max(0, viewport.Height) * .5f);
        var position = Safe(Position);
        var rotation = float.IsFinite(Rotation) ? Rotation : 0f;
        var zoom = SafeZoom;
        return Matrix.CreateTranslation(-position.X, -position.Y, 0f)
             * Matrix.CreateRotationZ(rotation)
             * Matrix.CreateScale(zoom, zoom, 1f)
             * Matrix.CreateTranslation(center.X, center.Y, 0f);
    }

    public float SafeZoom => _zoom;

    public Vector2 ScreenToWorld(Vector2 screenPosition, Viewport viewport)
    {
        if (!IsFinite(screenPosition))
            screenPosition = new Vector2(Math.Max(0, viewport.Width) * .5f, Math.Max(0, viewport.Height) * .5f);
        var matrix = GetViewMatrix(viewport);
        Matrix.Invert(ref matrix, out var inverse);
        var world = Vector2.Transform(screenPosition, inverse);
        return IsFinite(world) ? world : Position;
    }

    public Vector2 WorldToScreen(Vector2 worldPosition, Viewport viewport)
    {
        if (!IsFinite(worldPosition)) worldPosition = Position;
        var screen = Vector2.Transform(worldPosition, GetViewMatrix(viewport));
        return IsFinite(screen) ? screen : Vector2.Zero;
    }

    public void ClampPosition(Viewport viewport)
    {
        if (!ClampToBounds || !IsFinite(BoundsMin) || !IsFinite(BoundsMax)) return;
        var min = Vector2.Min(BoundsMin, BoundsMax);
        var max = Vector2.Max(BoundsMin, BoundsMax);
        var halfScreen = new Vector2(Math.Max(0, viewport.Width), Math.Max(0, viewport.Height)) * (.5f / SafeZoom);
        var rotation = float.IsFinite(Rotation) ? Rotation : 0f;
        var cos = MathF.Abs(MathF.Cos(rotation));
        var sin = MathF.Abs(MathF.Sin(rotation));
        // Clamp against the world-space AABB of the rotated camera viewport.
        var half = new Vector2(cos * halfScreen.X + sin * halfScreen.Y,
            sin * halfScreen.X + cos * halfScreen.Y);
        var low = min + half;
        var high = max - half;
        var safePosition = Safe(Position);
        Position = new Vector2(
            low.X > high.X ? (min.X + max.X) * .5f : Math.Clamp(safePosition.X, low.X, high.X),
            low.Y > high.Y ? (min.Y + max.Y) * .5f : Math.Clamp(safePosition.Y, low.Y, high.Y));
    }

    private static Vector2 Safe(Vector2 value)
        => new(float.IsFinite(value.X) ? value.X : 0f, float.IsFinite(value.Y) ? value.Y : 0f);
    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
