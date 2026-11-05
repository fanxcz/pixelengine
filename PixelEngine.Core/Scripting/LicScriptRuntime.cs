using PixelEngine.Compatibility;
using PixelEngine.Audio;
using PixelEngine.Physics;
using PixelEngine.Storage;
using MoonSharp.Interpreter;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PixelEngine.Core;
using PixelEngine.Assets;
using PixelEngine.Graphics;
using PixelEngine.Scene;
using PixelEngine.Serialization;
using PixelEngine.Animation;
using PixelEngine.Input;

namespace PixelEngine.Scripting;

/// <summary>
/// Runs the Lua subset used by PixelFight .lic projects. It intentionally uses MoonSharp's
/// soft sandbox: file-system, process and OS modules are not exposed to project scripts.
/// Legacy C# scripts are retained by the importer but are not executed by this runtime.
/// </summary>
public sealed class LicLuaBehaviour : Component
{
    private static readonly object RegistrationLock = new();
    private static bool _typesRegistered;

    private readonly LicRuntimeContext _context;
    private readonly LicObjectComponent _owner;
    private readonly LicScriptData _source;
    private Script? _script;
    private Closure? _update;
    private bool _faulted;

    public LicLuaBehaviour(LicRuntimeContext context, LicObjectComponent owner, LicScriptData source)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override void Start()
    {
        try
        {
            EnsureTypesRegistered();
            _script = new Script(CoreModules.Preset_SoftSandbox);
            _script.Globals["self"] = _context.GetProxy(_owner);
            _script.Globals["api"] = _context.Api;
            _script.Globals["dt"] = 0f;
            _script.Globals["time"] = 0f;
            _script.DoString(_source.Code ?? string.Empty);

            var start = _script.Globals.Get("start");
            if (start.Type == DataType.Function)
                start.Function.Call();

            var update = _script.Globals.Get("update");
            _update = update.Type == DataType.Function ? update.Function : null;
        }
        catch (Exception ex)
        {
            Fail(ex, "start");
        }
    }

    public override void Update(EngineTime time)
    {
        if (_faulted || _script is null || _update is null || _owner.IsDestroyed)
            return;

        try
        {
            _context.CurrentTime = time;
            _script.Globals["dt"] = time.DeltaTime;
            _script.Globals["time"] = (float)time.TotalTime;
            _update.Call(time.DeltaTime);
        }
        catch (Exception ex)
        {
            Fail(ex, "update");
        }
    }

    private void Fail(Exception exception, string phase)
    {
        _faulted = true;
        Enabled = false;
        _context.Log($"Lua '{_source.Name}' on '{_owner.Data.Name}' failed during {phase}: {exception.Message}");
    }

    private static void EnsureTypesRegistered()
    {
        if (_typesRegistered) return;
        lock (RegistrationLock)
        {
            if (_typesRegistered) return;
            UserData.RegisterType<LicScriptObjectProxy>();
            UserData.RegisterType<LicScriptApi>();
            _typesRegistered = true;
        }
    }

