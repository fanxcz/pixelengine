using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelEngine.Graphics;

public readonly record struct LightDrawCommand(Vector2 Position, float Radius, Color Color, float Intensity, float Falloff = 2f);
public readonly record struct ShadowSegment(Vector2 Start, Vector2 End);

/// <summary>
/// Low-resolution CPU light-map renderer with ambient darkness and 2D segment occlusion.
/// It uses the existing MonoGame SpriteBatch pipeline and therefore does not require a
/// platform-specific shader. The map is quarter-resolution and capped at 384x384, 16 lights and
/// 64 shadow edges per light for predictable CPU cost.
/// </summary>
public sealed class LightingRenderer : IDisposable
{
    private const int MaximumLights = 16;
    private const int MaximumSegmentsPerLight = 64;
    private readonly GraphicsDevice _device;
    private readonly Texture2D _whitePixel;
    private Texture2D? _lightMap;
    private Color[] _pixels = Array.Empty<Color>();
    private int _mapWidth;
    private int _mapHeight;
    private bool _disposed;

    public LightingRenderer(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _whitePixel = new Texture2D(_device, 1, 1, false, SurfaceFormat.Color);
        _whitePixel.SetData(new[] { Color.White });
    }

    /// <summary>Builds a radial grayscale gradient for previews and tests.</summary>
    public static Color[] BuildRadialGradientPixels(int size)
    {
        if (size is < 2 or > 1024) throw new ArgumentOutOfRangeException(nameof(size));
        var pixels = new Color[size * size];
        var center = (size - 1) * .5f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x - center) / center;
            var dy = (y - center) / center;
            var normalized = Math.Clamp(1f - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
            var value = (byte)Math.Clamp((int)MathF.Round(normalized * normalized * 255f), 0, 255);
            pixels[y * size + x] = new Color(value, value, value, value);
        }
        return pixels;
    }

    public static Color ComputeLightTint(Color color, float intensity)
    {
        var strength = float.IsFinite(intensity) ? Math.Clamp(intensity, 0f, 1f) * (color.A / 255f) : 0f;
        return new Color((byte)Math.Clamp((int)MathF.Round(color.R * strength), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(color.G * strength), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(color.B * strength), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(255f * strength), 0, 255));
    }

    /// <summary>
    /// Creates a screen-space additive light map. Occluders are tested in world space against
    /// each light-to-pixel ray. Output RGB stores additive light energy; alpha stores coverage.
    /// </summary>
    public static Color[] BuildLightMapPixels(int width, int height, Viewport viewport, Camera2D camera,
        IReadOnlyList<LightDrawCommand> lights, IReadOnlyList<ShadowSegment> occluders)
    {
        if (width is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > 2048) throw new ArgumentOutOfRangeException(nameof(height));
        var pixels = new Color[checked(width * height)];
        BuildLightMapPixels(pixels, width, height, viewport, camera, lights, occluders);
        return pixels;
    }

    private static void BuildLightMapPixels(Color[] pixels, int width, int height, Viewport viewport,
        Camera2D camera, IReadOnlyList<LightDrawCommand> lights, IReadOnlyList<ShadowSegment> occluders)
    {
        Array.Clear(pixels, 0, pixels.Length);
        if (viewport.Width <= 0 || viewport.Height <= 0 || lights.Count == 0) return;
        var view = camera.GetViewMatrix(viewport);
        var inverse = Matrix.Identity;
        Matrix.Invert(ref view, out inverse);
        var scaleX = viewport.Width / (float)width;
        var scaleY = viewport.Height / (float)height;
        var lightCount = Math.Min(lights.Count, MaximumLights);
        var segmentCount = Math.Min(occluders.Count, 4096);

        for (var lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            var light = lights[lightIndex];
            if (!IsFinite(light.Position) || !float.IsFinite(light.Radius) || light.Radius <= 0f ||
                !float.IsFinite(light.Intensity) || light.Intensity <= 0f) continue;
            var intensity = Math.Clamp(light.Intensity, 0f, 1f);
            var falloff = float.IsFinite(light.Falloff) ? Math.Clamp(light.Falloff, .25f, 8f) : 2f;
            var radiusWorld = Math.Clamp(light.Radius, .1f, 100000f);
            var lightScreen = Vector2.Transform(light.Position, view);
            if (!IsFinite(lightScreen)) continue;
            var radiusX = radiusWorld * camera.SafeZoom / Math.Max(.0001f, scaleX);
            var radiusY = radiusWorld * camera.SafeZoom / Math.Max(.0001f, scaleY);
            // Clamp in double precision before converting to int so remote lights cannot overflow pixel bounds.
            var minX = (int)Math.Clamp(Math.Floor((double)lightScreen.X / scaleX - radiusX), 0d, width - 1d);
            var maxX = (int)Math.Clamp(Math.Ceiling((double)lightScreen.X / scaleX + radiusX), 0d, width - 1d);
            var minY = (int)Math.Clamp(Math.Floor((double)lightScreen.Y / scaleY - radiusY), 0d, height - 1d);
            var maxY = (int)Math.Clamp(Math.Ceiling((double)lightScreen.Y / scaleY + radiusY), 0d, height - 1d);
            if (maxX < minX || maxY < minY) continue;

            var nearbySegments = new List<(float DistanceSquared, ShadowSegment Segment)>(segmentCount);
            var radiusSquared = radiusWorld * radiusWorld;
            for (var i = 0; i < segmentCount; i++)
            {
                var segment = occluders[i];
                if (!IsFinite(segment.Start) || !IsFinite(segment.End)) continue;
                var distanceSquared = DistanceSquaredToSegment(light.Position, segment.Start, segment.End);
                if (distanceSquared <= radiusSquared * 1.25f)
                    nearbySegments.Add((distanceSquared, segment));
            }
            // Prefer the nearest blockers, not whichever collider happened to be visited first.
            nearbySegments.Sort((a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
            var candidates = new ShadowSegment[Math.Min(nearbySegments.Count, MaximumSegmentsPerLight)];
            for (var i = 0; i < candidates.Length; i++) candidates[i] = nearbySegments[i].Segment;

            var red = light.Color.R / 255f;
            var green = light.Color.G / 255f;
            var blue = light.Color.B / 255f;
            var alpha = light.Color.A / 255f;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var screen = new Vector2((x + .5f) * scaleX, (y + .5f) * scaleY);
                var world = Vector2.Transform(screen, inverse);
                var delta = world - light.Position;
                var distanceSquared = delta.LengthSquared();
                if (distanceSquared >= radiusSquared) continue;
                if (candidates.Length > 0 && IsOccluded(light.Position, world, candidates)) continue;
                var normalized = 1f - MathF.Sqrt(distanceSquared / radiusSquared);
                var strength = MathF.Pow(Math.Clamp(normalized, 0f, 1f), falloff) * intensity * alpha;
                if (strength <= .0001f) continue;
                var index = y * width + x;
                var old = pixels[index];
                var r = Math.Clamp(old.R / 255f + red * strength, 0f, 1f);
                var g = Math.Clamp(old.G / 255f + green * strength, 0f, 1f);
                var b = Math.Clamp(old.B / 255f + blue * strength, 0f, 1f);
                var a = Math.Clamp(Math.Max(old.A / 255f, strength * Math.Max(red, Math.Max(green, blue))), 0f, 1f);
                pixels[index] = new Color(r, g, b, a);
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch, Camera2D camera, Viewport viewport,
        IReadOnlyList<LightDrawCommand> lights, IReadOnlyList<ShadowSegment> occluders, float ambientLight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        var ambient = float.IsFinite(ambientLight) ? Math.Clamp(ambientLight, 0f, 1f) : 1f;
        var darkness = 1f - ambient;
        if (darkness > .001f)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            try
            {
                var alpha = (byte)Math.Clamp((int)MathF.Round(darkness * 255f), 0, 255);
                spriteBatch.Draw(_whitePixel, new Rectangle(0, 0, viewport.Width, viewport.Height), new Color((byte)0, (byte)0, (byte)0, alpha));
            }
            finally { spriteBatch.End(); }
        }

        if (lights.Count == 0) return;
        EnsureLightMapSize(Math.Clamp((viewport.Width + 3) / 4, 80, 384),
            Math.Clamp((viewport.Height + 3) / 4, 45, 384));
        BuildLightMapPixels(_pixels, _mapWidth, _mapHeight, viewport, camera, lights, occluders);
        _lightMap!.SetData(_pixels);
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        try { spriteBatch.Draw(_lightMap, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.White); }
        finally { spriteBatch.End(); }
    }

    private void EnsureLightMapSize(int width, int height)
    {
        if (_lightMap is not null && _mapWidth == width && _mapHeight == height) return;
        _lightMap?.Dispose();
        _mapWidth = width;
        _mapHeight = height;
        _lightMap = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
        _pixels = new Color[checked(width * height)];
    }

    private static bool IsOccluded(Vector2 start, Vector2 end, IReadOnlyList<ShadowSegment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
            if (SegmentsIntersect(start, end, segments[i].Start, segments[i].End)) return true;
        return false;
    }

    private static bool SegmentsIntersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
    {
        var r = p2 - p;
        var s = q2 - q;
        var denominator = Cross(r, s);
        if (MathF.Abs(denominator) < 1e-5f) return false;
        var qp = q - p;
        var t = Cross(qp, s) / denominator;
        var u = Cross(qp, r) / denominator;
        return t > .001f && t < .999f && u >= 0f && u <= 1f;
    }

    private static float DistanceSquaredToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var lengthSquared = delta.LengthSquared();
        if (lengthSquared <= 1e-6f) return Vector2.DistanceSquared(point, start);
        var t = Math.Clamp(Vector2.Dot(point - start, delta) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(point, start + delta * t);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lightMap?.Dispose();
        _whitePixel.Dispose();
    }
}
