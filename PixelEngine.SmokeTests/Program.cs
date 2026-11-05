using System.Reflection;
using PixelEngine.Serialization;
using PixelEngine.Compatibility;
using PixelEngine.Scripting;
using PixelEngine.Animation;
using PixelEngine.Diagnostics;
using PixelEngine.Particles;
using PixelEngine.Scene;
using PixelEngine.Core;
using PixelEngine.Graphics;
using PixelEngine.Tilemaps;
using PixelEngine.Physics;
using PixelEngine.Input;
using PixelEngine.Audio;
using PixelEngine.Storage;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : AppContext.BaseDirectory;
var licDirectory = Path.Combine(root, "Lic");
if (!Directory.Exists(licDirectory))
{
    licDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lic"));
}
if (!Directory.Exists(licDirectory))
{
    Console.Error.WriteLine("Could not find Lic fixture directory. Pass the repository root as an argument.");
    return 2;
}

var files = Directory.GetFiles(licDirectory, "*.lic").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
if (files.Length == 0)
{
    Console.Error.WriteLine($"No .lic fixtures found in {licDirectory}");
    return 2;
}

var failures = new List<string>();

try
{
    var camera = new Camera2D();
    var viewport = new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 90, 90);
    var light = new LightDrawCommand(Vector2.Zero, 40f, Color.White, 1f, 2f);
    var shadowEdge = new ShadowSegment(new Vector2(10f, -20f), new Vector2(10f, 20f));
    var lightMap = LightingRenderer.BuildLightMapPixels(9, 9, viewport, camera, [light], [shadowEdge]);
    var litPixel = lightMap[4 * 9 + 2];
    var shadowPixel = lightMap[4 * 9 + 6];
    if (litPixel.R == 0 || shadowPixel.R != 0)
        throw new InvalidDataException($"2D light occlusion failed (lit={litPixel.R}, shadow={shadowPixel.R}).");
    var pingPong = Enumerable.Range(0, 5)
        .Select(frame => SpriteSheetAnimationComponent.ResolveFrameIndex(frame, 3, true, true)).ToArray();
    if (!pingPong.SequenceEqual(new[] { 0, 1, 2, 1, 0 }))
        throw new InvalidDataException("Sprite-sheet ping-pong animation sequence is incorrect.");
    var oneShotPingPong = Enumerable.Range(0, 5)
        .Select(frame => SpriteSheetAnimationComponent.ResolveFrameIndex(frame, 3, false, true)).ToArray();
    if (!oneShotPingPong.SequenceEqual(new[] { 0, 1, 2, 1, 0 }))
        throw new InvalidDataException("Non-looping ping-pong animation did not return to its first frame.");
    if (SpriteSheetAnimationComponent.ResolveFrameIndex(99, 4, false, false) != 3)
        throw new InvalidDataException("Non-looping sprite-sheet animation did not clamp to its final frame.");
    var nativeSpriteRenderer = new SpriteRenderer();
    var nativeAnimation = new SpriteSheetAnimationComponent(nativeSpriteRenderer, 16, 16, 3, 3, 8f, 1, true);
    nativeAnimation.Start();
    var initialRegion = nativeSpriteRenderer.SourceRectangle;
    if (!initialRegion.HasValue || initialRegion.Value.X != 16 || initialRegion.Value.Y != 0 ||
        initialRegion.Value.Width != 16 || initialRegion.Value.Height != 16)
        throw new InvalidDataException("Sprite-sheet animation did not select the first frame on a native SpriteRenderer.");
    Console.WriteLine("PASS Stage 10 light-map ray occlusion and sprite-sheet animation frame sequencing/native renderer");
}
catch (Exception ex)
{
    failures.Add("Stage 10 rendering/animation: " + ex.Message);
}

try
{
    const string stage10Fixture = """
    {"name":"Stage10","scenes":[{"name":"Main","objects":[{"name":"Hero","kind":"sprite","sprite":"hero.png","components":[{"type":"Animator","spriteFrameWidth":16,"spriteFrameHeight":16,"spriteFrameCount":6,"spriteFramesPerSecond":12,"spriteSheetColumns":3,"spriteLoop":false,"spritePingPong":true},{"type":"BoxCollider2D","castsShadows":true,"boxSizeX":24,"boxSizeY":40,"colliderOffsetX":2,"collisionLayer":3,"collisionMask":15},{"type":"CameraFollow","cameraTarget":"Hero","cameraOffsetX":4,"cameraSmoothSpeed":6,"cameraDeadZoneX":12},{"type":"Script","scriptName":"hero.lua"},{"type":"AudioSource","pitch":0.25,"pan":-0.5,"audioBus":"Music"}]}]}],"assets":[{"name":"hero.png","type":"png","base64":""}],"pixelEngine":{"ambientLight":0.28,"lights":[{"name":"Torch","radius":120,"intensity":0.8,"falloff":2.4,"flickerAmount":0.15,"flickerSpeed":6}]}}
    """;
    var stage10 = LicProjectReader.Parse(stage10Fixture);
    var serialized = LicProjectWriter.Serialize(stage10);
    var roundTrip = LicProjectReader.Parse(serialized);
    var hero = roundTrip.Scenes[0].Objects[0];
    var animator = hero.Components.Single(x => x.Type == "Animator");
    var collider = hero.Components.Single(x => x.Type == "BoxCollider2D");
    var cameraFollow = hero.Components.Single(x => x.Type == "CameraFollow");
    var scriptComponent = hero.Components.Single(x => x.Type == "Script");
    var audioSource = hero.Components.Single(x => x.Type == "AudioSource");
    var roundTripEditorData = roundTrip.EditorData ?? throw new InvalidDataException("Stage 10 extension was not read.");
    var lightData = roundTripEditorData.Lights.Single();
    if (animator.SpriteFrameWidth != 16 || animator.SpriteFrameCount != 6 || animator.SpriteLoop || !animator.SpritePingPong ||
        !collider.CastsShadows || Math.Abs(collider.BoxSizeX - 24f) > .001f || Math.Abs(collider.ColliderOffsetX - 2f) > .001f ||
        cameraFollow.CameraTarget != "Hero" || Math.Abs(cameraFollow.CameraSmoothSpeed - 6f) > .001f ||
        scriptComponent.ScriptName != "hero.lua" || Math.Abs(audioSource.Pitch - .25f) > .001f || audioSource.AudioBus != "Music" ||
        Math.Abs(roundTripEditorData.AmbientLight - .28f) > .001f ||
        Math.Abs(lightData.Falloff - 2.4f) > .001f || Math.Abs(lightData.FlickerAmount - .15f) > .001f)
        throw new InvalidDataException("Stage 10 rendering metadata did not survive LIC serialization.");
    Console.WriteLine("PASS Stage 10 LIC metadata: ambient light, shadow casters, sprite sheets and flicker settings");
}
catch (Exception ex)
{
    failures.Add("Stage 10 LIC metadata: " + ex.Message);
}
try
{
    const string extensionFixture = "{\"name\":\"Ext\",\"futureRoot\":17,\"scenes\":[{\"name\":\"S\",\"futureScene\":true,\"objects\":[{\"name\":\"O\",\"futureObject\":\"kept\"}]}]}";
    using var roundTripDocument = System.Text.Json.JsonDocument.Parse(LicProjectWriter.Serialize(LicProjectReader.Parse(extensionFixture)));
    if (roundTripDocument.RootElement.GetProperty("futureRoot").GetInt32() != 17
        || !roundTripDocument.RootElement.GetProperty("scenes")[0].GetProperty("futureScene").GetBoolean()
        || roundTripDocument.RootElement.GetProperty("scenes")[0].GetProperty("objects")[0].GetProperty("futureObject").GetString() != "kept")
        throw new InvalidDataException("Unknown LIC fields were not preserved by the writer.");
    Console.WriteLine("PASS LIC extension data round-trip");
}
catch (Exception ex)
{
    failures.Add("extension data round-trip: " + ex.Message);
}

try
{
    var containsMethod = typeof(LicRuntimeContext).GetMethod("ContainsTransformed", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("LIC transformed hit-test helper was not found.");
    var transformedRect = new LicObjectData
    {
        Name = "RotatedRect", Kind = "rect", X = 0f, Y = 0f, Width = 4f, Height = 2f
    };
    var rectTransform = new PixelEngine.Scene.Transform
    {
        Position = new Vector2(10f, 20f), Rotation = MathF.PI / 2f, Scale = new Vector2(2f, 1f)
    };
    var insideRotated = (bool)(containsMethod.Invoke(null, [transformedRect, rectTransform, new Vector2(10f, 23f)]) ?? false);
    var outsideRotated = (bool)(containsMethod.Invoke(null, [transformedRect, rectTransform, new Vector2(12f, 20f)]) ?? false);
    if (!insideRotated || outsideRotated)
        throw new InvalidDataException("LIC pointer picking did not account for object rotation and scale.");

    var transformedPolygon = new LicObjectData
    {
        Name = "MovedPolygon", Kind = "poly", X = 5f, Y = 5f,
        Points = [0f, 0f, 10f, 0f, 10f, 10f, 0f, 10f]
    };
    var polygonTransform = new PixelEngine.Scene.Transform
    {
        Position = new Vector2(100f, 100f), Rotation = MathF.PI / 2f, Scale = new Vector2(2f, 1f)
    };
    var polygonCenter = (bool)(containsMethod.Invoke(null, [transformedPolygon, polygonTransform, new Vector2(100f, 100f)]) ?? false);
    var polygonOutside = (bool)(containsMethod.Invoke(null, [transformedPolygon, polygonTransform, new Vector2(100f, 111f)]) ?? false);
    if (!polygonCenter || polygonOutside)
        throw new InvalidDataException("LIC polygon picking did not follow the object's live transform.");
    Console.WriteLine("PASS LIC transformed picking: rotation, scaling and moved polygons");
}
catch (Exception ex)
{
    failures.Add("LIC transformed picking: " + ex.Message);
}

try
{
    var rotatedProject = LicProjectReader.Parse("""{"name":"Rotation","scenes":[{"name":"Main","objects":[{"name":"Rotated","kind":"rect","x":12,"y":18,"w":24,"h":10,"rotation":37.5}]}]}""");
    var serialized = LicProjectWriter.Serialize(rotatedProject);
    var rotatedRoundTrip = LicProjectReader.Parse(serialized);
    if (Math.Abs(rotatedRoundTrip.Scenes[0].Objects[0].Rotation - 37.5f) > 0.001f)
        throw new InvalidDataException("Object rotation was not preserved by LIC round-trip.");
    Console.WriteLine("PASS object rotation authoring data round-trip");
}
catch (Exception ex)
{
    failures.Add("rotation round-trip: " + ex.Message);
}

try
{
    const string stage8Fixture = """
    {"name":"Stage8","scenes":[{"name":"Main","objects":[{"name":"Hero","kind":"rect","x":10,"y":20,"w":16,"h":16}]}],"pixelEngine":{"animations":[{"name":"Walk","scene":"Main","target":"Hero","duration":1,"loop":true,"keyframes":[{"time":0,"x":10,"y":20},{"time":1,"x":42,"y":20,"rotation":90}]}],"lights":[{"name":"Torch","scene":"Main","x":10,"y":20,"radius":100,"intensity":0.7,"color":"#FFCC88"}],"particleEmitters":[{"name":"Dust","scene":"Main","rate":10,"lifetime":1,"maxParticles":32}],"tileMaps":[{"name":"Ground","scene":"Main","width":2,"height":2,"tileSize":16,"cells":[0,1,2,0],"palette":["#00000000","#557744","#886644"]}]}}
    """;
    var stage8Project = LicProjectReader.Parse(stage8Fixture);
    var stage8Serialized = LicProjectWriter.Serialize(stage8Project);
    var stage8RoundTrip = LicProjectReader.Parse(stage8Serialized);
    var editorData = stage8RoundTrip.EditorData ?? throw new InvalidDataException("Stage 8 extension was not read.");
    if (editorData.Animations.Count != 1 || editorData.Lights.Count != 1 || editorData.ParticleEmitters.Count != 1 || editorData.TileMaps.Count != 1)
        throw new InvalidDataException("Stage 8 authoring data did not survive LIC serialization.");
    using var stage8Json = System.Text.Json.JsonDocument.Parse(stage8Serialized);
    if (!stage8Json.RootElement.TryGetProperty("pixelEngine", out _))
        throw new InvalidDataException("Stage 8 authoring data was not written under pixelEngine.");
    Console.WriteLine("PASS Stage 8 animation/light/particle/tilemap metadata round-trip");
    var oversizedAuthoring = new LicProjectData
    {
        Name = "AuthoringLimits",
        Scenes = [new LicSceneData { Name = "Main" }],
        EditorData = new LicEditorData { TileMaps = Enumerable.Range(0, 65).Select(i => new LicTileMapData { Name = "Map" + i }).ToList() }
    };
    var rejectedOversized = false;
    try { _ = LicProjectReader.Parse(LicProjectWriter.Serialize(oversizedAuthoring)); }
    catch (InvalidDataException ex) when (ex.Message.Contains("too many PixelEngine", StringComparison.OrdinalIgnoreCase)) { rejectedOversized = true; }
    if (!rejectedOversized) throw new InvalidDataException("LIC reader did not reject an excessive number of Stage 8 authoring records.");
    Console.WriteLine("PASS LIC authoring limits reject excessive extension records");
    var parsedColor = ColorParser.Parse("#80FF4000", Color.Transparent);
    if (parsedColor.A != 0x80 || parsedColor.R != 0xFF || parsedColor.G != 0x40 || parsedColor.B != 0x00)
        throw new InvalidDataException("Color parser did not decode #AARRGGBB correctly.");
}
catch (Exception ex)
{
    failures.Add("Stage 8 metadata round-trip: " + ex.Message);
}

try
{
    var time = new EngineTime();
    var componentHost = new GameObject("ComponentSnapshotHost");
    UpdateProbeComponent? lateComponent = null;
    componentHost.AddComponent(new CallbackProbeComponent(() =>
    {
        lateComponent = componentHost.AddComponent(new UpdateProbeComponent());
    }));
    var gameObjectUpdate = typeof(GameObject).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(GameObject).FullName, "Update");
    gameObjectUpdate.Invoke(componentHost, [time]);
    if (lateComponent is null || lateComponent.UpdateCount != 0)
        throw new InvalidDataException("A component added during update was incorrectly processed in the same component snapshot.");
    gameObjectUpdate.Invoke(componentHost, [time]);
    if (lateComponent.UpdateCount != 1)
        throw new InvalidDataException("Adding a component did not invalidate the cached component snapshot for the next frame.");

    var scene = new Scene("SceneSnapshot");
    UpdateProbeComponent? lateObjectComponent = null;
    var spawner = new GameObject("Spawner");
    spawner.AddComponent(new CallbackProbeComponent(() =>
    {
        var lateObject = new GameObject("LateObject");
        lateObjectComponent = lateObject.AddComponent(new UpdateProbeComponent());
        scene.Add(lateObject);
    }));
    scene.Add(spawner);
    var sceneStart = typeof(Scene).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Scene).FullName, "Start");
    var sceneUpdate = typeof(Scene).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Scene).FullName, "Update");
    sceneStart.Invoke(scene, null);
    sceneUpdate.Invoke(scene, [time]);
    if (lateObjectComponent is null || lateObjectComponent.UpdateCount != 0)
        throw new InvalidDataException("A GameObject added during scene update was processed in the same object snapshot.");
    sceneUpdate.Invoke(scene, [time]);
    if (lateObjectComponent.UpdateCount != 1 || scene.Find("LateObject") is null)
        throw new InvalidDataException("Adding a GameObject did not invalidate the cached scene snapshot for the next frame.");
    Console.WriteLine("PASS scene/component update snapshots invalidate on mutation without per-frame array rebuilds");
}
catch (Exception ex)
{
    failures.Add("scene/component snapshot cache: " + ex.Message);
}