    /// <summary>Parses source without running it. Used by import smoke tests and the editor Check command.</summary>
    public static string ValidateCode(string code)
    {
        try
        {
            var script = new Script(CoreModules.Preset_SoftSandbox);
            script.LoadString(code ?? string.Empty);
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>Dynamic proxy exposed to legacy Lua scripts as the global <c>self</c> value.</summary>
[MoonSharpUserData]
public sealed class LicScriptObjectProxy
{
    private readonly LicRuntimeContext _context;
    private readonly LicObjectComponent _target;
    private readonly Dictionary<string, float> _memory = new(StringComparer.Ordinal);

    internal LicScriptObjectProxy(LicRuntimeContext context, LicObjectComponent target)
    {
        _context = context;
        _target = target;
    }

    public string name { get => _target.Data.Name; set => _target.Data.Name = value ?? "Object"; }
    public float x
    {
        get => _target.Transform.Position.X;
        set
        {
            if (!float.IsFinite(value)) return;
            var currentY = _target.Transform.Position.Y;
            _target.Transform.Position = new Microsoft.Xna.Framework.Vector2(
                Math.Clamp(value, -10_000_000f, 10_000_000f), float.IsFinite(currentY) ? currentY : 0f);
        }
    }
    public float y
    {
        get => _target.Transform.Position.Y;
        set
        {
            if (!float.IsFinite(value)) return;
            var currentX = _target.Transform.Position.X;
            _target.Transform.Position = new Microsoft.Xna.Framework.Vector2(
                float.IsFinite(currentX) ? currentX : 0f, Math.Clamp(value, -10_000_000f, 10_000_000f));
        }
    }
    public float vx
    {
        get => _target.Velocity.X;
        set { if (float.IsFinite(value)) _target.Velocity = new Microsoft.Xna.Framework.Vector2(value, _target.Velocity.Y); }
    }
    public float vy
    {
        get => _target.Velocity.Y;
        set { if (float.IsFinite(value)) _target.Velocity = new Microsoft.Xna.Framework.Vector2(_target.Velocity.X, value); }
    }
    public float w { get => _target.Data.Width; set { if (float.IsFinite(value)) _target.Data.Width = Math.Clamp(value, 1f, 100_000f); } }
    public float h { get => _target.Data.Height; set { if (float.IsFinite(value)) _target.Data.Height = Math.Clamp(value, 1f, 100_000f); } }
    public bool ground => _target.OnGround;
    public bool dead => _target.IsDestroyed;
    public float num { get => _target.Number; set => _target.Number = float.IsFinite(value) ? value : 0f; }
    public float maxnum { get => _target.MaxNumber; set => _target.MaxNumber = float.IsFinite(value) ? value : 0f; }
    public float attack { get => _target.AttackTime; set => _target.AttackTime = float.IsFinite(value) ? Math.Clamp(value, 0f, 1_000_000f) : 0f; }
    public float face => _target.Face;
    public string color { get => _target.Data.Color; set => _target.Data.Color = value ?? "#FFFFFF"; }
    public string text { get => _target.Data.Text; set => _target.Data.Text = value ?? string.Empty; }

    public float Get(string key) => _memory.TryGetValue(key ?? string.Empty, out var value) ? value : 0f;
    public void Set(string key, float value) => _memory[key ?? string.Empty] = value;
    public void hit() => _target.Hit();
    public void destroy() => _context.Destroy(_target);
}

/// <summary>Compatibility facade exposed to Lua scripts as the global <c>api</c> value.</summary>
[MoonSharpUserData]
public sealed class LicScriptApi
{
    private readonly LicRuntimeContext _context;
    internal LicScriptApi(LicRuntimeContext context) => _context = context;

    public void log(string message) => _context.Log(message ?? string.Empty);
    public bool key(string keyName) => _context.IsKeyDown(keyName);
    public float time() => (float)(_context.CurrentTime?.TotalTime ?? 0d);
    public float rand(float min, float max)
    {
        if (!float.IsFinite(min) || !float.IsFinite(max)) return 0f;
        if (min > max) (min, max) = (max, min);
        return (float)((double)min + _context.Random.NextDouble() * ((double)max - min));
    }
    public LicScriptObjectProxy? find(string name)
    {
        var entity = _context.Find(name);
        return entity is null ? null : _context.GetProxy(entity);
    }
    public LicScriptObjectProxy spawn(string kind, DynValue options) => _context.Spawn(kind, options);
    public void destroy(string name) => _context.DestroyByName(name);
    public void purge() => _context.PurgeDestroyed();
    public void cam(float x, float y, float zoom) => _context.SetCamera(x, y, zoom);
    public void bg(string color1, string color2) => _context.SetBackground(color1, color2);
    public void save(string key, string value) => _context.SaveValue(key, value);
    public string load(string key) => _context.LoadValue(key);
    public float mousex() => _context.MouseWorldPosition.X;
    public float mousey() => _context.MouseWorldPosition.Y;
    public bool click() => _context.MouseClicked;
    public string pick() => _context.PickAtMouse();
    public void play(string assetName) => _context.PlayAsset(assetName);
    public bool action(string name) => _context.IsActionDown(name);
    public bool action_pressed(string name) => _context.WasActionPressed(name);
    public double axis(string name) => _context.GetActionAxis(name);
    public void bind_key(string actionName, string keyName) => _context.BindActionKey(actionName, keyName);
    public void bind_button(string actionName, string buttonName) => _context.BindActionButton(actionName, buttonName);
    public bool unbind_key(string actionName, string keyName) => _context.UnbindActionKey(actionName, keyName);
    public bool unbind_button(string actionName, string buttonName) => _context.UnbindActionButton(actionName, buttonName);
}

/// <summary>Scene/project bridge used by script proxies and dynamically spawned objects.</summary>
public sealed class LicRuntimeContext
{
    private readonly LicProjectData _project;
    private readonly PixelEngine.Scene.Scene _scene;
    private readonly LicSceneData _sceneData;
    private readonly AssetStore _assets;
    private readonly Camera2D _camera;
    private readonly List<LicObjectComponent> _entities;
    private readonly Dictionary<LicObjectComponent, LicScriptObjectProxy> _proxies = new();
    private readonly Dictionary<string, LicAssetData> _assetIndex;
    private readonly PersistentSaveStore _saveStore;
    private readonly InputActionMap _inputActions = new();

    public Random Random { get; } = new();
    public EngineTime? CurrentTime { get; set; }
    public LicScriptApi Api { get; }
    public Action<string>? LogMessage { get; set; }

    internal LicRuntimeContext(LicProjectData project, PixelEngine.Scene.Scene scene, LicSceneData sceneData,
        AssetStore assets, Camera2D camera, List<LicObjectComponent> entities)
    {
        _project = project;
        _scene = scene;
        _sceneData = sceneData;
        _assets = assets;
        _camera = camera;
        _entities = entities;
        _assetIndex = project.Assets
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        _saveStore = new PersistentSaveStore(project.Name);
        if (_saveStore.LoadWarning is not null)
            System.Diagnostics.Debug.WriteLine("[PixelEngine Save] " + _saveStore.LoadWarning);
        _inputActions.BindKey("move_left", Keys.Left);
        _inputActions.BindKey("move_left", Keys.A);
        _inputActions.BindButton("move_left", Buttons.DPadLeft);
        _inputActions.BindKey("move_right", Keys.Right);
        _inputActions.BindKey("move_right", Keys.D);
        _inputActions.BindButton("move_right", Buttons.DPadRight);
        _inputActions.BindKey("move_up", Keys.Up);
        _inputActions.BindKey("move_up", Keys.W);
        _inputActions.BindButton("move_up", Buttons.DPadUp);
        _inputActions.BindKey("move_down", Keys.Down);
        _inputActions.BindKey("move_down", Keys.S);
        _inputActions.BindButton("move_down", Buttons.DPadDown);
        _inputActions.BindKey("jump", Keys.Space);
        _inputActions.BindButton("jump", Buttons.A);
        _inputActions.BindKey("interact", Keys.E);
        _inputActions.BindButton("interact", Buttons.X);
        Api = new LicScriptApi(this);
    }

    private static Vector2 GetAuthoredColliderSize(LicObjectData data, LicComponentData collider)
    {
        static float Dimension(float overrideValue, float fallback)
            => float.IsFinite(overrideValue) && overrideValue > 0f
                ? Math.Clamp(overrideValue, .01f, 100000f)
                : float.IsFinite(fallback) ? Math.Clamp(MathF.Abs(fallback), .01f, 100000f) : 32f;
        // Zero override dimensions preserve the legacy object-bound behavior. Transform.Scale
        // is applied by BoxCollider2D itself, so authored values remain in local units.
        return new Vector2(Dimension(collider.BoxSizeX, data.Width), Dimension(collider.BoxSizeY, data.Height));
    }

    internal LicObjectComponent AddObject(LicObjectData data)
    {
        var gameObject = new GameObject(data.Name);
        var entity = gameObject.AddComponent(new LicObjectComponent(data, _assets, _assetIndex));
        _entities.Add(entity);

        var componentData = data.Components ?? new List<LicComponentData>();
        var rigidbodyData = componentData.FirstOrDefault(x => x.Enabled &&
            string.Equals(x.Type, "Rigidbody2D", StringComparison.OrdinalIgnoreCase));
        if (rigidbodyData is not null)
        {
            var bodyType = rigidbodyData.IsStatic ? Rigidbody2DType.Static
                : rigidbodyData.IsKinematic ? Rigidbody2DType.Kinematic : Rigidbody2DType.Dynamic;
            gameObject.AddComponent(new Rigidbody2DComponent
            {
                Enabled = rigidbodyData.Enabled,
                BodyType = bodyType,
                Mass = rigidbodyData.Mass,
                GravityScale = rigidbodyData.GravityScale,
                LinearDamping = rigidbodyData.LinearDamping,
                FreezePositionX = rigidbodyData.FreezePositionX,
                FreezePositionY = rigidbodyData.FreezePositionY,
                UseGravity = rigidbodyData.UseGravity && data.Gravity
            });
        }

        var hasCollider = false;
        foreach (var colliderData in componentData.Where(x => x.Enabled &&
                     (string.Equals(x.Type, "BoxCollider2D", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(x.Type, "CircleCollider2D", StringComparison.OrdinalIgnoreCase))))
        {
            if (string.Equals(colliderData.Type, "CircleCollider2D", StringComparison.OrdinalIgnoreCase))
            {
                gameObject.AddComponent(new CircleCollider2DComponent
                {
                    Enabled = colliderData.Enabled,
                    Radius = colliderData.Radius,
                    Offset = new Microsoft.Xna.Framework.Vector2(colliderData.ColliderOffsetX, colliderData.ColliderOffsetY),
                    IsTrigger = colliderData.IsTrigger,
                    CastsShadows = colliderData.CastsShadows,
                    Layer = colliderData.CollisionLayer,
                    CollisionMask = colliderData.CollisionMask,
                    Friction = colliderData.Friction,
                    Restitution = colliderData.Restitution
                });
            }
            else
            {
                gameObject.AddComponent(new BoxCollider2DComponent
                {
                    Enabled = colliderData.Enabled,
                    Size = GetAuthoredColliderSize(data, colliderData),
                    Offset = new Microsoft.Xna.Framework.Vector2(colliderData.ColliderOffsetX, colliderData.ColliderOffsetY),
                    IsTrigger = colliderData.IsTrigger,
                    CastsShadows = colliderData.CastsShadows,
                    Layer = colliderData.CollisionLayer,
                    CollisionMask = colliderData.CollisionMask,
                    Friction = colliderData.Friction,
                    Restitution = colliderData.Restitution
                });
            }
            hasCollider = true;
        }
        // A body without an explicit shape gets a box matching its authored object bounds.
        if (rigidbodyData is not null && !hasCollider)
            gameObject.AddComponent(new BoxCollider2DComponent { Size = new Vector2(
                float.IsFinite(data.Width) ? Math.Clamp(MathF.Abs(data.Width), .01f, 100000f) : 32f,
                float.IsFinite(data.Height) ? Math.Clamp(MathF.Abs(data.Height), .01f, 100000f) : 32f) });

        var animator = data.Components?.FirstOrDefault(x => x.Enabled &&
            string.Equals(x.Type, "Animator", StringComparison.OrdinalIgnoreCase));
        if (animator is not null)
        {
            if (!string.IsNullOrWhiteSpace(animator.Animation))
            {
                var clip = _project.EditorData?.Animations.FirstOrDefault(x =>
                    string.Equals(x.Name, animator.Animation, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, _sceneData.Name, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(x.Target) || string.Equals(x.Target, data.Name, StringComparison.OrdinalIgnoreCase)));
                if (clip is not null)
                    gameObject.AddComponent(new AnimationPlayerComponent(clip, entity) { Speed = animator.Speed });
                else
                    Log($"Animator on '{data.Name}' references missing animation '{animator.Animation}'.");
            }

            if (animator.SpriteFrameWidth > 0 && animator.SpriteFrameHeight > 0 && animator.SpriteFrameCount > 0)
            {
                var columns = animator.SpriteSheetColumns > 0
                    ? animator.SpriteSheetColumns : entity.ResolveSpriteSheetColumns(animator.SpriteFrameWidth);
                gameObject.AddComponent(new SpriteSheetAnimationComponent(entity,
                    animator.SpriteFrameWidth, animator.SpriteFrameHeight, animator.SpriteFrameCount,
                    columns, animator.SpriteFramesPerSecond, animator.SpriteFirstFrame, animator.SpritePingPong)
                { Loop = animator.SpriteLoop });
            }
        }

        foreach (var audioData in componentData.Where(x =>
                     x.Enabled && string.Equals(x.Type, "AudioSource", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(audioData.Clip)) continue;
            if (!_assetIndex.TryGetValue(audioData.Clip, out var audioAsset))
            {
                Log($"AudioSource on '{data.Name}' references missing clip '{audioData.Clip}'.");
                continue;
            }
            if (!string.Equals(audioAsset.Type, "wav", StringComparison.OrdinalIgnoreCase))
            {
                Log($"AudioSource on '{data.Name}' references '{audioAsset.Name}', which is not a WAV asset.");
                continue;
            }
            try
            {
                gameObject.AddComponent(new AudioSourceComponent(_assets, audioAsset.Name, audioAsset.Base64)
                {
                    Enabled = audioData.Enabled,
                    Volume = audioData.Volume,
                    Pitch = audioData.Pitch,
                    Pan = audioData.Pan,
                    Bus = string.Equals(audioData.AudioBus, "Music", StringComparison.OrdinalIgnoreCase)
                        ? AudioBus.Music : AudioBus.SoundEffects,
                    Loop = audioData.Loop,
                    PlayOnAwake = audioData.PlayOnAwake
                });
            }
            catch (Exception ex)
            {
                Log($"AudioSource on '{data.Name}' could not be created: {ex.Message}");
            }
        }

        var scriptComponent = componentData.FirstOrDefault(x => x.Enabled &&
            string.Equals(x.Type, "Script", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.ScriptName));
        var scriptName = !string.IsNullOrWhiteSpace(data.Script) ? data.Script : scriptComponent?.ScriptName;
        if (!string.IsNullOrWhiteSpace(scriptName))
        {
            // Script components are a first-class authoring path. The legacy object-level
            // script field remains authoritative when both are populated.
            if (string.IsNullOrWhiteSpace(data.Script)) data.Script = scriptName;
            var source = _project.Scripts.FirstOrDefault(x =>
                string.Equals(x.Name, scriptName, StringComparison.OrdinalIgnoreCase));
            if (source is null)
                Log($"Object '{data.Name}' references missing script '{scriptName}'.");
            else if (string.Equals(source.Language, "lua", StringComparison.OrdinalIgnoreCase))
                gameObject.AddComponent(new LicLuaBehaviour(this, entity, source));
            else
                Log($"C# script '{source.Name}' was imported but not run. Use only reviewed scripts; C# isolation is not part of this milestone.");
        }

        _scene.Add(gameObject);
        return entity;
    }

    internal bool AttachCameraFollowComponents()
    {
        var attached = false;
        foreach (var entity in _entities.ToArray())
        {
            var authoring = (entity.Data.Components ?? new List<LicComponentData>()).FirstOrDefault(x => x.Enabled &&
                string.Equals(x.Type, "CameraFollow", StringComparison.OrdinalIgnoreCase));
            if (authoring is null) continue;
            var target = string.IsNullOrWhiteSpace(authoring.CameraTarget)
                ? entity.GameObject
                : _entities.FirstOrDefault(candidate =>
                    string.Equals(candidate.Data.Name, authoring.CameraTarget, StringComparison.OrdinalIgnoreCase))?.GameObject;
            if (target is null)
            {
                Log($"CameraFollow on '{entity.Data.Name}' references missing target '{authoring.CameraTarget}'.");
                continue;
            }
            entity.GameObject.AddComponent(new CameraFollowComponent(_camera, target)
            {
                Offset = new Microsoft.Xna.Framework.Vector2(authoring.CameraOffsetX, authoring.CameraOffsetY),
                SmoothSpeed = authoring.CameraSmoothSpeed,
                DeadZone = new Microsoft.Xna.Framework.Vector2(authoring.CameraDeadZoneX, authoring.CameraDeadZoneY)
            });
            attached = true;
        }
        return attached;
    }

    internal LicScriptObjectProxy GetProxy(LicObjectComponent target)
    {
        if (!_proxies.TryGetValue(target, out var proxy))
        {
            proxy = new LicScriptObjectProxy(this, target);
            _proxies[target] = proxy;
        }
        return proxy;
    }

    internal LicObjectComponent? Find(string name)
    {
        for (var i = 0; i < _entities.Count; i++)
        {
            var entity = _entities[i];
            if (!entity.IsDestroyed && string.Equals(entity.Data.Name, name, StringComparison.OrdinalIgnoreCase))
                return entity;
        }
        return null;
    }

    internal LicScriptObjectProxy Spawn(string kind, DynValue options)
    {
        var data = new LicObjectData
        {
            Name = MakeUniqueSpawnName("spawn" + Random.Next(100000)),
            Kind = string.IsNullOrWhiteSpace(kind) ? "rect" : kind.ToLowerInvariant(),
            Width = 8,
            Height = 8,
            Gravity = true,
            Solid = true
        };

        if (options.Type == DataType.Table)
        {
            foreach (var pair in options.Table.Pairs)
            {
                var key = pair.Key.Type == DataType.String ? pair.Key.String.ToLowerInvariant() : string.Empty;
                var value = pair.Value;
                switch (key)
                {
                    case "name": data.Name = MakeUniqueSpawnName(ReadString(value, data.Name)); break;
                    case "x": data.X = ReadNumber(value, data.X); break;
                    case "y": data.Y = ReadNumber(value, data.Y); break;
                    case "w": data.Width = Math.Max(1f, ReadNumber(value, data.Width)); break;
                    case "h": data.Height = Math.Max(1f, ReadNumber(value, data.Height)); break;
                    case "color": data.Color = ReadString(value, data.Color); break;
                    case "text": data.Text = ReadString(value, data.Text); break;
                    case "sprite": data.Sprite = ReadString(value, data.Sprite); break;
                    case "script": data.Script = ReadString(value, data.Script); break;
                    case "solid": data.Solid = ReadBoolean(value, data.Solid); break;
                    case "gravity": data.Gravity = ReadBoolean(value, data.Gravity); break;
                }
            }
        }

        var spawned = AddObject(data);
        if (options.Type == DataType.Table)
        {
            spawned.Velocity = new Microsoft.Xna.Framework.Vector2(
                ReadNumber(options.Table.Get("vx"), 0f), ReadNumber(options.Table.Get("vy"), 0f));
            spawned.Number = ReadNumber(options.Table.Get("num"), 0f);
        }
        return GetProxy(spawned);
    }

    private string MakeUniqueSpawnName(string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Spawn" : requestedName.Trim();
        if (!string.Equals(baseName, requestedName, StringComparison.Ordinal))
            Log($"Spawn name '{requestedName}' was normalized to '{baseName}'.");
        var existing = _entities.Select(x => x.Data.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName)) return baseName;
        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    internal void Destroy(LicObjectComponent entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.IsDestroyed = true;
        // Mark inactive before detaching: the current cached Scene snapshot may
        // still contain this object until the next frame, but it must not update/draw.
        entity.GameObject.Active = false;
        // Scene.Remove is idempotent. Always call it so PurgeDestroyed also
        // removes objects that were marked destroyed by legacy behaviours.
        _scene.Remove(entity.GameObject);
        _entities.Remove(entity);
        _proxies.Remove(entity);
    }

    internal void DestroyByName(string name)
    {
        var entity = Find(name);
        if (entity is not null) Destroy(entity);
    }

    internal void PurgeDestroyed()
    {
        // Remove backwards so this maintenance pass does not allocate a LINQ array
        // and list indices remain valid as destroyed entities are removed.
        for (var i = _entities.Count - 1; i >= 0; i--)
        {
            var entity = _entities[i];
            if (entity.IsDestroyed) Destroy(entity);
        }
    }

    internal bool IsKeyDown(string name)
    {
        var normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
        var key = normalized switch
        {
            "left" => Keys.Left, "right" => Keys.Right, "up" => Keys.Up, "down" => Keys.Down,
            "space" => Keys.Space, "esc" or "escape" => Keys.Escape,
            "enter" => Keys.Enter, "ctrl" => Keys.LeftControl, "shift" => Keys.LeftShift,
            _ => Enum.TryParse<Keys>(normalized, true, out var parsed) ? parsed : Keys.None
        };
        return key != Keys.None && (CurrentTime?.Input?.IsKeyDown(key) ?? false);
    }

    internal bool IsActionDown(string actionName)
        => !string.IsNullOrWhiteSpace(actionName) && CurrentTime?.Input is { } input && _inputActions.IsDown(input, actionName);

    internal bool WasActionPressed(string actionName)
        => !string.IsNullOrWhiteSpace(actionName) && CurrentTime?.Input is { } input && _inputActions.WasPressed(input, actionName);

    internal float GetActionAxis(string axisName)
    {
        var input = CurrentTime?.Input;
        if (input is null) return 0f;
        var axis = (axisName ?? string.Empty).Trim().ToLowerInvariant();
        return axis switch
        {
            "move_x" => Math.Clamp((float.IsFinite(input.LeftStick.X) ? input.LeftStick.X : 0f)
                + (_inputActions.IsDown(input, "move_right") ? 1f : 0f)
                - (_inputActions.IsDown(input, "move_left") ? 1f : 0f), -1f, 1f),
            "move_y" => Math.Clamp((float.IsFinite(input.LeftStick.Y) ? input.LeftStick.Y : 0f)
                + (_inputActions.IsDown(input, "move_down") ? 1f : 0f)
                - (_inputActions.IsDown(input, "move_up") ? 1f : 0f), -1f, 1f),
            "look_x" => float.IsFinite(input.RightStick.X) ? Math.Clamp(input.RightStick.X, -1f, 1f) : 0f,
            "look_y" => float.IsFinite(input.RightStick.Y) ? Math.Clamp(input.RightStick.Y, -1f, 1f) : 0f,
            "left_trigger" => Math.Clamp(input.LeftTrigger, 0f, 1f),
            "right_trigger" => Math.Clamp(input.RightTrigger, 0f, 1f),
            _ => 0f
        };
    }

    internal void BindActionKey(string actionName, string keyName)
    {
        if (string.IsNullOrWhiteSpace(actionName) || !Enum.TryParse<Keys>(keyName, true, out var key) ||
            key == Keys.None || !Enum.IsDefined(typeof(Keys), key))
        {
            Log($"Input binding ignored: invalid action or key '{actionName}/{keyName}'.");
            return;
        }
        try { _inputActions.BindKey(actionName, key); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { Log($"Input binding '{actionName}/{keyName}' was not added: {ex.Message}"); }
    }

    internal void BindActionButton(string actionName, string buttonName)
    {
        if (string.IsNullOrWhiteSpace(actionName) || !Enum.TryParse<Buttons>(buttonName, true, out var button) ||
            button == Buttons.None || !Enum.IsDefined(typeof(Buttons), button))
        {
            Log($"GamePad binding ignored: invalid action or button '{actionName}/{buttonName}'.");
            return;
        }
        try { _inputActions.BindButton(actionName, button); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { Log($"GamePad binding '{actionName}/{buttonName}' was not added: {ex.Message}"); }
    }

    internal bool UnbindActionKey(string actionName, string keyName)
        => !string.IsNullOrWhiteSpace(actionName) && Enum.TryParse<Keys>(keyName, true, out var key) &&
           key != Keys.None && Enum.IsDefined(typeof(Keys), key) && _inputActions.UnbindKey(actionName, key);

    internal bool UnbindActionButton(string actionName, string buttonName)
        => !string.IsNullOrWhiteSpace(actionName) && Enum.TryParse<Buttons>(buttonName, true, out var button) &&
           button != Buttons.None && Enum.IsDefined(typeof(Buttons), button) && _inputActions.UnbindButton(actionName, button);

    internal Microsoft.Xna.Framework.Vector2 MouseWorldPosition
    {
        get
        {
            var input = CurrentTime?.Input;
            if (input is null) return Microsoft.Xna.Framework.Vector2.Zero;
            var view = _camera.GetViewMatrix(_assets.Viewport);
            Microsoft.Xna.Framework.Matrix.Invert(ref view, out var inverse);
            return Microsoft.Xna.Framework.Vector2.Transform(new Microsoft.Xna.Framework.Vector2(input.MouseX, input.MouseY), inverse);
        }
    }

    internal bool MouseClicked => CurrentTime?.Input?.LeftMousePressed ?? false;

    internal string PickAtMouse()
    {
        var point = MouseWorldPosition;
        for (var i = _entities.Count - 1; i >= 0; i--)
        {
            var entity = _entities[i];
            if (!entity.IsDestroyed && Contains(entity, point)) return entity.Data.Name;
        }
        return string.Empty;
    }

    private static bool Contains(LicObjectComponent entity, Microsoft.Xna.Framework.Vector2 point)
        => ContainsTransformed(entity.Data, entity.Transform, point);

    internal static bool ContainsTransformed(LicObjectData data, PixelEngine.Scene.Transform transform,
        Microsoft.Xna.Framework.Vector2 point)
    {
        if (string.Equals(data.Kind, "text", StringComparison.OrdinalIgnoreCase)) return false;
        var center = transform.Position;
        var scale = transform.Scale;
        var rotation = transform.Rotation;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
            || !float.IsFinite(center.X) || !float.IsFinite(center.Y)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y)
            || !float.IsFinite(rotation) || Math.Abs(scale.X) < 0.0001f || Math.Abs(scale.Y) < 0.0001f)
            return false;

        // Convert the pointer into object-local coordinates so picking uses exactly
        // the same translation, scale and rotation as the renderer.
        var delta = point - center;
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);
        var local = new Microsoft.Xna.Framework.Vector2(
            (delta.X * cos + delta.Y * sin) / scale.X,
            (-delta.X * sin + delta.Y * cos) / scale.Y);
        if (!float.IsFinite(local.X) || !float.IsFinite(local.Y)) return false;

        if (string.Equals(data.Kind, "circle", StringComparison.OrdinalIgnoreCase))
        {
            var dx = local.X / Math.Max(1f, data.Width * .5f);
            var dy = local.Y / Math.Max(1f, data.Height * .5f);
            return dx * dx + dy * dy <= 1f;
        }
        if (string.Equals(data.Kind, "poly", StringComparison.OrdinalIgnoreCase))
            return PointInPolygon(data.Points, new Microsoft.Xna.Framework.Vector2(data.X + local.X, data.Y + local.Y));
        return Math.Abs(local.X) <= data.Width * .5f
            && Math.Abs(local.Y) <= data.Height * .5f;
    }

    private static bool PointInPolygon(IReadOnlyList<float> points, Microsoft.Xna.Framework.Vector2 point)
    {
        var ring = new List<Microsoft.Xna.Framework.Vector2>();
        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            var x = points[i];
            var y = points[i + 1];
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                if (PointInRing(ring, point)) return true;
                ring.Clear();
                continue;
            }
            ring.Add(new Microsoft.Xna.Framework.Vector2(x, y));
        }
        return PointInRing(ring, point);
    }

    private static bool PointInRing(IReadOnlyList<Microsoft.Xna.Framework.Vector2> ring, Microsoft.Xna.Framework.Vector2 point)
    {
        if (ring.Count < 3) return false;
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            var intersects = ((a.Y > point.Y) != (b.Y > point.Y))
                && (point.X < (b.X - a.X) * (point.Y - a.Y) / ((b.Y - a.Y) == 0f ? float.Epsilon : b.Y - a.Y) + a.X);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    internal void PlayAsset(string assetName)
    {
        var asset = _project.Assets.FirstOrDefault(x =>
            string.Equals(x.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null || !string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(asset.Base64))
        {
            Log($"WAV asset not found: {assetName}");
            return;
        }
        try { _assets.PlaySoundEffectFromBase64(asset.Name, asset.Base64); }
        catch (Exception ex) { Log($"Audio '{assetName}' failed: {ex.Message}"); }
    }

    internal void SetCamera(float x, float y, float zoom)
    {
        var current = _camera.Position;
        _camera.Position = new Microsoft.Xna.Framework.Vector2(
            float.IsFinite(x) ? Math.Clamp(x, -10_000_000f, 10_000_000f) : (float.IsFinite(current.X) ? current.X : 0f),
            float.IsFinite(y) ? Math.Clamp(y, -10_000_000f, 10_000_000f) : (float.IsFinite(current.Y) ? current.Y : 0f));
        var safeZoom = float.IsFinite(zoom) ? zoom : _camera.SafeZoom / 8f;
        _camera.Zoom = Math.Clamp(safeZoom * 8f, 0.6f, 32f);
    }

    internal void SetBackground(string color1, string color2)
    {
        _sceneData.Background1 = color1 ?? string.Empty;
        _sceneData.Background2 = color2 ?? string.Empty;
        var parsed = TryParseColor(color1);
        if (parsed.HasValue) _scene.BackgroundColor = parsed.Value;
    }

    internal void SaveValue(string key, string value)
    {
        try { _saveStore.Set(key ?? string.Empty, value ?? string.Empty); }
        catch (Exception ex) { Log($"Save '{key}' failed: {ex.Message}"); }
    }

    internal string LoadValue(string key)
    {
        try { return _saveStore.Get(key ?? string.Empty); }
        catch (Exception ex) { Log($"Load '{key}' failed: {ex.Message}"); return string.Empty; }
    }
    internal void Log(string message) => LogMessage?.Invoke(message);

    private static float ReadNumber(DynValue value, float fallback) =>
        value.Type == DataType.Number && double.IsFinite(value.Number) ? (float)value.Number : fallback;
    private static string ReadString(DynValue value, string fallback) =>
        value.Type == DataType.String ? value.String : fallback;
    private static bool ReadBoolean(DynValue value, bool fallback) =>
        value.Type == DataType.Boolean ? value.Boolean : fallback;

    private static Microsoft.Xna.Framework.Color? TryParseColor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim().TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb)) return null;
        return new Microsoft.Xna.Framework.Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
