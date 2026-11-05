using Microsoft.Xna.Framework;
using PixelEngine.Rendering;
using PixelEngine.Scene;

namespace PixelEngine.Graphics;

/// <summary>2D point light with configurable falloff and deterministic animated flicker.</summary>
public sealed class Light2DComponent : Component
{
    private float _currentIntensity = .65f;
    public float Radius { get; set; } = 150f;
    public float Intensity { get; set; } = .65f;
    public Color LightColor { get; set; } = new(255, 204, 136);
    public float Falloff { get; set; } = 2f;
    public float FlickerAmount { get; set; }
    public float FlickerSpeed { get; set; } = 8f;

    public override void Update(PixelEngine.Core.EngineTime time)
    {
        var totalTime = time.TotalTime;
        var flickerAmount = float.IsFinite(FlickerAmount) ? Math.Clamp(FlickerAmount, 0f, .9f) : 0f;
        var flickerSpeed = float.IsFinite(FlickerSpeed) ? Math.Clamp(FlickerSpeed, 0f, 60f) : 0f;
        var t = double.IsFinite(totalTime) ? (float)Math.Clamp(totalTime, -1_000_000d, 1_000_000d) : 0f;
        var wave = MathF.Sin(t * flickerSpeed * 6.28318f) * .5f + MathF.Sin(t * flickerSpeed * 2.399f) * .25f;
        var flicker = 1f - flickerAmount * Math.Clamp((wave + .75f) / 1.5f, 0f, 1f);
        _currentIntensity = Math.Clamp((float.IsFinite(Intensity) ? Intensity : 0f) * flicker, 0f, 1f);
    }

    public override void Draw(RenderContext context)
    {
        if (!Enabled || !float.IsFinite(Radius) || Radius <= 0f ||
            !float.IsFinite(Intensity) || Intensity <= 0f ||
            !float.IsFinite(Transform.Position.X) || !float.IsFinite(Transform.Position.Y)) return;
        var falloff = float.IsFinite(Falloff) ? Math.Clamp(Falloff, .25f, 8f) : 2f;
        context.EnqueueLight(Transform.Position, Math.Clamp(Radius, 1f, 4096f), LightColor,
            _currentIntensity, falloff);
    }
}