try
{
    var scene = new Scene("Lifecycle");
    var gameObject = new GameObject("LifecycleObject");
    var probe = gameObject.AddComponent(new LifecycleProbeComponent());
    scene.Add(gameObject);
    var start = typeof(Scene).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Scene).FullName, "Start");
    start.Invoke(scene, null);
    start.Invoke(scene, null);
    if (probe.StartCount != 1) throw new InvalidDataException("Start was not idempotent.");

    var retryScene = new Scene("RetryStart");
    var retryObject = retryScene.Add(new GameObject("RetryObject"));
    var retryComponent = retryObject.AddComponent(new RetryStartComponent());
    var retryStart = typeof(Scene).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Scene).FullName, "Start");
    var firstStartFailed = false;
    try { retryStart.Invoke(retryScene, null); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { firstStartFailed = true; }
    retryStart.Invoke(retryScene, null);
    if (!firstStartFailed || retryComponent.Attempts != 2 || retryComponent.SuccessfulStarts != 1)
        throw new InvalidDataException("Scene/GameObject start state was not rolled back after a component Start failure.");

    var late = gameObject.AddComponent(new LifecycleProbeComponent());
    if (late.StartCount != 1) throw new InvalidDataException("A component added after scene start did not start immediately.");
    var disabled = gameObject.AddComponent(new LifecycleProbeComponent { Enabled = false });
    if (disabled.StartCount != 0) throw new InvalidDataException("A disabled component started before it was enabled.");
    disabled.Enabled = true;
    if (disabled.StartCount != 1) throw new InvalidDataException("Enabling a component after scene start did not call Start.");
    if (!gameObject.RemoveComponent(late) || late.RemovedCount != 1)
        throw new InvalidDataException("Removing a component did not invoke OnRemoved exactly once.");

    scene.Remove(gameObject);
    var detachedAddition = gameObject.AddComponent(new LifecycleProbeComponent());
    if (detachedAddition.StartCount != 0)
        throw new InvalidDataException("A component added while its GameObject was detached started too early.");
    scene.Add(gameObject);
    if (probe.StartCount != 2 || probe.AddedCount != 2 || detachedAddition.StartCount != 1)
        throw new InvalidDataException("Reattaching a GameObject did not restart its component lifecycle exactly once.");

    var failingStart = new FailingStartComponent();
    var failingAdded = new FailingAddedComponent();
    try { gameObject.AddComponent(failingStart); }
    catch (InvalidOperationException) { }
    try { gameObject.AddComponent(failingAdded); }
    catch (InvalidOperationException) { }
    if (failingStart.RemovedCount != 1 || failingStart.GameObject is not null
        || failingAdded.RemovedCount != 1 || failingAdded.GameObject is not null
        || gameObject.Components.Contains(failingStart) || gameObject.Components.Contains(failingAdded))
        throw new InvalidDataException("Failed component attachment did not roll back resources and ownership.");

    var removalScene = new Scene("RemovalCallbackAddsComponent");
    var removalHost = new GameObject("RemovalHost");
    var removalAdder = removalHost.AddComponent(new AddComponentOnRemovalProbe(removalHost));
    removalScene.Add(removalHost);
    var removalSceneStart = typeof(Scene).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Scene).FullName, "Start");
    removalSceneStart.Invoke(removalScene, null);
    removalScene.Remove(removalHost);
    var addedDuringRemoval = removalAdder.AddedComponent
        ?? throw new InvalidDataException("OnRemoved did not add the test component.");
    if (addedDuringRemoval.StartCount != 0)
        throw new InvalidDataException("A component added during OnRemoved started while its GameObject was detached.");
    removalScene.Add(removalHost);
    if (addedDuringRemoval.StartCount != 1)
        throw new InvalidDataException("A component added during OnRemoved did not start exactly once after reattachment.");

    var reusableHost = new GameObject("ReusableComponentHost");
    var reusable = reusableHost.AddComponent(new LifecycleProbeComponent());
    reusableHost.RemoveComponent(reusable);
    reusableHost.AddComponent(reusable);
    if (reusable.AddedCount != 2 || reusable.StartCount != 0)
        throw new InvalidDataException("A removed component could not be safely reattached to another host lifecycle.");
    Console.WriteLine("PASS component lifecycle, failed-attachment cleanup, removal-callback additions, reuse and reattachment");
}
catch (Exception ex)
{
    failures.Add("component lifecycle: " + ex.Message);
}

try
{
    var scene = new Scene("PhysicsSmoke") { Gravity = Vector2.Zero };
    var moving = new GameObject("MovingCircle");
    moving.Transform.Position = Vector2.Zero;
    var body = moving.AddComponent(new Rigidbody2DComponent { Velocity = new Vector2(30f, 0f), Mass = 1f });
    moving.AddComponent(new CircleCollider2DComponent { Radius = 5f, Friction = .2f });
    moving.AddComponent(new ThrowingCollisionComponent());
    var collisionProbe = moving.AddComponent(new CollisionProbeComponent());
    scene.Add(moving);

    var wall = new GameObject("StaticWall");
    wall.Transform.Position = new Vector2(10f, 0f);
    wall.AddComponent(new BoxCollider2DComponent { Size = new Vector2(10f, 40f), Friction = .2f });
    scene.Add(wall);
    scene.Physics.Step(scene, .1f, Vector2.Zero);
    if (body.Velocity.X >= 1f || collisionProbe.CollisionEnters != 1 || scene.Physics.LastContactCount != 1)
        throw new InvalidDataException($"Physics collision response failed (vx={body.Velocity.X}, enters={collisionProbe.CollisionEnters}, contacts={scene.Physics.LastContactCount}).");
    scene.Physics.Step(scene, .1f, Vector2.Zero);
    if (collisionProbe.CollisionStays != 1)
        throw new InvalidDataException("Persistent contact did not dispatch OnCollisionStay2D.");
    scene.Remove(wall);
    if (collisionProbe.CollisionExits != 1)
        throw new InvalidDataException("Removing a collider did not dispatch OnCollisionExit2D.");

    var triggerScene = new Scene("TriggerSmoke") { Gravity = Vector2.Zero };
    var triggerOwner = new GameObject("Trigger");
    var triggerProbe = triggerOwner.AddComponent(new CollisionProbeComponent());
    triggerOwner.AddComponent(new BoxCollider2DComponent { Size = new Vector2(20f), IsTrigger = true });
    triggerScene.Add(triggerOwner);
    var visitor = new GameObject("Visitor");
    visitor.Transform.Position = new Vector2(5f, 0f);
    visitor.AddComponent(new CircleCollider2DComponent { Radius = 3f });
    triggerScene.Add(visitor);
    triggerScene.Physics.Step(triggerScene, 1f / 60f, Vector2.Zero);
    if (triggerProbe.TriggerEnters != 1)
        throw new InvalidDataException("Trigger overlap did not dispatch OnTriggerEnter2D.");

    var rotatedScene = new Scene("RotatedBoxSmoke") { Gravity = Vector2.Zero };
    var rotatedBox = new GameObject("RotatedThinBox");
    rotatedBox.Transform.Rotation = MathF.PI / 4f;
    rotatedBox.AddComponent(new BoxCollider2DComponent { Size = new Vector2(20f, 2f) });
    rotatedScene.Add(rotatedBox);
    var nearAabbOnly = new GameObject("OutsideRotatedBox");
    nearAabbOnly.Transform.Position = new Vector2(-7f, 7f);
    nearAabbOnly.AddComponent(new CircleCollider2DComponent { Radius = 1f });
    rotatedScene.Add(nearAabbOnly);
    rotatedScene.Physics.Step(rotatedScene, 1f / 60f, Vector2.Zero);
    if (rotatedScene.Physics.LastContactCount != 0)
        throw new InvalidDataException("Rotated box narrowphase reported a false contact from overlapping AABBs.");
    Console.WriteLine("PASS 2D physics broadphase, rotated box/circle narrowphase, impulse response, collision and trigger events");
}
catch (Exception ex)
{
    failures.Add("2D physics: " + ex.Message);
}

