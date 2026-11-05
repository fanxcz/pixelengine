using Microsoft.Xna.Framework;

namespace PixelEngine.Scene;

/// <summary>2D transform. Non-finite values are normalized at assignment boundaries.</summary>
public sealed class Transform
{
    private Vector2 _position;
    private float _rotation;
    private Vector2 _scale = Vector2.One;
    private Vector2 _origin;

    public Vector2 Position
    {
        get => _position;
        set => _position = Sanitize(value, Vector2.Zero);
    }

    public float Rotation
    {
        get => _rotation;
        set => _rotation = float.IsFinite(value) ? value : 0f;
    }

    public Vector2 Scale
    {
        get => _scale;
        set => _scale = Sanitize(value, Vector2.One);
    }

    public Vector2 Origin
    {
        get => _origin;
        set => _origin = Sanitize(value, Vector2.Zero);
    }

    public Vector2 Forward => new(MathF.Cos(_rotation), MathF.Sin(_rotation));

    private static Vector2 Sanitize(Vector2 value, Vector2 fallback)
        => new(float.IsFinite(value.X) ? value.X : fallback.X,
            float.IsFinite(value.Y) ? value.Y : fallback.Y);
}
