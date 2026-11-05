using PixelEngine.Input;

namespace PixelEngine.Core;

/// <summary>Frame timing and input snapshot exposed to game components.</summary>
public sealed class EngineTime
{
    public const float MaximumDeltaSeconds = .25f;

    /// <summary>Normalizes elapsed time to a finite range, ignoring negative or invalid clock values.</summary>
    public static float SanitizeDeltaTime(double elapsedSeconds)
        => (float)Math.Clamp(double.IsFinite(elapsedSeconds) ? elapsedSeconds : 0d, 0d, MaximumDeltaSeconds);

    /// <summary>Normalizes the monotonic game clock, preserving the last valid value after a bad sample.</summary>
    public static double SanitizeTotalTime(double totalSeconds, double fallback = 0d)
    {
        var previous = double.IsFinite(fallback) && fallback >= 0d ? fallback : 0d;
        return double.IsFinite(totalSeconds) && totalSeconds >= 0d
            ? Math.Max(previous, totalSeconds)
            : previous;
    }

    public float DeltaTime { get; internal set; }
    public float FixedDeltaTime { get; internal set; }
    public double TotalTime { get; internal set; }
    public long Frame { get; internal set; }
    public InputState Input { get; internal set; } = null!;
}
