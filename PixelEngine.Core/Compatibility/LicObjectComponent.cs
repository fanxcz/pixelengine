using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelEngine.Assets;
using PixelEngine.Core;
using PixelEngine.Graphics;
using PixelEngine.Rendering;
using PixelEngine.Scene;
using PixelEngine.Serialization;
using PixelEngine.Physics;

namespace PixelEngine.Compatibility;

/// <summary>Render-side adapter for one object from the legacy .lic schema.</summary>
public sealed class LicObjectComponent : Component
{
    private readonly AssetStore _assets;
    private readonly IReadOnlyDictionary<string, LicAssetData> _assetData;
    private Texture2D? _sprite;
    private Texture2D? _circle;
    private bool _assetLoadAttempted;
    private Rectangle? _spriteSourceRectangle;

    public LicObjectData Data { get; }
    private Vector2 _velocity;
    public Vector2 Velocity
    {
        get => GameObject?.GetComponent<Rigidbody2DComponent>()?.Velocity ?? _velocity;
        set
        {
            var safe = float.IsFinite(value.X) && float.IsFinite(value.Y)
                ? new Vector2(Math.Clamp(value.X, -1_000_000f, 1_000_000f), Math.Clamp(value.Y, -1_000_000f, 1_000_000f))
                : Vector2.Zero;
            _velocity = safe;
            var body = GameObject?.GetComponent<Rigidbody2DComponent>();
            if (body is not null) body.Velocity = safe;
        }
    }
    public bool OnGround { get; internal set; }
    public bool IsDestroyed { get; internal set; }
    public float Number { get; set; }
    public float MaxNumber { get; set; }
    public float AttackTime { get; set; }
    public float Face { get; set; } = 1f;
    public float HitTime { get; private set; }
    public void Hit() => HitTime = 0.18f;

    public override void OnCollisionEnter2D(Collision2D collision) => UpdateGroundContact(collision);
    public override void OnCollisionStay2D(Collision2D collision) => UpdateGroundContact(collision);