try
{
    var actions = new InputActionMap();
    actions.BindKey("Jump", Keys.Space);
    actions.BindButton("Jump", Buttons.A);
    if (!actions.GetKeys(" Jump ").Contains(Keys.Space) || !actions.GetButtons("JUMP").Contains(Buttons.A)
        || !actions.UnbindKey(" jump ", Keys.Space) || !actions.UnbindButton("JUMP", Buttons.A)
        || actions.GetActions().Contains("Jump", StringComparer.OrdinalIgnoreCase))
        throw new InvalidDataException("Input action bindings did not normalize names or clean up empty actions.");
    actions.BindKey("Mixed", Keys.Space);
    actions.BindButton("Mixed", Buttons.B);
    if (!actions.UnbindKey("MIXED", Keys.Space) || !actions.GetActions().Contains("Mixed") || !actions.GetButtons("mixed").Contains(Buttons.B))
        throw new InvalidDataException("Unbinding one device removed an action still bound to another device.");
    actions.Clear(" mixed ");
    if (actions.GetActions().Contains("Mixed")) throw new InvalidDataException("Clear did not normalize the action name.");
    for (var i = 0; i < InputActionMap.MaximumActions; i++) actions.BindKey("Action" + i, Keys.A);
    var actionLimitEnforced = false;
    try { actions.BindButton("OneTooMany", Buttons.A); }
    catch (InvalidOperationException) { actionLimitEnforced = true; }
    if (!actionLimitEnforced) throw new InvalidDataException("Input action map did not enforce its maximum action count.");
    var bindingLimit = new InputActionMap();
    var validKeys = Enum.GetValues<Keys>().Where(x => x != Keys.None).Distinct()
        .Take(InputActionMap.MaximumBindingsPerAction + 1).ToArray();
    if (validKeys.Length <= InputActionMap.MaximumBindingsPerAction)
        throw new InvalidDataException("The input key enum does not provide enough values to test binding limits.");
    foreach (var key in validKeys.Take(InputActionMap.MaximumBindingsPerAction)) bindingLimit.BindKey("Move", key);
    var bindingLimitEnforced = false;
    try { bindingLimit.BindKey("Move", validKeys[^1]); }
    catch (InvalidOperationException) { bindingLimitEnforced = true; }
    var invalidKeyRejected = false;
    try { bindingLimit.BindKey("Invalid", Keys.None); }
    catch (ArgumentOutOfRangeException) { invalidKeyRejected = true; }
    if (!bindingLimitEnforced || !invalidKeyRejected)
        throw new InvalidDataException("Input action map did not enforce binding limits or reject invalid key values.");

    var backgroundDepth = SortingLayerRegistry.GetDepth("Background", 0);
    var foregroundDepth = SortingLayerRegistry.GetDepth("Foreground", 0);
    var stableDepthBeforeRegistration = SortingLayerRegistry.GetDepth("Foreground", 0);
    _ = SortingLayerRegistry.GetDepth("LayerAddedAfterExistingDepth", 0);
    var stableDepthAfterRegistration = SortingLayerRegistry.GetDepth("Foreground", 0);
    var trimmedLayerIndex = SortingLayerRegistry.Register(" AuditTrimLayer ");
    var repeatedTrimmedLayerIndex = SortingLayerRegistry.Register("AuditTrimLayer");
    if (Math.Abs(stableDepthBeforeRegistration - stableDepthAfterRegistration) > .000001f ||
        trimmedLayerIndex != repeatedTrimmedLayerIndex)
        throw new InvalidDataException("Sorting layer registration changed existing depths or failed to normalize names.");
    var orderedDepth = SortingLayerRegistry.GetDepth("Foreground", 100);
    if (!(backgroundDepth > foregroundDepth && foregroundDepth > orderedDepth))
        throw new InvalidDataException("Sorting layer/order depth is not front-to-back deterministic.");
    var stressLayerDepths = Enumerable.Range(0, 40)
        .Select(i => SortingLayerRegistry.GetDepth("AuditLayer" + i, 0)).ToArray();
    if (stressLayerDepths.Distinct().Count() != stressLayerDepths.Length ||
        stressLayerDepths.Zip(stressLayerDepths.Skip(1), (a, b) => a > b).Any(inOrder => !inOrder))
        throw new InvalidDataException("Sorting layer depth bands collided or became unordered after registering many layers.");
    if (SortingLayerRegistry.GetDepth("AuditLayer0", int.MaxValue) <=
        SortingLayerRegistry.GetDepth("AuditLayer1", int.MinValue))
        throw new InvalidDataException("Extreme sorting orders crossed the boundary between adjacent layers.");

    var camera = new Camera2D { Position = new Vector2(1000f, 1000f), Zoom = 2f, ClampToBounds = true,
        BoundsMin = Vector2.Zero, BoundsMax = new Vector2(1000f, 800f) };
    camera.ClampPosition(new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600));
    if (camera.Position.X > 800f || camera.Position.Y > 650f)
        throw new InvalidDataException("Camera bounds did not account for viewport size and zoom.");
    camera.Position = new Vector2(1000f, 800f);
    camera.Rotation = MathF.PI / 2f;
    camera.ClampPosition(new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600));
    if (Math.Abs(camera.Position.X - 850f) > .1f || Math.Abs(camera.Position.Y - 600f) > .1f)
        throw new InvalidDataException("Rotated camera bounds did not account for the rotated viewport extents.");
    camera.Position = new Vector2(float.NaN, float.PositiveInfinity);
    camera.ClampPosition(new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 800, 600));
    if (!float.IsFinite(camera.Position.X) || !float.IsFinite(camera.Position.Y))
        throw new InvalidDataException("Camera bounds did not recover a non-finite camera position.");

    var followTarget = new GameObject("FollowTarget");
    followTarget.Transform.Position = new Vector2(100f, 50f);
    var followCamera = new Camera2D();
    var followOwner = new GameObject("CameraRig");
    var follow = followOwner.AddComponent(new CameraFollowComponent(followCamera, followTarget)
        { SmoothSpeed = 100f, Offset = new Vector2(5f, -5f), DeadZone = Vector2.Zero });
    var followTime = new EngineTime();
    typeof(EngineTime).GetProperty(nameof(EngineTime.DeltaTime))!.SetValue(followTime, .5f);
    follow.Update(followTime);
    if (Vector2.Distance(followCamera.Position, new Vector2(105f, 45f)) > .1f)
        throw new InvalidDataException("CameraFollow did not move toward the target plus offset.");
    var snapCamera = new Camera2D();
    var snapTarget = new GameObject("SnapTarget");
    snapTarget.Transform.Position = new Vector2(100f, 50f);
    var snapOwner = new GameObject("SnapCameraRig");
    var snapFollow = snapOwner.AddComponent(new CameraFollowComponent(snapCamera, snapTarget)
        { SmoothSpeed = 0f, DeadZone = new Vector2(10f, 5f) });
    snapFollow.Update(followTime);
    if (Vector2.Distance(snapCamera.Position, new Vector2(90f, 45f)) > .001f)
        throw new InvalidDataException("CameraFollow with zero smoothing did not snap to the dead-zone boundary.");
    snapFollow.Offset = new Vector2(float.NaN, float.PositiveInfinity);
    snapFollow.Update(followTime);
    if (!float.IsFinite(snapCamera.Position.X) || !float.IsFinite(snapCamera.Position.Y))
        throw new InvalidDataException("CameraFollow allowed invalid offsets to poison the camera transform.");
    snapTarget.Transform.Position = new Vector2(float.MaxValue, float.MaxValue);
    snapFollow.Offset = new Vector2(float.MaxValue, float.MaxValue);
    snapFollow.Update(followTime);
    if (!float.IsFinite(snapCamera.Position.X) || !float.IsFinite(snapCamera.Position.Y))
        throw new InvalidDataException("CameraFollow allowed finite-but-overflowing target/offset values to poison the camera.");

    var frozenScene = new Scene("FreezeAxes") { Gravity = Vector2.Zero };
    var frozenObject = new GameObject("FrozenX");
    var frozenBody = frozenObject.AddComponent(new Rigidbody2DComponent
        { FreezePositionX = true, UseGravity = false, Velocity = new Vector2(25f, 10f) });
    frozenBody.AddForce(new Vector2(500f, 0f));
    frozenScene.Add(frozenObject);
    frozenScene.Physics.Step(frozenScene, .1f, Vector2.Zero);
    if (Math.Abs(frozenBody.Velocity.X) > .001f || Math.Abs(frozenObject.Transform.Position.X) > .001f ||
        Math.Abs(frozenBody.Velocity.Y - 10f) > .001f)
        throw new InvalidDataException("Rigidbody freeze-axis constraints were bypassed by accumulated forces.");

    var mixer = new AudioMixer { MasterVolume = .5f, EffectsVolume = .8f };
    if (Math.Abs(mixer.GetOutputVolume(.5f, AudioBus.SoundEffects) - .2f) > .001f)
        throw new InvalidDataException("Audio mixer volume composition was incorrect.");
    Console.WriteLine("PASS input bindings/unbinding, render sorting, rotated camera bounds/follow, Rigidbody freeze constraints and audio mixer levels");
}
catch (Exception ex)
{
    failures.Add("input/render/camera/audio foundations: " + ex.Message);
}

try
{
    // A legacy behaviour may mark an entity as destroyed before the context purges it.
    // Purge must still detach the GameObject from the active Scene.
    var sceneData = new LicSceneData { Name = "DestroyPurge" };
    var project = new LicProjectData { Name = "DestroyPurge", Scenes = [sceneData] };
    var scene = new Scene("DestroyPurge");
    var entities = new List<LicObjectComponent>();
    var contextConstructor = typeof(LicRuntimeContext).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(x => x.GetParameters().Length == 6);
    var context = (LicRuntimeContext)contextConstructor.Invoke(new object?[]
        { project, scene, sceneData, null, new Camera2D(), entities });
    var entityConstructor = typeof(LicObjectComponent).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(x => x.GetParameters().Length == 3);
    var entity = (LicObjectComponent)entityConstructor.Invoke(new object?[]
        { new LicObjectData { Name = "DestroyedByLegacyBehaviour" }, null, new Dictionary<string, LicAssetData>() });
    var gameObject = new GameObject("DestroyedByLegacyBehaviour");
    gameObject.AddComponent(entity);
    scene.Add(gameObject);
    entities.Add(entity);
    typeof(LicObjectComponent).GetProperty(nameof(LicObjectComponent.IsDestroyed))!.SetValue(entity, true);
    var purge = typeof(LicRuntimeContext).GetMethod("PurgeDestroyed", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(LicRuntimeContext).FullName, "PurgeDestroyed");
    purge.Invoke(context, null);
    if (scene.Find("DestroyedByLegacyBehaviour") is not null || entities.Count != 0 || gameObject.Active)
        throw new InvalidDataException("PurgeDestroyed left an already-flagged entity in the active Scene or allowed its stale snapshot to remain active.");
    purge.Invoke(context, null); // Purge remains idempotent.
    Console.WriteLine("PASS destroyed legacy entities are detached from Scene during idempotent purge");
}
catch (Exception ex)
{
    failures.Add("legacy destroyed-entity purge: " + ex.Message);
}

