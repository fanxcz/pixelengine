using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PixelEngine.Assets;
using PixelEngine.Core;
using PixelEngine.Graphics;
using PixelEngine.Scene;
using PixelEngine.Serialization;
using PixelEngine.Scripting;
using PixelEngine.Particles;
using PixelEngine.Physics;
using PixelEngine.Tilemaps;
using GameScene = PixelEngine.Scene.Scene;

namespace PixelEngine.Compatibility;

/// <summary>Builds a MonoGame scene from a legacy PixelFight .lic project.</summary>
public static class LicSceneFactory
{
    public static GameScene Create(LicProjectData project, int sceneIndex, AssetStore assets, Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(camera);
        if (project.Scenes.Count == 0) throw new InvalidDataException("LIC project has no scenes.");
        sceneIndex = Math.Clamp(sceneIndex, 0, project.Scenes.Count - 1);
        // Legacy Runtime used a 3x pixel scale in a 480x270 viewport. This 1280x720
        // player uses the matching logical view size (160x90 world units).
        camera.Zoom = 8f;
        var data = project.Scenes[sceneIndex];
        var scene = new GameScene(data.Name)
        {
            BackgroundColor = ParseColor(data.Background1, new Color(24, 26, 32)),
            Gravity = new Vector2(0f, float.IsFinite(data.Gravity) ? Math.Clamp(data.Gravity, -10000f, 10000f) : 900f),
            AmbientLight = project.EditorData is { AmbientLight: var ambient } && float.IsFinite(ambient)
                ? Math.Clamp(ambient, 0f, 1f) : 1f
        };

        var entities = new List<LicObjectComponent>(data.Objects.Count);
        var runtime = new LicRuntimeContext(project, scene, data, assets, camera, entities);
        runtime.LogMessage = message =>
        {
            System.Diagnostics.Debug.WriteLine("[PixelEngine LIC] " + message);
            Console.WriteLine("[PixelEngine LIC] " + message);
        };

        var editorData = project.EditorData;
        if (editorData is not null)
        {
            // Tile maps are inserted before actors so terrain stays behind sprites.
            foreach (var tileMap in editorData.TileMaps.Where(x =>
                         string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, data.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var mapObject = new GameObject(tileMap.Name);
                mapObject.Transform.Position = new Vector2(
                    float.IsFinite(tileMap.X) ? tileMap.X : 0f,
                    float.IsFinite(tileMap.Y) ? tileMap.Y : 0f);
                mapObject.AddComponent(new TileMapComponent(tileMap));
                scene.Add(mapObject);
                if (tileMap.CollisionEnabled) AddTileMapCollisionGeometry(scene, tileMap);
            }
        }

        foreach (var item in data.Objects)
            runtime.AddObject(item);
        var hasCameraFollow = runtime.AttachCameraFollowComponents();

        if (editorData is not null)
        {
            // The CPU light-map renderer is deliberately bounded to 16 point lights per scene.
            foreach (var light in editorData.Lights.Where(x => x.Enabled &&
                         (string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, data.Name, StringComparison.OrdinalIgnoreCase)))
                     .Take(16))
            {
                var lightObject = new GameObject(light.Name);
                lightObject.Transform.Position = new Vector2(
                    float.IsFinite(light.X) ? light.X : 0f,
                    float.IsFinite(light.Y) ? light.Y : 0f);
                lightObject.AddComponent(new Light2DComponent
                {
                    Radius = float.IsFinite(light.Radius) ? Math.Clamp(light.Radius, 1f, 4096f) : 150f,
                    Intensity = float.IsFinite(light.Intensity) ? Math.Clamp(light.Intensity, 0f, 1f) : 0.65f,
                    LightColor = ColorParser.Parse(light.Color, Color.White),
                    Falloff = float.IsFinite(light.Falloff) ? Math.Clamp(light.Falloff, .25f, 8f) : 2f,
                    FlickerAmount = float.IsFinite(light.FlickerAmount) ? Math.Clamp(light.FlickerAmount, 0f, .9f) : 0f,
                    FlickerSpeed = float.IsFinite(light.FlickerSpeed) ? Math.Clamp(light.FlickerSpeed, 0f, 60f) : 8f
                });
                scene.Add(lightObject);
            }

            // A project may contain many authoring emitters. Keep one scene's runtime
            // particle budget bounded even when the project bypasses Diagnostics.
            const int maxRuntimeEmittersPerScene = 64;
            const int maxParticlesPerEmitter = 512;
            foreach (var emitter in editorData.ParticleEmitters.Where(x => x.Enabled &&
                         (string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, data.Name, StringComparison.OrdinalIgnoreCase)))
                     .Take(maxRuntimeEmittersPerScene))
            {
                var emitterObject = new GameObject(emitter.Name);
                emitterObject.Transform.Position = new Vector2(
                    float.IsFinite(emitter.X) ? emitter.X : 0f,
                    float.IsFinite(emitter.Y) ? emitter.Y : 0f);
                emitterObject.AddComponent(new ParticleEmitterComponent
                {
                    Rate = float.IsFinite(emitter.Rate) ? Math.Clamp(emitter.Rate, 0f, 10000f) : 0f,
                    Lifetime = float.IsFinite(emitter.Lifetime) ? Math.Clamp(emitter.Lifetime, .05f, 120f) : 1.2f,
                    Speed = float.IsFinite(emitter.Speed) ? Math.Clamp(emitter.Speed, 0f, 10000f) : 36f,
                    SpreadDegrees = float.IsFinite(emitter.Spread) ? Math.Clamp(emitter.Spread, 0f, 360f) : 90f,
                    ParticleSize = float.IsFinite(emitter.Size) ? Math.Clamp(emitter.Size, .5f, 256f) : 3f,
                    Gravity = float.IsFinite(emitter.Gravity) ? Math.Clamp(emitter.Gravity, -10000f, 10000f) : 0f,
                    MaxParticles = Math.Clamp(emitter.MaxParticles, 1, maxParticlesPerEmitter),
                    ParticleColor = ColorParser.Parse(emitter.Color, Color.White)
                });
                scene.Add(emitterObject);
            }
        }

