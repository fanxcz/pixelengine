using System.Collections.ObjectModel;
using PixelEngine.Core;
using PixelEngine.Rendering;

namespace PixelEngine.Scene;

public class GameObject
{
    private readonly List<Component> _components = new();
    private readonly ReadOnlyCollection<Component> _componentsView;
    private Component[]? _componentSnapshot;
    private bool _started;
    private bool _wasRemovedFromScene;

    public string Name { get; set; }
    public bool Active { get; set; } = true;
    public Transform Transform { get; } = new();
    /// <summary>Read-only live view. Mutations must go through AddComponent/RemoveComponent so lifecycle hooks stay consistent.</summary>
    public IReadOnlyList<Component> Components => _componentsView;
    internal Scene? SceneOwner { get; set; }
    internal bool WasRemovedFromScene => _wasRemovedFromScene;

    public GameObject(string name = "GameObject")
    {
        Name = name;
        _componentsView = _components.AsReadOnly();
    }

    public T AddComponent<T>(T component) where T : Component
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component.GameObject != null) throw new InvalidOperationException("Component is already attached.");
        component.GameObject = this;
        _components.Add(component);
        _componentSnapshot = null;
        try
        {
            if (!component.NotifyReattached()) component.OnAdded();
            if (_started) component.StartOnce();
            return component;
        }
        catch
        {
            // OnAdded/Start may have acquired resources before failing. Give the component
            // one teardown callback before detaching it; never mask the original exception.
            try { component.NotifyRemoved(); } catch { /* Preserve the attachment failure. */ }
            _components.Remove(component);
            _componentSnapshot = null;
            component.ResetAfterFailedAttachment();
            component.GameObject = null!;
            throw;
        }
    }

    public bool RemoveComponent(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!_components.Remove(component)) return false;
        _componentSnapshot = null;
        try { component.NotifyRemoved(); }
        finally { component.GameObject = null!; }
        return true;
    }

    public bool RemoveComponent<T>() where T : Component
    {
        var component = GetComponent<T>();
        return component is not null && RemoveComponent(component);
    }

    public T? GetComponent<T>() where T : Component
    {
        for (var i = 0; i < _components.Count; i++)
            if (_components[i] is T typed) return typed;
        return null;
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        // Iterate the same stable snapshot used by Update/Draw so callers may safely
        // add or remove components from callbacks without invalidating an enumeration.
        var snapshot = ComponentSnapshot;
        for (var i = 0; i < snapshot.Length; i++)
            if (snapshot[i] is T typed && ReferenceEquals(typed.GameObject, this)) yield return typed;
    }

    private Component[] ComponentSnapshot => _componentSnapshot ??= _components.ToArray();

    internal void StartComponent(Component component)
    {
        if (_started) component.StartOnce();
    }

    internal void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            foreach (var component in ComponentSnapshot)
                if (ReferenceEquals(component.GameObject, this)) component.StartOnce();
        }
        catch
        {
            _started = false;
            throw;
        }
    }

    internal void FixedUpdate(EngineTime time)
    {
        if (!Active || _wasRemovedFromScene) return;
        foreach (var component in ComponentSnapshot)
        {
            if (_wasRemovedFromScene) break;
            if (ReferenceEquals(component.GameObject, this) && component.Enabled) component.FixedUpdate(time);
        }
    }

    internal void Update(EngineTime time)
    {
        if (!Active || _wasRemovedFromScene) return;
        foreach (var component in ComponentSnapshot)
        {
            if (_wasRemovedFromScene) break;
            if (ReferenceEquals(component.GameObject, this) && component.Enabled) component.Update(time);
        }
    }

    internal void Draw(RenderContext context)
    {
        if (!Active || _wasRemovedFromScene) return;
        foreach (var component in ComponentSnapshot)
        {
            if (_wasRemovedFromScene) break;
            if (ReferenceEquals(component.GameObject, this) && component.Enabled) component.Draw(context);
        }
    }

    internal void PrepareForSceneRemoval()
    {
        // Stop the update/start lifecycle before exit callbacks or OnRemoved callbacks run.
        // Those callbacks are allowed to add components; they must not start on a detached object.
        _started = false;
        _wasRemovedFromScene = true;
    }

    internal void NotifyRemoved()
    {
        PrepareForSceneRemoval();
        Exception? firstError = null;
        foreach (var component in ComponentSnapshot)
        {
            try { component.NotifyRemoved(); }
            catch (Exception ex) { firstError ??= ex; }
        }
        if (firstError is not null) throw new InvalidOperationException(
            $"One or more components on '{Name}' failed during removal.", firstError);
    }

    internal void NotifyReattached()
    {
        if (!_wasRemovedFromScene) return;
        foreach (var component in ComponentSnapshot)
            component.NotifyReattached();
        _wasRemovedFromScene = false;
    }
}