try
{
    var clip = new LicAnimationClipData
    {
        Name = "Move",
        Duration = 2f,
        Loop = false,
        Keyframes = new List<LicAnimationKeyframeData>
        {
            new() { Time = 0f, X = 0f, Y = 10f, Rotation = 350f, ScaleX = 1f, ScaleY = 1f },
            new() { Time = 2f, X = 100f, Y = 30f, Rotation = 10f, ScaleX = 3f, ScaleY = 3f }
        }
    };
    var baseline = new AnimationPose(Vector2.Zero, 0f, Vector2.One);
    var middle = AnimationEvaluator.Evaluate(clip, 1f, baseline);
    if (Math.Abs(middle.Position.X - 50f) > .01f || Math.Abs(middle.Position.Y - 20f) > .01f ||
        Math.Abs(middle.Scale.X - 2f) > .01f || Math.Abs(Math.Abs(middle.Rotation) - 360f) > .1f)
        throw new InvalidDataException($"Animation interpolation failed (x={middle.Position.X}, y={middle.Position.Y}, rot={middle.Rotation}, scale={middle.Scale.X}).");
    var end = AnimationEvaluator.Evaluate(clip, 3f, baseline);
    if (Math.Abs(end.Position.X - 100f) > .01f)
        throw new InvalidDataException("Non-looping animation did not clamp to its last keyframe.");
    clip.Loop = true;
    var looped = AnimationEvaluator.Evaluate(clip, 2.5f, baseline);
    if (Math.Abs(looped.Position.X - 25f) > .01f)
        throw new InvalidDataException("Looping animation did not wrap time into clip duration.");
    Console.WriteLine("PASS animation keyframe interpolation, angle wrap, clamp and loop");
    var malformedClip = new LicAnimationClipData
    {
        Duration = 1f,
        Loop = false,
        Keyframes = new List<LicAnimationKeyframeData>
        {
            new() { Time = float.NaN, X = -999f },
            new() { Time = 4f, X = 80f }
        }
    };
    var malformedPose = AnimationEvaluator.Evaluate(malformedClip, 1f, baseline);
    if (!float.IsFinite(malformedPose.Position.X) || Math.Abs(malformedPose.Position.X - 80f) > .01f)
        throw new InvalidDataException("Animation evaluator did not safely ignore invalid key times and clamp out-of-range keys.");
    var scaleOnly = new LicAnimationClipData
    {
        Duration = 1f,
        Keyframes = [new LicAnimationKeyframeData { Time = 0f, ScaleX = 1f, ScaleY = 1f }, new LicAnimationKeyframeData { Time = 1f, ScaleX = 2f, ScaleY = 2f }]
    };
    var movingBaseline = new AnimationPose(new Vector2(123f, 456f), 27f, Vector2.One);
    var scalePose = AnimationEvaluator.Evaluate(scaleOnly, .5f, movingBaseline);
    if (scalePose.Position != movingBaseline.Position || Math.Abs(scalePose.Rotation - 27f) > .01f || Math.Abs(scalePose.Scale.X - 1.5f) > .01f)
        throw new InvalidDataException("Animation changed transform channels that were not keyed (e.g. live player movement).");
    var unsorted = new LicAnimationClipData { Duration = 2f, Loop = false, Keyframes = [
        new() { Time = 2f, X = 20f }, new() { Time = 0f, X = 0f }, new() { Time = 1f, X = 10f }] };
    if (Math.Abs(AnimationEvaluator.Evaluate(unsorted, 1.5f, baseline).Position.X - 15f) > .01f)
        throw new InvalidDataException("Animation evaluator did not handle unsorted keys correctly.");
    var invalidOnly = new LicAnimationClipData { Duration = 1f, Keyframes = [new() { Time = float.NaN, X = 50f }] };
    if (AnimationEvaluator.Evaluate(invalidOnly, .5f, movingBaseline) != movingBaseline)
        throw new InvalidDataException("Animation evaluator changed the baseline when all key times were invalid.");
    var timelineKeys = new List<LicAnimationKeyframeData>
    {
        new() { Time = 2f }, new() { Time = float.NaN }, new() { Time = 0f }, new() { Time = 1f }
    };
    if (AnimationEvaluator.FindNearestKeyframeIndex(timelineKeys, 1.7f) != 0 ||
        AnimationEvaluator.FindNearestKeyframeIndex(timelineKeys, 0.1f) != 2 ||
        AnimationEvaluator.FindNearestKeyframeIndex(timelineKeys, float.NaN) != -1 ||
        AnimationEvaluator.FindNearestKeyframeIndex(new[] { new LicAnimationKeyframeData { Time = float.PositiveInfinity } }, 1f) != -1 ||
        AnimationEvaluator.FindNearestKeyframeIndex(new[]
        {
            new LicAnimationKeyframeData { Time = -float.MaxValue },
            new LicAnimationKeyframeData { Time = float.MaxValue }
        }, float.MaxValue) != 1)
        throw new InvalidDataException("Timeline nearest-key selection did not handle unsorted or invalid key times correctly.");
    Console.WriteLine("PASS animation handles unsorted/invalid keys and timeline nearest-key selection in linear time");

    var sparseClip = new LicAnimationClipData
    {
        Duration = 2f, Loop = false,
        Keyframes = [
            new LicAnimationKeyframeData { Time = 0f, X = 10f },
            new LicAnimationKeyframeData { Time = 1f }, // A blank key must not alter keyed or unkeyed channels.
            new LicAnimationKeyframeData { Time = 2f, X = 30f, ScaleY = 4f }
        ]
    };
    var sparseBaseline = new AnimationPose(new Vector2(999f, 456f), 27f, new Vector2(2f, 3f));
    var sparseMiddle = AnimationEvaluator.Evaluate(sparseClip, 1f, sparseBaseline);
    if (Math.Abs(sparseMiddle.Position.X - 20f) > .01f || sparseMiddle.Position.Y != sparseBaseline.Position.Y ||
        Math.Abs(sparseMiddle.Rotation - sparseBaseline.Rotation) > .01f || sparseMiddle.Scale.X != sparseBaseline.Scale.X ||
        Math.Abs(sparseMiddle.Scale.Y - 3.5f) > .01f)
        throw new InvalidDataException("Sparse/blank keyframes modified unkeyed transform channels.");

    var duplicateTimeClip = new LicAnimationClipData
    {
        Duration = 1f, Loop = false,
        Keyframes = [new LicAnimationKeyframeData { Time = .5f, X = 10f }, new LicAnimationKeyframeData { Time = .5f, X = 25f }]
    };
    if (Math.Abs(AnimationEvaluator.Evaluate(duplicateTimeClip, .5f, baseline).Position.X - 25f) > .01f)
        throw new InvalidDataException("Duplicate keyframe timestamps did not deterministically select the last serialized key.");

    var futureKeyClip = new LicAnimationClipData
    {
        Duration = 1f, Loop = false,
        Keyframes = [
            new LicAnimationKeyframeData { Time = .5f, X = 40f },
            new LicAnimationKeyframeData { Time = .75f, Y = 200f },
            new LicAnimationKeyframeData { Time = 1f, X = 80f, Rotation = 90f },
            new LicAnimationKeyframeData { Time = 1f }]
    };
    var futureBaseline = new AnimationPose(new Vector2(17f, 29f), 12f, new Vector2(2f, 3f));
    var beforeFirstX = AnimationEvaluator.Evaluate(futureKeyClip, .25f, futureBaseline);
    if (beforeFirstX != futureBaseline)
        throw new InvalidDataException("Animation modified live transform channels before the first key authored for those channels.");
    var betweenSparseKeys = AnimationEvaluator.Evaluate(futureKeyClip, .625f, futureBaseline);
    if (Math.Abs(betweenSparseKeys.Position.X - 50f) > .01f || betweenSparseKeys.Position.Y != futureBaseline.Position.Y ||
        Math.Abs(betweenSparseKeys.Rotation - futureBaseline.Rotation) > .01f || betweenSparseKeys.Scale != futureBaseline.Scale)
        throw new InvalidDataException("A partial keyframe modified transform channels that were not keyed at that time.");
    var afterOnlyYKey = AnimationEvaluator.Evaluate(futureKeyClip, .875f, futureBaseline);
    if (Math.Abs(afterOnlyYKey.Position.Y - 200f) > .01f || Math.Abs(afterOnlyYKey.Position.X - 70f) > .01f ||
        Math.Abs(afterOnlyYKey.Rotation - 12f) > .01f)
        throw new InvalidDataException("Independent animation channels did not interpolate or hold their authored values correctly.");
    var emptyKeyClip = new LicAnimationClipData
    {
        Duration = 1f, Loop = false,
        Keyframes = [new LicAnimationKeyframeData { Time = .25f }, new LicAnimationKeyframeData { Time = .75f }]
    };
    if (AnimationEvaluator.Evaluate(emptyKeyClip, .5f, futureBaseline) != futureBaseline)
        throw new InvalidDataException("An animation clip containing only blank keyframes changed the live transform.");
    var repeatedRotationClip = new LicAnimationClipData
    {
        Duration = 1f, Loop = false,
        Keyframes = [new LicAnimationKeyframeData { Time = .5f, Rotation = 45f }, new LicAnimationKeyframeData { Time = .5f, Rotation = 135f }]
    };
    if (Math.Abs(AnimationEvaluator.Evaluate(repeatedRotationClip, .5f, futureBaseline).Rotation - 135f) > .01f)
        throw new InvalidDataException("Duplicate-time rotation keys did not select the last serialized rotation value.");
    Console.WriteLine("PASS animation sparse channels, pre-key live baseline, partial channels and duplicate-time determinism");

    var reverseClip = new LicAnimationClipData
    {
        Duration = 1f, Loop = false,
        Keyframes = [new LicAnimationKeyframeData { Time = 0f, X = 0f }, new LicAnimationKeyframeData { Time = 1f, X = 100f }]
    };
    var animatedObject = new GameObject("ReverseAnimation");
    animatedObject.Transform.Position = new Vector2(100f, 0f);
    var reversePlayer = animatedObject.AddComponent(new AnimationPlayerComponent(reverseClip) { Speed = -1f });
    reversePlayer.Start();
    var reverseTime = new EngineTime();
    typeof(EngineTime).GetProperty(nameof(EngineTime.DeltaTime))!.SetValue(reverseTime, .1f);
    for (var i = 0; i < 10; i++) reversePlayer.Update(reverseTime);
    if (reversePlayer.Playing || Math.Abs(reversePlayer.TimePosition) > .001f || Math.Abs(animatedObject.Transform.Position.X) > .01f)
        throw new InvalidDataException("Reverse non-looping animation did not start at the end and stop at time zero.");
    reversePlayer.Play(restart: true);
    if (!reversePlayer.Playing || Math.Abs(reversePlayer.TimePosition - 1f) > .001f)
        throw new InvalidDataException("Restarting reverse playback did not reset the playhead to clip duration.");
    var loopingObject = new GameObject("LongLoopAnimation");
    var loopingClip = new LicAnimationClipData { Duration = .25f, Loop = true, Keyframes = [new LicAnimationKeyframeData { Time = 0f, X = 0f }, new LicAnimationKeyframeData { Time = .25f, X = 10f }] };
    var loopingPlayer = loopingObject.AddComponent(new AnimationPlayerComponent(loopingClip));
    loopingPlayer.Start();
    for (var i = 0; i < 100_000; i++) loopingPlayer.Update(reverseTime);
    if (!float.IsFinite(loopingPlayer.TimePosition) || loopingPlayer.TimePosition < 0f || loopingPlayer.TimePosition >= loopingClip.Duration)
        throw new InvalidDataException("Looping animation playhead accumulated beyond clip duration.");
    Console.WriteLine("PASS reverse non-looping animation playback, restart and bounded long-running loop");
}
catch (Exception ex)
{
    failures.Add("animation evaluator: " + ex.Message);
}

