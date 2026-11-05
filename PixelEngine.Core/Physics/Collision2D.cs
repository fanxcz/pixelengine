using Microsoft.Xna.Framework;

namespace PixelEngine.Physics;

/// <summary>Contact information. Normal points from ColliderA toward ColliderB.</summary>
public readonly record struct Collision2D(
    Collider2DComponent ColliderA,
    Collider2DComponent ColliderB,
    Vector2 Normal,
    float Penetration,
    Vector2 RelativeVelocity)
{
    public Collider2DComponent Other(Collider2DComponent collider)
        => ReferenceEquals(collider, ColliderA) ? ColliderB : ColliderA;

    internal Collision2D Reversed()
        => new(ColliderB, ColliderA, -Normal, Penetration, -RelativeVelocity);
}