    private void UpdateGroundContact(Collision2D collision)
    {
        if (collision.Normal.Y > 0.5f) OnGround = true;
    }
    // Transform scale is shared by rendering, compatibility collision bounds and animations.
    public float Width
    {
        get
        {
            var scaled = Data.Width * Math.Abs(Transform.Scale.X);
            return float.IsFinite(scaled) ? Math.Clamp(scaled, 0.01f, 100000f) : Math.Clamp(Data.Width, 0.01f, 100000f);
        }
    }
    public float Height
    {
        get
        {
            var scaled = Data.Height * Math.Abs(Transform.Scale.Y);
            return float.IsFinite(scaled) ? Math.Clamp(scaled, 0.01f, 100000f) : Math.Clamp(Data.Height, 0.01f, 100000f);
        }
    }
    public LicComponentData? FindAuthoringComponent(string type) => Data.Components?.FirstOrDefault(x =>
        string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));

    internal LicObjectComponent(LicObjectData data, AssetStore assets, IReadOnlyDictionary<string, LicAssetData> assetData)
    {
        Data = data;
        _assets = assets;
        _assetData = assetData;
    }

    public override void OnAdded()
    {
        Transform.Position = new Vector2(Data.X, Data.Y);
        Transform.Rotation = Data.Rotation * MathF.PI / 180f;
    }

    public override void Update(EngineTime time)
    {
        HitTime = Math.Max(0f, HitTime - time.DeltaTime);
        AttackTime = Math.Max(0f, AttackTime - time.DeltaTime);
        if (Math.Abs(Velocity.X) > 0.01f) Face = Math.Sign(Velocity.X);
    }

    public override void Draw(RenderContext context)
    {
        if (IsDestroyed) return;
        var spriteComponent = FindAuthoringComponent("SpriteRenderer");
        if (spriteComponent is { Enabled: false }) return;
        var batch = context.SpriteBatch;
        var position = Transform.Position;
        var depth = SortingLayerRegistry.GetDepth(spriteComponent?.SortingLayer, spriteComponent?.SortingOrder ?? 0);
        var rotation = float.IsFinite(Transform.Rotation)
            ? MathF.IEEERemainder(Transform.Rotation * (180f / MathF.PI), 360f)
            : 0f;
        var tint = ParseColor(spriteComponent is not null && !string.IsNullOrWhiteSpace(spriteComponent.Tint)
            ? spriteComponent.Tint : Data.Color);
        if (HitTime > 0f) tint = Color.Lerp(tint, Color.White, Math.Clamp(HitTime / 0.18f, 0f, 1f));

        switch (Data.Kind)
        {
            case "text":
                BitmapText.Draw(batch, _assets.WhitePixel, Data.Text, position, tint,
                    Math.Clamp(3f / Math.Max(.1f, context.Camera.Zoom), .1f, 2f), rotation, depth);
                break;
            case "poly":
                DrawPolygon(batch, _assets.WhitePixel, tint, depth);
                break;
            case "circle":
                _circle ??= _assets.GetCircleTexture();
                DrawTexture(batch, _circle, position, tint, Width, Height, rotation, depth);
                break;
            case "sprite":
                var spriteName = spriteComponent is not null && !string.IsNullOrWhiteSpace(spriteComponent.Texture)
                    ? spriteComponent.Texture : Data.Sprite;
                TryLoadSprite(spriteName);
                if (_sprite is not null) DrawTexture(batch, _sprite, position, tint, Width, Height, rotation, depth, _spriteSourceRectangle);
                else DrawRectangle(batch, _assets.WhitePixel, position, tint, Width, Height, rotation, depth);
                break;
            case "button":
                DrawRectangle(batch, _assets.WhitePixel, position, new Color(10, 12, 18, 220), Width + 2, Height + 2, rotation, depth);
                DrawRectangle(batch, _assets.WhitePixel, position, tint, Width, Height, rotation, depth);
                BitmapText.Draw(batch, _assets.WhitePixel, Data.Text, position, Color.White,
                    Math.Clamp(3f / Math.Max(.1f, context.Camera.Zoom), .1f, 2f), rotation, depth);
                break;
            default:
                // Legacy special entities (knight/castle/tower/etc.) are represented by
                // their authored bounds until their dedicated renderers are migrated.
                DrawRectangle(batch, _assets.WhitePixel, position, tint, Width, Height, rotation, depth);
                break;
        }
    }

    private void TryLoadSprite(string? assetName)
    {
        if (_assetLoadAttempted) return;
        _assetLoadAttempted = true;
        if (string.IsNullOrWhiteSpace(assetName)) return;
        if (!_assetData.TryGetValue(assetName, out var asset) || string.IsNullOrWhiteSpace(asset.Base64)) return;
        if (!string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase)) return;
        try { _sprite = _assets.LoadTextureFromBase64(asset.Name, asset.Base64); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LIC sprite '{Data.Sprite}' failed to load: {ex.Message}"); }
    }

    public int ResolveSpriteSheetColumns(int frameWidth)
    {
        if (frameWidth <= 0) return 1;
        var spriteComponent = FindAuthoringComponent("SpriteRenderer");
        var spriteName = spriteComponent is not null && !string.IsNullOrWhiteSpace(spriteComponent.Texture)
            ? spriteComponent.Texture : Data.Sprite;
        TryLoadSprite(spriteName);
        return _sprite is not null ? Math.Max(1, _sprite.Width / frameWidth) : 1;
    }

    public void SetSpriteSheetFrame(int frameWidth, int frameHeight, int columns, int frameIndex)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || columns <= 0 || frameIndex < 0)
        {
            _spriteSourceRectangle = null;
            return;
        }
        var x = (frameIndex % columns) * frameWidth;
        var y = (frameIndex / columns) * frameHeight;
        if (x < 0 || y < 0 || (long)x + frameWidth > 1_000_000 || (long)y + frameHeight > 1_000_000)
        {
            _spriteSourceRectangle = null;
            return;
        }
        _spriteSourceRectangle = new Rectangle(x, y, frameWidth, frameHeight);
    }

    private static void DrawTexture(SpriteBatch batch, Texture2D texture, Vector2 position, Color tint,
        float width, float height, float rotationDegrees = 0f, float depth = 0f, Rectangle? sourceRectangle = null)
    {
        var source = sourceRectangle.HasValue
            ? Rectangle.Intersect(sourceRectangle.Value, new Rectangle(0, 0, texture.Width, texture.Height))
            : new Rectangle(0, 0, texture.Width, texture.Height);
        // An invalid sheet frame must not silently draw the entire sheet as one sprite.
        if (source.Width <= 0 || source.Height <= 0) return;
        var sourceSize = new Vector2(source.Width, source.Height);
        var origin = sourceSize * .5f;
        var scale = new Vector2(width / Math.Max(1, source.Width), height / Math.Max(1, source.Height));
        batch.Draw(texture, position, source, tint, rotationDegrees * MathF.PI / 180f, origin, scale, SpriteEffects.None, depth);
    }

    private static void DrawRectangle(SpriteBatch batch, Texture2D pixel, Vector2 position, Color tint, float width, float height, float rotationDegrees = 0f, float depth = 0f)
    {
        batch.Draw(pixel, position, null, tint, rotationDegrees * MathF.PI / 180f, new Vector2(.5f, .5f),
            new Vector2(Math.Max(1f, width), Math.Max(1f, height)), SpriteEffects.None, depth);
    }

    private void DrawPolygon(SpriteBatch batch, Texture2D pixel, Color tint, float depth)
    {
        var points = Data.Points;
        if (points.Count < 4) return;
        var position = Transform.Position;
        var scale = Transform.Scale;
        var radians = float.IsFinite(Transform.Rotation) ? Transform.Rotation : 0f;
        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y)) return;

        var ring = new List<Vector2>();
        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            var x = points[i];
            var y = points[i + 1];
            if (!float.IsFinite(x) || !float.IsFinite(y))
            {
                DrawRing(batch, pixel, ring, tint, depth);
                ring.Clear();
                continue;
            }
            // LIC polygon vertices are authored in world coordinates. Convert them to
            // object-local coordinates before applying the live Transform.
            var local = new Vector2((x - Data.X) * scale.X, (y - Data.Y) * scale.Y);
            var world = position + new Vector2(
                local.X * cos - local.Y * sin,
                local.X * sin + local.Y * cos);
            if (!float.IsFinite(world.X) || !float.IsFinite(world.Y)
                || Math.Abs(world.X) > 10_000_000f || Math.Abs(world.Y) > 10_000_000f)
            {
                DrawRing(batch, pixel, ring, tint, depth);
                ring.Clear();
                continue;
            }
            ring.Add(world);
        }
        DrawRing(batch, pixel, ring, tint, depth);
    }

    private static void DrawRing(SpriteBatch batch, Texture2D pixel, List<Vector2> points, Color tint, float depth)
    {
        if (points.Count < 2) return;
        for (var i = 0; i < points.Count; i++)
            DrawLine(batch, pixel, points[i], points[(i + 1) % points.Count], tint, depth);
    }

    private static void DrawLine(SpriteBatch batch, Texture2D pixel, Vector2 start, Vector2 end, Color tint, float depth)
    {
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y)
            || !float.IsFinite(end.X) || !float.IsFinite(end.Y)) return;
        var delta = end - start;
        var length = delta.Length();
        if (!float.IsFinite(length) || length < .001f || length > 20_000_000f) return;
        batch.Draw(pixel, (start + end) * .5f, null, tint, MathF.Atan2(delta.Y, delta.X),
            new Vector2(.5f, .5f), new Vector2(length, 1.25f), SpriteEffects.None, depth);
    }

    private static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Color.White;
        try
        {
            var text = value.Trim().TrimStart('#');
            if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
                return new Color((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
        }
        catch { /* fall through to white */ }
        return Color.White;
    }
}