try
{
    var tileMap = new LicTileMapData { Width = 2, Height = 2, TileSize = 16 };
    tileMap.EnsureCellStorage();
    tileMap.SetCell(1, 1, 2);
    if (tileMap.GetCell(1, 1) != 2 || tileMap.GetCell(-1, 0) != 0)
        throw new InvalidDataException("Tile map cell access failed.");
    tileMap.Resize(3, 2);
    if (tileMap.Width != 3 || tileMap.Height != 2 || tileMap.GetCell(1, 1) != 2 || tileMap.Cells.Count != 6)
        throw new InvalidDataException("Tile map resize did not preserve the overlapping cells.");
    var threw = false;
    try { tileMap.SetCell(99, 99, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
    if (!threw) throw new InvalidDataException("Tile map accepted an out-of-range coordinate.");
    var fillMap = new LicTileMapData { Width = 5, Height = 4 };
    fillMap.EnsureCellStorage();
    fillMap.PaintRectangle(1, 1, 3, 2, 1);
    if (fillMap.GetCell(2, 2) != 1 || fillMap.GetNeighborMask(2, 2, 1) != 11)
        throw new InvalidDataException("TileMap rectangle painting or adjacency mask failed.");
    var changed = fillMap.FloodFill(0, 0, 2);
    if (changed != 14 || fillMap.GetCell(0, 0) != 2 || fillMap.GetCell(2, 2) != 1)
        throw new InvalidDataException("TileMap flood fill crossed a boundary or changed the wrong number of cells.");
    fillMap.Fill(3);
    if (fillMap.Cells.Any(x => x != 3)) throw new InvalidDataException("TileMap fill did not update the entire map.");
    Console.WriteLine("PASS TileMap cell editing, bounds, resize, brush rectangles, flood fill and adjacency masks");
    var runtimeMapData = new LicTileMapData { Width = 900, Height = 900, TileSize = 999, Palette = Enumerable.Range(0, 300).Select(_ => "#FFFFFF").ToList() };
    runtimeMapData.EnsureCellStorage();
    var runtimeMap = new TileMapComponent(runtimeMapData);
    if (runtimeMap.Data.Width != 512 || runtimeMap.Data.Height != 512 || runtimeMap.Data.TileSize != 256 || runtimeMap.Data.Palette.Count != 256)
        throw new InvalidDataException("TileMap data did not clamp untrusted dimensions, tile size and palette safely.");

    var collisionMap = new LicTileMapData { Name = "BakedMap", X = 10f, Y = 20f, Width = 4, Height = 3, TileSize = 16, CollisionEnabled = true };
    collisionMap.EnsureCellStorage();
    collisionMap.PaintRectangle(1, 1, 2, 1, 1);
    var collisionScene = new Scene("TileMapCollisionBake");
    var bakeMethod = typeof(LicSceneFactory).GetMethod("AddTileMapCollisionGeometry", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(LicSceneFactory).FullName, "AddTileMapCollisionGeometry");
    bakeMethod.Invoke(null, new object[] { collisionScene, collisionMap });
    var bakedColliders = collisionScene.Objects.Select(x => x.GetComponent<BoxCollider2DComponent>()).Where(x => x is not null).ToArray();
    if (bakedColliders.Length != 1 || bakedColliders[0]!.Size != new Vector2(32f, 16f)
        || collisionScene.Objects[0].Transform.Position != new Vector2(50f, 44f))
        throw new InvalidDataException("TileMap collision baker did not merge adjacent solid cells into the expected static rectangle.");

    var caveA = new LicTileMapData { Width = 32, Height = 24 };
    var caveB = new LicTileMapData { Width = 32, Height = 24 };
    caveA.EnsureCellStorage(); caveB.EnsureCellStorage();
    caveA.GenerateCave(1, .46, 5, 1234);
    caveB.GenerateCave(1, .46, 5, 1234);
    if (!caveA.Cells.SequenceEqual(caveB.Cells)
        || Enumerable.Range(0, caveA.Width).Any(x => caveA.GetCell(x, 0) == 0 || caveA.GetCell(x, caveA.Height - 1) == 0)
        || Enumerable.Range(0, caveA.Height).Any(y => caveA.GetCell(0, y) == 0 || caveA.GetCell(caveA.Width - 1, y) == 0))
        throw new InvalidDataException("Seeded cave generation was not deterministic or did not preserve solid map borders.");
}
catch (Exception ex)
{
    failures.Add("tilemap operations: " + ex.Message);
}

try
{
    var directory = Path.Combine(Path.GetTempPath(), "PixelEngineSaveStore_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var firstStore = new PersistentSaveStore("Test/Game", directory);
        firstStore.Set("coins", "125");
        firstStore.Set("level", "3");
        var secondStore = new PersistentSaveStore("Test/Game", directory);
        if (secondStore.Get("coins") != "125" || secondStore.Get("level") != "3")
            throw new InvalidDataException("Persistent save values did not survive a new store instance.");
        if (!secondStore.Remove("level") || secondStore.TryGet("level", out _))
            throw new InvalidDataException("Persistent save key removal failed.");

        var corruptPath = Path.Combine(directory, "Corrupt.json");
        File.WriteAllText(corruptPath, "{ definitely not json");
        var recovered = new PersistentSaveStore("Corrupt", directory);
        if (recovered.LoadWarning is null || File.Exists(corruptPath))
            throw new InvalidDataException("Corrupt save data was not quarantined during recovery.");

        var blocker = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocker, "file");
        var memoryOnly = new PersistentSaveStore("MemoryOnly", blocker);
        memoryOnly.Set("session", "ok");
        if (memoryOnly.IsPersistent || memoryOnly.Get("session") != "ok")
            throw new InvalidDataException("Save store did not gracefully fall back to session memory when storage was unavailable.");
    }
    finally { Directory.Delete(directory, recursive: true); }
    Console.WriteLine("PASS project save persistence, safe file recovery and in-memory fallback");
}
catch (Exception ex)
{
    failures.Add("persistent save store: " + ex.Message);
}

try
{
    var profiler = new EngineProfiler();
    profiler.RecordSample(1d / 50d, 2.0, 3.0, 12, 20, 1024 * 1024);
    profiler.RecordSample(1d / 70d, 1.0, 2.0, 14, 22, 2 * 1024 * 1024);
    var snapshot = profiler.Snapshot;
    if (snapshot.FrameCount != 2 || snapshot.FramesPerSecond <= 0 || snapshot.ObjectCount != 14 || snapshot.ComponentCount != 22 || snapshot.ManagedMemoryBytes != 2 * 1024 * 1024)
        throw new InvalidDataException("Engine profiler did not report the latest sample correctly.");
    profiler.RecordSample(double.NaN, double.NaN, double.PositiveInfinity, -5, -10, -42);
    var sanitizedProfiler = profiler.Snapshot;
    if (sanitizedProfiler.FrameCount != 3 || sanitizedProfiler.ManagedMemoryBytes != 0 || sanitizedProfiler.ObjectCount != 0 || sanitizedProfiler.ComponentCount != 0 || sanitizedProfiler.UpdateMilliseconds < 0 || sanitizedProfiler.DrawMilliseconds < 0)
        throw new InvalidDataException("Engine profiler did not sanitize invalid sample values.");
    // Restore an ordinary sample so later rolling-window expectations remain explicit.
    profiler = new EngineProfiler();
    profiler.RecordSample(1d / 50d, 2.0, 3.0, 12, 20, 1024 * 1024);
    profiler.RecordSample(1d / 70d, 1.0, 2.0, 14, 22, 2 * 1024 * 1024);
    Console.WriteLine("PASS EngineProfiler rolling sample and metrics");
    var radial = LightingRenderer.BuildRadialGradientPixels(9);
    var centerPixel = radial[4 * 9 + 4];
    var cornerPixel = radial[0];
    if (centerPixel.R < 250 || centerPixel.A < 250 || cornerPixel.R != 0 || cornerPixel.G != 0 || cornerPixel.B != 0 || cornerPixel.A != 0)
        throw new InvalidDataException("Radial lighting mask must premultiply RGB with the alpha falloff to avoid square additive glows.");
    if (radial.Any(pixel => pixel.R != pixel.A || pixel.G != pixel.A || pixel.B != pixel.A))
        throw new InvalidDataException("Radial lighting mask is not premultiplied consistently.");
    Console.WriteLine("PASS radial lighting mask falloff and additive-blend premultiplication");
    var lightTint = LightingRenderer.ComputeLightTint(new Color(255, 128, 64, 128), .5f);
    if (lightTint.A is < 63 or > 65 || lightTint.R > lightTint.A || lightTint.G > lightTint.A || lightTint.B > lightTint.A)
        throw new InvalidDataException("Light tint did not include source alpha and intensity correctly.");
    if (LightingRenderer.ComputeLightTint(Color.White, float.NaN).A != 0)
        throw new InvalidDataException("Invalid light intensity did not become transparent.");
    Console.WriteLine("PASS lighting tint intensity/alpha composition");
    var profileDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineProfiler_" + Guid.NewGuid().ToString("N"));
    try
    {
        RuntimeProfilerStore.WriteSnapshot(snapshot, profileDirectory, processId: 9001);
        if (!RuntimeProfilerStore.TryReadLatest(TimeSpan.FromMinutes(1), out var document, profileDirectory) ||
            document is null || document.Snapshot.FrameCount != snapshot.FrameCount)
            throw new InvalidDataException("Runtime profiler store did not round-trip its snapshot.");
    }
    finally { if (Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory, recursive: true); }
    for (var i = 0; i < 150; i++) profiler.RecordSample(1d / 60d, 1, 2, i, i * 2, 4096 + i);
    var wrapped = profiler.Snapshot;
    if (wrapped.FrameCount != 152 || wrapped.ObjectCount != 149 || wrapped.ComponentCount != 298 || wrapped.ManagedMemoryBytes != 4245)
        throw new InvalidDataException("Profiler ring buffer did not retain the latest sample after wrapping.");
}
catch (Exception ex)
{
    failures.Add("profiler: " + ex.Message);
}

try
{
    var time = new EngineTime();
    typeof(EngineTime).GetProperty(nameof(EngineTime.DeltaTime))!.SetValue(time, .1f);
    var emitterObject = new GameObject("ParticleTest");
    var emitter = emitterObject.AddComponent(new ParticleEmitterComponent(seed: 42)
    {
        Rate = 10000f,
        Lifetime = .05f,
        MaxParticles = 12
    });
    emitter.Update(time);
    if (emitter.ActiveParticles != 12 || emitter.AllocatedParticleCount != 12)
        throw new InvalidDataException($"Particle emitter did not fill its bounded pool (active={emitter.ActiveParticles}, allocated={emitter.AllocatedParticleCount}).");
    emitter.Update(time); // Expire the short-lived particles and refill from the reusable pool.
    if (emitter.ActiveParticles != 12 || emitter.AllocatedParticleCount != 12)
        throw new InvalidDataException("Particle emitter exceeded its cap or allocated replacement particles instead of reusing its pool.");
    Console.WriteLine("PASS particle emitter bounded reusable pool and deterministic update");
    var fadedTint = ParticleEmitterComponent.ComputeParticleTint(new Color(200, 120, 80, 200), .5f);
    if (fadedTint.A is < 99 or > 101 || fadedTint.R > fadedTint.A || fadedTint.G > fadedTint.A || fadedTint.B > fadedTint.A)
        throw new InvalidDataException("Particle tint is not correctly premultiplied for AlphaBlend.");
    if (ParticleEmitterComponent.ComputeParticleTint(Color.White, float.NaN).A != 0)
        throw new InvalidDataException("Particle tint did not safely fade invalid particle age to transparent.");
    Console.WriteLine("PASS particle tint premultiplied alpha and invalid-age safety");
}
catch (Exception ex)
{
    failures.Add("particle emitter: " + ex.Message);
}

try
{
    var diagnosticProject = LicProjectReader.Parse("""{"name":"Diagnostics","scenes":[{"name":"Main","objects":[{"name":"Hero","kind":"sprite","sprite":"missing.png","script":"missing.lua"}]}]}""");
    var issues = ProjectDiagnostics.Analyze(diagnosticProject);
    if (!issues.Any(x => x.Code == "PE1104") || !issues.Any(x => x.Code == "PE1203"))
        throw new InvalidDataException("Diagnostics did not detect missing asset/script references.");
    var badAssetProject = new LicProjectData
    {
        Scenes = [new LicSceneData { Name = "Main" }],
        Assets =
        [
            new LicAssetData { Name = "broken.png", Type = "png", Base64 = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]) },
            new LicAssetData { Name = "broken.wav", Type = "wav", Base64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("NOPE0000FAIL")) }
        ]
    };
    var badAssetIssues = ProjectDiagnostics.Analyze(badAssetProject);
    if (!badAssetIssues.Any(x => x.Code == "PE1106") || !badAssetIssues.Any(x => x.Code == "PE1107"))
        throw new InvalidDataException("Diagnostics did not validate embedded PNG and WAV signatures.");
    var diagnosticsExtensions = new LicProjectData
    {
        Name = "DiagnosticsExtensions",
        Scenes = [new LicSceneData { Name = "Main", Objects = [new LicObjectData { Name = "Broken", Components = [new LicComponentData { Type = "SpriteRenderer", Texture = "sound.wav", Mass = float.NaN }] }] }],
        Scripts = [new LicScriptData { Name = "legacy.cs", Language = "csharp", Code = "public class Legacy {}" }],
        Assets = [
            new LicAssetData { Name = "sound.wav", Type = "wav", Base64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("RIFF0000WAVE")) },
            new LicAssetData { Name = "empty.png", Type = "png", Base64 = "" },
            new LicAssetData { Name = "editor.bin", Type = "custom", Base64 = "AQID" }
        ],
        Prefabs = [new LicPrefabData { Name = "BrokenPrefab", Object = new LicObjectData { Name = "Template", Sprite = "missing-template.png", Script = "missing-template.lua", Components = [new LicComponentData { Type = "AudioSource", Clip = "empty.png", ScriptName = "missing-component.lua" }] } }]
    };
    var extensionIssues = ProjectDiagnostics.Analyze(diagnosticsExtensions);
    foreach (var requiredCode in new[] { "PE1112", "PE1113", "PE1114", "PE1108", "PE1109", "PE1207", "PE1304" })
        if (!extensionIssues.Any(x => x.Code == requiredCode))
            throw new InvalidDataException($"Diagnostics did not report the expected issue code {requiredCode}.");
    var runtimeBudgetProject = new LicProjectData
    {
        Scenes = [new LicSceneData { Name = "Main" }],
        EditorData = new LicEditorData
        {
            Lights = Enumerable.Range(0, 257).Select(i => new LicLightData { Name = "Light" + i }).ToList(),
            ParticleEmitters = Enumerable.Range(0, 65).Select(i => new LicParticleEmitterData { Name = "Emitter" + i, MaxParticles = 512 }).ToList()
        }
    };
    var budgetIssues = ProjectDiagnostics.Analyze(runtimeBudgetProject);
    if (!budgetIssues.Any(x => x.Code == "PE1505") || !budgetIssues.Any(x => x.Code == "PE1605") || !budgetIssues.Any(x => x.Code == "PE1606"))
        throw new InvalidDataException("Diagnostics did not warn about runtime light and particle scene budgets.");
    var cleanProject = LicProjectReader.Parse("""{"name":"Clean","scenes":[{"name":"Main","objects":[{"name":"Hero","kind":"rect","w":16,"h":16}]}]}""");
    if (ProjectDiagnostics.Analyze(cleanProject).Any(x => x.Severity == DiagnosticSeverity.Error))
        throw new InvalidDataException("Diagnostics reported errors for a minimal valid project.");
    var invalidCameraFollowProject = new LicProjectData
    {
        Name = "InvalidCameraFollow",
        Scenes = [new LicSceneData { Name = "Main", Objects = [new LicObjectData
        {
            Name = "Hero", Components = [new LicComponentData
            {
                Type = "CameraFollow", CameraTarget = "MissingTarget", CameraOffsetX = float.NaN,
                CameraSmoothSpeed = 500f, CameraDeadZoneX = -1f
            }]
        }] }]
    };
    var invalidCameraIssues = ProjectDiagnostics.Analyze(invalidCameraFollowProject);
    if (!invalidCameraIssues.Any(x => x.Code == "PE1210") || !invalidCameraIssues.Any(x => x.Code == "PE1211"))
        throw new InvalidDataException("Diagnostics did not flag invalid CameraFollow settings or a missing target.");
    var validCameraFollowProject = LicProjectReader.Parse("""{"name":"CameraFollowValid","scenes":[{"name":"Main","objects":[{"name":"Hero","components":[{"type":"CameraFollow","cameraTarget":"hErO","cameraSmoothSpeed":0}]}]}]}""");
    if (ProjectDiagnostics.Analyze(validCameraFollowProject).Any(x => x.Code is "PE1210" or "PE1211"))
        throw new InvalidDataException("Diagnostics did not resolve a case-insensitive CameraFollow self target.");
    var unsupportedComponentsProject = LicProjectReader.Parse("""{"name":"UnsupportedComponents","scenes":[{"name":"Main","objects":[{"name":"Hero","components":[{"type":"Rigidbody2D"},{"type":"Rigidbody2D"},{"type":"FutureJoint"},{"type":"AudioSource","audioBus":"Voices"},{"type":"BoxCollider2D","collisionLayer":99}]}]}]}""");
    var unsupportedComponentIssues = ProjectDiagnostics.Analyze(unsupportedComponentsProject);
    foreach (var requiredCode in new[] { "PE1207", "PE1212", "PE1213", "PE1214" })
        if (!unsupportedComponentIssues.Any(x => x.Code == requiredCode))
            throw new InvalidDataException($"Diagnostics did not report unsupported/conflicting component configuration {requiredCode}.");
    var manySortingLayersProject = new LicProjectData
    {
        Name = "TooManySortingLayers",
        Scenes = [new LicSceneData { Name = "Main", Objects = Enumerable.Range(0, 253).Select(index => new LicObjectData
        {
            Name = "Layered" + index,
            Components = [new LicComponentData { Type = "SpriteRenderer", SortingLayer = "CustomLayer" + index }]
        }).ToList() }]
    };
    if (!ProjectDiagnostics.Analyze(manySortingLayersProject).Any(x => x.Code == "PE1215"))
        throw new InvalidDataException("Diagnostics did not report a project that exceeds the stable sorting-layer budget.");
    var scopedAnimationProject = LicProjectReader.Parse("""{"name":"ScopedAnimations","scenes":[{"name":"A","objects":[{"name":"HeroA","components":[{"type":"Animator","animation":"Idle"}]}]},{"name":"B","objects":[{"name":"HeroB","components":[{"type":"Animator","animation":"Idle"}]}]}],"pixelEngine":{"animations":[{"name":"Idle","scene":"A","target":"HeroA","duration":1,"keyframes":[{"time":0}]},{"name":"Idle","scene":"B","target":"HeroB","duration":1,"keyframes":[{"time":0}]}]}}""");
    if (ProjectDiagnostics.Analyze(scopedAnimationProject).Any(x => x.Code is "PE1206" or "PE1400"))
        throw new InvalidDataException("Diagnostics did not support same-named animations scoped to different scenes.");
    var wrongSceneAnimator = LicProjectReader.Parse("""{"name":"WrongSceneAnimator","scenes":[{"name":"A","objects":[{"name":"HeroA","components":[{"type":"Animator","animation":"Idle"}]}]},{"name":"B","objects":[]}],"pixelEngine":{"animations":[{"name":"Idle","scene":"B","target":"HeroB","duration":1,"keyframes":[{"time":0}]}]}}""");
    if (!ProjectDiagnostics.Analyze(wrongSceneAnimator).Any(x => x.Code == "PE1206"))
        throw new InvalidDataException("Diagnostics failed to flag an Animator that references a clip from another scene/target.");
    var invalidTileProject = LicProjectReader.Parse("""{"name":"InvalidTile","scenes":[{"name":"Main","objects":[]}],"pixelEngine":{"tileMaps":[{"name":"Map","width":2,"height":2,"tileSize":16,"palette":["#00000000","#FFFFFF"],"cells":[0,99,0,0]}]}}""");
    if (!ProjectDiagnostics.Analyze(invalidTileProject).Any(x => x.Code == "PE1703"))
        throw new InvalidDataException("LIC reader discarded an invalid tile index before Diagnostics could report it.");
    var invalidTileDimensions = LicProjectReader.Parse("""{"name":"InvalidTileDimensions","scenes":[{"name":"Main","objects":[]}],"pixelEngine":{"tileMaps":[{"name":"Oversized","width":513,"height":1,"tileSize":300,"palette":["#00000000","#FFFFFF"],"cells":[1,2,3]}]}}""");
    var oversizedMap = invalidTileDimensions.EditorData!.TileMaps[0];
    if (oversizedMap.Width != 513 || !ProjectDiagnostics.Analyze(invalidTileDimensions).Any(x => x.Code == "PE1701"))
        throw new InvalidDataException("LIC reader normalized malformed TileMap dimensions before Diagnostics could report them.");
    ProjectDiagnostics.ApplySafeFixes(invalidTileDimensions);
    if (oversizedMap.Width != 512 || oversizedMap.TileSize != 256 || oversizedMap.GetCell(0, 0) != 1 || oversizedMap.GetCell(1, 0) != 2)
        throw new InvalidDataException("TileMap safe repair did not clamp dimensions while preserving overlapping authored cells.");
    var shortTileBuffer = LicProjectReader.Parse("""{"name":"ShortTileBuffer","scenes":[{"name":"Main","objects":[]}],"pixelEngine":{"tileMaps":[{"name":"Short","width":2,"height":2,"tileSize":16,"palette":["#00000000","#FFFFFF"],"cells":[1]}]}}""");
    if (!ProjectDiagnostics.Analyze(shortTileBuffer).Any(x => x.Code == "PE1702"))
        throw new InvalidDataException("LIC reader silently padded malformed TileMap cell buffers before Diagnostics could report them.");
    var invalidAnimationChannels = LicProjectReader.Parse("""{"name":"InvalidAnimationChannels","scenes":[{"name":"Main","objects":[]}],"pixelEngine":{"animations":[{"name":"Bad","scene":"Main","duration":1,"keyframes":[{"time":0,"x":"NaN","scaleX":500}]}]}}""");
    var animationChannelIssues = ProjectDiagnostics.Analyze(invalidAnimationChannels);
    if (!animationChannelIssues.Any(x => x.Code == "PE1407") || !animationChannelIssues.Any(x => x.Code == "PE1408"))
        throw new InvalidDataException("Diagnostics did not detect invalid animation channels and out-of-range scale values.");
    Console.WriteLine("PASS ProjectDiagnostics references, prefab templates, asset signatures/formats, C# runtime warning, component values and animation/tilemap channels");
}
catch (Exception ex)
{
    failures.Add("project diagnostics: " + ex.Message);
}

