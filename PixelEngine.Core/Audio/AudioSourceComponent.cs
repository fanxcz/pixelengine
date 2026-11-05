using Microsoft.Xna.Framework.Audio;
using PixelEngine.Assets;
using PixelEngine.Scene;

namespace PixelEngine.Audio;

/// <summary>Controllable 2D audio source backed by an embedded project WAV asset.</summary>
public sealed class AudioSourceComponent : Component
{
    private readonly AssetStore _assets;
    private readonly string _assetName;
    private readonly string _base64;
    private SoundEffectInstance? _instance;
    private bool _resumeAfterDisable;
    private float _volume = 1f;
    private float _pitch;
    private float _pan;
    private bool _loop;
    private AudioBus _bus;

    private AudioMixer _mixer = AudioMixer.Shared;
    public AudioMixer Mixer
    {
        get => _mixer;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_mixer, value)) return;
            _mixer.VolumeChanged -= OnMixerVolumeChanged;
            _mixer = value;
            if (_instance is not null) _mixer.VolumeChanged += OnMixerVolumeChanged;
            ApplySettings();
        }
    }
    public AudioBus Bus { get => _bus; set { _bus = value; ApplySettings(); } }
    public float Volume { get => _volume; set { _volume = Normalize(value, 1f); ApplySettings(); } }
    public float Pitch { get => _pitch; set { _pitch = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f; ApplySettings(); } }
    public float Pan { get => _pan; set { _pan = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f; ApplySettings(); } }
    public bool Loop { get => _loop; set { _loop = value; ApplySettings(); } }
    public bool PlayOnAwake { get; set; }
    public bool IsPlaying => _instance?.State == SoundState.Playing;
    public string AssetName => _assetName;
    public string? InitializationError { get; private set; }

    public AudioSourceComponent(AssetStore assets, string assetName, string base64)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        _assetName = assetName;
        _base64 = base64;
    }

    public override void Start()
    {
        try
        {
            _instance ??= _assets.CreateSoundInstanceFromBase64(_assetName, _base64);
            ApplySettings();
            if (PlayOnAwake) Play();
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            Enabled = false;
            System.Diagnostics.Debug.WriteLine($"AudioSource '{_assetName}' failed to initialize: {ex.Message}");
        }
    }

    public override void OnDisabled()
    {
        try
        {
            _resumeAfterDisable = _instance?.State == SoundState.Playing;
            if (_resumeAfterDisable) _instance!.Pause();
        }
        catch (Exception ex)
        {
            _resumeAfterDisable = false;
            System.Diagnostics.Debug.WriteLine($"AudioSource '{_assetName}' pause failed: {ex.Message}");
        }
    }

    public override void OnEnabled()
    {
        if (!_resumeAfterDisable) return;
        try
        {
            if (_instance?.State == SoundState.Paused) _instance.Resume();
            _resumeAfterDisable = false;
        }
        catch (Exception ex)
        {
            _resumeAfterDisable = false;
            InitializationError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"AudioSource '{_assetName}' resume failed: {ex.Message}");
        }
    }

    public override void OnRemoved()
    {
        _resumeAfterDisable = false;
        Mixer.VolumeChanged -= OnMixerVolumeChanged;
        _assets.ReleaseSoundInstance(_instance);
        _instance = null;
    }

    public void Play()
    {
        if (!Enabled) return;
        try
        {
            EnsureInstance();
            ApplySettings();
            _resumeAfterDisable = false;
            if (_instance?.State == SoundState.Paused) _instance.Resume();
            else if (_instance is not null) _instance.Play();
            InitializationError = null;
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            System.Diagnostics.Debug.WriteLine($"AudioSource '{_assetName}' failed to play: {ex.Message}");
        }
    }

    public void Pause()
    {
        _resumeAfterDisable = false;
        try { if (_instance?.State == SoundState.Playing) _instance.Pause(); }
        catch (Exception ex) { InitializationError = ex.Message; }
    }

    public void Stop()
    {
        _resumeAfterDisable = false;
        try { _instance?.Stop(); }
        catch (Exception ex) { InitializationError = ex.Message; }
    }

    private void EnsureInstance()
    {
        if (_instance is not null) return;
        _instance = _assets.CreateSoundInstanceFromBase64(_assetName, _base64);
        ApplySettings();
    }

    private void ApplySettings()
    {
        if (_instance is null) return;
        _instance.IsLooped = Loop;
        _instance.Volume = Mixer.GetOutputVolume(Volume, Bus);
        _instance.Pitch = Pitch;
        _instance.Pan = Pan;
        Mixer.VolumeChanged -= OnMixerVolumeChanged;
        Mixer.VolumeChanged += OnMixerVolumeChanged;
    }

    private void OnMixerVolumeChanged(object? sender, EventArgs e)
    {
        if (_instance is not null) _instance.Volume = Mixer.GetOutputVolume(Volume, Bus);
    }

    private static float Normalize(float value, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;
}
