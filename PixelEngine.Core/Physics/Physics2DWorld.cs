using Microsoft.Xna.Framework;
using PixelEngine.Scene;
using SceneType = PixelEngine.Scene.Scene;

namespace PixelEngine.Physics;

/// <summary>
/// Deterministic fixed-step 2D rigid-body solver with a uniform-grid broadphase,
/// oriented-box/circle narrowphase, trigger contacts, impulses and positional correction.
/// </summary>
public sealed class Physics2DWorld
{
    private const float CellSize = 128f;
    private const int MaxCellsPerCollider = 256;
    private const float PositionCorrectionPercent = .8f;
    private const float PenetrationSlop = .01f;
    private readonly Dictionary<long, Contact> _previousContacts = new();

    private readonly record struct Contact(Collider2DComponent A, Collider2DComponent B, Collision2D Collision);

    public int LastBroadphasePairCount { get; private set; }
    public int LastContactCount { get; private set; }

    public void Step(SceneType scene, float deltaTime, Vector2 gravity)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        deltaTime = Math.Clamp(deltaTime, 0f, .25f);
        if (!float.IsFinite(gravity.X) || !float.IsFinite(gravity.Y)) gravity = Vector2.Zero;

        var colliders = new List<Collider2DComponent>();
        foreach (var gameObject in scene.Objects)
        {
            if (!gameObject.Active) continue;
            if (gameObject.GetComponent<Rigidbody2DComponent>() is { Enabled: true } body)
            {
                body.Integrate(deltaTime, gravity);
            }
            foreach (var collider in gameObject.GetComponents<Collider2DComponent>())
                if (collider.Enabled && collider.GameObject.Active) colliders.Add(collider);
        }