try
{
    var damaged = new LicProjectData
    {
        Name = "SafeFixes",
        Scenes = [new LicSceneData { Name = "Main" }],
        EditorData = new LicEditorData
        {
            Lights = [new LicLightData { Name = "BadLight", X = float.NaN, Y = float.PositiveInfinity, Radius = 9000, Intensity = float.NaN, Color = "bad" }],
            ParticleEmitters = [new LicParticleEmitterData { Name = "BadEmitter", X = float.NaN, Y = float.NaN, Rate = float.NaN, Lifetime = -1, Speed = float.PositiveInfinity, Spread = -5, Size = float.NaN, Gravity = 20000, MaxParticles = 50000, Color = "bad" }],
            TileMaps = [new LicTileMapData { Name = "BadMap", X = float.NaN, Y = float.PositiveInfinity, Width = 2, Height = 2, Cells = [0, 1, 99, 0], Palette = ["bad"] }],
            Animations = [new LicAnimationClipData { Name = "BadClip", Duration = 1f, Keyframes = [new LicAnimationKeyframeData { Time = 0f, ScaleX = 500f, ScaleY = float.NaN, X = float.PositiveInfinity, Rotation = float.NaN }] }]
        }
    };
    var safeFixes = ProjectDiagnostics.ApplySafeFixes(damaged);
    var repairedLight = damaged.EditorData!.Lights[0];
    var repairedEmitter = damaged.EditorData.ParticleEmitters[0];
    var repairedMap = damaged.EditorData.TileMaps[0];
    var repairedKey = damaged.EditorData.Animations[0].Keyframes[0];
    if (safeFixes.Count < 13 || !float.IsFinite(repairedLight.X) || !float.IsFinite(repairedLight.Intensity) || repairedLight.Intensity < 0 || repairedLight.Intensity > 1 || repairedLight.Radius != 4096f ||
        !float.IsFinite(repairedEmitter.Rate) || !float.IsFinite(repairedEmitter.Speed) || !float.IsFinite(repairedEmitter.Size) || repairedEmitter.MaxParticles > 10000 || repairedEmitter.Gravity != 10000f ||
        repairedMap.X != 0f || repairedMap.Y != 0f || repairedMap.Cells.Count != repairedMap.Width * repairedMap.Height || repairedMap.Cells.Any(x => x >= repairedMap.Palette.Count) ||
        repairedKey.ScaleX != 100f || repairedKey.ScaleY.HasValue || repairedKey.X.HasValue || repairedKey.Rotation.HasValue)
        throw new InvalidDataException("Diagnostics safe repair did not normalize malformed stage 8 metadata.");
    if (ProjectDiagnostics.ApplySafeFixes(damaged).Count != 0)
        throw new InvalidDataException("Diagnostics safe repair is not idempotent.");

    var globalClipProject = new LicProjectData
    {
        Scenes = [new LicSceneData { Name = "Main", Objects = [new LicObjectData { Name = "Hero" }] }],
        EditorData = new LicEditorData { Animations = [new LicAnimationClipData { Name = "Global", Scene = "", Target = "", Duration = 1f, Keyframes = [new LicAnimationKeyframeData { Time = 0 }] }] }
    };
    var globalIssues = ProjectDiagnostics.Analyze(globalClipProject);
    if (globalIssues.Any(x => x.Code is "PE1402" or "PE1403"))
        throw new InvalidDataException("Diagnostics rejected a global animation clip with intentionally blank scene/target fields.");
    Console.WriteLine("PASS Diagnostics safe fixes, NaN handling, idempotence and global animation references");
}
catch (Exception ex)
{
    failures.Add("diagnostics safe fixes: " + ex.Message);
}

try
{
    var prefabRegression = LicProjectReader.Parse("""{"name":"PrefabRegression","scenes":[{"name":"Main","objects":[{"name":"Hero","kind":"rect","x":1,"y":2,"w":8,"h":9,"rotation":15,"color":"#112233","components":[{"type":"SpriteRenderer","tint":"#ABCDEF"}]}]},{"name":"Other","objects":[]}]}""");
    var source = prefabRegression.Scenes[0].Objects[0];
    var prefab = LicProjectOperations.CreatePrefab(prefabRegression, source);
    var first = LicProjectOperations.InstantiatePrefab(prefabRegression.Scenes[0], prefab);
    var second = LicProjectOperations.InstantiatePrefab(prefabRegression.Scenes[1], prefab);
    first.X = 111; first.Y = 222; first.Rotation = 33;
    second.X = -10; second.Y = 44; second.Rotation = 123;
    source.Width = 64; source.Height = 32; source.Color = "#445566"; source.Rotation = 75;
    source.Components[0].Tint = "#FFFFFF";
    var count = LicProjectOperations.UpdatePrefabFromObject(prefabRegression, prefab, source, true);
    if (count != 2 || first.Width != 64 || second.Height != 32 || first.Color != "#445566" || first.Components[0].Tint != "#FFFFFF")
        throw new InvalidDataException("Prefab template fields did not propagate to instances across scenes.");
    if (first.X != 111 || first.Y != 222 || first.Rotation != 33 || second.X != -10 || second.Y != 44 || second.Rotation != 123)
        throw new InvalidDataException("Prefab update overwrote one or more per-instance transform overrides.");
    if (!LicProjectOperations.RevertPrefabInstance(prefabRegression, first) || first.X != 111 || first.Y != 222 || first.Rotation != 33 || first.Width != 64)
        throw new InvalidDataException("Prefab revert failed to preserve instance transform while restoring template fields.");
    Console.WriteLine("PASS prefab propagation across scenes and per-instance transform preservation");
}
catch (Exception ex)
{
    failures.Add("prefab propagation regression: " + ex.Message);
}

try
{
    var sceneMetadataProject = LicProjectReader.Parse("""{"name":"SceneMetadata","scenes":[{"name":"Main","objects":[{"name":"Hero","components":[{"type":"Animator","animation":"Idle"}]}]},{"name":"Other","objects":[]}],"pixelEngine":{"animations":[{"name":"Idle","scene":"Main","target":"Hero","duration":1,"keyframes":[{"time":0}]}],"lights":[{"name":"Key","scene":"Main"}],"particleEmitters":[{"name":"Dust","scene":"Main"}],"tileMaps":[{"name":"Ground","scene":"Main","width":2,"height":2,"cells":[0,1,0,1]}]}}""");
    var duplicated = LicProjectOperations.DuplicateScene(sceneMetadataProject, 0);
    var duplicateName = sceneMetadataProject.Scenes[duplicated].Name;
    var meta = sceneMetadataProject.EditorData!;
    if (!meta.Animations.Any(x => x.Scene == duplicateName) || !meta.Lights.Any(x => x.Scene == duplicateName) ||
        !meta.ParticleEmitters.Any(x => x.Scene == duplicateName) || !meta.TileMaps.Any(x => x.Scene == duplicateName))
        throw new InvalidDataException("Duplicating a scene did not clone associated Stage 8 authoring data.");
    var clonedClip = meta.Animations.Single(x => x.Scene == duplicateName);
    var clonedAnimator = sceneMetadataProject.Scenes[duplicated].Objects.Single(x => x.Name == "Hero").Components.Single(x => x.Type == "Animator");
    if (clonedAnimator.Animation != clonedClip.Name || clonedClip.Target != "Hero")
        throw new InvalidDataException("Duplicating a scene did not remap cloned Animator components to their new scene-scoped clip names.");
    if (ProjectDiagnostics.Analyze(sceneMetadataProject).Any(x => x.Code == "PE1206" || x.Code == "PE1403"))
        throw new InvalidDataException("Duplicated scene contains a broken animation reference.");
    LicProjectOperations.RemoveScene(sceneMetadataProject, duplicated);
    if (meta.Animations.Any(x => x.Scene == duplicateName) || meta.Lights.Any(x => x.Scene == duplicateName) ||
        meta.ParticleEmitters.Any(x => x.Scene == duplicateName) || meta.TileMaps.Any(x => x.Scene == duplicateName))
        throw new InvalidDataException("Removing a scene left orphaned Stage 8 authoring data.");
    Console.WriteLine("PASS scene duplicate/delete keeps Stage 8 metadata and Animator references in sync");
}
catch (Exception ex)
{
    failures.Add("scene metadata operations: " + ex.Message);
}

try
{
    const string operationsFixture = """
    {
      "name":"SceneOps",
      "startScene":1,
      "scenes":[
        {"name":"Opening","gravity":700,"futureScene":"keep","objects":[{"name":"Hero","kind":"rect","x":5,"y":6,"w":12,"h":20,"rotation":18,"futureObject":{"tag":7}}]},
        {"name":"Level 2","objects":[]}
      ]
    }
    """;
    var operationsProject = LicProjectReader.Parse(operationsFixture);
    var copyIndex = LicProjectOperations.DuplicateScene(operationsProject, 0);
    if (copyIndex != 1 || operationsProject.Scenes.Count != 3 || operationsProject.StartScene != 2)
        throw new InvalidDataException("Duplicating a scene did not preserve the startup-scene index.");
    var originalObject = operationsProject.Scenes[0].Objects[0];
    var copiedObject = operationsProject.Scenes[copyIndex].Objects[0];
    if (ReferenceEquals(originalObject, copiedObject) || copiedObject.X != originalObject.X)
        throw new InvalidDataException("Duplicated scene objects are not independent copies.");
    if (Math.Abs(copiedObject.Rotation - 18f) > 0.001f)
        throw new InvalidDataException("Duplicating a scene did not preserve the object's rotation.");
    copiedObject.X = 99;
    if (originalObject.X != 5)
        throw new InvalidDataException("Editing a duplicated object changed the source scene.");
    if (!operationsProject.Scenes[copyIndex].ExtraFields!.ContainsKey("futureScene")
        || !operationsProject.Scenes[copyIndex].Objects[0].ExtraFields!.ContainsKey("futureObject"))
        throw new InvalidDataException("Duplicating a scene dropped unknown extension fields.");

    LicProjectOperations.SetStartScene(operationsProject, copyIndex);
    var nextIndex = LicProjectOperations.RemoveScene(operationsProject, copyIndex);
    if (operationsProject.Scenes.Count != 2 || operationsProject.StartScene != 1 || nextIndex != 1)
        throw new InvalidDataException("Removing the startup scene did not repair indices.");
    var newIndex = LicProjectOperations.AddScene(operationsProject);
    if (newIndex != 2 || operationsProject.Scenes[newIndex].Name != "Scene")
        throw new InvalidDataException("Adding a new scene returned an unexpected index or name.");
    var savedOperations = LicProjectReader.Parse(LicProjectWriter.Serialize(operationsProject));
    if (savedOperations.Scenes.Count != 3 || savedOperations.StartScene != 1)
        throw new InvalidDataException("Scene operations failed LIC save/load round-trip.");
    Console.WriteLine("PASS scene add/duplicate/delete/startup operations");
}
catch (Exception ex)
{
    failures.Add("scene operations: " + ex.Message);
}


