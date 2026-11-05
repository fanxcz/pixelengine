using Microsoft.Xna.Framework;
using PixelEngine.Core;
using PixelEngine.Scene;

namespace PixelEngine.Graphics;

/// <summary>Follows a target with frame-rate-independent exponential smoothing.</summary>
public sealed class CameraFollowComponent : Component
{
    private readonly Camera2D _camera;

    public GameObject? Target { get; set; }
    public Vector2 Offset { get; set; }
    public float SmoothSpeed { get; set; } = 8f;
    public Vector2 DeadZone { get; set; }

    public CameraFollowComponent(Camera2D camera, GameObject? target = null)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        Target = target;
    }

    public override void Update(EngineTime time)
    {
        if (Target is null || !Target.Active || !float.IsFinite(time.DeltaTime) || time.DeltaTime <= 0f) return;

        var targetPosition = Target.Transform.Position;
        if (!IsFinite(_camera.Position)) _camera.Position = Vector2.Zero;
        if (!IsFinite(targetPosition)) return;
        var cameraPosition = _camera.Position;
        var offset = new Vector2(
            float.IsFinite(Offset.X) ? Offset.X : 0f,
            float.IsFinite(Offset.Y) ? Offset.Y : 0f);
        var desired = targetPosition + offset;
        if (!IsFinite(desired)) return;
        var delta = desired - cameraPosition;
        if (!IsFinite(delta)) return;
        var deadZone = new Vector2(
            float.IsFinite(DeadZone.X) ? Math.Max(0f, DeadZone.X) : 0f,
            float.IsFinite(DeadZone.Y) ? Math.Max(0f, DeadZone.Y) : 0f);
        if (MathF.Abs(delta.X) <= deadZone.X) delta.X = 0f;
        else delta.X -= MathF.Sign(delta.X) * deadZone.X;
        if (MathF.Abs(delta.Y) <= deadZone.Y) delta.Y = 0f;
        else delta.Y -= MathF.Sign(delta.Y) * deadZone.Y;

        var speed = float.IsFinite(SmoothSpeed) ? Math.Clamp(SmoothSpeed, 0f, 100f) : 0f;
        // A speed of zero explicitly means no smoothing (snap to the dead-zone edge).
        var alpha = speed <= 0f ? 1f : 1f - MathF.Exp(-speed * time.DeltaTime);
        var nextPosition = cameraPosition + delta * alpha;
        if (IsFinite(nextPosition)) _camera.Position = nextPosition;
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
