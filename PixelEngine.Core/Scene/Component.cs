using PixelEngine.Core;
using PixelEngine.Rendering;

namespace PixelEngine.Scene;

/// <summary>Base class for attachable behaviour on a GameObject.</summary>
public abstract class Component
{
    private bool _enabled = true;
    private bool _started;
    private bool _removed;

    public GameObject GameObject { get; internal set; } = null!;
    public Transform Transform => GameObject.Transform;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            if (!value)
            {
                _enabled = false;
                OnDisabled();
                return;
            }

            _enabled = true;
            try
            {
                OnEnabled();
                if (GameObject is not null) GameObject.StartComponent(this);
            }
            catch
            {
                // A failed enable/start must not leave the component marked active.
                _enabled = false;
                try { OnDisabled(); } catch { /* Preserve the original activation error. */ }
                throw;
            }
        }
    }

    public virtual void OnAdded() { }
    public virtual void OnEnabled() { }
    public virtual void OnDisabled() { }
    public virtual void Start() { }
    public virtual void FixedUpdate(EngineTime time) { }
    public virtual void Update(EngineTime time) { }
    public virtual void Draw(RenderContext context) { }
    public virtual void OnRemoved() { }
    public virtual void OnCollisionEnter2D(PixelEngine.Physics.Collision2D collision) { }
    public virtual void OnCollisionStay2D(PixelEngine.Physics.Collision2D collision) { }
    public virtual void OnCollisionExit2D(PixelEngine.Physics.Collision2D collision) { }
    public virtual void OnTriggerEnter2D(PixelEngine.Physics.Collider2DComponent other) { }
    public virtual void OnTriggerStay2D(PixelEngine.Physics.Collider2DComponent other) { }
    public virtual void OnTriggerExit2D(PixelEngine.Physics.Collider2DComponent other) { }

    internal void StartOnce()
    {
        if (_started || _removed || !_enabled) return;
        _started = true;
        try { Start(); }
        catch
        {
            _started = false;
            throw;
        }
    }

    internal void NotifyRemoved()
    {
        if (_removed) return;
        _removed = true;
        OnRemoved();
    }

    internal bool NotifyReattached()
    {
        if (!_removed) return false;
        _removed = false;
        _started = false;
        OnAdded();
        return true;
    }

    internal void ResetAfterFailedAttachment()
    {
        _started = false;
        _removed = true;
    }
}
