using Microsoft.Xna.Framework;
using PixelEngine.Scene;

namespace PixelEngine.Physics;

public enum Rigidbody2DType { Static, Kinematic, Dynamic }

/// <summary>2D body integrated by the Scene's fixed-step Physics2DWorld.</summary>
public sealed class Rigidbody2DComponent : Component
{
    private float _mass = 1f;
    private float _gravityScale = 1f;
    private float _linearDamping;
    private Vector2 _accumulatedForce;
    private Vector2 _velocity;

    public Rigidbody2DType BodyType { get; set; } = Rigidbody2DType.Dynamic;
    public Vector2 Velocity
    {
        get => _velocity;
        set => _velocity = IsFinite(value) ? value : Vector2.Zero;
    }
    public float Mass { get => _mass; set => _mass = float.IsFinite(value) ? Math.Clamp(value, .001f, 1_000_000f) : 1f; }
    public float GravityScale { get => _gravityScale; set => _gravityScale = float.IsFinite(value) ? Math.Clamp(value, -100f, 100f) : 1f; }
    public float LinearDamping { get => _linearDamping; set => _linearDamping = float.IsFinite(value) ? Math.Clamp(value, 0f, 1000f) : 0f; }
    public bool FreezePositionX { get; set; }
    public bool FreezePositionY { get; set; }
    public bool UseGravity { get; set; } = true;
    public bool IsDynamic => BodyType == Rigidbody2DType.Dynamic;

    public void AddForce(Vector2 force)
    {
        if (!IsDynamic || !IsFinite(force)) return;
        var accumulated = _accumulatedForce + force;
        if (IsFinite(accumulated)) _accumulatedForce = accumulated;
    }

    public void AddImpulse(Vector2 impulse)
    {
        if (!IsDynamic || !IsFinite(impulse)) return;
        ApplyVelocityDelta(impulse / Mass);
    }

    public void MovePosition(Vector2 position)
    {
        if (!IsFinite(position)) return;
        Transform.Position = position;
    }

    internal float InverseMass => IsDynamic ? 1f / Mass : 0f;

    internal void ApplyVelocityDelta(Vector2 delta)
    {
        if (!IsFinite(delta)) return;
        if (FreezePositionX) delta.X = 0f;
        if (FreezePositionY) delta.Y = 0f;
        Velocity += delta;
    }

    internal void ApplyPositionDelta(Vector2 delta)
    {
        if (!IsFinite(delta)) return;
        if (FreezePositionX) delta.X = 0f;
        if (FreezePositionY) delta.Y = 0f;
        var position = Transform.Position + delta;
        if (IsFinite(position)) Transform.Position = position;
    }

    internal void Integrate(float deltaTime, Vector2 gravity)
    {
        if (!IsDynamic || !Enabled || deltaTime <= 0f)
        {
            _accumulatedForce = Vector2.Zero;
            return;
        }
        var velocity = Velocity;
        if (UseGravity) velocity += gravity * (GravityScale * deltaTime);
        if (LinearDamping > 0f) velocity *= MathF.Exp(-LinearDamping * deltaTime);
        velocity += _accumulatedForce / Mass * deltaTime;
        _accumulatedForce = Vector2.Zero;
        // Constraints must be applied after gravity, damping and accumulated forces; otherwise
        // a force can reintroduce velocity on an axis that is meant to remain frozen.
        if (FreezePositionX) velocity.X = 0f;
        if (FreezePositionY) velocity.Y = 0f;
        Velocity = IsFinite(velocity) ? velocity : Vector2.Zero;
        var position = Transform.Position + Velocity * deltaTime;
        if (FreezePositionX) position.X = Transform.Position.X;
        if (FreezePositionY) position.Y = Transform.Position.Y;
        if (IsFinite(position)) Transform.Position = position;
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
