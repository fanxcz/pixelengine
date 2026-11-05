namespace PixelEngine.Audio;

public enum AudioBus { SoundEffects, Music }

/// <summary>Central volume controls for music, effects and master output.</summary>
public sealed class AudioMixer
{
    private float _masterVolume = 1f;
    private float _effectsVolume = 1f;
    private float _musicVolume = 1f;

    public static AudioMixer Shared { get; } = new();
    public event EventHandler? VolumeChanged;

    public float MasterVolume { get => _masterVolume; set => SetVolume(ref _masterVolume, value); }
    public float EffectsVolume { get => _effectsVolume; set => SetVolume(ref _effectsVolume, value); }
    public float MusicVolume { get => _musicVolume; set => SetVolume(ref _musicVolume, value); }

    public float GetOutputVolume(float sourceVolume, AudioBus bus)
    {
        var busVolume = bus == AudioBus.Music ? _musicVolume : _effectsVolume;
        var source = float.IsFinite(sourceVolume) ? Math.Clamp(sourceVolume, 0f, 1f) : 0f;
        return Math.Clamp(source * busVolume * _masterVolume, 0f, 1f);
    }

    private void SetVolume(ref float field, float value)
    {
        var normalized = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
        if (Math.Abs(field - normalized) < .0001f) return;
        field = normalized;
        VolumeChanged?.Invoke(this, EventArgs.Empty);
    }
}
