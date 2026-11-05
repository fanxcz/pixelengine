using System.Text.Json;

namespace PixelEngine.Serialization;

/// <summary>Safe, schema-preserving editing operations for LIC projects.</summary>
public static class LicProjectOperations
{
    public static int AddScene(LicProjectData project, string? requestedName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Scenes ??= new List<LicSceneData>();
        var name = MakeUniqueSceneName(project, string.IsNullOrWhiteSpace(requestedName) ? "Scene" : requestedName.Trim());
        project.Scenes.Add(new LicSceneData { Name = name });
        return project.Scenes.Count - 1;
    }

    /// <summary>Duplicates a scene and inserts the copy immediately after the source scene.</summary>
    public static int DuplicateScene(LicProjectData project, int sceneIndex)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateSceneIndex(project, sceneIndex);
        var source = project.Scenes[sceneIndex];
        var duplicate = CloneScene(source);
        duplicate.Name = MakeUniqueSceneName(project, source.Name + " Copy");
        var insertIndex = sceneIndex + 1;
        project.Scenes.Insert(insertIndex, duplicate);
        var clonedAnimationNames = DuplicateSceneEditorData(project, source.Name, duplicate.Name);
        if (clonedAnimationNames.Count > 0)
        {
            foreach (var component in duplicate.Objects.SelectMany(x => x.Components ?? new List<LicComponentData>()))
            {
                if (!string.Equals(component.Type, "Animator", StringComparison.OrdinalIgnoreCase)) continue;
                if (clonedAnimationNames.TryGetValue(component.Animation, out var clonedName))
                    component.Animation = clonedName;
            }
        }
        if (project.StartScene > sceneIndex) project.StartScene++;
        return insertIndex;
    }

    /// <summary>Removes a scene. A project is never allowed to have zero scenes.</summary>
    public static int RemoveScene(LicProjectData project, int sceneIndex)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateSceneIndex(project, sceneIndex);
        if (project.Scenes.Count <= 1)
            throw new InvalidOperationException("A LIC project must contain at least one scene.");

        var removedSceneName = project.Scenes[sceneIndex].Name;
        project.Scenes.RemoveAt(sceneIndex);
        RemoveSceneEditorData(project, removedSceneName);
        if (project.StartScene > sceneIndex) project.StartScene--;
        else if (project.StartScene == sceneIndex) project.StartScene = Math.Min(sceneIndex, project.Scenes.Count - 1);
        project.StartScene = Math.Clamp(project.StartScene, 0, project.Scenes.Count - 1);
        return Math.Clamp(sceneIndex, 0, project.Scenes.Count - 1);
    }

    public static void SetStartScene(LicProjectData project, int sceneIndex)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateSceneIndex(project, sceneIndex);
        project.StartScene = sceneIndex;
    }

    private static void ValidateSceneIndex(LicProjectData project, int sceneIndex)
    {
        if (project.Scenes is null || project.Scenes.Count == 0)
            throw new InvalidOperationException("The project has no scenes.");
        if (sceneIndex < 0 || sceneIndex >= project.Scenes.Count)
            throw new ArgumentOutOfRangeException(nameof(sceneIndex));
    }

    private static string MakeUniqueSceneName(LicProjectData project, string baseName)
    {
        var candidate = string.IsNullOrWhiteSpace(baseName) ? "Scene" : baseName.Trim();
        var existing = project.Scenes.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(candidate)) return candidate;
        for (var suffix = 1; ; suffix++)
        {
            var numbered = $"{candidate} {suffix}";
            if (!existing.Contains(numbered)) return numbered;
        }
    }

    public static LicPrefabData CreatePrefab(LicProjectData project, LicObjectData source, string? requestedName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        project.Prefabs ??= new List<LicPrefabData>();
        var name = MakeUniquePrefabName(project, string.IsNullOrWhiteSpace(requestedName) ? source.Name : requestedName.Trim());
        var prefab = new LicPrefabData { Name = name, Object = CloneObject(source) };
        prefab.Object.Name = name;
        prefab.Object.Prefab = name;
        source.Prefab = name;
        project.Prefabs.Add(prefab);
        return prefab;
    }

    public static LicObjectData InstantiatePrefab(LicSceneData scene, LicPrefabData prefab)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(prefab);
        var clone = CloneObject(prefab.Object);
        var baseName = prefab.Name;
        var names = scene.Objects.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        clone.Name = baseName;
        for (var i = 1; names.Contains(clone.Name); i++) clone.Name = $"{baseName} {i}";
        clone.Prefab = prefab.Name;
        clone.X += 16f;
        clone.Y += 16f;
        scene.Objects.Add(clone);
        return clone;
    }

    /// <summary>Replaces a prefab template and propagates its non-transform data to linked instances.</summary>
    public static int UpdatePrefabFromObject(LicProjectData project, LicPrefabData prefab, LicObjectData source, bool updateInstances = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(prefab);
        ArgumentNullException.ThrowIfNull(source);

        if (!project.Prefabs.Contains(prefab))
            throw new InvalidOperationException("Prefab does not belong to this project.");
        var replacement = CloneObject(source);
        replacement.Name = prefab.Name;
        replacement.Prefab = prefab.Name;
        prefab.Object = replacement;
        if (!updateInstances) return 0;

        var updated = 0;
        foreach (var instance in project.Scenes.SelectMany(scene => scene.Objects)
                     .Where(item => string.Equals(item.Prefab, prefab.Name, StringComparison.OrdinalIgnoreCase)))
        {
            if (ReferenceEquals(instance, source)) continue;
            var name = instance.Name;
            var x = instance.X;
            var y = instance.Y;
            var rotation = instance.Rotation;
            var prefabName = instance.Prefab;
            CopyObjectData(replacement, instance);
            // Prefab propagation updates render/gameplay/component data but keeps
            // per-instance transform overrides intact.
            instance.Name = name;
            instance.X = x;
            instance.Y = y;
            instance.Rotation = rotation;
            instance.Prefab = prefabName;
            updated++;
        }
        return updated;
    }

    private static void CopyObjectData(LicObjectData source, LicObjectData target)
    {
        target.Kind = source.Kind;
        target.Rotation = source.Rotation;
        target.Width = source.Width;
        target.Height = source.Height;
        target.Color = source.Color;
        target.Text = source.Text;
        target.Sprite = source.Sprite;
        target.Solid = source.Solid;
        target.Gravity = source.Gravity;
        target.Follow = source.Follow;
        target.Script = source.Script;
        target.Points = source.Points?.ToList() ?? new List<float>();
        target.Components = source.Components?.Select(CloneComponent).ToList() ?? new List<LicComponentData>();
        target.ExtraFields = CloneExtraFields(source.ExtraFields);
    }

    public static LicPrefabData DuplicatePrefab(LicProjectData project, LicPrefabData source, string? requestedName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        if (!project.Prefabs.Contains(source)) throw new InvalidOperationException("Prefab does not belong to this project.");
        var name = MakeUniquePrefabName(project, string.IsNullOrWhiteSpace(requestedName) ? source.Name + " Copy" : requestedName.Trim());
        var clone = new LicPrefabData
        {
            Name = name,
            Object = CloneObject(source.Object),
            ExtraFields = CloneExtraFields(source.ExtraFields)
        };
        clone.Object.Name = name;
        clone.Object.Prefab = name;
        project.Prefabs.Add(clone);
        return clone;
    }

    public static void RenamePrefab(LicProjectData project, LicPrefabData prefab, string newName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(prefab);
        if (!project.Prefabs.Contains(prefab)) throw new InvalidOperationException("Prefab does not belong to this project.");
        var name = (newName ?? string.Empty).Trim();
        if (name.Length == 0 || name.Any(char.IsControl) || name.IndexOfAny("/\\".ToCharArray()) >= 0)
            throw new ArgumentException("Prefab name must be non-empty and cannot contain path separators or control characters.", nameof(newName));
        if (project.Prefabs.Any(x => !ReferenceEquals(x, prefab) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Prefab '{name}' already exists.");
        var oldName = prefab.Name;
        prefab.Name = name;
        prefab.Object.Name = name;
        prefab.Object.Prefab = name;
        foreach (var instance in project.Scenes.SelectMany(scene => scene.Objects)
                     .Where(item => string.Equals(item.Prefab, oldName, StringComparison.OrdinalIgnoreCase)))
            instance.Prefab = name;
    }

    /// <summary>Restores a linked scene instance from its prefab template while preserving its name and transform.</summary>
    public static bool RevertPrefabInstance(LicProjectData project, LicObjectData instance)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(instance.Prefab)) return false;
        var prefab = project.Prefabs.FirstOrDefault(x => string.Equals(x.Name, instance.Prefab, StringComparison.OrdinalIgnoreCase));
        if (prefab is null) return false;
        var name = instance.Name;
        var x = instance.X;
        var y = instance.Y;
        var rotation = instance.Rotation;
        var prefabName = instance.Prefab;
        CopyObjectData(prefab.Object, instance);
        instance.Name = name;
        instance.X = x;
        instance.Y = y;
        instance.Rotation = rotation;
        instance.Prefab = prefabName;
        return true;
    }

    /// <summary>Breaks the link to a prefab without changing the instance's visible or component data.</summary>
    public static bool UnpackPrefabInstance(LicObjectData instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(instance.Prefab)) return false;
        instance.Prefab = string.Empty;
        return true;
    }

    /// <summary>Returns a deep copy of component authoring data including unknown JSON extension fields.</summary>
    public static LicComponentData CloneComponentData(LicComponentData source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CloneComponent(source);
    }

    public static bool RemovePrefab(LicProjectData project, LicPrefabData prefab)
    {
        return RemovePrefabAndUnpackInstances(project, prefab) >= 0;
    }

    /// <summary>Removes a prefab template and unpacks every linked scene instance so no dangling links remain.</summary>
    public static int RemovePrefabAndUnpackInstances(LicProjectData project, LicPrefabData prefab)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(prefab);
        if (!project.Prefabs.Contains(prefab)) return -1;
        var unpacked = 0;
        foreach (var instance in project.Scenes.SelectMany(scene => scene.Objects)
                     .Where(item => string.Equals(item.Prefab, prefab.Name, StringComparison.OrdinalIgnoreCase)))
        {
            instance.Prefab = string.Empty;
            unpacked++;
        }
        project.Prefabs.Remove(prefab);
        return unpacked;
    }

    private static string MakeUniquePrefabName(LicProjectData project, string baseName)
    {
        var candidate = string.IsNullOrWhiteSpace(baseName) ? "Prefab" : baseName.Trim();
        var existing = project.Prefabs.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(candidate)) return candidate;
        for (var suffix = 1; ; suffix++)
        {
            var numbered = $"{candidate} {suffix}";
            if (!existing.Contains(numbered)) return numbered;
        }
    }

    private static Dictionary<string, string> DuplicateSceneEditorData(LicProjectData project, string sourceScene, string newScene)
    {
        var clonedAnimationNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (project.EditorData is not { } editor) return clonedAnimationNames;
        foreach (var clip in editor.Animations.Where(x => string.Equals(x.Scene, sourceScene, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var clone = CloneAnimation(clip);
            clone.Scene = newScene;
            clone.Name = MakeUniqueName(editor.Animations.Select(x => x.Name), clone.Name + " Copy");
            editor.Animations.Add(clone);
            clonedAnimationNames[clip.Name] = clone.Name;
        }
        foreach (var light in editor.Lights.Where(x => string.Equals(x.Scene, sourceScene, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var clone = CloneLight(light); clone.Scene = newScene;
            clone.Name = MakeUniqueName(editor.Lights.Select(x => x.Name), clone.Name + " Copy");
            editor.Lights.Add(clone);
        }
        foreach (var emitter in editor.ParticleEmitters.Where(x => string.Equals(x.Scene, sourceScene, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var clone = CloneEmitter(emitter); clone.Scene = newScene;
            clone.Name = MakeUniqueName(editor.ParticleEmitters.Select(x => x.Name), clone.Name + " Copy");
            editor.ParticleEmitters.Add(clone);
        }
        foreach (var map in editor.TileMaps.Where(x => string.Equals(x.Scene, sourceScene, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var clone = CloneTileMap(map); clone.Scene = newScene;
            clone.Name = MakeUniqueName(editor.TileMaps.Select(x => x.Name), clone.Name + " Copy");
            editor.TileMaps.Add(clone);
        }
        return clonedAnimationNames;
    }

    private static void RemoveSceneEditorData(LicProjectData project, string sceneName)
    {
        if (project.EditorData is not { } editor) return;
        editor.Animations.RemoveAll(x => string.Equals(x.Scene, sceneName, StringComparison.OrdinalIgnoreCase));
        editor.Lights.RemoveAll(x => string.Equals(x.Scene, sceneName, StringComparison.OrdinalIgnoreCase));
        editor.ParticleEmitters.RemoveAll(x => string.Equals(x.Scene, sceneName, StringComparison.OrdinalIgnoreCase));
        editor.TileMaps.RemoveAll(x => string.Equals(x.Scene, sceneName, StringComparison.OrdinalIgnoreCase));
    }

    private static string MakeUniqueName(IEnumerable<string> names, string baseName)
    {
        var existing = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName)) return baseName;
        for (var i = 1; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static LicAnimationClipData CloneAnimation(LicAnimationClipData source) => new()
    {
        Name = source.Name, Scene = source.Scene, Target = source.Target, Duration = source.Duration, Loop = source.Loop,
        Keyframes = source.Keyframes.Select(x => new LicAnimationKeyframeData
        {
            Time = x.Time, X = x.X, Y = x.Y, Rotation = x.Rotation, ScaleX = x.ScaleX, ScaleY = x.ScaleY,
            ExtraFields = CloneExtraFields(x.ExtraFields)
        }).ToList(), ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static LicLightData CloneLight(LicLightData source) => new()
    {
        Name = source.Name, Scene = source.Scene, X = source.X, Y = source.Y, Radius = source.Radius,
        Color = source.Color, Intensity = source.Intensity, Enabled = source.Enabled, ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static LicParticleEmitterData CloneEmitter(LicParticleEmitterData source) => new()
    {
        Name = source.Name, Scene = source.Scene, X = source.X, Y = source.Y, Rate = source.Rate, Lifetime = source.Lifetime,
        Speed = source.Speed, Spread = source.Spread, Size = source.Size, Gravity = source.Gravity,
        MaxParticles = source.MaxParticles, Color = source.Color, Enabled = source.Enabled, ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static LicTileMapData CloneTileMap(LicTileMapData source) => new()
    {
        Name = source.Name, Scene = source.Scene, X = source.X, Y = source.Y, Width = source.Width, Height = source.Height,
        TileSize = source.TileSize, Cells = source.Cells.ToList(), Palette = source.Palette.ToList(), ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static LicComponentData CloneComponent(LicComponentData source) => new()
    {
        Type = source.Type,
        Enabled = source.Enabled,
        Mass = source.Mass,
        GravityScale = source.GravityScale,
        LinearDamping = source.LinearDamping,
        UseGravity = source.UseGravity,
        FreezePositionX = source.FreezePositionX,
        FreezePositionY = source.FreezePositionY,
        IsKinematic = source.IsKinematic,
        IsStatic = source.IsStatic,
        Friction = source.Friction,
        Restitution = source.Restitution,
        IsTrigger = source.IsTrigger,
        CollisionLayer = source.CollisionLayer,
        CollisionMask = source.CollisionMask,
        BoxSizeX = source.BoxSizeX,
        BoxSizeY = source.BoxSizeY,
        ColliderOffsetX = source.ColliderOffsetX,
        ColliderOffsetY = source.ColliderOffsetY,
        Radius = source.Radius,
        SortingLayer = source.SortingLayer,
        SortingOrder = source.SortingOrder,
        Texture = source.Texture,
        Tint = source.Tint,
        Clip = source.Clip,
        Volume = source.Volume,
        Pitch = source.Pitch,
        Pan = source.Pan,
        AudioBus = source.AudioBus,
        Loop = source.Loop,
        PlayOnAwake = source.PlayOnAwake,
        Animation = source.Animation,
        Speed = source.Speed,
        SpriteFrameWidth = source.SpriteFrameWidth,
        SpriteFrameHeight = source.SpriteFrameHeight,
        SpriteFrameCount = source.SpriteFrameCount,
        SpriteFramesPerSecond = source.SpriteFramesPerSecond,
        SpriteSheetColumns = source.SpriteSheetColumns,
        SpriteFirstFrame = source.SpriteFirstFrame,
        SpriteLoop = source.SpriteLoop,
        SpritePingPong = source.SpritePingPong,
        CastsShadows = source.CastsShadows,
        CameraTarget = source.CameraTarget,
        CameraOffsetX = source.CameraOffsetX,
        CameraOffsetY = source.CameraOffsetY,
        CameraSmoothSpeed = source.CameraSmoothSpeed,
        CameraDeadZoneX = source.CameraDeadZoneX,
        CameraDeadZoneY = source.CameraDeadZoneY,
        ScriptName = source.ScriptName,
        ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static LicSceneData CloneScene(LicSceneData source) => new()
    {
        Name = source.Name,
        Gravity = source.Gravity,
        GroundY = source.GroundY,
        LockCamera = source.LockCamera,
        FreeCamera = source.FreeCamera,
        Decoration = source.Decoration,
        CameraX = source.CameraX,
        CameraY = source.CameraY,
        Background1 = source.Background1,
        Background2 = source.Background2,
        Objects = source.Objects?.Select(CloneObject).ToList() ?? new List<LicObjectData>(),
        ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static LicObjectData CloneObject(LicObjectData source) => new()
    {
        Name = source.Name,
        Kind = source.Kind,
        X = source.X,
        Y = source.Y,
        Rotation = source.Rotation,
        Width = source.Width,
        Height = source.Height,
        Color = source.Color,
        Text = source.Text,
        Sprite = source.Sprite,
        Solid = source.Solid,
        Gravity = source.Gravity,
        Follow = source.Follow,
        Script = source.Script,
        Points = source.Points?.ToList() ?? new List<float>(),
        Prefab = source.Prefab,
        Components = source.Components?.Select(CloneComponent).ToList() ?? new List<LicComponentData>(),
        ExtraFields = CloneExtraFields(source.ExtraFields)
    };

    private static Dictionary<string, JsonElement>? CloneExtraFields(Dictionary<string, JsonElement>? fields)
    {
        if (fields is null || fields.Count == 0) return fields is null ? null : new Dictionary<string, JsonElement>();
        return fields.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    }
}
