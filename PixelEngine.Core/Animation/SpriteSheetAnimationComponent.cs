using PixelEngine.Compatibility;
using PixelEngine.Core;
using PixelEngine.Scene;
using PixelEngine.Graphics;
using Microsoft.Xna.Framework;

namespace PixelEngine.Animation;

/// <summary>Advances rectangular sprite-sheet animation on legacy LIC sprites or native SpriteRenderer components.</summary>
public sealed class SpriteSheetAnimationComponent : Component
{
    private readonly Action<int, int, int, int> _setFrame;
    private double _elapsed;

    public int FrameWidth { get; }
    public int FrameHeight { get; }
    public int FrameCount { get; }
    public int Columns { get; }
    public int FirstFrame { get; }
    public float FramesPerSecond { get; set; }
    public bool Loop { get; set; } = true;
    public bool PingPong { get; set; }
    public bool Playing { get; private set; } = true;
    public int CurrentFrame { get; private set; }

    public SpriteSheetAnimationComponent(LicObjectComponent target, int frameWidth, int frameHeight,
        int frameCount, int columns, float framesPerSecond = 8f, int firstFrame = 0, bool pingPong = false)
        : this((target ?? throw new ArgumentNullException(nameof(target))).SetSpriteSheetFrame,
            frameWidth, frameHeight, frameCount, columns, framesPerSecond, firstFrame, pingPong)
    {
    }

    /// <summary>Creates sprite-sheet playback for a native SpriteRenderer component.</summary>
    public SpriteSheetAnimationComponent(SpriteRenderer target, int frameWidth, int frameHeight,
        int frameCount, int columns = 0, float framesPerSecond = 8f, int firstFrame = 0, bool pingPong = false)
        : this(CreateSpriteRendererSetter(target), frameWidth, frameHeight, frameCount,
            columns > 0 ? columns : Math.Max(1, (target.Texture?.Width ?? frameWidth) / Math.Max(1, frameWidth)),
            framesPerSecond, firstFrame, pingPong)
    {
    }

    private SpriteSheetAnimationComponent(Action<int, int, int, int> setFrame, int frameWidth, int frameHeight,
        int frameCount, int columns, float framesPerSecond, int firstFrame, bool pingPong)
    {
        _setFrame = setFrame ?? throw new ArgumentNullException(nameof(setFrame));
        FrameWidth = Math.Clamp(frameWidth, 1, 8192);
        FrameHeight = Math.Clamp(frameHeight, 1, 8192);
        FrameCount = Math.Clamp(frameCount, 1, 100_000);
        Columns = Math.Max(1, columns);
        FirstFrame = Math.Clamp(firstFrame, 0, 100_000);
        FramesPerSecond = float.IsFinite(framesPerSecond) ? Math.Clamp(framesPerSecond, .01f, 240f) : 8f;
        PingPong = pingPong;
    }

    private static Action<int, int, int, int> CreateSpriteRendererSetter(SpriteRenderer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (width, height, columns, frameIndex) =>
            target.SourceRectangle = width > 0 && height > 0 && columns > 0 && frameIndex >= 0
                ? new Rectangle((frameIndex % columns) * width, (frameIndex / columns) * height, width, height)
                : null;
    }

    public override void Start() => ApplyFrame(0);

    public override void Update(EngineTime time)
    {
        if (!Playing || !Enabled || !float.IsFinite(time.DeltaTime) || time.DeltaTime <= 0f) return;
        var fps = float.IsFinite(FramesPerSecond) ? Math.Clamp(FramesPerSecond, .01f, 240f) : 8f;
        _elapsed += time.DeltaTime * fps;
        if (!double.IsFinite(_elapsed) || _elapsed < 0d) _elapsed = 0d;
        if (Loop && _elapsed >= EffectiveFramePeriod)
            _elapsed %= EffectiveFramePeriod;
        else if (!Loop)
            // Keep very large time steps bounded before converting to an integer frame index.
            _elapsed = Math.Min(_elapsed, Math.Max(0d, EffectiveFramePeriod - 1d));
        var rawFrame = (long)Math.Floor(_elapsed);
        var frame = ResolveFrameIndex(rawFrame, FrameCount, Loop, PingPong);
        ApplyFrame(frame);
        if (!Loop && rawFrame >= EffectiveFramePeriod - 1)
        {
            ApplyFrame(ResolveFrameIndex(EffectiveFramePeriod - 1, FrameCount, false, PingPong));
            Playing = false;
        }
    }

    public void Play(bool restart = true)
    {
        if (restart)
        {
            _elapsed = 0;
            ApplyFrame(0);
        }
        else
        {
            var frame = ResolveFrameIndex((long)Math.Floor(Math.Max(0d, _elapsed)), FrameCount, Loop, PingPong);
            ApplyFrame(frame);
        }
        Playing = true;
    }

    public void Pause() => Playing = false;

    private long EffectiveFramePeriod => PingPong && FrameCount > 1
        ? FrameCount * 2L - (Loop ? 2L : 1L)
        : FrameCount;

    public static int ResolveFrameIndex(long rawFrame, int frameCount, bool loop, bool pingPong)
    {
        frameCount = Math.Clamp(frameCount, 1, 100_000);
        if (frameCount <= 1) return 0;
        var period = pingPong ? frameCount * 2L - (loop ? 2L : 1L) : frameCount;
        if (loop) rawFrame = ((rawFrame % period) + period) % period;
        else rawFrame = Math.Clamp(rawFrame, 0, period - 1);
        if (pingPong && rawFrame >= frameCount)
            rawFrame = loop ? period - rawFrame : period - 1L - rawFrame;
        return (int)Math.Clamp(rawFrame, 0, frameCount - 1);
    }

    private void ApplyFrame(int frame)
    {
        CurrentFrame = Math.Clamp(frame, 0, FrameCount - 1);
        _setFrame(FrameWidth, FrameHeight, Columns, FirstFrame + CurrentFrame);
    }
}
