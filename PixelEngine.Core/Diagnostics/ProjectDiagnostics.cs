using PixelEngine.Scripting;
using PixelEngine.Graphics;
using PixelEngine.Serialization;

namespace PixelEngine.Diagnostics;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record ProjectDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Location,
    string Message,
    string Suggestion);

/// <summary>Read-only project validator. It never edits project data.</summary>
public static class ProjectDiagnostics
{
    private static readonly HashSet<string> RuntimeComponentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpriteRenderer", "Rigidbody2D", "BoxCollider2D", "CircleCollider2D",
        "AudioSource", "Animator", "CameraFollow", "Script"
    };

    private static readonly HashSet<string> SingleInstanceComponentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpriteRenderer", "Rigidbody2D", "Animator", "CameraFollow", "Script"
    };

    public static IReadOnlyList<ProjectDiagnostic> Analyze(LicProjectData project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var results = new List<ProjectDiagnostic>();
        var assetList = project.Assets ?? new();
        var assets = assetList.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assetTypes = assetList.Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Type ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var scripts = (project.Scripts ?? new()).ToDictionarySafe();
        var prefabs = (project.Prefabs ?? new()).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sortingLayers = new HashSet<string>(new[] { "Background", "Default", "Foreground", "UI" }, StringComparer.OrdinalIgnoreCase);
        foreach (var component in (project.Scenes ?? new()).SelectMany(scene => scene.Objects ?? new())
                     .SelectMany(obj => obj.Components ?? new())
                     .Concat((project.Prefabs ?? new()).Where(prefab => prefab.Object is not null)
                         .SelectMany(prefab => prefab.Object!.Components ?? new())))
        {
            if (string.Equals(component.Type, "SpriteRenderer", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(component.SortingLayer))
                sortingLayers.Add(component.SortingLayer.Trim());
        }
        if (sortingLayers.Count > SortingLayerRegistry.MaximumLayers)
            results.Add(new(DiagnosticSeverity.Warning, "PE1215", "Rendering/SortingLayers",
                $"Project uses {sortingLayers.Count} sorting layers; the runtime supports {SortingLayerRegistry.MaximumLayers}.",
                "Merge rarely used layers or reduce the number of custom sorting layers."));
        foreach (var duplicate in (project.Prefabs ?? new()).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            results.Add(new(DiagnosticSeverity.Error, "PE1105", "Prefabs", $"Duplicate prefab name '{duplicate.Key}'.", "Give each prefab a unique name."));
        foreach (var duplicate in (project.Scripts ?? new()).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            results.Add(new(DiagnosticSeverity.Error, "PE1303", "Scripts", $"Duplicate script name '{duplicate.Key}'.", "Rename one of the scripts to make references deterministic."));
        foreach (var prefab in project.Prefabs ?? new())
        {
            var template = prefab.Object;
            if (template is null)
            {
                results.Add(new(DiagnosticSeverity.Error, "PE1110", $"Prefab:{prefab.Name}", "Prefab has no template object.", "Recreate the prefab from a valid scene object."));
                continue;
            }
            var where = $"Prefab:{prefab.Name}/Object:{template.Name}";
            if (!float.IsFinite(template.X) || !float.IsFinite(template.Y) || !float.IsFinite(template.Rotation) ||
                !float.IsFinite(template.Width) || !float.IsFinite(template.Height) || template.Width <= 0f || template.Height <= 0f)
                results.Add(new(DiagnosticSeverity.Error, "PE1111", where, "Prefab template contains an invalid transform or size.", "Use finite position/rotation and positive width/height."));
            CheckAsset(template.Sprite, where + "/Sprite", assets, results);
            CheckAssetFormat(template.Sprite, "png", where + "/Sprite", assetTypes, results);
            if (!string.IsNullOrWhiteSpace(template.Script) && !scripts.ContainsKey(template.Script))
                results.Add(new(DiagnosticSeverity.Error, "PE1112", where, $"Prefab script '{template.Script}' does not exist.", "Assign an existing script or create the missing script."));
            foreach (var component in template.Components ?? new())
            {
                CheckAsset(component.Texture, where + "/" + component.Type + "/Texture", assets, results);
                CheckAssetFormat(component.Texture, "png", where + "/" + component.Type + "/Texture", assetTypes, results);
                CheckAsset(component.Clip, where + "/" + component.Type + "/Audio", assets, results);
                CheckAssetFormat(component.Clip, "wav", where + "/" + component.Type + "/Audio", assetTypes, results);
                if (!string.IsNullOrWhiteSpace(component.ScriptName) && !scripts.ContainsKey(component.ScriptName))
                    results.Add(new(DiagnosticSeverity.Error, "PE1113", where, $"Prefab component script '{component.ScriptName}' does not exist.", "Assign an existing script or create the missing script."));
            }
        }

        if (project.Scenes is null || project.Scenes.Count == 0)
        {
            results.Add(new(DiagnosticSeverity.Error, "PE1001", "Project", "Project contains no scenes.", "Create a scene before running the project."));
            return results;
        }
        if (project.StartScene < 0 || project.StartScene >= project.Scenes.Count)
            results.Add(new(DiagnosticSeverity.Error, "PE1002", "Project", "Startup scene index is outside the scene list.", "Choose a valid startup scene."));

        foreach (var asset in project.Assets ?? new())
        {
            if (string.IsNullOrWhiteSpace(asset.Name))
                results.Add(new(DiagnosticSeverity.Error, "PE1101", "Assets", "An asset has an empty name.", "Rename the asset."));
            var assetType = (asset.Type ?? string.Empty).Trim();
            if (!string.Equals(assetType, "png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(assetType, "wav", StringComparison.OrdinalIgnoreCase))
                results.Add(new(DiagnosticSeverity.Warning, "PE1109", $"Asset:{asset.Name}", $"Asset format '{asset.Type}' is not supported by the current runtime.", "Use PNG for textures or WAV for audio, or keep the asset as editor-only data."));
            if (string.IsNullOrWhiteSpace(asset.Base64))
            {
                if (string.Equals(assetType, "png", StringComparison.OrdinalIgnoreCase) || string.Equals(assetType, "wav", StringComparison.OrdinalIgnoreCase))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1108", $"Asset:{asset.Name}", "Asset payload is empty.", "Reimport the original resource file."));
            }
            else
            {
                try
                {
                    var payload = Convert.FromBase64String(asset.Base64);
                    if (string.Equals(assetType, "png", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
                        if (payload.Length < signature.Length || !payload.AsSpan(0, signature.Length).SequenceEqual(signature))
                            results.Add(new(DiagnosticSeverity.Error, "PE1106", $"Asset:{asset.Name}", "PNG asset has an invalid file signature.", "Reimport a valid PNG image."));
                    }
                    else if (string.Equals(assetType, "wav", StringComparison.OrdinalIgnoreCase) &&
                             (payload.Length < 12 || !payload.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !payload.AsSpan(8, 4).SequenceEqual("WAVE"u8)))
                    {
                        results.Add(new(DiagnosticSeverity.Error, "PE1107", $"Asset:{asset.Name}", "WAV asset has an invalid RIFF/WAVE signature.", "Reimport a valid PCM/MonoGame-compatible WAV file."));
                    }
                }
                catch (FormatException)
                {
                    results.Add(new(DiagnosticSeverity.Error, "PE1102", $"Asset:{asset.Name}", "Asset payload is not valid Base64.", "Reimport the original file."));
                }
            }
        }
        foreach (var duplicate in (project.Assets ?? new()).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            results.Add(new(DiagnosticSeverity.Error, "PE1103", "Assets", $"Duplicate asset name '{duplicate.Key}'.", "Give each asset a unique name."));

        foreach (var duplicate in project.Scenes.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            results.Add(new(DiagnosticSeverity.Error, "PE1003", "Scenes", $"Duplicate scene name '{duplicate.Key}'.", "Rename scenes so references remain unambiguous."));

        foreach (var scene in project.Scenes)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in scene.Objects ?? new())
            {
                var where = $"Scene:{scene.Name}/Object:{obj.Name}";
                if (!seenNames.Add(obj.Name))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1201", where, $"Object name '{obj.Name}' is duplicated in this scene.", "Rename one of the objects to make script lookup unambiguous."));
                if (!float.IsFinite(obj.X) || !float.IsFinite(obj.Y) || !float.IsFinite(obj.Rotation) || !float.IsFinite(obj.Width) || !float.IsFinite(obj.Height) || obj.Width <= 0 || obj.Height <= 0)
                    results.Add(new(DiagnosticSeverity.Error, "PE1202", where, "Object transform or size contains an invalid number.", "Use finite coordinates and positive width/height."));
                CheckAsset(obj.Sprite, where + "/Sprite", assets, results);
                CheckAssetFormat(obj.Sprite, "png", where + "/Sprite", assetTypes, results);
                if (!string.IsNullOrWhiteSpace(obj.Script) && !scripts.ContainsKey(obj.Script))
                    results.Add(new(DiagnosticSeverity.Error, "PE1203", where, $"Script '{obj.Script}' does not exist.", "Assign an existing script or create the missing script."));
                var enabledComponents = (obj.Components ?? new()).Where(x => x.Enabled).ToArray();
                foreach (var duplicate in enabledComponents.Where(x => SingleInstanceComponentTypes.Contains(x.Type))
                             .GroupBy(x => x.Type, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1213", where,
                        $"Object has {duplicate.Count()} enabled '{duplicate.Key}' components, but the runtime uses only the first one.",
                        "Keep one enabled instance of this component type on the object."));
                foreach (var component in obj.Components ?? new())
                {
                    CheckAsset(component.Texture, where + "/" + component.Type + "/Texture", assets, results);
                    CheckAssetFormat(component.Texture, "png", where + "/" + component.Type + "/Texture", assetTypes, results);
                    CheckAsset(component.Clip, where + "/" + component.Type + "/Audio", assets, results);
                    CheckAssetFormat(component.Clip, "wav", where + "/" + component.Type + "/Audio", assetTypes, results);
                    if (!float.IsFinite(component.Mass) || component.Mass <= 0f ||
                        !float.IsFinite(component.GravityScale) ||
                        !float.IsFinite(component.LinearDamping) || component.LinearDamping < 0f || component.LinearDamping > 1000f ||
                        !float.IsFinite(component.Friction) || component.Friction < 0f ||
                        !float.IsFinite(component.Restitution) || component.Restitution < 0f || component.Restitution > 1f ||
                        !float.IsFinite(component.Radius) || component.Radius <= 0f ||
                        !float.IsFinite(component.BoxSizeX) || component.BoxSizeX < 0f ||
                        !float.IsFinite(component.BoxSizeY) || component.BoxSizeY < 0f ||
                        !float.IsFinite(component.ColliderOffsetX) || !float.IsFinite(component.ColliderOffsetY) ||
                        !float.IsFinite(component.Volume) || component.Volume < 0f || component.Volume > 1f ||
                        !float.IsFinite(component.Pitch) || component.Pitch < -1f || component.Pitch > 1f ||
                        !float.IsFinite(component.Pan) || component.Pan < -1f || component.Pan > 1f ||
                        !float.IsFinite(component.Speed) || component.CollisionLayer is < 0 or > 31 ||
                        (component.IsStatic && component.IsKinematic))
                        results.Add(new(DiagnosticSeverity.Warning, "PE1207", where + "/" + component.Type,
                            "One or more component numeric properties are invalid.",
                            "Check Mass, Gravity Scale, Damping, Collision Layer, Friction, Restitution, Radius, Volume, Pitch, Pan and Speed."));
                    if (!RuntimeComponentTypes.Contains(component.Type))
                        results.Add(new(DiagnosticSeverity.Warning, "PE1214", where + "/" + component.Type,
                            $"Component type '{component.Type}' is preserved in the project but is not implemented by this runtime.",
                            "Use one of the supported component types or keep this component disabled until a runtime implementation is added."));
                    if (string.Equals(component.Type, "AudioSource", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(component.AudioBus, "SoundEffects", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(component.AudioBus, "Music", StringComparison.OrdinalIgnoreCase))
                        results.Add(new(DiagnosticSeverity.Warning, "PE1212", where + "/AudioSource",
                            $"Audio bus '{component.AudioBus}' is unknown; runtime playback falls back to SoundEffects.",
                            "Choose SoundEffects or Music."));
                    if (string.Equals(component.Type, "Animator", StringComparison.OrdinalIgnoreCase) &&
                        (component.SpriteFrameWidth != 0 || component.SpriteFrameHeight != 0 || component.SpriteFrameCount != 0))
                    {
                        if (component.SpriteFrameWidth is < 1 or > 8192 || component.SpriteFrameHeight is < 1 or > 8192 ||
                            component.SpriteFrameCount is < 1 or > 100_000 || !float.IsFinite(component.SpriteFramesPerSecond) ||
                            component.SpriteFramesPerSecond < .01f || component.SpriteFramesPerSecond > 240f ||
                            component.SpriteSheetColumns is < 0 or > 8192 || component.SpriteFirstFrame is < 0 or > 100_000)
                            results.Add(new(DiagnosticSeverity.Error, "PE1208", where + "/Animator",
                                "Sprite-sheet animation dimensions, frame count or rate are invalid.",
                                "Set frame size to 1–8192 px, frame count to 1–100,000, FPS to 0.01–240, columns to 0–8192 and first frame to 0–100,000."));
                    }
                    if (!string.IsNullOrWhiteSpace(component.ScriptName) && !scripts.ContainsKey(component.ScriptName))
                        results.Add(new(DiagnosticSeverity.Error, "PE1204", where, $"Component script '{component.ScriptName}' does not exist.", "Assign a valid script."));
                    if (string.Equals(component.Type, "CameraFollow", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!float.IsFinite(component.CameraOffsetX) || !float.IsFinite(component.CameraOffsetY) ||
                            !float.IsFinite(component.CameraSmoothSpeed) || component.CameraSmoothSpeed < 0f || component.CameraSmoothSpeed > 100f ||
                            !float.IsFinite(component.CameraDeadZoneX) || component.CameraDeadZoneX < 0f ||
                            !float.IsFinite(component.CameraDeadZoneY) || component.CameraDeadZoneY < 0f)
                            results.Add(new(DiagnosticSeverity.Error, "PE1210", where + "/CameraFollow",
                                "Camera follow offset, smoothing or dead-zone values are invalid.",
                                "Use finite offsets, smoothing speed 0–100 and non-negative dead-zone dimensions."));
                        if (!string.IsNullOrWhiteSpace(component.CameraTarget) &&
                            scene.Objects?.Any(x => string.Equals(x.Name, component.CameraTarget, StringComparison.OrdinalIgnoreCase)) != true)
                            results.Add(new(DiagnosticSeverity.Warning, "PE1211", where + "/CameraFollow",
                                $"Camera target '{component.CameraTarget}' was not found in this scene.",
                                "Choose an existing scene object or leave Camera Target empty to follow this object."));
                    }
                    if (string.Equals(component.Type, "Animator", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(component.Animation) &&
                        !(project.EditorData?.Animations.Any(x =>
                            string.Equals(x.Name, component.Animation, StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, scene.Name, StringComparison.OrdinalIgnoreCase)) &&
                            (string.IsNullOrWhiteSpace(x.Target) || string.Equals(x.Target, obj.Name, StringComparison.OrdinalIgnoreCase))) ?? false))
                        results.Add(new(DiagnosticSeverity.Error, "PE1206", where, $"Animation '{component.Animation}' is not available for this scene/object.", "Create a clip for this scene/object or select a compatible clip."));
                }
                if (!string.IsNullOrWhiteSpace(obj.Prefab) && !prefabs.Contains(obj.Prefab))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1205", where, $"Prefab '{obj.Prefab}' is missing.", "Relink the instance or unpack it to remove the missing reference."));
            }
        }

        foreach (var script in project.Scripts ?? new())
        {
            if (string.Equals(script.Language, "lua", StringComparison.OrdinalIgnoreCase))
            {
                var error = LicLuaBehaviour.ValidateCode(script.Code ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(error))
                    results.Add(new(DiagnosticSeverity.Error, "PE1301", $"Script:{script.Name}", error, "Fix the Lua syntax error before running the project."));
            }
            else if (string.Equals(script.Language, "csharp", StringComparison.OrdinalIgnoreCase))
                results.Add(new(DiagnosticSeverity.Warning, "PE1304", $"Script:{script.Name}", "C# script is imported but is not executed by the compatibility player.", "Port the behavior to Lua for the current runtime, or keep the C# file as source until a reviewed plugin host is added."));
            else
                results.Add(new(DiagnosticSeverity.Warning, "PE1302", $"Script:{script.Name}", $"Unknown script language '{script.Language}'.", "Use lua or csharp."));
        }

        var editor = project.EditorData;
        if (editor is not null)
        {
            if (!float.IsFinite(editor.AmbientLight) || editor.AmbientLight < 0f || editor.AmbientLight > 1f)
                results.Add(new(DiagnosticSeverity.Error, "PE1506", "Lighting/Ambient", "Ambient light must be a finite value between 0 and 1.", "Set Ambient Light between 0 (dark) and 1 (fully lit)."));

            var duplicateClips = editor.Animations
                .GroupBy(x => (Scene: x.Scene ?? string.Empty, Name: x.Name ?? string.Empty), new AnimationScopeComparer())
                .Where(x => x.Count() > 1);
            foreach (var duplicate in duplicateClips)
                results.Add(new(DiagnosticSeverity.Error, "PE1400", "Animations", $"Duplicate animation name '{duplicate.Key.Name}' in scene scope '{duplicate.Key.Scene}'.", "Give animation clips unique names within each scene scope."));

            foreach (var clip in editor.Animations)
            {
                var where = $"Animation:{clip.Name}";
                if (!float.IsFinite(clip.Duration) || clip.Duration <= 0)
                    results.Add(new(DiagnosticSeverity.Error, "PE1401", where, "Animation duration must be a positive finite number.", "Set duration above zero."));
                if (!string.IsNullOrWhiteSpace(clip.Scene) &&
                    !project.Scenes.Any(x => string.Equals(x.Name, clip.Scene, StringComparison.OrdinalIgnoreCase)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1402", where, $"Scene '{clip.Scene}' was not found.", "Select an existing scene."));
                var scene = string.IsNullOrWhiteSpace(clip.Scene) ? null : project.Scenes.FirstOrDefault(x => string.Equals(x.Name, clip.Scene, StringComparison.OrdinalIgnoreCase));
                if (scene is not null && !string.IsNullOrWhiteSpace(clip.Target) &&
                    !scene.Objects.Any(x => string.Equals(x.Name, clip.Target, StringComparison.OrdinalIgnoreCase)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1403", where, $"Target object '{clip.Target}' was not found.", "Choose an object in the selected scene."));
                if (clip.Keyframes.Count == 0)
                    results.Add(new(DiagnosticSeverity.Warning, "PE1404", where, "Animation has no keyframes.", "Add at least one keyframe."));
                if (clip.Keyframes.Any(x => !float.IsFinite(x.Time) || x.Time < 0 || x.Time > clip.Duration))
                    results.Add(new(DiagnosticSeverity.Error, "PE1405", where, "A keyframe time is outside the clip duration.", "Keep keyframe times between zero and duration."));
                if (clip.Keyframes.Any(HasInvalidTransformChannel))
                    results.Add(new(DiagnosticSeverity.Error, "PE1407", where, "A keyframe contains a non-finite transform value.", "Use finite X/Y/rotation/scale values or clear the optional channel."));
                if (clip.Keyframes.Any(x => (x.ScaleX.HasValue && float.IsFinite(x.ScaleX.Value) && Math.Abs(x.ScaleX.Value) > 100f) ||
                                             (x.ScaleY.HasValue && float.IsFinite(x.ScaleY.Value) && Math.Abs(x.ScaleY.Value) > 100f)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1408", where, "A keyframe scale exceeds the runtime limit of ±100.", "Keep scale channels between -100 and 100."));
                if (!clip.Keyframes.Select(x => x.Time).SequenceEqual(clip.Keyframes.Select(x => x.Time).OrderBy(x => x)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1406", where, "Keyframes are not sorted by time.", "Sort keyframes in ascending order."));
            }

            foreach (var scene in project.Scenes)
            {
                var sceneLights = editor.Lights.Where(x => x.Enabled && (string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, scene.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (sceneLights.Length > 16)
                    results.Add(new(DiagnosticSeverity.Warning, "PE1505", $"Scene:{scene.Name}/Lighting", $"Scene has {sceneLights.Length} lights; the CPU renderer instantiates the first 16 enabled lights.", "Disable unused lights or split local lighting across scenes."));

                var sceneEmitters = editor.ParticleEmitters.Where(x => x.Enabled && (string.IsNullOrWhiteSpace(x.Scene) || string.Equals(x.Scene, scene.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (sceneEmitters.Length > 64)
                    results.Add(new(DiagnosticSeverity.Warning, "PE1605", $"Scene:{scene.Name}/Particles", $"Scene has {sceneEmitters.Length} emitters; the preview instantiates the first 64 enabled emitters.", "Disable unused emitters or split effects across scenes."));
                var requestedParticles = sceneEmitters.Where(x => x.Enabled).Sum(x => (long)Math.Clamp(x.MaxParticles, 1, 512));
                if (requestedParticles > 32_768)
                    results.Add(new(DiagnosticSeverity.Warning, "PE1606", $"Scene:{scene.Name}/Particles", $"Scene requests a particle pool budget of {requestedParticles}; the preview budget is 32,768.", "Reduce emitter counts or per-emitter Max Particles values."));
            }

            foreach (var duplicate in editor.Lights.GroupBy(x => (Scene: x.Scene ?? string.Empty, Name: x.Name ?? string.Empty), new AnimationScopeComparer()).Where(x => x.Count() > 1))
                results.Add(new(DiagnosticSeverity.Warning, "PE1504", "Lighting", $"Duplicate light name '{duplicate.Key.Name}' in scene scope '{duplicate.Key.Scene}'.", "Give each light a unique name within its scene."));
            foreach (var light in editor.Lights)
            {
                var where = $"Light:{light.Name}";
                if (!float.IsFinite(light.X) || !float.IsFinite(light.Y) || !float.IsFinite(light.Radius) || light.Radius < 1 || light.Radius > 4096 ||
                    !float.IsFinite(light.Intensity) || light.Intensity < 0 || light.Intensity > 1 ||
                    !float.IsFinite(light.Falloff) || light.Falloff < .25f || light.Falloff > 8f ||
                    !float.IsFinite(light.FlickerAmount) || light.FlickerAmount < 0f || light.FlickerAmount > .9f ||
                    !float.IsFinite(light.FlickerSpeed) || light.FlickerSpeed < 0f || light.FlickerSpeed > 60f)
                    results.Add(new(DiagnosticSeverity.Error, "PE1501", where, "Light position, radius, intensity, falloff or flicker settings are invalid or outside runtime limits.", "Use finite coordinates, radius 1–4096, intensity 0–1, falloff 0.25–8, flicker amount 0–0.9 and flicker speed 0–60."));
                if (!IsValidColor(light.Color))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1502", where, $"Light color '{light.Color}' is not a supported hex color.", "Use #RRGGBB or #AARRGGBB."));
                if (!string.IsNullOrWhiteSpace(light.Scene) && !project.Scenes.Any(x => string.Equals(x.Name, light.Scene, StringComparison.OrdinalIgnoreCase)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1503", where, $"Scene '{light.Scene}' was not found.", "Choose an existing scene."));
            }

            foreach (var duplicate in editor.ParticleEmitters.GroupBy(x => (Scene: x.Scene ?? string.Empty, Name: x.Name ?? string.Empty), new AnimationScopeComparer()).Where(x => x.Count() > 1))
                results.Add(new(DiagnosticSeverity.Warning, "PE1604", "Particles", $"Duplicate emitter name '{duplicate.Key.Name}' in scene scope '{duplicate.Key.Scene}'.", "Give each emitter a unique name within its scene."));
            foreach (var emitter in editor.ParticleEmitters)
            {
                var where = $"Particles:{emitter.Name}";
                if (!float.IsFinite(emitter.X) || !float.IsFinite(emitter.Y) || !float.IsFinite(emitter.Rate) || emitter.Rate < 0 || emitter.Rate > 10000 ||
                    !float.IsFinite(emitter.Lifetime) || emitter.Lifetime < 0.05f || emitter.Lifetime > 120 || emitter.MaxParticles is < 1 or > 10000 ||
                    !float.IsFinite(emitter.Speed) || emitter.Speed < 0 || emitter.Speed > 10000 ||
                    !float.IsFinite(emitter.Spread) || emitter.Spread < 0 || emitter.Spread > 360 ||
                    !float.IsFinite(emitter.Size) || emitter.Size < 0.5f || emitter.Size > 256 ||
                    !float.IsFinite(emitter.Gravity) || Math.Abs(emitter.Gravity) > 10000)
                    results.Add(new(DiagnosticSeverity.Error, "PE1601", where, "Particle emitter position or settings are invalid or outside runtime limits.", "Use rate 0–10000, lifetime 0–120, speed 0–10000, spread 0–360, size 0.5–256 and max particles 1–10000."));
                if (!IsValidColor(emitter.Color))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1602", where, $"Particle color '{emitter.Color}' is not a supported hex color.", "Use #RRGGBB or #AARRGGBB."));
                if (!string.IsNullOrWhiteSpace(emitter.Scene) && !project.Scenes.Any(x => string.Equals(x.Name, emitter.Scene, StringComparison.OrdinalIgnoreCase)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1603", where, $"Scene '{emitter.Scene}' was not found.", "Choose an existing scene."));
            }

            foreach (var duplicate in editor.TileMaps.GroupBy(x => (Scene: x.Scene ?? string.Empty, Name: x.Name ?? string.Empty), new AnimationScopeComparer()).Where(x => x.Count() > 1))
                results.Add(new(DiagnosticSeverity.Warning, "PE1706", "TileMaps", $"Duplicate tile map name '{duplicate.Key.Name}' in scene scope '{duplicate.Key.Scene}'.", "Give each tile map a unique name within its scene."));
            foreach (var map in editor.TileMaps)
            {
                var where = $"TileMap:{map.Name}";
                if (map.Width is < 1 or > 512 || map.Height is < 1 or > 512 || map.TileSize is < 1 or > 256)
                    results.Add(new(DiagnosticSeverity.Error, "PE1701", where, "Tile map dimensions or tile size are outside supported limits.", "Use width/height 1–512 and tile size 1–256."));
                if (!float.IsFinite(map.X) || !float.IsFinite(map.Y))
                    results.Add(new(DiagnosticSeverity.Error, "PE1707", where, "Tile map position contains a non-finite coordinate.", "Use finite world-space X and Y coordinates."));
                else if (map.Cells.Count != (long)map.Width * map.Height)
                    results.Add(new(DiagnosticSeverity.Warning, "PE1702", where, "Tile cell buffer does not match map dimensions.", "Resize or normalize the tile map."));
                if (map.Cells.Any(x => x < 0 || x >= Math.Max(1, map.Palette.Count)))
                    results.Add(new(DiagnosticSeverity.Error, "PE1703", where, "Tile map contains a cell with an invalid palette index.", "Paint the invalid cells with an available palette entry."));
                for (var i = 0; i < map.Palette.Count; i++)
                    if (!IsValidColor(map.Palette[i]))
                        results.Add(new(DiagnosticSeverity.Warning, "PE1705", where, $"Palette entry {i} is not a supported hex color.", "Use #RRGGBB or #AARRGGBB."));
                if (!string.IsNullOrWhiteSpace(map.Scene) && !project.Scenes.Any(x => string.Equals(x.Name, map.Scene, StringComparison.OrdinalIgnoreCase)))
                    results.Add(new(DiagnosticSeverity.Warning, "PE1704", where, $"Scene '{map.Scene}' was not found.", "Choose an existing scene."));
            }
        }

        if (results.Count == 0)
            results.Add(new(DiagnosticSeverity.Info, "PE0000", "Project", "No project issues found.", "The project passed the current static diagnostics."));
        return results;
    }

    /// <summary>Applies only deterministic value repairs; missing references and duplicate names are left for the user.</summary>
    public static IReadOnlyList<string> ApplySafeFixes(LicProjectData project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var fixes = new List<string>();
        project.Scenes ??= new List<LicSceneData>();
        if (project.Scenes.Count == 0)
        {
            project.Scenes.Add(new LicSceneData { Name = "Main" });
            fixes.Add("Created a Main scene because the project had no scenes.");
        }
        project.StartScene = Math.Clamp(project.StartScene, 0, project.Scenes.Count - 1);
        foreach (var scene in project.Scenes)
        foreach (var obj in scene.Objects ?? new())
        {
            if (!float.IsFinite(obj.X)) { obj.X = 0; fixes.Add($"Reset invalid X on {scene.Name}/{obj.Name}."); }
            if (!float.IsFinite(obj.Y)) { obj.Y = 0; fixes.Add($"Reset invalid Y on {scene.Name}/{obj.Name}."); }
            if (!float.IsFinite(obj.Rotation)) { obj.Rotation = 0; fixes.Add($"Reset invalid rotation on {scene.Name}/{obj.Name}."); }
            if (!float.IsFinite(obj.Width) || obj.Width <= 0) { obj.Width = 1; fixes.Add($"Repaired width on {scene.Name}/{obj.Name}."); }
            if (!float.IsFinite(obj.Height) || obj.Height <= 0) { obj.Height = 1; fixes.Add($"Repaired height on {scene.Name}/{obj.Name}."); }
            foreach (var component in obj.Components ?? new())
            {
                if (!float.IsFinite(component.Mass) || component.Mass <= 0) { component.Mass = 1; fixes.Add($"Repaired mass on {scene.Name}/{obj.Name}."); }
                if (!float.IsFinite(component.GravityScale)) { component.GravityScale = 1; fixes.Add($"Repaired gravity scale on {scene.Name}/{obj.Name}."); }
                if (!float.IsFinite(component.Friction) || component.Friction < 0) { component.Friction = .4f; fixes.Add($"Repaired friction on {scene.Name}/{obj.Name}."); }
                if (!float.IsFinite(component.Restitution) || component.Restitution < 0 || component.Restitution > 1) { component.Restitution = Math.Clamp(float.IsFinite(component.Restitution) ? component.Restitution : 0f, 0f, 1f); fixes.Add($"Clamped restitution on {scene.Name}/{obj.Name}."); }
                if (!float.IsFinite(component.Volume) || component.Volume < 0 || component.Volume > 1) { component.Volume = Math.Clamp(float.IsFinite(component.Volume) ? component.Volume : 1f, 0f, 1f); fixes.Add($"Clamped audio volume on {scene.Name}/{obj.Name}."); }
                if (string.Equals(component.Type, "Animator", StringComparison.OrdinalIgnoreCase) &&
                    (component.SpriteFrameWidth != 0 || component.SpriteFrameHeight != 0 || component.SpriteFrameCount != 0))
                {
                    var frameWidth = Math.Clamp(component.SpriteFrameWidth, 1, 8192);
                    var frameHeight = Math.Clamp(component.SpriteFrameHeight, 1, 8192);
                    var frameCount = Math.Clamp(component.SpriteFrameCount, 1, 100_000);
                    var fps = Math.Clamp(float.IsFinite(component.SpriteFramesPerSecond) ? component.SpriteFramesPerSecond : 8f, .01f, 240f);
                    var columns = Math.Clamp(component.SpriteSheetColumns, 0, 8192);
                    var firstFrame = Math.Clamp(component.SpriteFirstFrame, 0, 100_000);
                    if (frameWidth != component.SpriteFrameWidth || frameHeight != component.SpriteFrameHeight ||
                        frameCount != component.SpriteFrameCount || !float.IsFinite(component.SpriteFramesPerSecond) || Math.Abs(fps - component.SpriteFramesPerSecond) > .0001f ||
                        columns != component.SpriteSheetColumns || firstFrame != component.SpriteFirstFrame)
                    {
                        component.SpriteFrameWidth = frameWidth;
                        component.SpriteFrameHeight = frameHeight;
                        component.SpriteFrameCount = frameCount;
                        component.SpriteFramesPerSecond = fps;
                        component.SpriteSheetColumns = columns;
                        component.SpriteFirstFrame = firstFrame;
                        fixes.Add($"Repaired sprite-sheet settings on {scene.Name}/{obj.Name}.");
                    }
                }
            }
        }

        project.EditorData ??= new LicEditorData();
        var editor = project.EditorData;
        editor.Animations ??= new List<LicAnimationClipData>();
        editor.Lights ??= new List<LicLightData>();
        editor.ParticleEmitters ??= new List<LicParticleEmitterData>();
        editor.TileMaps ??= new List<LicTileMapData>();
        var ambient = Math.Clamp(float.IsFinite(editor.AmbientLight) ? editor.AmbientLight : 1f, 0f, 1f);
        if (!float.IsFinite(editor.AmbientLight) || Math.Abs(ambient - editor.AmbientLight) > 0.0001f)
        {
            editor.AmbientLight = ambient;
            fixes.Add("Clamped ambient lighting to 0–1.");
        }
        foreach (var clip in editor.Animations)
        {
            if (!float.IsFinite(clip.Duration) || clip.Duration <= 0) { clip.Duration = 1f; fixes.Add($"Set a positive duration on animation {clip.Name}."); }
            clip.Keyframes ??= new List<LicAnimationKeyframeData>();
            foreach (var key in clip.Keyframes)
            {
                if (!float.IsFinite(key.Time)) { key.Time = 0; fixes.Add($"Reset invalid keyframe time in {clip.Name}."); }
                key.Time = Math.Clamp(key.Time, 0f, clip.Duration);
                if (key.X.HasValue && !float.IsFinite(key.X.Value)) { key.X = null; fixes.Add($"Cleared invalid X channel in {clip.Name}."); }
                if (key.Y.HasValue && !float.IsFinite(key.Y.Value)) { key.Y = null; fixes.Add($"Cleared invalid Y channel in {clip.Name}."); }
                if (key.Rotation.HasValue && !float.IsFinite(key.Rotation.Value)) { key.Rotation = null; fixes.Add($"Cleared invalid rotation channel in {clip.Name}."); }
                if (key.ScaleX.HasValue)
                {
                    var repaired = float.IsFinite(key.ScaleX.Value) ? Math.Clamp(key.ScaleX.Value, -100f, 100f) : (float?)null;
                    if (!Nullable.Equals(repaired, key.ScaleX)) fixes.Add($"Repaired scale X channel in {clip.Name}.");
                    key.ScaleX = repaired;
                }
                if (key.ScaleY.HasValue)
                {
                    var repaired = float.IsFinite(key.ScaleY.Value) ? Math.Clamp(key.ScaleY.Value, -100f, 100f) : (float?)null;
                    if (!Nullable.Equals(repaired, key.ScaleY)) fixes.Add($"Repaired scale Y channel in {clip.Name}.");
                    key.ScaleY = repaired;
                }
            }
            var sorted = clip.Keyframes.OrderBy(x => x.Time).ToList();
            if (!clip.Keyframes.SequenceEqual(sorted)) { clip.Keyframes = sorted; fixes.Add($"Sorted keyframes in {clip.Name}."); }
        }
        foreach (var light in editor.Lights)
        {
            if (!float.IsFinite(light.X)) { light.X = 0f; fixes.Add($"Reset X on light {light.Name}."); }
            if (!float.IsFinite(light.Y)) { light.Y = 0f; fixes.Add($"Reset Y on light {light.Name}."); }
            var radius = Math.Clamp(float.IsFinite(light.Radius) ? light.Radius : 1f, 1f, 4096f);
            if (!float.IsFinite(light.Radius) || Math.Abs(radius - light.Radius) > 0.0001f) { light.Radius = radius; fixes.Add($"Clamped radius on light {light.Name}."); }
            var intensity = Math.Clamp(float.IsFinite(light.Intensity) ? light.Intensity : 0.65f, 0f, 1f);
            if (!float.IsFinite(light.Intensity) || Math.Abs(intensity - light.Intensity) > 0.0001f) { light.Intensity = intensity; fixes.Add($"Clamped intensity on light {light.Name}."); }
            if (!IsValidColor(light.Color)) { light.Color = "#FFCC88"; fixes.Add($"Reset invalid color on light {light.Name}."); }
            var falloff = Math.Clamp(float.IsFinite(light.Falloff) ? light.Falloff : 2f, .25f, 8f);
            if (!float.IsFinite(light.Falloff) || Math.Abs(falloff - light.Falloff) > .0001f) { light.Falloff = falloff; fixes.Add($"Clamped falloff on light {light.Name}."); }
            var flickerAmount = Math.Clamp(float.IsFinite(light.FlickerAmount) ? light.FlickerAmount : 0f, 0f, .9f);
            if (!float.IsFinite(light.FlickerAmount) || Math.Abs(flickerAmount - light.FlickerAmount) > .0001f) { light.FlickerAmount = flickerAmount; fixes.Add($"Clamped flicker amount on light {light.Name}."); }
            var flickerSpeed = Math.Clamp(float.IsFinite(light.FlickerSpeed) ? light.FlickerSpeed : 8f, 0f, 60f);
            if (!float.IsFinite(light.FlickerSpeed) || Math.Abs(flickerSpeed - light.FlickerSpeed) > .0001f) { light.FlickerSpeed = flickerSpeed; fixes.Add($"Clamped flicker speed on light {light.Name}."); }
        }
        foreach (var emitter in editor.ParticleEmitters)
        {
            if (!float.IsFinite(emitter.X)) { emitter.X = 0f; fixes.Add($"Reset X on particle emitter {emitter.Name}."); }
            if (!float.IsFinite(emitter.Y)) { emitter.Y = 0f; fixes.Add($"Reset Y on particle emitter {emitter.Name}."); }
            var rate = Math.Clamp(float.IsFinite(emitter.Rate) ? emitter.Rate : 0f, 0f, 10000f);
            if (!float.IsFinite(emitter.Rate) || Math.Abs(rate - emitter.Rate) > 0.0001f) { emitter.Rate = rate; fixes.Add($"Clamped particle rate on {emitter.Name}."); }
            var lifetime = Math.Clamp(float.IsFinite(emitter.Lifetime) ? emitter.Lifetime : 0.1f, 0.05f, 120f);
            if (!float.IsFinite(emitter.Lifetime) || Math.Abs(lifetime - emitter.Lifetime) > 0.0001f) { emitter.Lifetime = lifetime; fixes.Add($"Clamped lifetime on {emitter.Name}."); }
            var speed = Math.Clamp(float.IsFinite(emitter.Speed) ? emitter.Speed : 0f, 0f, 10000f);
            if (!float.IsFinite(emitter.Speed) || Math.Abs(speed - emitter.Speed) > 0.0001f) { emitter.Speed = speed; fixes.Add($"Clamped particle speed on {emitter.Name}."); }
            var spread = Math.Clamp(float.IsFinite(emitter.Spread) ? emitter.Spread : 90f, 0f, 360f);
            if (!float.IsFinite(emitter.Spread) || Math.Abs(spread - emitter.Spread) > 0.0001f) { emitter.Spread = spread; fixes.Add($"Clamped particle spread on {emitter.Name}."); }
            var size = Math.Clamp(float.IsFinite(emitter.Size) ? emitter.Size : 3f, 0.5f, 256f);
            if (!float.IsFinite(emitter.Size) || Math.Abs(size - emitter.Size) > 0.0001f) { emitter.Size = size; fixes.Add($"Clamped particle size on {emitter.Name}."); }
            var gravity = Math.Clamp(float.IsFinite(emitter.Gravity) ? emitter.Gravity : 0f, -10000f, 10000f);
            if (!float.IsFinite(emitter.Gravity) || Math.Abs(gravity - emitter.Gravity) > 0.0001f) { emitter.Gravity = gravity; fixes.Add($"Clamped gravity on particle emitter {emitter.Name}."); }
            var cap = Math.Clamp(emitter.MaxParticles, 1, 10000);
            if (cap != emitter.MaxParticles) { emitter.MaxParticles = cap; fixes.Add($"Clamped particle cap on {emitter.Name}."); }
            if (!IsValidColor(emitter.Color)) { emitter.Color = "#FFD166"; fixes.Add($"Reset invalid color on particle emitter {emitter.Name}."); }
        }
        foreach (var map in editor.TileMaps)
        {
            if (!float.IsFinite(map.X)) { map.X = 0f; fixes.Add($"Reset X on tile map {map.Name}."); }
            if (!float.IsFinite(map.Y)) { map.Y = 0f; fixes.Add($"Reset Y on tile map {map.Name}."); }
            var width = Math.Clamp(map.Width, 1, 512);
            var height = Math.Clamp(map.Height, 1, 512);
            var tileSize = Math.Clamp(map.TileSize, 1, 256);
            if (width != map.Width || height != map.Height)
            {
                map.Resize(width, height);
                fixes.Add($"Clamped dimensions on tile map {map.Name}.");
            }
            if (tileSize != map.TileSize) { map.TileSize = tileSize; fixes.Add($"Clamped tile size on map {map.Name}."); }
            map.EnsureCellStorage();
            var paletteMax = Math.Max(1, map.Palette.Count) - 1;
            for (var i = 0; i < map.Cells.Count; i++)
                if (map.Cells[i] < 0 || map.Cells[i] > paletteMax) { map.Cells[i] = 0; fixes.Add($"Cleared an invalid cell on tile map {map.Name}."); }
            for (var i = 0; i < map.Palette.Count; i++)
                if (!IsValidColor(map.Palette[i])) { map.Palette[i] = "#00000000"; fixes.Add($"Reset invalid palette color on tile map {map.Name}."); }
        }
        return fixes;
    }

    private static bool HasInvalidTransformChannel(LicAnimationKeyframeData key) =>
        (key.X.HasValue && !float.IsFinite(key.X.Value)) ||
        (key.Y.HasValue && !float.IsFinite(key.Y.Value)) ||
        (key.Rotation.HasValue && !float.IsFinite(key.Rotation.Value)) ||
        (key.ScaleX.HasValue && !float.IsFinite(key.ScaleX.Value)) ||
        (key.ScaleY.HasValue && !float.IsFinite(key.ScaleY.Value));

    private sealed class AnimationScopeComparer : IEqualityComparer<(string Scene, string Name)>
    {
        public bool Equals((string Scene, string Name) x, (string Scene, string Name) y)
            => string.Equals(x.Scene, y.Scene, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Scene, string Name) value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Scene ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name ?? string.Empty));
    }

    private static bool IsValidColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().TrimStart('#');
        return (text.Length == 6 || text.Length == 8) && uint.TryParse(text,
            System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static void CheckAsset(string? name, string location, HashSet<string> assets, List<ProjectDiagnostic> results)
    {
        if (!string.IsNullOrWhiteSpace(name) && !assets.Contains(name))
            results.Add(new(DiagnosticSeverity.Error, "PE1104", location, $"Asset '{name}' was not found.", "Import the file or assign an existing asset."));
    }

    private static void CheckAssetFormat(string? name, string expectedType, string location,
        IReadOnlyDictionary<string, string> assetTypes, List<ProjectDiagnostic> results)
    {
        if (string.IsNullOrWhiteSpace(name) || !assetTypes.TryGetValue(name, out var actualType)) return;
        if (!string.Equals(actualType, expectedType, StringComparison.OrdinalIgnoreCase))
            results.Add(new(DiagnosticSeverity.Warning, "PE1114", location,
                $"Asset '{name}' is '{actualType}', but this slot expects '{expectedType}'.",
                expectedType == "png" ? "Assign a PNG image resource." : "Assign a WAV audio resource."));
    }

    private static Dictionary<string, LicScriptData> ToDictionarySafe(this IEnumerable<LicScriptData> scripts)
    {
        return scripts.Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }
}
