using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using PixelEngine.Graphics;
using PixelEngine.Audio;

namespace PixelEngine.Assets;

/// <summary>GPU texture cache. Textures loaded or created here are owned by this store.</summary>
public sealed class AssetStore : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoundEffect> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteAtlas> _atlases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SoundEffectInstance> _soundInstances = new();
    private bool _disposed;

    public AssetStore(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        WhitePixel = new Texture2D(_device, 1, 1, false, SurfaceFormat.Color);
        WhitePixel.SetData(new[] { Color.White });
    }

    /// <summary>Shared one-pixel white texture for primitive rendering.</summary>
    public Texture2D WhitePixel { get; }
    public Viewport Viewport => _device.Viewport;

    public Texture2D LoadTexture(string path)
    {
        ThrowIfDisposed();
        var key = Path.GetFullPath(path);
        if (_textures.TryGetValue(key, out var cached)) return cached;
        using var stream = File.OpenRead(key);
        return AddTexture(key, Texture2D.FromStream(_device, stream));
    }

    public Texture2D LoadTextureFromBytes(string cacheKey, ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        if (_textures.TryGetValue(cacheKey, out var cached)) return cached;
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        return AddTexture(cacheKey, Texture2D.FromStream(_device, stream));
    }

    public Texture2D LoadTextureFromBase64(string assetName, string base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        var key = "lic:" + assetName;
        if (_textures.TryGetValue(key, out var cached)) return cached;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new InvalidDataException($"Asset '{assetName}' contains invalid Base64 data.", ex); }
        return LoadTextureFromBytes(key, bytes);
    }


    public void PlaySoundEffectFromBase64(string assetName, string base64, float volume = 1f,
        float pitch = 0f, float pan = 0f, AudioBus bus = AudioBus.SoundEffects)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        var outputVolume = AudioMixer.Shared.GetOutputVolume(volume, bus);
        if (outputVolume <= 0f) return;
        pitch = float.IsFinite(pitch) ? Math.Clamp(pitch, -1f, 1f) : 0f;
        pan = float.IsFinite(pan) ? Math.Clamp(pan, -1f, 1f) : 0f;
        GetOrLoadSoundEffect(assetName, base64).Play(outputVolume, pitch, pan);
    }

    /// <summary>Creates a controllable, independently disposable instance of an embedded WAV asset.</summary>
    public SoundEffectInstance CreateSoundInstanceFromBase64(string assetName, string base64)
    {
        ThrowIfDisposed();
        var instance = GetOrLoadSoundEffect(assetName, base64).CreateInstance();
        _soundInstances.Add(instance);
        return instance;
    }

    /// <summary>Releases an instance created by this store. Safe to call more than once.</summary>
    public void ReleaseSoundInstance(SoundEffectInstance? instance)
    {
        if (instance is null) return;
        if (_soundInstances.Remove(instance))
        {
            try { instance.Stop(); } catch { /* A stopped/disposed audio device must not block teardown. */ }
            instance.Dispose();
        }
    }

    private SoundEffect GetOrLoadSoundEffect(string assetName, string base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        if (_sounds.TryGetValue(assetName, out var cached)) return cached;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException ex) { throw new InvalidDataException($"Audio asset '{assetName}' contains invalid Base64 data.", ex); }
        using var stream = new MemoryStream(bytes, writable: false);
        var sound = SoundEffect.FromStream(stream);
        _sounds.Add(assetName, sound);
        return sound;
    }

    /// <summary>
    /// Builds or replaces a named atlas from textures already loaded by this store.
    /// Dictionary keys become region names; values are the cache keys returned/used by LoadTexture APIs
    /// (for embedded LIC assets, use the key "lic:asset-name.png").
    /// The store owns the atlas and disposes it with the rest of its GPU resources.
    /// </summary>
    public SpriteAtlas BuildSpriteAtlas(string atlasName, IReadOnlyDictionary<string, string> namedTextureKeys,
        int maximumSize = 2048, int padding = 2)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(atlasName);
        ArgumentNullException.ThrowIfNull(namedTextureKeys);
        var sprites = new List<SpriteAtlasInput>(namedTextureKeys.Count);
        foreach (var pair in namedTextureKeys.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Value);
            if (!_textures.TryGetValue(pair.Value, out var texture))
                throw new KeyNotFoundException($"Atlas source texture '{pair.Value}' is not loaded in this AssetStore.");
            sprites.Add(new SpriteAtlasInput(pair.Key, texture));
        }

        var atlas = SpriteAtlasBuilder.Build(_device, sprites, maximumSize, padding);
        if (_atlases.Remove(atlasName, out var previous)) previous.Dispose();
        _atlases.Add(atlasName, atlas);
        return atlas;
    }

    public SpriteAtlas? GetSpriteAtlas(string atlasName)
    {
        ThrowIfDisposed();
        return _atlases.TryGetValue(atlasName ?? string.Empty, out var atlas) ? atlas : null;
    }

    public Texture2D GetCircleTexture(int diameter = 64)
    {
        ThrowIfDisposed();
        diameter = Math.Clamp(diameter, 8, 256);
        var key = $"primitive:circle:{diameter}";
        if (_textures.TryGetValue(key, out var cached)) return cached;
        var texture = new Texture2D(_device, diameter, diameter, false, SurfaceFormat.Color);
        var pixels = new Color[diameter * diameter];
        var radius = diameter * .5f;
        var radiusSquared = radius * radius;
        for (var y = 0; y < diameter; y++)
        for (var x = 0; x < diameter; x++)
        {
            var dx = x + .5f - radius;
            var dy = y + .5f - radius;
            pixels[y * diameter + x] = dx * dx + dy * dy <= radiusSquared ? Color.White : Color.Transparent;
        }
        texture.SetData(pixels);
        return AddTexture(key, texture);
    }

    private Texture2D AddTexture(string key, Texture2D texture)
    {
        _textures.Add(key, texture);
        return texture;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AssetStore));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var atlas in _atlases.Values)
        {
            try { atlas.Dispose(); }
            catch (Exception ex) { Console.Error.WriteLine($"[AssetStore] Could not dispose atlas: {ex.Message}"); }
        }
        _atlases.Clear();
        foreach (var texture in _textures.Values)
        {
            try { texture.Dispose(); }
            catch (Exception ex) { Console.Error.WriteLine($"[AssetStore] Could not dispose texture: {ex.Message}"); }
        }
        _textures.Clear();
        foreach (var instance in _soundInstances)
        {
            // Raylib throws if a sound is already stopped or the audio device is gone during
            // shutdown; disposal is best-effort and must never take the process down.
            try { instance.Stop(); }
            catch (Exception ex) { Console.Error.WriteLine($"[AssetStore] Could not stop sound instance: {ex.Message}"); }
            try { instance.Dispose(); }
            catch (Exception ex) { Console.Error.WriteLine($"[AssetStore] Could not dispose sound instance: {ex.Message}"); }
        }
        _soundInstances.Clear();
        foreach (var sound in _sounds.Values)
        {
            try { sound.Dispose(); }
            catch (Exception ex) { Console.Error.WriteLine($"[AssetStore] Could not dispose sound: {ex.Message}"); }
        }
        _sounds.Clear();
        WhitePixel.Dispose();
    }
}
