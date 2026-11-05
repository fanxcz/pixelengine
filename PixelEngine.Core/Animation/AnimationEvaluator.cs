using Microsoft.Xna.Framework;
using PixelEngine.Serialization;

namespace PixelEngine.Animation;

public readonly record struct AnimationPose(Vector2 Position, float Rotation, Vector2 Scale);

/// <summary>Deterministic, allocation-light transform keyframe evaluator.</summary>
public static class AnimationEvaluator
{
    /// <summary>Returns the index of the closest finite-time keyframe, or -1 when none is usable.</summary>
    /// <remarks>Used by the editor timeline; a linear scan avoids sorting and repeated LINQ allocations.</remarks>
    public static int FindNearestKeyframeIndex(IReadOnlyList<LicAnimationKeyframeData>? keyframes, float time)
    {
        if (keyframes is null || keyframes.Count == 0 || !float.IsFinite(time)) return -1;

        var bestIndex = -1;
        // Keep the distance in double: two finite float timestamps can differ by more than float.MaxValue.
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < keyframes.Count; i++)
        {
            var key = keyframes[i];
            if (key is null || !float.IsFinite(key.Time)) continue;
            var distance = Math.Abs((double)key.Time - time);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    public static AnimationPose Evaluate(LicAnimationClipData clip, float time, AnimationPose baseline)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Keyframes is null || clip.Keyframes.Count == 0) return baseline;

        var duration = float.IsFinite(clip.Duration) && clip.Duration > 0f ? clip.Duration : 0.0001f;
        if (!float.IsFinite(time)) time = 0f;
        var t = clip.Loop ? PositiveModulo(time, duration) : Math.Clamp(time, 0f, duration);

        // Resolve every channel independently. A blank keyframe or a keyframe that only
        // animates X must not pull Y/rotation/scale toward the live baseline.
        return new AnimationPose(
            new Vector2(
                EvaluateChannel(clip.Keyframes, t, duration, baseline.Position.X, static key => key.X),
                EvaluateChannel(clip.Keyframes, t, duration, baseline.Position.Y, static key => key.Y)),
            EvaluateAngleChannel(clip.Keyframes, t, duration, baseline.Rotation),
            new Vector2(
                Math.Clamp(EvaluateChannel(clip.Keyframes, t, duration, baseline.Scale.X, static key => key.ScaleX), -100f, 100f),
                Math.Clamp(EvaluateChannel(clip.Keyframes, t, duration, baseline.Scale.Y, static key => key.ScaleY), -100f, 100f)));
    }

    private static float EvaluateChannel(IReadOnlyList<LicAnimationKeyframeData> keys, float time,
        float duration, float baseline, Func<LicAnimationKeyframeData, float?> selector)
    {
        LicAnimationKeyframeData? before = null;
        LicAnimationKeyframeData? after = null;
        var beforeTime = float.NegativeInfinity;
        var afterTime = float.PositiveInfinity;

        foreach (var key in keys)
        {
            if (key is null || !float.IsFinite(key.Time)) continue;
            var value = selector(key);
            if (!value.HasValue || !float.IsFinite(value.Value)) continue;
            var keyTime = Math.Clamp(key.Time, 0f, duration);
            // >= and <= intentionally make the last serialized key win when timestamps repeat.
            if (keyTime <= time && keyTime >= beforeTime) { beforeTime = keyTime; before = key; }
            if (keyTime >= time && keyTime <= afterTime) { afterTime = keyTime; after = key; }
        }

        if (before is null && after is null) return baseline;
        // A channel is not animated before its first authored key. Keep the live
        // baseline untouched rather than blending toward a future key; this matters
        // for partially keyed clips layered over physics/player movement.
        if (before is null) return baseline;
        if (after is null) return selector(before)!.Value;
        var from = selector(before)!.Value;
        var to = selector(after)!.Value;
        if (ReferenceEquals(before, after) || afterTime - beforeTime <= 0.0001f) return to;
        var amount = Math.Clamp((time - beforeTime) / (afterTime - beforeTime), 0f, 1f);
        return from + (to - from) * amount;
    }

    private static float EvaluateAngleChannel(IReadOnlyList<LicAnimationKeyframeData> keys, float time,
        float duration, float baseline)
    {
        LicAnimationKeyframeData? before = null;
        LicAnimationKeyframeData? after = null;
        var beforeTime = float.NegativeInfinity;
        var afterTime = float.PositiveInfinity;

        foreach (var key in keys)
        {
            if (key is null || !float.IsFinite(key.Time) || !key.Rotation.HasValue || !float.IsFinite(key.Rotation.Value)) continue;
            var keyTime = Math.Clamp(key.Time, 0f, duration);
            if (keyTime <= time && keyTime >= beforeTime) { beforeTime = keyTime; before = key; }
            if (keyTime >= time && keyTime <= afterTime) { afterTime = keyTime; after = key; }
        }

        if (before is null && after is null) return baseline;
        // Match scalar channels: rotation remains live until its first authored key.
        if (before is null) return baseline;
        if (after is null) return NormalizeAngle(before.Rotation!.Value);
        var from = NormalizeAngle(before.Rotation!.Value);
        var to = NormalizeAngle(after.Rotation!.Value);
        if (ReferenceEquals(before, after) || afterTime - beforeTime <= 0.0001f) return to;
        var amount = Math.Clamp((time - beforeTime) / (afterTime - beforeTime), 0f, 1f);
        return LerpAngle(from, to, amount);
    }

    private static float NormalizeAngle(float value) => PositiveModulo(value, 360f);

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static float LerpAngle(float from, float to, float amount)
    {
        if (!float.IsFinite(from)) from = 0f;
        if (!float.IsFinite(to)) to = from;
        var delta = ((to - from + 540f) % 360f) - 180f;
        return from + delta * amount;
    }
}