try
{
    var prefabProject = LicProjectReader.Parse("""
    {"name":"PrefabTest","scenes":[{"name":"Main","objects":[{"name":"Hero","kind":"sprite","sprite":"hero.png","components":[{"type":"SpriteRenderer","enabled":true,"tint":"#ABCDEF"},{"type":"Rigidbody2D","mass":2.5,"gravityScale":0.5},{"type":"BoxCollider2D","isTrigger":false},{"type":"AudioSource","clip":"hit.wav","volume":0.65,"loop":true}],"futureObject":{"x":1}}]}]}
    """);
    var sourceObject = prefabProject.Scenes[0].Objects[0];
    sourceObject.Rotation = 7f;
    var prefab = LicProjectOperations.CreatePrefab(prefabProject, sourceObject);
    if (Math.Abs(prefab.Object.Rotation - 7f) > 0.001f)
        throw new InvalidDataException("Creating a prefab did not copy the source rotation into its template.");
    var instance = LicProjectOperations.InstantiatePrefab(prefabProject.Scenes[0], prefab);
    if (Math.Abs(instance.Rotation - 7f) > 0.001f)
        throw new InvalidDataException("Instantiating a prefab did not retain its template rotation.");
    // Give the instance an explicit per-instance position override. The update
    // assertion below verifies prefab propagation does not overwrite it.
    instance.X = 21f;
    instance.Y = 22f;
    instance.Rotation = 12f;
    if (prefab.Name != "Hero" || sourceObject.Prefab != prefab.Name || instance.Prefab != prefab.Name)
        throw new InvalidDataException("Prefab creation or instance reference failed.");
    if (instance.Components.Count != 4 || instance.Components[1].Mass != 2.5f || instance.Components[0].Tint != "#ABCDEF" || instance.Components[3].Clip != "hit.wav" || Math.Abs(instance.Components[3].Volume - 0.65f) > 0.001f)
        throw new InvalidDataException("Component metadata was not cloned into the prefab instance.");
    instance.Components[1].Mass = 9f;
    if (prefab.Object.Components[1].Mass != 2.5f)
        throw new InvalidDataException("Prefab instance components are not independent copies.");
    sourceObject.Width = 24f;
    sourceObject.Components[3].Clip = "impact.wav";
    var updatedInstances = LicProjectOperations.UpdatePrefabFromObject(prefabProject, prefab, sourceObject, updateInstances: true);
    if (updatedInstances != 1)
        throw new InvalidDataException($"Prefab update expected one linked instance, but updated {updatedInstances}.");
    if (instance.Width != 24f)
        throw new InvalidDataException($"Prefab width did not propagate (expected 24, got {instance.Width}).");
    if (instance.Components[3].Clip != "impact.wav")
        throw new InvalidDataException($"Prefab component metadata did not propagate (expected impact.wav, got {instance.Components[3].Clip}).");
    if (instance.X != 21f || instance.Y != 22f || instance.Rotation != 12f)
        throw new InvalidDataException($"Prefab update overwrote the instance transform (got X={instance.X}, Y={instance.Y}, Rotation={instance.Rotation}).");
    var prefabCopy = LicProjectOperations.DuplicatePrefab(prefabProject, prefab);
    if (prefabCopy.Name != "Hero Copy" || prefabProject.Prefabs.Count != 2)
        throw new InvalidDataException("Prefab duplication failed.");
    LicProjectOperations.RenamePrefab(prefabProject, prefabCopy, "Hero Variant");
    var renamedInstance = LicProjectOperations.InstantiatePrefab(prefabProject.Scenes[0], prefabCopy);
    if (renamedInstance.Prefab != "Hero Variant" || prefabCopy.Object.Name != "Hero Variant")
        throw new InvalidDataException("Prefab rename did not retain prefab references.");
    LicProjectOperations.RenamePrefab(prefabProject, prefab, "Hero Unit");
    if (instance.Prefab != "Hero Unit" || sourceObject.Prefab != "Hero Unit")
        throw new InvalidDataException("Renaming a prefab did not update its existing scene instance links.");
    instance.Width = 3f;
    instance.Components[0].Tint = "#010203";
    if (!LicProjectOperations.RevertPrefabInstance(prefabProject, instance) || instance.Width != 24f || instance.Components[0].Tint != "#ABCDEF" || instance.X != 21f || instance.Y != 22f || instance.Rotation != 12f)
        throw new InvalidDataException("Reverting a prefab instance failed to restore template data while preserving position and rotation.");
    if (!LicProjectOperations.UnpackPrefabInstance(instance) || instance.Prefab.Length != 0)
        throw new InvalidDataException("Unpacking a prefab instance did not break its link.");
    var removedPrefabInstances = LicProjectOperations.RemovePrefabAndUnpackInstances(prefabProject, prefabCopy);
    if (removedPrefabInstances != 1 || renamedInstance.Prefab.Length != 0 || prefabProject.Prefabs.Contains(prefabCopy))
        throw new InvalidDataException("Removing a prefab did not safely unpack linked objects.");
    var removedOriginalPrefabInstances = LicProjectOperations.RemovePrefabAndUnpackInstances(prefabProject, prefab);
    if (removedOriginalPrefabInstances != 1 || sourceObject.Prefab.Length != 0 || prefabProject.Prefabs.Contains(prefab))
        throw new InvalidDataException("Removing a prefab left stale scene links.");
    var clonedComponent = LicProjectOperations.CloneComponentData(sourceObject.Components[0]);
    if (ReferenceEquals(clonedComponent, sourceObject.Components[0]) || clonedComponent.Tint != sourceObject.Components[0].Tint)
        throw new InvalidDataException("Component deep-copy helper failed.");
    using var futureFieldDocument = System.Text.Json.JsonDocument.Parse("42");
    var fullComponent = new LicComponentData
    {
        Type = "BoxCollider2D", Enabled = true, Mass = 2.5f, GravityScale = .75f, LinearDamping = 1.25f,
        UseGravity = false, FreezePositionX = true, FreezePositionY = true, IsKinematic = false, IsStatic = true,
        Friction = .35f, Restitution = .6f, IsTrigger = true, CollisionLayer = 7, CollisionMask = 0x1234,
        BoxSizeX = 24f, BoxSizeY = 36f, ColliderOffsetX = 2f, ColliderOffsetY = -3f, Radius = 11f,
        SortingLayer = "Foreground", SortingOrder = 23, Texture = "sprite.png", Tint = "#112233",
        Clip = "sound.wav", Volume = .4f, Pitch = .3f, Pan = -.2f, AudioBus = "Music", Loop = true,
        PlayOnAwake = true, Animation = "Run", Speed = 1.5f, SpriteFrameWidth = 16,
        SpriteFrameHeight = 20, SpriteFrameCount = 8, SpriteFramesPerSecond = 12f,
        SpriteSheetColumns = 4, SpriteFirstFrame = 2, SpriteLoop = false, SpritePingPong = true,
        CastsShadows = true, CameraTarget = "Hero", CameraOffsetX = 4f, CameraOffsetY = 5f,
        CameraSmoothSpeed = 6f, CameraDeadZoneX = 9f, CameraDeadZoneY = 10f,
        ScriptName = "hero.lua", ExtraFields = new Dictionary<string, System.Text.Json.JsonElement>
        { ["futureComponentValue"] = futureFieldDocument.RootElement.Clone() }
    };
    var fullClone = LicProjectOperations.CloneComponentData(fullComponent);
    var lostComponentProperties = typeof(LicComponentData).GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead && property.Name != nameof(LicComponentData.ExtraFields))
        .Where(property => !Equals(property.GetValue(fullComponent), property.GetValue(fullClone)))
        .Select(property => property.Name).ToArray();
    if (lostComponentProperties.Length > 0 || ReferenceEquals(fullComponent.ExtraFields, fullClone.ExtraFields) ||
        fullClone.ExtraFields?["futureComponentValue"].GetInt32() != 42)
        throw new InvalidDataException("Component clone lost authoring properties: " + string.Join(", ", lostComponentProperties));
    var prefabRoundTrip = LicProjectReader.Parse(LicProjectWriter.Serialize(prefabProject));
    if (prefabRoundTrip.Prefabs.Count != 0 || prefabRoundTrip.Scenes[0].Objects[1].Components.Count != 4)
        throw new InvalidDataException("Prefabs or components failed LIC serialization round-trip.");
    Console.WriteLine("PASS prefab create/duplicate/rename/instantiate and component metadata round-trip");
}
catch (Exception ex)
{
    failures.Add("prefabs/components: " + ex.Message);
}

try
{
    var tempDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineSmoke_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    try
    {
        var pngPath = Path.Combine(tempDirectory, "test.png");
        File.WriteAllBytes(pngPath, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 });
        var sizeLimitRejected = false;
        try { LicAssetOperations.ImportFile(new LicProjectData(), pngPath, maximumBytes: 8); }
        catch (InvalidDataException) { sizeLimitRejected = true; }
        var invalidLimitRejected = false;
        try { LicAssetOperations.ImportFile(new LicProjectData(), pngPath, maximumBytes: 0); }
        catch (ArgumentOutOfRangeException) { invalidLimitRejected = true; }
        if (!sizeLimitRejected || !invalidLimitRejected)
            throw new InvalidDataException("Asset import did not enforce its configured byte limit.");
        var assetProject = new LicProjectData();
        var imported = LicAssetOperations.ImportFile(assetProject, pngPath);
        var duplicate = LicAssetOperations.ImportFile(assetProject, pngPath);
        var referenced = new LicObjectData { Name = "Sprite", Kind = "sprite", Sprite = imported.Name, Components = [new LicComponentData { Type = "SpriteRenderer", Texture = imported.Name }] };
        var prefabReference = new LicObjectData { Name = "Prefab Sprite", Kind = "sprite", Sprite = imported.Name, Components = [new LicComponentData { Type = "SpriteRenderer", Texture = imported.Name }] };
        assetProject.Scenes.Add(new LicSceneData { Name = "Main", Objects = [referenced] });
        assetProject.Prefabs.Add(new LicPrefabData { Name = "SpritePrefab", Object = prefabReference });
        LicAssetOperations.RenameAsset(assetProject, imported, "hero-renamed.png");
        if (imported.Name != "hero-renamed.png" || referenced.Sprite != imported.Name || referenced.Components[0].Texture != imported.Name
            || prefabReference.Sprite != imported.Name || prefabReference.Components[0].Texture != imported.Name)
            throw new InvalidDataException("Asset rename did not update scene and prefab references.");
        if (duplicate.Name != "test_1.png" || !LicAssetOperations.GetBytes(imported).SequenceEqual(File.ReadAllBytes(pngPath)))
            throw new InvalidDataException("Asset import, unique naming or Base64 decode failed.");
        var removedRefs = LicAssetOperations.RemoveAsset(assetProject, imported, clearReferences: true);
        if (removedRefs != 4 || referenced.Sprite.Length != 0 || referenced.Components[0].Texture.Length != 0
            || prefabReference.Sprite.Length != 0 || prefabReference.Components[0].Texture.Length != 0 || assetProject.Assets.Contains(imported))
            throw new InvalidDataException("Removing an asset did not clear dangling scene/prefab component references.");
        Console.WriteLine("PASS bounded PNG import, scene/prefab rename-reference update, safe removal and embedded Base64 data");
    }
    finally { Directory.Delete(tempDirectory, recursive: true); }
}
catch (Exception ex)
{
    failures.Add("asset import: " + ex.Message);
}

