using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;

namespace PixelEngine.Serialization;

/// <summary>
/// Read-only compatibility model for the original PixelFight .lic v1 JSON format.
/// The legacy schema is deliberately preserved; this adapter does not rewrite files.
/// </summary>
public static class LicProjectReader
{
    /// <summary>Maximum on-disk or in-memory UTF-8 LIC document size (256 MiB).</summary>
    public const long MaximumProjectBytes = 256L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        MaxDepth = 128
    };

    public static LicProjectData Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("LIC project was not found.", path);

        var json = ReadProjectText(path);
        var project = JsonSerializer.Deserialize<LicProjectData>(json, Options)
            ?? throw new InvalidDataException("LIC project is empty or contains invalid JSON.");
        Validate(project, path);
        return project;
    }

    public static LicProjectData Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumProjectBytes)
            throw new InvalidDataException($"LIC project exceeds the {MaximumProjectBytes / (1024 * 1024)} MiB document limit.");
        var project = JsonSerializer.Deserialize<LicProjectData>(json, Options)
            ?? throw new InvalidDataException("LIC project is empty or contains invalid JSON.");
        Validate(project, "<memory>");
        return project;
    }

    private static string ReadProjectText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumProjectBytes)
            throw new InvalidDataException($"LIC project exceeds the {MaximumProjectBytes / (1024 * 1024)} MiB document limit.");

        using var buffer = new MemoryStream((int)Math.Min(stream.Length, MaximumProjectBytes));
        var chunk = new byte[81_920];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > MaximumProjectBytes)
                throw new InvalidDataException($"LIC project exceeds the {MaximumProjectBytes / (1024 * 1024)} MiB document limit.");
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void Validate(LicProjectData project, string source)
    {
        project.Name = string.IsNullOrWhiteSpace(project.Name) ? Path.GetFileNameWithoutExtension(source) : project.Name;
        project.Scenes ??= new List<LicSceneData>();
        project.Scripts ??= new List<LicScriptData>();
        project.Assets ??= new List<LicAssetData>();
        project.Prefabs ??= new List<LicPrefabData>();
        if (project.Scenes.Any(x => x is null) || project.Scripts.Any(x => x is null) ||
            project.Assets.Any(x => x is null) || project.Prefabs.Any(x => x is null))
            throw new InvalidDataException("LIC project contains a null scene, script, asset or prefab record.");
        if (project.Scenes.Count == 0)
            throw new InvalidDataException("LIC project must contain at least one scene.");
        if (project.Scenes.Count > 10_000)
            throw new InvalidDataException("LIC project contains an unreasonable number of scenes.");
        if (project.Scripts.Count > 20_000 || project.Assets.Count > 100_000 || project.Prefabs.Count > 100_000)
            throw new InvalidDataException("LIC project contains too many scripts, assets or prefabs.");
        long totalObjects = 0;
        long totalScriptChars = 0;
        long totalAssetChars = 0;
        foreach (var script in project.Scripts) totalScriptChars += script.Code?.Length ?? 0;
        foreach (var asset in project.Assets) totalAssetChars += asset.Base64?.Length ?? 0;
        if (totalScriptChars > 50_000_000 || totalAssetChars > 180_000_000)
            throw new InvalidDataException("LIC project exceeds the safe aggregate script or embedded asset payload limit.");

        foreach (var scene in project.Scenes)
        {
            scene.Name = string.IsNullOrWhiteSpace(scene.Name) ? "Scene" : scene.Name;
            scene.Objects ??= new List<LicObjectData>();
            if (scene.Objects.Count > 250_000)
                throw new InvalidDataException($"Scene '{scene.Name}' contains too many objects.");
            totalObjects += scene.Objects.Count;
            if (totalObjects > 1_000_000)
                throw new InvalidDataException("LIC project contains more than one million objects across all scenes.");
            foreach (var item in scene.Objects)
            {
                if (item is null) throw new InvalidDataException($"Scene '{scene.Name}' contains a null object record.");
                item.Name = string.IsNullOrWhiteSpace(item.Name) ? "Object" : item.Name;
                item.Kind = string.IsNullOrWhiteSpace(item.Kind) ? "rect" : item.Kind.ToLowerInvariant();
                item.Width = Math.Max(1f, IsFinite(item.Width) ? item.Width : 1f);
                item.Height = Math.Max(1f, IsFinite(item.Height) ? item.Height : 1f);
                item.X = IsFinite(item.X) ? item.X : 0f;
                item.Y = IsFinite(item.Y) ? item.Y : 0f;
                item.Rotation = IsFinite(item.Rotation) ? item.Rotation : 0f;
                item.Points ??= new List<float>();
                item.Components ??= new List<LicComponentData>();
                if (item.Components.Count > 1024 || item.Points.Count > 100_000)
                    throw new InvalidDataException($"Object '{item.Name}' exceeds the component or polygon-point limit.");
                if (item.Components.Any(x => x is null))
                    throw new InvalidDataException($"Object '{item.Name}' contains a null component record.");
                foreach (var component in item.Components)
                {
                    component.Type = string.IsNullOrWhiteSpace(component.Type) ? "Unknown" : component.Type;
                    component.Mass = IsFinite(component.Mass) && component.Mass > 0 ? component.Mass : 1f;
                    component.GravityScale = IsFinite(component.GravityScale) ? component.GravityScale : 1f;
                    component.Friction = IsFinite(component.Friction) ? Math.Max(0f, component.Friction) : 0.4f;
                    component.Restitution = IsFinite(component.Restitution) ? Math.Clamp(component.Restitution, 0f, 1f) : 0f;
                    component.Radius = IsFinite(component.Radius) ? Math.Max(0.1f, component.Radius) : 8f;
                    component.Volume = IsFinite(component.Volume) ? Math.Clamp(component.Volume, 0f, 1f) : 1f;
                    component.Speed = IsFinite(component.Speed) ? Math.Clamp(component.Speed, -100f, 100f) : 1f;
                }
            }
        }

        if (project.EditorData is { } editor)
        {
            editor.Animations ??= new List<LicAnimationClipData>();
            editor.Lights ??= new List<LicLightData>();
            editor.ParticleEmitters ??= new List<LicParticleEmitterData>();
            editor.TileMaps ??= new List<LicTileMapData>();
            if (editor.Animations.Count > 10_000 || editor.Lights.Count > 10_000 ||
                editor.ParticleEmitters.Count > 10_000 || editor.TileMaps.Count > 64)
                throw new InvalidDataException("LIC project contains too many PixelEngine animation, light, particle or tilemap authoring records.");
            long totalKeyframes = 0;
            if (editor.Animations.Any(x => x is null) || editor.Lights.Any(x => x is null) ||
                editor.ParticleEmitters.Any(x => x is null) || editor.TileMaps.Any(x => x is null))
                throw new InvalidDataException("LIC project contains a null PixelEngine authoring record.");
            foreach (var clip in editor.Animations)
            {
                clip.Keyframes ??= new List<LicAnimationKeyframeData>();
                if (clip.Keyframes.Count > 100_000)
                    throw new InvalidDataException($"Animation '{clip.Name}' contains too many keyframes.");
                if (clip.Keyframes.Any(x => x is null))
                    throw new InvalidDataException($"Animation '{clip.Name}' contains a null keyframe record.");
                totalKeyframes += clip.Keyframes.Count;
                if (totalKeyframes > 500_000)
                    throw new InvalidDataException("LIC project contains more than 500,000 animation keyframes.");
            }
            foreach (var map in editor.TileMaps)
            {
                map.Cells ??= new List<int>();
                map.Palette ??= new List<string> { "#00000000", "#587C43", "#8B6B45", "#5278A6", "#C5A85C" };
                if (map.Cells.Count > 512 * 512 || map.Palette.Count > 256)
                    throw new InvalidDataException($"Tile map '{map.Name}' exceeds the safe cell or palette limit.");
                // Preserve authored dimensions and buffer length for Diagnostics. Runtime
                // TileMapComponent and the editor grid normalize the data at their boundary.
                // Normalizing here would hide malformed values before the user can inspect them.
            }
        }

        foreach (var prefab in project.Prefabs)
        {
            prefab.Name = string.IsNullOrWhiteSpace(prefab.Name) ? "Prefab" : prefab.Name;
            prefab.Object ??= new LicObjectData { Name = prefab.Name };
            prefab.Object.Components ??= new List<LicComponentData>();
            prefab.Object.Points ??= new List<float>();
        }

        project.StartScene = Math.Clamp(project.StartScene, 0, project.Scenes.Count - 1);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

public sealed class LicProjectData
{
    [JsonPropertyName("app")] public string App { get; set; } = "PixelEngine";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("name")] public string Name { get; set; } = "MyGame";
    [JsonPropertyName("startScene")] public int StartScene { get; set; }
    [JsonPropertyName("scenes")] public List<LicSceneData> Scenes { get; set; } = new();
    [JsonPropertyName("scripts")] public List<LicScriptData> Scripts { get; set; } = new();
    [JsonPropertyName("assets")] public List<LicAssetData> Assets { get; set; } = new();
    [JsonPropertyName("prefabs")] public List<LicPrefabData> Prefabs { get; set; } = new();
    [JsonPropertyName("pixelEngine"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LicEditorData? EditorData { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicSceneData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Scene";
    [JsonPropertyName("gravity")] public float Gravity { get; set; } = 900f;
    [JsonPropertyName("groundY")] public float GroundY { get; set; }
    [JsonPropertyName("lockCam")] public bool LockCamera { get; set; }
    [JsonPropertyName("freeCam")] public bool FreeCamera { get; set; }
    [JsonPropertyName("deco")] public bool Decoration { get; set; }
    [JsonPropertyName("camX0")] public float CameraX { get; set; }
    [JsonPropertyName("camY0")] public float CameraY { get; set; }
    [JsonPropertyName("bg1")] public string Background1 { get; set; } = "";
    [JsonPropertyName("bg2")] public string Background2 { get; set; } = "";
    [JsonPropertyName("objects")] public List<LicObjectData> Objects { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicObjectData
{
    [Category("General")]
    [JsonPropertyName("name")] public string Name { get; set; } = "Object";
    [Category("Rendering")]
    [JsonPropertyName("kind")] public string Kind { get; set; } = "rect";
    [Category("Transform"), Description("World position on the X axis.")]
    [JsonPropertyName("x")] public float X { get; set; }
    [Category("Transform"), Description("World position on the Y axis.")]
    [JsonPropertyName("y")] public float Y { get; set; }
    [Category("Transform")]
    [JsonPropertyName("w")] public float Width { get; set; } = 12f;
    [Category("Transform")]
    [JsonPropertyName("h")] public float Height { get; set; } = 20f;
    [Category("Transform"), Description("Rotation in degrees, clockwise in the editor coordinate system.")]
    [JsonPropertyName("rotation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float Rotation { get; set; }
    [Category("Rendering")]
    [JsonPropertyName("color")] public string Color { get; set; } = "#3E589E";
    [Category("Rendering")]
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [Category("Rendering"), Description("Name of a PNG asset embedded in this project.")]
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
    [Category("Physics")]
    [JsonPropertyName("solid")] public bool Solid { get; set; } = true;
    [Category("Physics")]
    [JsonPropertyName("gravity")] public bool Gravity { get; set; } = true;
    [Category("Gameplay")]
    [JsonPropertyName("follow")] public bool Follow { get; set; }
    [Category("Gameplay")]
    [JsonPropertyName("script")] public string Script { get; set; } = "";
    [JsonPropertyName("pts")] public List<float> Points { get; set; } = new();
    [JsonPropertyName("prefab")] public string Prefab { get; set; } = "";
    [JsonPropertyName("components")] public List<LicComponentData> Components { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicScriptData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "script.lua";
    [JsonPropertyName("lang")] public string Language { get; set; } = "lua";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicAssetData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "asset.png";
    [JsonPropertyName("base64")] public string Base64 { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "png";
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    public override string ToString() => $"{Name}  [{Type.ToUpperInvariant()}]";
}


/// <summary>Optional authoring component data stored as a forward-compatible LIC extension.</summary>
public sealed class LicComponentData
{
    [Category("General"), Description("Component type attached to this object."), ReadOnly(true)]
    [JsonPropertyName("type")] public string Type { get; set; } = "SpriteRenderer";
    [Category("General")]
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    [Category("Physics"), Description("Mass used by the 2D body solver.")]
    [JsonPropertyName("mass")] public float Mass { get; set; } = 1f;
    [Category("Physics"), Description("Multiplier applied to scene gravity.")]
    [JsonPropertyName("gravityScale")] public float GravityScale { get; set; } = 1f;
    [Category("Physics"), Description("Exponential linear velocity damping.")]
    [JsonPropertyName("linearDamping")] public float LinearDamping { get; set; }
    [Category("Physics"), Description("Apply the scene gravity to this body when enabled.")]
    [JsonPropertyName("useGravity")] public bool UseGravity { get; set; } = true;
    [Category("Physics")]
    [JsonPropertyName("freezePositionX")] public bool FreezePositionX { get; set; }
    [Category("Physics")]
    [JsonPropertyName("freezePositionY")] public bool FreezePositionY { get; set; }
    [Category("Physics")]
    [JsonPropertyName("isKinematic")] public bool IsKinematic { get; set; }
    [Category("Physics"), Description("Static bodies never move and can be used for world geometry.")]
    [JsonPropertyName("isStatic"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsStatic { get; set; }
    [Category("Physics"), Description("Collider friction coefficient.")]
    [JsonPropertyName("friction")] public float Friction { get; set; } = 0.4f;
    [Category("Physics"), Description("Bounciness coefficient (0–1).")]
    [JsonPropertyName("restitution")] public float Restitution { get; set; }
    [Category("Physics")]
    [JsonPropertyName("isTrigger")] public bool IsTrigger { get; set; }
    [Category("Physics"), Description("Collision layer index (0–31).")]
    [JsonPropertyName("collisionLayer")] public int CollisionLayer { get; set; }
    [Category("Physics"), Description("Bitmask selecting which collision layers this collider can contact.")]
    [JsonPropertyName("collisionMask")] public uint CollisionMask { get; set; } = uint.MaxValue;
    [Category("Physics"), Description("Optional BoxCollider width in world units. Zero uses the object's width.")]
    [JsonPropertyName("boxSizeX"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float BoxSizeX { get; set; }
    [Category("Physics"), Description("Optional BoxCollider height in world units. Zero uses the object's height.")]
    [JsonPropertyName("boxSizeY"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float BoxSizeY { get; set; }
    [Category("Physics"), Description("Collider local X offset before object rotation/scale.")]
    [JsonPropertyName("colliderOffsetX"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float ColliderOffsetX { get; set; }
    [Category("Physics"), Description("Collider local Y offset before object rotation/scale.")]
    [JsonPropertyName("colliderOffsetY"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float ColliderOffsetY { get; set; }
    [Category("Physics")]
    [JsonPropertyName("radius")] public float Radius { get; set; } = 8f;

    [Category("Rendering"), Description("Named render layer: Background, Default, Foreground or UI.")]
    [JsonPropertyName("sortingLayer")] public string SortingLayer { get; set; } = "Default";
    [Category("Rendering")]
    [JsonPropertyName("sortingOrder")] public int SortingOrder { get; set; }
    [Category("Rendering"), Description("Name of an imported PNG asset in this LIC project.")]
    [JsonPropertyName("texture")] public string Texture { get; set; } = "";
    [Category("Rendering")]
    [JsonPropertyName("tint")] public string Tint { get; set; } = "";

    [Category("Audio"), Description("Project WAV asset assigned to this AudioSource.")]
    [JsonPropertyName("clip")] public string Clip { get; set; } = "";
    [Category("Audio"), Description("Playback volume from 0 to 1.")]
    [JsonPropertyName("volume")] public float Volume { get; set; } = 1f;
    [Category("Audio")]
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    [Category("Audio")]
    [JsonPropertyName("playOnAwake")] public bool PlayOnAwake { get; set; }
    [Category("Audio"), Description("Playback pitch from -1 to 1.")]
    [JsonPropertyName("pitch")] public float Pitch { get; set; }
    [Category("Audio"), Description("Stereo pan from -1 (left) to 1 (right).")]
    [JsonPropertyName("pan")] public float Pan { get; set; }
    [Category("Audio"), Description("Mixer bus: SoundEffects or Music.")]
    [JsonPropertyName("audioBus")] public string AudioBus { get; set; } = "SoundEffects";

    [Category("Animation")]
    [JsonPropertyName("animation")] public string Animation { get; set; } = "";
    [Category("Animation")]
    [JsonPropertyName("speed")] public float Speed { get; set; } = 1f;
    [Category("Sprite Sheet"), Description("Frame width in pixels. Zero disables sprite-sheet animation.")]
    [JsonPropertyName("spriteFrameWidth")] public int SpriteFrameWidth { get; set; }
    [Category("Sprite Sheet"), Description("Frame height in pixels.")]
    [JsonPropertyName("spriteFrameHeight")] public int SpriteFrameHeight { get; set; }
    [Category("Sprite Sheet"), Description("Number of frames in the animation.")]
    [JsonPropertyName("spriteFrameCount")] public int SpriteFrameCount { get; set; }
    [Category("Sprite Sheet"), Description("Frames per second.")]
    [JsonPropertyName("spriteFramesPerSecond")] public float SpriteFramesPerSecond { get; set; } = 8f;
    [Category("Sprite Sheet"), Description("Sprite sheet columns. Zero derives columns from the loaded texture.")]
    [JsonPropertyName("spriteSheetColumns")] public int SpriteSheetColumns { get; set; }
    [Category("Sprite Sheet"), Description("Zero-based first frame in the sheet.")]
    [JsonPropertyName("spriteFirstFrame")] public int SpriteFirstFrame { get; set; }
    [Category("Sprite Sheet"), Description("Repeat the sprite-sheet animation when the last frame is reached.")]
    [JsonPropertyName("spriteLoop")] public bool SpriteLoop { get; set; } = true;
    [Category("Sprite Sheet")]
    [JsonPropertyName("spritePingPong")] public bool SpritePingPong { get; set; }

    [Category("Rendering"), Description("Allows this collider to block 2D point lights.")]
    [JsonPropertyName("castsShadows")] public bool CastsShadows { get; set; }
    [Category("Camera"), Description("Target object name. Empty follows the object this component is attached to.")]
    [JsonPropertyName("cameraTarget")] public string CameraTarget { get; set; } = "";
    [Category("Camera")]
    [JsonPropertyName("cameraOffsetX")] public float CameraOffsetX { get; set; }
    [Category("Camera")]
    [JsonPropertyName("cameraOffsetY")] public float CameraOffsetY { get; set; }
    [Category("Camera"), Description("Exponential follow smoothing speed; zero moves immediately to the dead-zone edge.")]
    [JsonPropertyName("cameraSmoothSpeed")] public float CameraSmoothSpeed { get; set; } = 8f;
    [Category("Camera")]
    [JsonPropertyName("cameraDeadZoneX")] public float CameraDeadZoneX { get; set; }
    [Category("Camera")]
    [JsonPropertyName("cameraDeadZoneY")] public float CameraDeadZoneY { get; set; }

    [Category("Script"), Description("Name of a LIC script associated with this component.")]
    [JsonPropertyName("scriptName")] public string ScriptName { get; set; } = "";

    [Browsable(false), JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    public static LicComponentData Create(string type)
    {
        var component = new LicComponentData { Type = type };
        switch (type)
        {
            case "SpriteRenderer": component.Enabled = true; break;
            case "Rigidbody2D": component.Mass = 1f; component.GravityScale = 1f; component.Friction = 0.4f; break;
            case "BoxCollider2D": component.IsTrigger = false; component.Friction = 0.4f; break;
            case "CircleCollider2D": component.Radius = 8f; component.IsTrigger = false; break;
            case "AudioSource": component.Volume = 1f; component.PlayOnAwake = false; break;
            case "Animator": component.Speed = 1f; component.SpriteFramesPerSecond = 8f; component.SpriteLoop = true; break;
            case "CameraFollow": component.CameraSmoothSpeed = 8f; break;
        }
        return component;
    }
    public override string ToString() => $"{(Enabled ? "✓ " : "□ ")}{Type}";
}

/// <summary>A reusable object template stored in the LIC project's prefab library.</summary>
public sealed class LicPrefabData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Prefab";
    [JsonPropertyName("object")] public LicObjectData Object { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    public override string ToString() => Name;
}
