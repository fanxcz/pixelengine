using System.Threading;
using Microsoft.Xna.Framework;
using PixelEngine.Scene;
using PixelEngine.Rendering;

namespace PixelEngine.Physics;

internal readonly record struct PhysicsShape(bool IsCircle, Vector2 Center, Vector2 HalfSize, float Radius, float Rotation = 0f)
{
    public Vector2 AxisX => new(MathF.Cos(Rotation), MathF.Sin(Rotation));
    public Vector2 AxisY => new(-MathF.Sin(Rotation), MathF.Cos(Rotation));
    public Vector2 Extents
    {
        get
        {
            if (IsCircle) return new Vector2(Radius);
            var c = MathF.Abs(MathF.Cos(Rotation));
            var s = MathF.Abs(MathF.Sin(Rotation));
            return new Vector2(c * HalfSize.X + s * HalfSize.Y, s * HalfSize.X + c * HalfSize.Y);
        }
    }
    public Vector2 Min => Center - Extents;
    public Vector2 Max => Center + Extents;
}

/// <summary>Base for 2D colliders. Layer is a 0..31 index; CollisionMask selects layers to test.</summary>
public abstract class Collider2DComponent : Component
{
    private static int _nextId;
    protected Collider2DComponent() => InstanceId = Interlocked.Increment(ref _nextId);

    internal int InstanceId { get; }
    public bool IsTrigger { get; set; }
    public int Layer { get; set; }
    public uint CollisionMask { get; set; } = uint.MaxValue;
    public float Friction { get; set; } = .4f;
    public float Restitution { get; set; }
    /// <summary>When true, this collider contributes edges to the 2D light shadow map.</summary>
    public bool CastsShadows { get; set; }
    public bool IsStatic => GameObject.GetComponent<Rigidbody2DComponent>()?.BodyType == Rigidbody2DType.Static
        || GameObject.GetComponent<Rigidbody2DComponent>() is null;

    internal abstract PhysicsShape GetShape();

    public override void Draw(RenderContext context)
    {
        if (!Enabled || !CastsShadows) return;
        var shape = GetShape();
        if (shape.IsCircle)
        {
            const int segments = 20;
            var previous = shape.Center + new Vector2(shape.Radius, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = MathF.Tau * i / segments;
                var next = shape.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * shape.Radius;
                context.EnqueueShadowSegment(previous, next);
                previous = next;
            }
            return;
        }
        var axisX = shape.AxisX * shape.HalfSize.X;
        var axisY = shape.AxisY * shape.HalfSize.Y;
        var a = shape.Center - axisX - axisY;
        var b = shape.Center + axisX - axisY;
        var c = shape.Center + axisX + axisY;
        var d = shape.Center - axisX + axisY;
        context.EnqueueShadowSegment(a, b);
        context.EnqueueShadowSegment(b, c);
        context.EnqueueShadowSegment(c, d);
        context.EnqueueShadowSegment(d, a);
    }

    internal bool CanCollideWith(Collider2DComponent other)
    {
        if (ReferenceEquals(GameObject, other.GameObject) || !other.Enabled || !other.GameObject.Active) return false;
        var ownLayer = 1u << Math.Clamp(Layer, 0, 31);
        var otherLayer = 1u << Math.Clamp(other.Layer, 0, 31);
        return (CollisionMask & otherLayer) != 0 && (other.CollisionMask & ownLayer) != 0;
    }

    protected Vector2 GetWorldOffset(Vector2 offset)
    {
        var scale = Transform.Scale;
        var scaled = new Vector2(
            float.IsFinite(offset.X) && float.IsFinite(scale.X) ? offset.X * scale.X : 0f,
            float.IsFinite(offset.Y) && float.IsFinite(scale.Y) ? offset.Y * scale.Y : 0f);
        var rotation = float.IsFinite(Transform.Rotation) ? Transform.Rotation : 0f;
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        return new Vector2(scaled.X * cos - scaled.Y * sin, scaled.X * sin + scaled.Y * cos);
    }
}

/// <summary>Rotatable oriented-box collider. Transform scale is applied to Size and Offset.</summary>
public sealed class BoxCollider2DComponent : Collider2DComponent
{
    public Vector2 Size { get; set; } = new(32f, 32f);
    public Vector2 Offset { get; set; }

    internal override PhysicsShape GetShape()
    {
        var scale = Transform.Scale;
        var size = new Vector2(
            ScaleDimension(Size.X, scale.X),
            ScaleDimension(Size.Y, scale.Y));
        var rotation = float.IsFinite(Transform.Rotation) ? Transform.Rotation : 0f;
        return new PhysicsShape(false, Transform.Position + GetWorldOffset(Offset), size * .5f, 0f, rotation);
    }

    private static float ScaleDimension(float size, float scale)
    {
        if (!float.IsFinite(size) || !float.IsFinite(scale)) return .001f;
        return Math.Clamp(MathF.Abs(size * scale), .001f, 1_000_000f);
    }
}

public sealed class CircleCollider2DComponent : Collider2DComponent
{
    public float Radius { get; set; } = 8f;
    public Vector2 Offset { get; set; }

    internal override PhysicsShape GetShape()
    {
        var scale = Transform.Scale;
        var maximumScale = Math.Max(float.IsFinite(scale.X) ? MathF.Abs(scale.X) : 1f,
            float.IsFinite(scale.Y) ? MathF.Abs(scale.Y) : 1f);
        var radius = float.IsFinite(Radius) ? Math.Clamp(Radius * maximumScale, .001f, 1_000_000f) : 8f;
        return new PhysicsShape(true, Transform.Position + GetWorldOffset(Offset), new Vector2(radius), radius);
    }
}
