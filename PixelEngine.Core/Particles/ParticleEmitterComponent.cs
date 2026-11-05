using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelEngine.Graphics;
using PixelEngine.Rendering;
using PixelEngine.Scene;

namespace PixelEngine.Particles;

/// <summary>Small CPU particle emitter for 2D effects. Uses a capped pool to avoid per-frame allocations.</summary>
public sealed class ParticleEmitterComponent : Component
{
    private sealed class Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Lifetime;
        public float Size;
    }

    private readonly List<Particle> _particles = new();
    private readonly Stack<Particle> _pool = new();
    private readonly Random _random;
    private float _spawnAccumulator;

    public float Rate { get; set; } = 12f;
    public float Lifetime { get; set; } = 1.2f;
    public float Speed { get; set; } = 36f;
    public float SpreadDegrees { get; set; } = 90f;
    public float ParticleSize { get; set; } = 3f;
    public float Gravity { get; set; }
    public int MaxParticles { get; set; } = 128;
    public Color ParticleColor { get; set; } = new(255, 209, 102);
    public int ActiveParticles => _particles.Count;
    /// <summary>Total particles allocated by this emitter, including reusable inactive particles.</summary>
    public int AllocatedParticleCount => _particles.Count + _pool.Count;

    public ParticleEmitterComponent(int? seed = null)
        => _random = new Random(seed ?? Random.Shared.Next());

    /// <summary>Returns a premultiplied tint suitable for MonoGame's default AlphaBlend state.</summary>
    public static Color ComputeParticleTint(Color color, float normalizedAge)
    {
        var lifeFade = float.IsFinite(normalizedAge) ? 1f - Math.Clamp(normalizedAge, 0f, 1f) : 0f;
        var alpha = color.A / 255f * lifeFade;
        return new Color(
            (byte)Math.Clamp((int)MathF.Round(color.R * alpha), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(color.G * alpha), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(color.B * alpha), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(255f * alpha), 0, 255));
    }

    public override void Update(PixelEngine.Core.EngineTime time)
    {
        var dt = float.IsFinite(time.DeltaTime) ? Math.Clamp(time.DeltaTime, 0f, .1f) : 0f;
        if (dt <= 0f) return;

        var gravity = float.IsFinite(Gravity) ? Math.Clamp(Gravity, -10000f, 10000f) : 0f;
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.Age += dt;
            particle.Velocity += new Vector2(0, gravity * dt);
            particle.Position += particle.Velocity * dt;
            if (particle.Age >= particle.Lifetime)
            {
                _particles.RemoveAt(i);
                _pool.Push(particle);
            }
        }

        var cap = Math.Clamp(MaxParticles, 1, 10000);
        while (_particles.Count > cap)
        {
            var last = _particles[^1];
            _particles.RemoveAt(_particles.Count - 1);
            _pool.Push(last);
        }
        while (_particles.Count + _pool.Count > cap) _pool.Pop();
        if (!Enabled || !float.IsFinite(Rate) || Rate <= 0f || !float.IsFinite(Lifetime) || Lifetime <= 0f || _particles.Count >= cap)
        {
            // Do not accumulate a huge backlog while the pool is full or the emitter is disabled.
            _spawnAccumulator = 0f;
            return;
        }

        _particles.EnsureCapacity(cap);
        _spawnAccumulator = Math.Min(10000f, _spawnAccumulator + dt * Math.Clamp(Rate, 0f, 10000f));
        var requested = (int)Math.Min(Math.Floor(_spawnAccumulator), cap - _particles.Count);
        for (var i = 0; i < requested; i++) Spawn();
        _spawnAccumulator -= requested;
        if (_particles.Count >= cap) _spawnAccumulator = 0f;
    }

    private void Spawn()
    {
        var spread = float.IsFinite(SpreadDegrees) ? Math.Clamp(SpreadDegrees, 0f, 360f) : 90f;
        var baseSpeed = float.IsFinite(Speed) ? Math.Clamp(Speed, 0f, 10000f) : 0f;
        var direction = ((float)_random.NextDouble() - .5f) * spread - 90f;
        var radians = direction * MathF.PI / 180f;
        var speed = baseSpeed * (.65f + (float)_random.NextDouble() * .7f);
        var particle = _pool.Count > 0 ? _pool.Pop() : new Particle();
        particle.Position = new Vector2(float.IsFinite(Transform.Position.X) ? Transform.Position.X : 0f,
            float.IsFinite(Transform.Position.Y) ? Transform.Position.Y : 0f);
        particle.Velocity = new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * speed;
        particle.Age = 0f;
        particle.Lifetime = Math.Max(.05f, Math.Clamp(Lifetime, .05f, 120f) * (.75f + (float)_random.NextDouble() * .5f));
        particle.Size = Math.Max(.5f, (float.IsFinite(ParticleSize) ? Math.Clamp(ParticleSize, .5f, 256f) : 3f) * (.7f + (float)_random.NextDouble() * .6f));
        _particles.Add(particle);
    }

    public override void Draw(RenderContext context)
    {
        foreach (var particle in _particles)
        {
            var normalizedAge = particle.Lifetime > 0f ? particle.Age / particle.Lifetime : 1f;
            var tint = ComputeParticleTint(ParticleColor, normalizedAge);
            context.SpriteBatch.Draw(context.WhitePixel, particle.Position, null, tint, 0f,
                new Vector2(.5f), new Vector2(particle.Size), SpriteEffects.None, .1f);
        }
    }
}
