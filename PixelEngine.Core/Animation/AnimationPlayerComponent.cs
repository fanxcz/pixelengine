using PixelEngine.Compatibility;
using PixelEngine.Core;
using PixelEngine.Scene;
using PixelEngine.Serialization;

namespace PixelEngine.Animation;

/// <summary>Plays a LIC transform clip on a GameObject; suitable for Sprite and compatibility objects.</summary>
public sealed class AnimationPlayerComponent : Component
{
    private readonly LicAnimationClipData _clip;
    private readonly LicObjectComponent? _legacyObject;
    private float _elapsed;

    public float Speed { get; set; } = 1f;
    public bool Playing { get; private set; } = true;
    public float TimePosition => _elapsed;

    public AnimationPlayerComponent(LicAnimationClipData clip, LicObjectComponent? legacyObject = null)
    {
        _clip = clip ?? throw new ArgumentNullException(nameof(clip));
        _legacyObject = legacyObject;
        Speed = 1f;
    }

    private float Duration => float.IsFinite(_clip.Duration) && _clip.Duration > 0f ? _clip.Duration : 0.0001f;

    public override void Start() => _elapsed = !_clip.Loop && Speed < 0f ? Duration : 0f;

    public override void Update(EngineTime time)
    {
        if (!Playing || !Enabled || !float.IsFinite(time.DeltaTime) || time.DeltaTime <= 0f || !float.IsFinite(Speed)) return;
        _elapsed += time.DeltaTime * Math.Clamp(Speed, -10f, 10f);
        if (!float.IsFinite(_elapsed)) _elapsed = 0f;
        if (_clip.Loop)
        {
            _elapsed %= Duration;
            if (_elapsed < 0f) _elapsed += Duration;
        }
        // Missing channels must preserve the live transform (for example, a scale-only
        // animation must not reset X/Y written by player input or physics every frame).
        var currentRotation = _legacyObject is null ? Transform.Rotation * 180f / MathF.PI : _legacyObject.Data.Rotation;
        var currentPose = new AnimationPose(Transform.Position, currentRotation, Transform.Scale);
        var pose = AnimationEvaluator.Evaluate(_clip, _elapsed, currentPose);
        Transform.Position = pose.Position;
        Transform.Rotation = pose.Rotation * MathF.PI / 180f;
        Transform.Scale = pose.Scale;
        if (_legacyObject is not null)
        {
            _legacyObject.Data.X = pose.Position.X;
            _legacyObject.Data.Y = pose.Position.Y;
            _legacyObject.Data.Rotation = pose.Rotation;
        }
        if (!_clip.Loop)
        {
            if (Speed > 0f && _elapsed >= Duration) { _elapsed = Duration; Playing = false; }
            else if (Speed < 0f && _elapsed <= 0f) { _elapsed = 0f; Playing = false; }
        }
    }

    public void Play(bool restart = true)
    {
        if (restart) _elapsed = !_clip.Loop && Speed < 0f ? Duration : 0f;
        Playing = true;
    }

    public void Pause() => Playing = false;
}