/// <summary>Small built-in 5x7 pixel font. Cyrillic is transliterated for the first compatibility slice.</summary>
internal static class BitmapText
{
    private static readonly Dictionary<string, string> NormalizedTextCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<char, string> Transliteration = BuildTransliteration();

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
        ['C'] = new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
        ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
        ['G'] = new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" },
        ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['I'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" },
        ['J'] = new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" },
        ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
        ['N'] = new[] { "10001", "11001", "11001", "10101", "10011", "10011", "10001" },
        ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['Q'] = new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
        ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
        ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
        ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" },
        ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
        ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
        ['Z'] = new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
        ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
        ['3'] = new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
        ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
        ['5'] = new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" },
        ['6'] = new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" },
        ['+'] = new[] { "00000", "00100", "00100", "11111", "00100", "00100", "00000" },
        ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
        ['/'] = new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
        [':'] = new[] { "00000", "00100", "00100", "00000", "00100", "00100", "00000" },
        ['.'] = new[] { "00000", "00000", "00000", "00000", "00000", "00100", "00100" },
        [','] = new[] { "00000", "00000", "00000", "00000", "00100", "00100", "01000" },
        ['?'] = new[] { "01110", "10001", "00001", "00010", "00100", "00000", "00100" },
        ['!'] = new[] { "00100", "00100", "00100", "00100", "00100", "00000", "00100" },
        ['('] = new[] { "00010", "00100", "01000", "01000", "01000", "00100", "00010" },
        [')'] = new[] { "01000", "00100", "00010", "00010", "00010", "00100", "01000" },
        [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" }
    };

    public static void Draw(SpriteBatch batch, Texture2D pixel, string? text, Vector2 center, Color color, float scale, float rotationDegrees = 0f, float depth = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!NormalizedTextCache.TryGetValue(text, out var normalized))
        {
            normalized = Transliterate(text).ToUpperInvariant();
            if (NormalizedTextCache.Count > 4096) NormalizedTextCache.Clear();
            NormalizedTextCache[text] = normalized;
        }
        const int glyphWidth = 5;
        const int advance = 6;
        var startX = center.X - Math.Max(0, normalized.Length * advance - 1) * scale * .5f;
        var startY = center.Y - 3.5f * scale;
        var radians = rotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        var origin = new Vector2(.5f, .5f);
        var pixelScale = new Vector2(scale, scale);
        foreach (var raw in normalized)
        {
            var ch = Glyphs.TryGetValue(raw, out var glyph) ? glyph : Glyphs['?'];
            for (var row = 0; row < ch.Length; row++)
            for (var col = 0; col < glyphWidth; col++)
            {
                if (ch[row][col] != '1') continue;
                var pixelCenter = new Vector2(startX + (col + .5f) * scale, startY + (row + .5f) * scale);
                var offset = pixelCenter - center;
                var drawCenter = center + new Vector2(
                    offset.X * cos - offset.Y * sin,
                    offset.X * sin + offset.Y * cos);
                batch.Draw(pixel, drawCenter, null, color, radians, origin,
                    pixelScale, SpriteEffects.None, depth);
            }
            startX += advance * scale;
        }
    }

    private static Dictionary<char, string> BuildTransliteration()
    {
        const string from = "абвгдеёзийклмнопрстуфхы";
        string[] to = { "a", "b", "v", "g", "d", "e", "e", "z", "i", "i", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "h", "y" };
        var map = new Dictionary<char, string>();
        for (var i = 0; i < from.Length; i++) map[from[i]] = to[i];
        map['ж'] = "zh"; map['ц'] = "ts"; map['ч'] = "ch"; map['ш'] = "sh"; map['щ'] = "sh";
        map['ъ'] = ""; map['ь'] = ""; map['э'] = "e"; map['ю'] = "yu"; map['я'] = "ya";
        return map;
    }

    private static string Transliterate(string text)
    {
        var result = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Transliteration.TryGetValue(char.ToLowerInvariant(c), out var latin))
                result.Append(char.IsUpper(c) ? latin.ToUpperInvariant() : latin);
            else if (c == '—' || c == '–') result.Append('-');
            else result.Append(c);
        }
        return result.ToString();
    }
}