        var controllerObject = new GameObject("__LIC_Runtime");
        controllerObject.AddComponent(new LicSceneController(data, entities, camera, assets, useLegacyCameraFollow: !hasCameraFollow));
        scene.Add(controllerObject);
        return scene;
    }

    private static void AddTileMapCollisionGeometry(GameScene scene, LicTileMapData map)
    {
        // Merge solid cells into maximal static rectangles. Horizontal spans are extended
        // downward while the identical span is solid; a span that was already covered by a
        // taller run above is skipped so adjacent rows never emit duplicate geometry.
        // This keeps a solid block as one collider instead of one collider per row, and a
        // hard cap prevents adversarial checkerboard maps from creating hundreds of
        // thousands of GameObjects during project load.
        const int maximumStaticShapes = 8192;
        var finished = new List<(int X, int Y, int Width, int Height)>();
        var covered = new bool[checked(map.Width * Math.Max(1, map.Height))];
        for (var y = 0; y < map.Height; y++)
        {
            var x = 0;
            while (x < map.Width)
            {
                while (x < map.Width && (map.GetCell(x, y) <= 0 || covered[y * map.Width + x])) x++;
                if (x >= map.Width) break;
                var start = x;
                while (x < map.Width && map.GetCell(x, y) > 0 && !covered[y * map.Width + x]) x++;
                var width = x - start;

                // Extend downward only while the complete span stays solid and uncovered.
                var height = 1;
                while (y + height < map.Height)
                {
                    var rowStart = (y + height) * map.Width;
                    var rowIsSolid = true;
                    for (var column = start; column < start + width; column++)
                    {
                        if (map.GetCell(column, y + height) <= 0 || covered[rowStart + column]) { rowIsSolid = false; break; }
                    }
                    if (!rowIsSolid) break;
                    height++;
                }

                for (var row = y; row < y + height; row++)
                for (var column = start; column < start + width; column++)
                    covered[row * map.Width + column] = true;
                finished.Add((start, y, width, height));
                if (finished.Count > maximumStaticShapes) break;
            }
            if (finished.Count > maximumStaticShapes) break;
        }

        var tileSize = Math.Clamp(map.TileSize, 1, 256);
        var count = Math.Min(finished.Count, maximumStaticShapes);
        for (var i = 0; i < count; i++)
        {
            var rect = finished[i];
            var colliderObject = new GameObject($"{map.Name}_Collider_{i}");
            var mapX = float.IsFinite(map.X) ? map.X : 0f;
            var mapY = float.IsFinite(map.Y) ? map.Y : 0f;
            colliderObject.Transform.Position = new Vector2(
                mapX + (rect.X + rect.Width * .5f) * tileSize,
                mapY + (rect.Y + rect.Height * .5f) * tileSize);
            colliderObject.AddComponent(new BoxCollider2DComponent
            {
                Size = new Vector2(rect.Width * tileSize, rect.Height * tileSize),
                Friction = .5f
            });
            scene.Add(colliderObject);
        }
        if (finished.Count > maximumStaticShapes)
            System.Diagnostics.Debug.WriteLine($"[PixelEngine TileMap] '{map.Name}' generated {finished.Count} collider rectangles; only the first {maximumStaticShapes} were created. Simplify the collision map.");
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = value.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return fallback;
    }
}

