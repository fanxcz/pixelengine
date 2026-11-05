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
        // animates X must not pull Y/rotation/scale toward the live baseline, and a blank
        // keyframe never acts as a held step that pins a channel to zero.
        var x = EvaluateChannel(clip.Keyframes, t, duration, baseline.Position.X, static key => key.X, out var xMatches);
        var y = EvaluateChannel(clip.Keyframes, t, duration, baseline.Position.Y, static key => key.Y, out var yMatches);
        // Position is a single 2D channel for authored motion: a key that animates only X
        // still publishes the resolved Y so a lerp cannot snap Y toward the interpolated
        // value of a neighbour key that left Y unkeyed.
        if (xMatches || yMatches)
        {
            if (!xMatches) x = baseline.Position.X;
            if (!yMatches) y = baseline.Position.Y;
        }
        return new AnimationPose(
            new Vector2(x, y),
            EvaluateAngleChannel(clip.Keyframes, t, duration, baseline.Rotation),
            new Vector2(
                Math.Clamp(EvaluateChannel(clip.Keyframes, t, duration, baseline.Scale.X, static key => key.ScaleX, out _), -100f, 100f),
                Math.Clamp(EvaluateChannel(clip.Keyframes, t, duration, baseline.Scale.Y, static key => key.ScaleY, out _), -100f, 100f)));
    }

    /// <summary>
    /// Evaluates one scalar channel. Blank keyframes are transparent: they neither start a
    /// channel nor terminate a held value, so <c>{X:10}@{0s}, {}@{1s}, {X:30}@{2s}</c>
    /// interpolates X linearly from 10 to 30 instead of dropping it to zero at the blank key.
    /// </summary>
    private static float EvaluateChannel(IReadOnlyList<LicAnimationKeyframeData> keys, float time,
        float duration, float baseline, Func<LicAnimationKeyframeData, float?> selector, out bool channelIsKeyed)
    {
        channelIsKeyed = false;
        float? beforeValue = null;
        float? afterValue = null;
        var beforeTime = float.NegativeInfinity;
        var afterTime = float.PositiveInfinity;

        foreach (var key in keys)
        {
            if (key is null || !float.IsFinite(key.Time)) continue;
            var value = selector(key);
            if (!value.HasValue || !float.IsFinite(value.Value)) continue;
            channelIsKeyed = true;
            var keyTime = Math.Clamp(key.Time, 0f, duration);
            // >= and <= intentionally make the last serialized key win when timestamps repeat.
            if (keyTime <= time && keyTime >= beforeTime) { beforeTime = keyTime; beforeValue = value.Value; }
            if (keyTime >= time && keyTime <= afterTime) { afterTime = keyTime; afterValue = value.Value; }
        }

        // A channel is not animated before its first authored key. Keep the live baseline
        // untouched rather than blending toward a future key; this matters for partially
        // keyed clips layered over physics or player movement.
        if (beforeValue is null) return baseline;
        // Keys authored at the sample time win outright (hard hold); interpolation only
        // happens strictly between two distinct authored times.
        if (afterValue is null || afterTime - beforeTime <= 0.0001f) return afterValue ?? beforeValue.Value;
        var amount = Math.Clamp((time - beforeTime) / (afterTime - beforeTime), 0f, 1f);
        return beforeValue.Value + (afterValue.Value - beforeValue.Value) * amount;
    }

    /// <summary>Rotation channel using shortest-arc interpolation; blank keys stay transparent.</summary>
    private static float EvaluateAngleChannel(IReadOnlyList<LicAnimationKeyframeData> keys, float time,
        float duration, float baseline)
    {
        float? beforeValue = null;
        float? afterValue = null;
        var beforeTime = float.NegativeInfinity;
        var afterTime = float.PositiveInfinity;

        foreach (var key in keys)
        {
            if (key is null || !float.IsFinite(key.Time) || !key.Rotation.HasValue || !float.IsFinite(key.Rotation.Value)) continue;
            var keyTime = Math.Clamp(key.Time, 0f, duration);
            if (keyTime <= time && keyTime >= beforeTime) { beforeTime = keyTime; beforeValue = key.Rotation.Value; }
            if (keyTime >= time && keyTime <= afterTime) { afterTime = keyTime; afterValue = key.Rotation.Value; }
        }

        // Match scalar channels: rotation remains live until its first authored key.
        if (beforeValue is null) return baseline;
        if (afterValue is null || afterTime - beforeTime <= 0.0001f) return NormalizeAngle(afterValue ?? beforeValue.Value);
        var amount = Math.Clamp((time - beforeTime) / (afterTime - beforeTime), 0f, 1f);
        return LerpAngle(NormalizeAngle(beforeValue.Value), NormalizeAngle(afterValue.Value), amount);
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