        var grid = new Dictionary<(int X, int Y), List<Collider2DComponent>>();
        var oversized = new List<Collider2DComponent>();
        foreach (var collider in colliders)
        {
            var shape = collider.GetShape();
            var min = shape.IsCircle ? shape.Center - new Vector2(shape.Radius) : shape.Min;
            var max = shape.IsCircle ? shape.Center + new Vector2(shape.Radius) : shape.Max;
            if (!IsFinite(min) || !IsFinite(max)) continue;
            var x0 = ToCell(min.X); var x1 = ToCell(max.X);
            var y0 = ToCell(min.Y); var y1 = ToCell(max.Y);
            var cellCount = ((long)x1 - x0 + 1L) * ((long)y1 - y0 + 1L);
            if (cellCount <= 0 || cellCount > MaxCellsPerCollider)
            {
                oversized.Add(collider);
                continue;
            }
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                if (!grid.TryGetValue((x, y), out var bucket)) grid[(x, y)] = bucket = new List<Collider2DComponent>(4);
                bucket.Add(collider);
            }
        }

        var candidateKeys = new HashSet<long>();
        var candidatePairs = new List<(Collider2DComponent A, Collider2DComponent B)>();
        foreach (var bucket in grid.Values)
        {
            for (var i = 0; i < bucket.Count; i++)
            for (var j = i + 1; j < bucket.Count; j++) AddCandidate(bucket[i], bucket[j], candidateKeys, candidatePairs);
        }
        foreach (var huge in oversized)
            foreach (var collider in colliders)
                if (!ReferenceEquals(huge, collider)) AddCandidate(huge, collider, candidateKeys, candidatePairs);

        LastBroadphasePairCount = candidatePairs.Count;
        var currentContacts = new Dictionary<long, Contact>();
        foreach (var pair in candidatePairs)
        {
            if (!pair.A.CanCollideWith(pair.B)) continue;
            if (!TryOverlap(pair.A.GetShape(), pair.B.GetShape(), out var normal, out var penetration)) continue;

            var bodyA = pair.A.GameObject.GetComponent<Rigidbody2DComponent>();
            var bodyB = pair.B.GameObject.GetComponent<Rigidbody2DComponent>();
            var relativeVelocity = (bodyB?.Velocity ?? Vector2.Zero) - (bodyA?.Velocity ?? Vector2.Zero);
            var collision = new Collision2D(pair.A, pair.B, normal, penetration, relativeVelocity);
            var key = PairKey(pair.A, pair.B);
            currentContacts[key] = new Contact(pair.A, pair.B, collision);
            if (!pair.A.IsTrigger && !pair.B.IsTrigger) Resolve(collision, bodyA, bodyB);
        }

        foreach (var (key, contact) in currentContacts)
        {
            var isNew = !_previousContacts.ContainsKey(key);
            Dispatch(contact, isNew ? ContactPhase.Enter : ContactPhase.Stay);
        }
        foreach (var (key, contact) in _previousContacts)
            if (!currentContacts.ContainsKey(key)) Dispatch(contact, ContactPhase.Exit);

        _previousContacts.Clear();
        foreach (var item in currentContacts)
        {
            // Collision callbacks are allowed to disable or remove objects. Do not retain
            // a contact for a collider that was detached during event dispatch.
            if (ReferenceEquals(item.Value.A.GameObject.SceneOwner, scene)
                && ReferenceEquals(item.Value.B.GameObject.SceneOwner, scene)
                && item.Value.A.Enabled && item.Value.B.Enabled
                && item.Value.A.GameObject.Active && item.Value.B.GameObject.Active)
                _previousContacts.Add(item.Key, item.Value);
        }
        LastContactCount = _previousContacts.Count;
    }

    internal void Forget(GameObject gameObject)
    {
        foreach (var key in _previousContacts.Where(x => ReferenceEquals(x.Value.A.GameObject, gameObject)
                    || ReferenceEquals(x.Value.B.GameObject, gameObject)).Select(x => x.Key).ToArray())
        {
            var contact = _previousContacts[key];
            Dispatch(contact, ContactPhase.Exit);
            _previousContacts.Remove(key);
        }
    }

    private static void AddCandidate(Collider2DComponent a, Collider2DComponent b, HashSet<long> keys,
        List<(Collider2DComponent A, Collider2DComponent B)> pairs)
    {
        if (ReferenceEquals(a.GameObject, b.GameObject)) return;
        var key = PairKey(a, b);
        if (keys.Add(key)) pairs.Add(a.InstanceId < b.InstanceId ? (a, b) : (b, a));
    }

    private static long PairKey(Collider2DComponent a, Collider2DComponent b)
    {
        var first = (uint)Math.Min(a.InstanceId, b.InstanceId);
        var second = (uint)Math.Max(a.InstanceId, b.InstanceId);
        return ((long)first << 32) | second;
    }

    private static int ToCell(float value)
        => (int)Math.Clamp(MathF.Floor(value / CellSize), int.MinValue + 1d, int.MaxValue - 1d);

    private static bool TryOverlap(PhysicsShape a, PhysicsShape b, out Vector2 normal, out float penetration)
    {
        if (a.IsCircle && b.IsCircle) return CircleCircle(a, b, out normal, out penetration);
        if (!a.IsCircle && !b.IsCircle) return BoxBox(a, b, out normal, out penetration);
        if (!a.IsCircle) return BoxCircle(a, b, out normal, out penetration);
        var overlaps = BoxCircle(b, a, out normal, out penetration);
        normal = -normal;
        return overlaps;
    }

    private static bool CircleCircle(PhysicsShape a, PhysicsShape b, out Vector2 normal, out float penetration)
    {
        var delta = b.Center - a.Center;
        var distanceSquared = delta.LengthSquared();
        var radius = a.Radius + b.Radius;
        if (distanceSquared >= radius * radius)
        {
            normal = Vector2.Zero; penetration = 0f; return false;
        }
        var distance = MathF.Sqrt(Math.Max(distanceSquared, 0f));
        normal = distance > 1e-6f ? delta / distance : Vector2.UnitX;
        penetration = radius - distance;
        return true;
    }

    private static bool BoxBox(PhysicsShape a, PhysicsShape b, out Vector2 normal, out float penetration)
    {
        var delta = b.Center - a.Center;
        var axes = new[] { a.AxisX, a.AxisY, b.AxisX, b.AxisY };
        var minimumOverlap = float.PositiveInfinity;
        var bestAxis = Vector2.UnitX;
        foreach (var axis in axes)
        {
            var radiusA = a.HalfSize.X * MathF.Abs(Vector2.Dot(axis, a.AxisX))
                + a.HalfSize.Y * MathF.Abs(Vector2.Dot(axis, a.AxisY));
            var radiusB = b.HalfSize.X * MathF.Abs(Vector2.Dot(axis, b.AxisX))
                + b.HalfSize.Y * MathF.Abs(Vector2.Dot(axis, b.AxisY));
            var projectedDistance = Vector2.Dot(delta, axis);
            var overlap = radiusA + radiusB - MathF.Abs(projectedDistance);
            if (overlap <= 0f)
            {
                normal = Vector2.Zero;
                penetration = 0f;
                return false;
            }
            if (overlap < minimumOverlap)
            {
                minimumOverlap = overlap;
                bestAxis = projectedDistance < 0f ? -axis : axis;
            }
        }
        normal = bestAxis;
        penetration = minimumOverlap;
        return true;
    }

    private static bool BoxCircle(PhysicsShape box, PhysicsShape circle, out Vector2 normal, out float penetration)
    {
        var relative = circle.Center - box.Center;
        var localCenter = new Vector2(Vector2.Dot(relative, box.AxisX), Vector2.Dot(relative, box.AxisY));
        var closest = Vector2.Clamp(localCenter, -box.HalfSize, box.HalfSize);
        var localDelta = localCenter - closest;
        var distanceSquared = localDelta.LengthSquared();
        if (distanceSquared >= circle.Radius * circle.Radius)
        {
            normal = Vector2.Zero;
            penetration = 0f;
            return false;
        }

        Vector2 localNormal;
        if (distanceSquared > 1e-10f)
        {
            var distance = MathF.Sqrt(distanceSquared);
            localNormal = localDelta / distance;
            penetration = circle.Radius - distance;
        }
        else
        {
            var toLeft = localCenter.X + box.HalfSize.X;
            var toRight = box.HalfSize.X - localCenter.X;
            var toTop = localCenter.Y + box.HalfSize.Y;
            var toBottom = box.HalfSize.Y - localCenter.Y;
            var smallest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));
            if (smallest == toLeft) localNormal = -Vector2.UnitX;
            else if (smallest == toRight) localNormal = Vector2.UnitX;
            else if (smallest == toTop) localNormal = -Vector2.UnitY;
            else localNormal = Vector2.UnitY;
            penetration = circle.Radius + Math.Max(0f, smallest);
        }
        normal = localNormal.X * box.AxisX + localNormal.Y * box.AxisY;
        return true;
    }

    private static void Resolve(Collision2D collision, Rigidbody2DComponent? bodyA, Rigidbody2DComponent? bodyB)
    {
        var inverseA = bodyA?.InverseMass ?? 0f;
        var inverseB = bodyB?.InverseMass ?? 0f;
        var inverseTotal = inverseA + inverseB;
        if (inverseTotal <= 0f) return;

        var correctionMagnitude = Math.Max(collision.Penetration - PenetrationSlop, 0f)
            / inverseTotal * PositionCorrectionPercent;
        var correction = collision.Normal * correctionMagnitude;
        if (bodyA is not null && inverseA > 0f)
            bodyA.ApplyPositionDelta(-correction * inverseA);
        if (bodyB is not null && inverseB > 0f)
            bodyB.ApplyPositionDelta(correction * inverseB);

        var velocityA = bodyA?.Velocity ?? Vector2.Zero;
        var velocityB = bodyB?.Velocity ?? Vector2.Zero;
        var relative = velocityB - velocityA;
        var alongNormal = Vector2.Dot(relative, collision.Normal);
        if (alongNormal > 0f) return;

        var restitutionA = float.IsFinite(collision.ColliderA.Restitution) ? collision.ColliderA.Restitution : 0f;
        var restitutionB = float.IsFinite(collision.ColliderB.Restitution) ? collision.ColliderB.Restitution : 0f;
        var restitution = Math.Clamp(Math.Max(restitutionA, restitutionB), 0f, 1f);
        var impulseMagnitude = -(1f + restitution) * alongNormal / inverseTotal;
        var impulse = collision.Normal * impulseMagnitude;
        if (bodyA is not null && inverseA > 0f) bodyA.ApplyVelocityDelta(-impulse * inverseA);
        if (bodyB is not null && inverseB > 0f) bodyB.ApplyVelocityDelta(impulse * inverseB);

        velocityA = bodyA?.Velocity ?? Vector2.Zero;
        velocityB = bodyB?.Velocity ?? Vector2.Zero;
        relative = velocityB - velocityA;
        var tangent = relative - Vector2.Dot(relative, collision.Normal) * collision.Normal;
        if (tangent.LengthSquared() <= 1e-10f) return;
        tangent.Normalize();
        var frictionImpulse = -Vector2.Dot(relative, tangent) / inverseTotal;
        var frictionA = float.IsFinite(collision.ColliderA.Friction) ? Math.Clamp(collision.ColliderA.Friction, 0f, 10f) : 0f;
        var frictionB = float.IsFinite(collision.ColliderB.Friction) ? Math.Clamp(collision.ColliderB.Friction, 0f, 10f) : 0f;
        var friction = MathF.Sqrt(frictionA * frictionB);
        frictionImpulse = Math.Clamp(frictionImpulse, -impulseMagnitude * friction, impulseMagnitude * friction);
        var frictionVector = tangent * frictionImpulse;
        if (bodyA is not null && inverseA > 0f) bodyA.ApplyVelocityDelta(-frictionVector * inverseA);
        if (bodyB is not null && inverseB > 0f) bodyB.ApplyVelocityDelta(frictionVector * inverseB);
    }

    private enum ContactPhase { Enter, Stay, Exit }

    private static void Dispatch(Contact contact, ContactPhase phase)
    {
        var trigger = contact.A.IsTrigger || contact.B.IsTrigger;
        if (trigger)
        {
            DispatchTrigger(contact.A.GameObject, contact.B, phase);
            DispatchTrigger(contact.B.GameObject, contact.A, phase);
            return;
        }
        DispatchCollision(contact.A.GameObject, contact.Collision, phase);
        DispatchCollision(contact.B.GameObject, contact.Collision.Reversed(), phase);
    }

    private static void DispatchTrigger(GameObject owner, Collider2DComponent other, ContactPhase phase)
    {
        foreach (var component in owner.Components.ToArray())
        {
            if (!ReferenceEquals(component.GameObject, owner) || !component.Enabled) continue;
            try
            {
                switch (phase)
                {
                    case ContactPhase.Enter: component.OnTriggerEnter2D(other); break;
                    case ContactPhase.Stay: component.OnTriggerStay2D(other); break;
                    case ContactPhase.Exit: component.OnTriggerExit2D(other); break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PixelEngine Physics] Trigger callback on '{owner.Name}' failed: {ex}");
            }
        }
    }

    private static void DispatchCollision(GameObject owner, Collision2D collision, ContactPhase phase)
    {
        foreach (var component in owner.Components.ToArray())
        {
            if (!ReferenceEquals(component.GameObject, owner) || !component.Enabled) continue;
            try
            {
                switch (phase)
                {
                    case ContactPhase.Enter: component.OnCollisionEnter2D(collision); break;
                    case ContactPhase.Stay: component.OnCollisionStay2D(collision); break;
                    case ContactPhase.Exit: component.OnCollisionExit2D(collision); break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PixelEngine Physics] Collision callback on '{owner.Name}' failed: {ex}");
            }
        }
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