/// <summary>
/// Compatibility physics for the original .lic top-down Y-axis convention. The first slice
/// intentionally implements the common platformer subset (gravity, ground and platform tops).
/// </summary>
internal sealed class LicSceneController : Component
{
    private readonly LicSceneData _scene;
    private readonly IReadOnlyList<LicObjectComponent> _entities;
    private readonly Camera2D _camera;
    private readonly AssetStore _assets;
    private readonly LicObjectComponent? _player;
    private readonly bool _useLegacyCameraFollow;
    private int _lastMouseX;
    private int _lastMouseY;
    private bool _wasPanning;

    public LicSceneController(LicSceneData scene, IReadOnlyList<LicObjectComponent> entities, Camera2D camera,
        AssetStore assets, bool useLegacyCameraFollow = true)
    {
        _scene = scene;
        _entities = entities;
        _camera = camera;
        _assets = assets;
        _useLegacyCameraFollow = useLegacyCameraFollow;
        _player = entities.FirstOrDefault(x => x.Data.Follow)
            ?? entities.FirstOrDefault(x => x.Data.Name.Equals("hero", StringComparison.OrdinalIgnoreCase));

        if (scene.LockCamera)
        {
            _camera.Position = new Vector2(scene.CameraX, scene.CameraY);
        }
        else
        {
            _camera.Position = new Vector2(_player?.Transform.Position.X ?? 0f, scene.GroundY - 22f);
        }
    }