try
{
    var scene = new Scene("ReadOnlyGraph");
    var gameObject = scene.Add(new GameObject("ProtectedObject"));
    gameObject.AddComponent(new UpdateProbeComponent());

    if (scene.Objects is List<GameObject> || gameObject.Components is List<Component>)
        throw new InvalidDataException("Scene graph exposed its mutable backing List.");
    if (scene.Objects is not IList<GameObject> objectList || !objectList.IsReadOnly)
        throw new InvalidDataException("Scene.Objects is not a read-only collection view.");
    if (gameObject.Components is not IList<Component> componentList || !componentList.IsReadOnly)
        throw new InvalidDataException("GameObject.Components is not a read-only collection view.");

    var objectMutationRejected = false;
    try { objectList.Add(new GameObject("Injected")); }
    catch (NotSupportedException) { objectMutationRejected = true; }
    var componentMutationRejected = false;
    try { componentList.Clear(); }
    catch (NotSupportedException) { componentMutationRejected = true; }
    if (!objectMutationRejected || !componentMutationRejected || scene.Objects.Count != 1 || gameObject.Components.Count != 1)
        throw new InvalidDataException("External collection mutation bypassed Scene/GameObject lifecycle APIs.");

    var stableObjects = scene.FindAll("ProtectedObject").GetEnumerator();
    if (!stableObjects.MoveNext()) throw new InvalidDataException("FindAll did not find the existing object.");
    scene.Add(new GameObject("ProtectedObject"));
    if (stableObjects.MoveNext()) throw new InvalidDataException("FindAll enumeration was not stable across scene mutation.");
    stableObjects.Dispose();

    var stableComponents = gameObject.GetComponents<UpdateProbeComponent>().GetEnumerator();
    if (!stableComponents.MoveNext()) throw new InvalidDataException("GetComponents did not find the existing component.");
    gameObject.AddComponent(new UpdateProbeComponent());
    if (stableComponents.MoveNext()) throw new InvalidDataException("GetComponents enumeration was not stable across component mutation.");
    stableComponents.Dispose();

    var transform = new Transform
    {
        Position = new Vector2(float.NaN, 12f),
        Scale = new Vector2(float.PositiveInfinity, -2f),
        Rotation = float.NegativeInfinity,
        Origin = new Vector2(3f, float.NaN)
    };
    if (transform.Position != new Vector2(0f, 12f) || transform.Scale != new Vector2(1f, -2f)
        || transform.Rotation != 0f || transform.Origin != new Vector2(3f, 0f)
        || !float.IsFinite(transform.Forward.X) || !float.IsFinite(transform.Forward.Y))
        throw new InvalidDataException("Transform did not normalize non-finite values at assignment time.");

    if (EngineTime.SanitizeDeltaTime(-1d) != 0f || EngineTime.SanitizeDeltaTime(double.NaN) != 0f
        || EngineTime.SanitizeDeltaTime(double.PositiveInfinity) != 0f
        || EngineTime.SanitizeDeltaTime(1d) != EngineTime.MaximumDeltaSeconds
        || Math.Abs(EngineTime.SanitizeDeltaTime(1d / 60d) - 1f / 60f) > .000001f
        || EngineTime.SanitizeTotalTime(double.NaN, 12.5d) != 12.5d
        || EngineTime.SanitizeTotalTime(double.NegativeInfinity, 12.5d) != 12.5d
        || EngineTime.SanitizeTotalTime(-5d, 12.5d) != 12.5d
        || EngineTime.SanitizeTotalTime(42d, 12.5d) != 42d
        || EngineTime.SanitizeTotalTime(5d, 12.5d) != 12.5d)
        throw new InvalidDataException("Engine clock sanitization did not preserve a valid monotonic time sample.");

    var camera = new Camera2D
    {
        Position = new Vector2(float.NaN, float.PositiveInfinity),
        Zoom = float.PositiveInfinity,
        Rotation = float.NaN,
        BoundsMin = new Vector2(float.NaN, -100f),
        BoundsMax = new Vector2(100f, float.PositiveInfinity)
    };
    if (camera.Position != Vector2.Zero || camera.Zoom != 1f || camera.SafeZoom != 1f || camera.Rotation != 0f
        || camera.BoundsMin.X != 0f || camera.BoundsMax.Y != 0f
        || !float.IsFinite(camera.ScreenToWorld(new Vector2(float.NaN, float.NaN), new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 100, 100)).X)
        || !float.IsFinite(camera.WorldToScreen(new Vector2(float.PositiveInfinity, float.PositiveInfinity), new Microsoft.Xna.Framework.Graphics.Viewport(0, 0, 100, 100)).Y))
        throw new InvalidDataException("Camera2D did not normalize invalid position, zoom, rotation or coordinate values.");

    var enableProbe = new FailingEnableComponent();
    var enableOwner = new GameObject("EnableRollback");
    enableOwner.AddComponent(enableProbe);
    enableProbe.Enabled = false;
    var disabledCountBeforeRetry = enableProbe.DisabledCount;
    var enableFailureObserved = false;
    try { enableProbe.Enabled = true; }
    catch (InvalidOperationException) { enableFailureObserved = true; }
    if (!enableFailureObserved || enableProbe.Enabled || enableProbe.DisabledCount != disabledCountBeforeRetry + 1)
        throw new InvalidDataException("Component activation failure did not roll back its enabled state and teardown callback.");

    var forceScene = new Scene("ForceOverflow");
    var forceObject = forceScene.Add(new GameObject("ForceBody"));
    var forceBody = forceObject.AddComponent(new Rigidbody2DComponent { UseGravity = false });
    forceBody.AddForce(new Vector2(float.MaxValue, 0f));
    forceBody.AddForce(new Vector2(float.MaxValue, 0f));
    forceScene.Physics.Step(forceScene, 1f / 60f, Vector2.Zero);
    if (!float.IsFinite(forceBody.Velocity.X) || forceBody.Velocity.X <= 0f || !float.IsFinite(forceObject.Transform.Position.X))
        throw new InvalidDataException("Rigidbody2D force accumulation overflow corrupted velocity or position.");

    Console.WriteLine("PASS read-only scene graph, safe Transform/Camera/time values, lifecycle rollback and finite physics forces");
}
catch (Exception ex)
{
    failures.Add("Stage 11 scene graph/time guards: " + ex.Message);
}

try
{
    var temporaryDirectory = Path.Combine(Path.GetTempPath(), "PixelEngineProfilerSmoke_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporaryDirectory);
    try
    {
        var expected = new EngineProfilerSnapshot(60d, 16.67d, 2.5d, 3.1d, 4096, 7, 12, 100);
        RuntimeProfilerStore.WriteSnapshot(expected, temporaryDirectory, 43210);
        File.WriteAllText(Path.Combine(temporaryDirectory, "runtime-profiler-99999.json"), "{invalid-json");
        if (!RuntimeProfilerStore.TryReadLatest(TimeSpan.FromMinutes(1), out var document, temporaryDirectory)
            || document is null || document.Snapshot != expected || document.TimestampUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("Profiler store failed to find a valid fresh snapshot when a newer file was corrupt.");
        if (RuntimeProfilerStore.TryReadLatest(TimeSpan.FromMinutes(-1), out _, temporaryDirectory))
            throw new InvalidDataException("Profiler store accepted a negative maximum age.");
        var invalidSnapshotRejected = false;
        try { RuntimeProfilerStore.WriteSnapshot(expected with { FramesPerSecond = double.NaN }, temporaryDirectory, 43211); }
        catch (ArgumentOutOfRangeException) { invalidSnapshotRejected = true; }
        if (!invalidSnapshotRejected)
            throw new InvalidDataException("Profiler store accepted non-finite metrics.");
        Console.WriteLine("PASS profiler telemetry atomic write/read, corrupt-file fallback, age bounds and metric validation");
    }
    finally { Directory.Delete(temporaryDirectory, recursive: true); }
}
catch (Exception ex)
{
    failures.Add("Stage 11 profiler telemetry: " + ex.Message);
}

foreach (var file in files)
{
    try
    {
        var project = LicProjectReader.Load(file);
        var objectCount = project.Scenes.Sum(scene => scene.Objects.Count);
        if (project.Scenes.Count == 0) throw new InvalidDataException("No scenes were imported.");
        if (project.StartScene < 0 || project.StartScene >= project.Scenes.Count) throw new InvalidDataException("Start scene is out of range.");

        var roundTrip = LicProjectReader.Parse(LicProjectWriter.Serialize(project));
        if (roundTrip.Scenes.Count != project.Scenes.Count || roundTrip.Scripts.Count != project.Scripts.Count || roundTrip.Assets.Count != project.Assets.Count || roundTrip.Prefabs.Count != project.Prefabs.Count)
            throw new InvalidDataException("LIC serialize/parse round-trip changed collection counts.");

        var assetNames = project.Assets.Select(asset => asset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (assetNames.Count != project.Assets.Count)
            throw new InvalidDataException("Project contains duplicate asset names.");
        foreach (var asset in project.Assets.Where(asset => !string.IsNullOrWhiteSpace(asset.Base64)))
        {
            var bytes = Convert.FromBase64String(asset.Base64);
            if (string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase))
            {
                byte[] pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
                if (bytes.Length < pngSignature.Length || !bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
                    throw new InvalidDataException($"PNG asset '{asset.Name}' has an invalid signature.");
            }
        }

        var allObjects = project.Scenes.SelectMany(scene => scene.Objects).ToArray();
        var missingAssets = allObjects.SelectMany(item =>
                new[] { item.Sprite }.Concat(item.Components.Select(component => component.Texture)))
            .Where(reference => !string.IsNullOrWhiteSpace(reference) && !assetNames.Contains(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missingAssets.Length > 0)
            throw new InvalidDataException("Missing asset references: " + string.Join(", ", missingAssets));

        if (string.Equals(Path.GetFileName(file), "demo.lic", StringComparison.OrdinalIgnoreCase))
        {
            if (objectCount < 30 || project.Assets.Count < 4)
                throw new InvalidDataException("Crystal Valley demo should include a varied scene and four embedded pixel-art PNG assets.");
            foreach (var requiredScript in new[] { "hero.lua", "enemy.lua", "bolt.lua" })
                if (!project.Scripts.Any(script => string.Equals(script.Name, requiredScript, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Demo is missing required script '{requiredScript}'.");
            if (project.Scripts.Any(script => script.Code.Contains("api.log('pew')", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Demo still contains per-shot output spam.");
            Console.WriteLine("PASS Crystal Valley demo scene, sprite references and no-spam sample scripts");
        }
        if (string.Equals(Path.GetFileName(file), "stage8-showcase.lic", StringComparison.OrdinalIgnoreCase))
        {
            var editor = project.EditorData ?? throw new InvalidDataException("Stage 8 showcase has no PixelEngine metadata.");
            if (editor.Animations.Count != 1 || editor.Lights.Count < 1 || editor.ParticleEmitters.Count < 1 || editor.TileMaps.Count != 1)
                throw new InvalidDataException("Stage 8 showcase is missing one of its authoring systems.");
            if (ProjectDiagnostics.Analyze(project).Any(x => x.Severity == DiagnosticSeverity.Error))
                throw new InvalidDataException("Stage 8 showcase contains diagnostics errors.");
            var hero = project.Scenes[0].Objects.First(x => x.Name == "hero");
            if (!hero.Components.Any(x => x.Type == "Animator" && x.Animation == "IdlePulse"))
                throw new InvalidDataException("Stage 8 showcase hero is not bound to its animation clip.");
            Console.WriteLine("PASS Stage 8 showcase animation, lights, particles and tilemap wiring");
        }

        var scriptNames = project.Scripts.Select(script => script.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingScripts = project.Scenes.SelectMany(scene => scene.Objects)
            .Where(item => !string.IsNullOrWhiteSpace(item.Script) && !scriptNames.Contains(item.Script)).ToArray();
        if (missingScripts.Length > 0)
            throw new InvalidDataException($"{missingScripts.Length} object(s) reference missing scripts.");

        var luaScripts = project.Scripts.Where(script => string.Equals(script.Language, "lua", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var script in luaScripts)
        {
            var syntaxError = LicLuaBehaviour.ValidateCode(script.Code);
            if (!string.IsNullOrEmpty(syntaxError))
                throw new InvalidDataException($"Lua syntax error in '{script.Name}': {syntaxError}");
        }

        Console.WriteLine($"PASS {Path.GetFileName(file),-16} scenes={project.Scenes.Count,3} objects={objectCount,5} scripts={project.Scripts.Count,3} lua-checked={luaScripts.Length,3} assets={project.Assets.Count,3} roundtrip=ok");
    }
    catch (Exception ex)
    {
        failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("\nLIC import failures:");
    foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine($"\nAll {files.Length} .lic fixtures passed import validation.");
return 0;


internal sealed class UpdateProbeComponent : Component
{
    public int UpdateCount { get; private set; }
    public override void Update(EngineTime time) => UpdateCount++;
}

internal sealed class CallbackProbeComponent : Component
{
    private Action? _callback;
    public CallbackProbeComponent(Action callback) => _callback = callback;
    public override void Update(EngineTime time)
    {
        var callback = _callback;
        _callback = null;
        callback?.Invoke();
    }
}

internal sealed class LifecycleProbeComponent : Component
{
    public int AddedCount { get; private set; }
    public int StartCount { get; private set; }
    public int RemovedCount { get; private set; }
    public override void OnAdded() => AddedCount++;
    public override void Start() => StartCount++;
    public override void OnRemoved() => RemovedCount++;
}

internal sealed class FailingEnableComponent : Component
{
    public int DisabledCount { get; private set; }
    public override void OnEnabled() => throw new InvalidOperationException("Intentional enable failure.");
    public override void OnDisabled() => DisabledCount++;
}

internal sealed class ThrowingCollisionComponent : Component
{
    public override void OnCollisionEnter2D(Collision2D collision)
        => throw new InvalidOperationException("Intentional smoke-test callback failure.");
}

internal sealed class CollisionProbeComponent : Component
{
    public int CollisionEnters { get; private set; }
    public int CollisionStays { get; private set; }
    public int CollisionExits { get; private set; }
    public int TriggerEnters { get; private set; }
    public override void OnCollisionEnter2D(Collision2D collision) => CollisionEnters++;
    public override void OnCollisionStay2D(Collision2D collision) => CollisionStays++;
    public override void OnCollisionExit2D(Collision2D collision) => CollisionExits++;
    public override void OnTriggerEnter2D(Collider2DComponent other) => TriggerEnters++;
}


internal sealed class RetryStartComponent : Component
{
    public int Attempts { get; private set; }
    public int SuccessfulStarts { get; private set; }
    public override void Start()
    {
        Attempts++;
        if (Attempts == 1) throw new InvalidOperationException("Intentional first-start failure.");
        SuccessfulStarts++;
    }
}

internal sealed class FailingStartComponent : Component
{
    public int RemovedCount { get; private set; }
    public override void Start() => throw new InvalidOperationException("Intentional Start failure.");
    public override void OnRemoved() => RemovedCount++;
}

internal sealed class FailingAddedComponent : Component
{
    public int RemovedCount { get; private set; }
    public override void OnAdded() => throw new InvalidOperationException("Intentional OnAdded failure.");
    public override void OnRemoved() => RemovedCount++;
}


internal sealed class AddComponentOnRemovalProbe : Component
{
    private readonly GameObject _owner;
    public LifecycleProbeComponent? AddedComponent { get; private set; }
    public AddComponentOnRemovalProbe(GameObject owner) => _owner = owner;
    public override void OnRemoved() => AddedComponent = _owner.AddComponent(new LifecycleProbeComponent());
}
