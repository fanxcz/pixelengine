using System.Collections.ObjectModel;
using PixelEngine.Core;
using PixelEngine.Rendering;
using PixelEngine.Physics;
using Microsoft.Xna.Framework;

namespace PixelEngine.Scene;

public sealed class Scene
{
    private readonly List<GameObject> _objects = new();
    private readonly ReadOnlyCollection<GameObject> _objectsView;
    private GameObject[]? _objectSnapshot;
    private bool _started;
    private readonly Physics2DWorld _physics = new();

    public string Name { get; set; }
    public Color BackgroundColor { get; set; } = new(24, 26, 32);
    public Vector2 Gravity { get; set; } = new(0f, 980f);
    /// <summary>Ambient lighting multiplier. 1 leaves legacy scenes visually unchanged.</summary>
    public float AmbientLight { get; set; } = 1f;
    /// <summary>Read-only live view. Use Add/Remove so scene ownership and lifecycle remain valid.</summary>
    public IReadOnlyList<GameObject> Objects => _objectsView;
    public Physics2DWorld Physics => _physics;

    public Scene(string name = "Scene")
    {
        Name = name;
        _objectsView = _objects.AsReadOnly();
    }

    public T Add<T>(T gameObject) where T : GameObject
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        if (gameObject.SceneOwner is not null && !ReferenceEquals(gameObject.SceneOwner, this))
            throw new InvalidOperationException("GameObject already belongs to another scene.");
        if (_objects.Contains(gameObject)) throw new InvalidOperationException("Object already belongs to this scene.");
        _objects.Add(gameObject);
        gameObject.SceneOwner = this;
        _objectSnapshot = null;
        try
        {
            gameObject.NotifyReattached();
            if (_started) gameObject.Start();
            return gameObject;
        }
        catch
        {
            _objects.Remove(gameObject);
            _objectSnapshot = null;
            gameObject.SceneOwner = null;
            try { gameObject.NotifyRemoved(); }
            catch { /* Preserve the original attachment exception. */ }
            throw;
        }
    }

    public bool Remove(GameObject gameObject)
    {
        if (!_objects.Remove(gameObject)) return false;
        _objectSnapshot = null;
        gameObject.PrepareForSceneRemoval();
        try { _physics.Forget(gameObject); }
        finally
        {
            gameObject.SceneOwner = null;
            gameObject.NotifyRemoved();
        }
        return true;
    }

    public GameObject? Find(string name)
    {
        for (var i = 0; i < _objects.Count; i++)
            if (string.Equals(_objects[i].Name, name, StringComparison.Ordinal)) return _objects[i];
        return null;
    }

    public IEnumerable<GameObject> FindAll(string name)
    {
        var snapshot = ObjectSnapshot;
        for (var i = 0; i < snapshot.Length; i++)
            if (ReferenceEquals(snapshot[i].SceneOwner, this)
                && string.Equals(snapshot[i].Name, name, StringComparison.Ordinal)) yield return snapshot[i];
    }

    private GameObject[] ObjectSnapshot => _objectSnapshot ??= _objects.ToArray();

    internal void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            foreach (var gameObject in ObjectSnapshot)
                if (ReferenceEquals(gameObject.SceneOwner, this)) gameObject.Start();
        }
        catch
        {
            _started = false;
            throw;
        }
    }

    internal void FixedUpdate(EngineTime time)
    {
        foreach (var gameObject in ObjectSnapshot)
            if (ReferenceEquals(gameObject.SceneOwner, this)) gameObject.FixedUpdate(time);
        _physics.Step(this, time.FixedDeltaTime, Gravity);
    }

    internal void Update(EngineTime time)
    {
        foreach (var gameObject in ObjectSnapshot)
            if (ReferenceEquals(gameObject.SceneOwner, this)) gameObject.Update(time);
    }

    internal void Draw(RenderContext context)
    {
        foreach (var gameObject in ObjectSnapshot)
            if (ReferenceEquals(gameObject.SceneOwner, this)) gameObject.Draw(context);
    }
}