    public override void FixedUpdate(EngineTime time)
    {
        var dt = time.FixedDeltaTime;
        if (dt <= 0f) return;
        if (_scene.FreeCamera) UpdateFreeCamera(time.Input);
        ApplyPlayerInput(time.Input);

        // The entity collection is stable during this physics pass; index iteration avoids
        // allocating a fresh array every fixed tick. Runtime mutations are applied between ticks.
        for (var entityIndex = 0; entityIndex < _entities.Count; entityIndex++)
        {
            var entity = _entities[entityIndex];
            if (entity.IsDestroyed) continue;
            var modernBody = entity.GameObject.GetComponent<Rigidbody2DComponent>();
            if (modernBody is { Enabled: true })
            {
                entity.OnGround = false;
                continue; // The Scene's Physics2DWorld integrates this body after component FixedUpdate.
            }
            var previousBottom = entity.Transform.Position.Y + entity.Height * .5f;
            var rigidbody = entity.FindAuthoringComponent("Rigidbody2D");
            var physicsEnabled = rigidbody is null ? entity.Data.Gravity : rigidbody.Enabled && !rigidbody.IsKinematic;
            var gravityScale = rigidbody?.GravityScale ?? 1f;
            if (physicsEnabled) entity.Velocity += new Vector2(0, Math.Max(0, _scene.Gravity) * gravityScale * dt);
            else if (rigidbody is { IsKinematic: true }) entity.Velocity = Vector2.Zero;
            entity.Transform.Position += entity.Velocity * dt;
            entity.OnGround = false;
            if (!physicsEnabled || entity.Velocity.Y < 0f) continue;

            var bottom = entity.Transform.Position.Y + entity.Height * .5f;
            if (bottom >= _scene.GroundY)
            {
                entity.Transform.Position = new Vector2(entity.Transform.Position.X, _scene.GroundY - entity.Height * .5f);
                entity.Velocity = new Vector2(entity.Velocity.X, 0f);
                entity.OnGround = true;
                continue;
            }

            for (var platformIndex = 0; platformIndex < _entities.Count; platformIndex++)
            {
                var platform = _entities[platformIndex];
                if (platform.IsDestroyed || ReferenceEquals(platform, entity)) continue;
                var boxCollider = platform.FindAuthoringComponent("BoxCollider2D");
                var circleCollider = platform.FindAuthoringComponent("CircleCollider2D");
                var collider = boxCollider ?? circleCollider;
                if (collider is { Enabled: false } or { IsTrigger: true }) continue;
                var hasAuthoringCollider = collider is { Enabled: true, IsTrigger: false };
                if (!platform.Data.Solid && !hasAuthoringCollider) continue;
                var platformBody = platform.FindAuthoringComponent("Rigidbody2D");
                var platformHasGravity = platformBody is null ? platform.Data.Gravity : platformBody.Enabled && !platformBody.IsKinematic && Math.Abs(platformBody.GravityScale) > 0.001f;
                if (platformHasGravity) continue;
                var platformTop = platform.Transform.Position.Y - platform.Height * .5f;
                var overlapsX = Math.Abs(entity.Transform.Position.X - platform.Transform.Position.X)
                    < (entity.Width + platform.Width) * .5f * .9f;
                if (overlapsX && bottom >= platformTop && previousBottom <= platformTop + 1f)
                {
                    entity.Transform.Position = new Vector2(entity.Transform.Position.X, platformTop - entity.Height * .5f);
                    entity.Velocity = new Vector2(entity.Velocity.X, 0f);
                    entity.OnGround = true;
                    break;
                }
            }
        }

        if (_useLegacyCameraFollow && !_scene.LockCamera && _player is not null && !_player.IsDestroyed)
        {
            var target = new Vector2(_player.Transform.Position.X, _scene.GroundY - 22f);
            _camera.Position = Vector2.Lerp(_camera.Position, target, 1f - MathF.Pow(.01f, dt));
        }
    }


    private void UpdateFreeCamera(PixelEngine.Input.InputState input)
    {
        if (input.ScrollWheelDelta != 0)
        {
            var mouseScreen = new Vector2(input.MouseX, input.MouseY);
            var worldBefore = ScreenToWorld(mouseScreen);
            var wheelSteps = input.ScrollWheelDelta / 120f;
            _camera.Zoom = Math.Clamp(_camera.Zoom * MathF.Pow(1.12f, wheelSteps), 4.8f, 32f);
            var worldAfter = ScreenToWorld(mouseScreen);
            _camera.Position += worldBefore - worldAfter;
        }

        if (input.RightMouseDown)
        {
            if (_wasPanning)
            {
                var delta = new Vector2(input.MouseX - _lastMouseX, input.MouseY - _lastMouseY);
                _camera.Position -= delta / Math.Max(0.1f, _camera.Zoom);
            }
            _wasPanning = true;
        }
        else
        {
            _wasPanning = false;
        }

        _lastMouseX = input.MouseX;
        _lastMouseY = input.MouseY;
    }

    private Vector2 ScreenToWorld(Vector2 screen)
    {
        var view = _camera.GetViewMatrix(_assets.Viewport);
        Matrix.Invert(ref view, out var inverse);
        return Vector2.Transform(screen, inverse);
    }

    private void ApplyPlayerInput(PixelEngine.Input.InputState input)
    {
        if (_player is null || _player.IsDestroyed) return;
        var direction = 0f;
        if (input.IsKeyDown(Keys.A) || input.IsKeyDown(Keys.Left)) direction -= 1f;
        if (input.IsKeyDown(Keys.D) || input.IsKeyDown(Keys.Right)) direction += 1f;
        _player.Velocity = new Vector2(direction * 140f, _player.Velocity.Y);
        var jumpPressed = input.WasKeyPressed(Keys.W) || input.WasKeyPressed(Keys.Up) || input.WasKeyPressed(Keys.Space);
        if (jumpPressed && _player.OnGround)
            _player.Velocity = new Vector2(_player.Velocity.X, -380f);
    }
}
