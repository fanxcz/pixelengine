using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelEngine.Serialization;

/// <summary>PixelEngine authoring metadata. Kept outside legacy LIC v1 fields for compatibility.</summary>
public sealed class LicEditorData
{
    [Category("Lighting"), Description("Ambient light multiplier. 1 keeps the scene fully lit; lower values enable darkness around point lights.")]
    [JsonPropertyName("ambientLight")] public float AmbientLight { get; set; } = 1f;
    [JsonPropertyName("animations")] public List<LicAnimationClipData> Animations { get; set; } = new();
    [JsonPropertyName("lights")] public List<LicLightData> Lights { get; set; } = new();
    [JsonPropertyName("particleEmitters")] public List<LicParticleEmitterData> ParticleEmitters { get; set; } = new();
    [JsonPropertyName("tileMaps")] public List<LicTileMapData> TileMaps { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicAnimationClipData
{
    [Category("Clip"), Description("Name referenced by Animator components.")][JsonPropertyName("name")] public string Name { get; set; } = "Animation";
    [Category("Clip"), Description("Scene name; leave empty to make the clip project-wide.")][JsonPropertyName("scene")] public string Scene { get; set; } = "";
    [Category("Clip"), Description("Target object name; leave empty to allow any object in scope.")][JsonPropertyName("target")] public string Target { get; set; } = "";
    [Category("Timing"), Description("Clip length in seconds.")][JsonPropertyName("duration")] public float Duration { get; set; } = 1f;
    [Category("Timing")][JsonPropertyName("loop")] public bool Loop { get; set; } = true;
    [Browsable(false), JsonPropertyName("keyframes")] public List<LicAnimationKeyframeData> Keyframes { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicAnimationKeyframeData
{
    [JsonPropertyName("time")] public float Time { get; set; }
    [JsonPropertyName("x")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? X { get; set; }
    [JsonPropertyName("y")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Y { get; set; }
    [JsonPropertyName("rotation")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? Rotation { get; set; }
    [JsonPropertyName("scaleX")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? ScaleX { get; set; }
    [JsonPropertyName("scaleY")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public float? ScaleY { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicLightData
{
    [Category("General"), JsonPropertyName("name")] public string Name { get; set; } = "Light";
    [Category("General"), Description("Scene name; leave empty to make the light project-wide.")][JsonPropertyName("scene")] public string Scene { get; set; } = "";
    [Category("Transform"), JsonPropertyName("x")] public float X { get; set; }
    [Category("Transform"), JsonPropertyName("y")] public float Y { get; set; }
    [Category("Light"), Description("Glow radius in world units (1–4096).")][JsonPropertyName("radius")] public float Radius { get; set; } = 150f;
    [Category("Light"), Description("Hex color in #RRGGBB or #AARRGGBB format.")][JsonPropertyName("color")] public string Color { get; set; } = "#FFCC88";
    [Category("Light"), Description("Additive light intensity (0–1).")][JsonPropertyName("intensity")] public float Intensity { get; set; } = 0.65f;
    [Category("Light"), Description("Light falloff exponent (0.25–8).")][JsonPropertyName("falloff")] public float Falloff { get; set; } = 2f;
    [Category("Light"), Description("Flicker variation (0–0.9).")][JsonPropertyName("flickerAmount")] public float FlickerAmount { get; set; }
    [Category("Light"), Description("Flicker frequency in cycles per second (0–60).")][JsonPropertyName("flickerSpeed")] public float FlickerSpeed { get; set; } = 8f;
    [Category("General"), JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicParticleEmitterData
{
    [Category("General"), JsonPropertyName("name")] public string Name { get; set; } = "Particles";
    [Category("General"), Description("Scene name; leave empty to make the emitter project-wide.")][JsonPropertyName("scene")] public string Scene { get; set; } = "";
    [Category("Transform"), JsonPropertyName("x")] public float X { get; set; }
    [Category("Transform"), JsonPropertyName("y")] public float Y { get; set; }
    [Category("Emission"), Description("Particles emitted per second (0–10000).")][JsonPropertyName("rate")] public float Rate { get; set; } = 12f;
    [Category("Particle"), Description("Average lifetime in seconds (0.05–120).")][JsonPropertyName("lifetime")] public float Lifetime { get; set; } = 1.2f;
    [Category("Particle"), Description("Initial speed in world units per second.")][JsonPropertyName("speed")] public float Speed { get; set; } = 36f;
    [Category("Emission"), Description("Emission cone in degrees (0–360).")][JsonPropertyName("spread")] public float Spread { get; set; } = 90f;
    [Category("Particle"), Description("Particle size in world units (0.5–256).")][JsonPropertyName("size")] public float Size { get; set; } = 3f;
    [Category("Particle"), Description("Vertical acceleration in world units per second squared.")][JsonPropertyName("gravity")] public float Gravity { get; set; } = 0f;
    [Category("Emission"), Description("Maximum active plus pooled particles (1–10000).")][JsonPropertyName("maxParticles")] public int MaxParticles { get; set; } = 128;
    [Category("Particle"), Description("Hex color in #RRGGBB or #AARRGGBB format.")][JsonPropertyName("color")] public string Color { get; set; } = "#FFD166";
    [Category("General"), JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class LicTileMapData
{
    [Category("General"), JsonPropertyName("name")] public string Name { get; set; } = "TileMap";
    [Category("General"), Description("Scene name; leave empty to make the map project-wide.")][JsonPropertyName("scene")] public string Scene { get; set; } = "";
    [Category("Transform"), JsonPropertyName("x")] public float X { get; set; }
    [Category("Transform"), JsonPropertyName("y")] public float Y { get; set; }
    [Category("Layout"), Description("Tile columns (1–512).")][JsonPropertyName("width")] public int Width { get; set; } = 20;
    [Category("Layout"), Description("Tile rows (1–512).")][JsonPropertyName("height")] public int Height { get; set; } = 12;
    [Category("Layout"), Description("Square tile size in world units (1–256).")][JsonPropertyName("tileSize")] public int TileSize { get; set; } = 16;
    [Category("Physics"), Description("Treat every non-empty tile as static collision geometry for Rigidbody2D objects.")][JsonPropertyName("collisionEnabled")] public bool CollisionEnabled { get; set; }
    [Browsable(false), JsonPropertyName("cells")] public List<int> Cells { get; set; } = new();
    [Browsable(false), JsonPropertyName("palette")] public List<string> Palette { get; set; } = new() { "#00000000", "#587C43", "#8B6B45", "#5278A6", "#C5A85C" };
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    public int GetCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        var index = y * Width + x;
        return index < Cells.Count ? Cells[index] : 0;
    }

    public void SetCell(int x, int y, int value)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) throw new ArgumentOutOfRangeException(nameof(x), "Tile coordinates are outside the map.");
        if (value < 0 || value >= Math.Max(1, Palette.Count)) throw new ArgumentOutOfRangeException(nameof(value), "Tile palette index is out of range.");
        EnsureCellStorage();
        Cells[y * Width + x] = value;
    }

    /// <summary>Sets every cell to a palette index.</summary>
    public void Fill(int paletteIndex)
    {
        if (paletteIndex < 0 || paletteIndex >= Math.Max(1, Palette.Count))
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        EnsureCellStorage();
        for (var i = 0; i < Cells.Count; i++) Cells[i] = paletteIndex;
    }

    /// <summary>Paints a clipped rectangle of cells. Negative or out-of-bounds edges are safely clipped.</summary>
    public int PaintRectangle(int x, int y, int width, int height, int paletteIndex)
    {
        if (paletteIndex < 0 || paletteIndex >= Math.Max(1, Palette.Count))
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        if (width <= 0 || height <= 0) return 0;
        EnsureCellStorage();
        var left = (int)Math.Clamp((long)x, 0L, Width);
        var top = (int)Math.Clamp((long)y, 0L, Height);
        var right = (int)Math.Clamp((long)x + width, 0L, Width);
        var bottom = (int)Math.Clamp((long)y + height, 0L, Height);
        var changed = 0;
        for (var row = top; row < bottom; row++)
        for (var column = left; column < right; column++)
        {
            var index = row * Width + column;
            if (Cells[index] == paletteIndex) continue;
            Cells[index] = paletteIndex;
            changed++;
        }
        return changed;
    }

    /// <summary>Four-way flood fill, returning the number of cells changed.</summary>
    public int FloodFill(int startX, int startY, int paletteIndex)
    {
        if (startX < 0 || startY < 0 || startX >= Width || startY >= Height)
            throw new ArgumentOutOfRangeException(nameof(startX), "Flood-fill origin is outside the map.");
        if (paletteIndex < 0 || paletteIndex >= Math.Max(1, Palette.Count))
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        EnsureCellStorage();
        var startIndex = startY * Width + startX;
        var target = Cells[startIndex];
        if (target == paletteIndex) return 0;
        var visited = new bool[checked(Width * Height)];
        var queue = new Queue<int>();
        queue.Enqueue(startIndex);
        visited[startIndex] = true;
        var changed = 0;
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            if (Cells[index] != target) continue;
            Cells[index] = paletteIndex;
            changed++;
            var x = index % Width;
            var y = index / Width;
            Enqueue(x - 1, y); Enqueue(x + 1, y); Enqueue(x, y - 1); Enqueue(x, y + 1);
        }
        return changed;

        void Enqueue(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            var index = y * Width + x;
            if (visited[index] || Cells[index] != target) return;
            visited[index] = true;
            queue.Enqueue(index);
        }
    }

    /// <summary>
    /// Generates a deterministic cellular-automata cave. Palette index 0 is empty;
    /// wallPaletteIndex is used for solid cells. The outer border remains solid.
    /// </summary>
    public void GenerateCave(int wallPaletteIndex = 1, double initialWallChance = .46,
        int smoothingIterations = 5, int seed = 1)
    {
        EnsureCellStorage();
        if (wallPaletteIndex <= 0 || wallPaletteIndex >= Palette.Count)
            throw new ArgumentOutOfRangeException(nameof(wallPaletteIndex), "A cave wall needs a non-empty palette entry.");
        if (!double.IsFinite(initialWallChance)) initialWallChance = .46;
        initialWallChance = Math.Clamp(initialWallChance, .05, .95);
        smoothingIterations = Math.Clamp(smoothingIterations, 0, 24);
        var random = new Random(seed);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var border = x == 0 || y == 0 || x == Width - 1 || y == Height - 1;
            Cells[y * Width + x] = border || random.NextDouble() < initialWallChance ? wallPaletteIndex : 0;
        }

        var next = new int[checked(Width * Height)];
        for (var iteration = 0; iteration < smoothingIterations; iteration++)
        {
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var wallNeighbours = 0;
                for (var oy = -1; oy <= 1; oy++)
                for (var ox = -1; ox <= 1; ox++)
                {
                    if (ox == 0 && oy == 0) continue;
                    var nx = x + ox;
                    var ny = y + oy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height || Cells[ny * Width + nx] != 0)
                        wallNeighbours++;
                }
                next[y * Width + x] = (x == 0 || y == 0 || x == Width - 1 || y == Height - 1 || wallNeighbours >= 5)
                    ? wallPaletteIndex : 0;
            }
            for (var i = 0; i < Cells.Count; i++) Cells[i] = next[i];
        }
    }

    /// <summary>Returns a four-neighbour adjacency mask: north=1, east=2, south=4, west=8.</summary>
    public int GetNeighborMask(int x, int y, int paletteIndex)
    {
        var mask = 0;
        if (GetCell(x, y - 1) == paletteIndex) mask |= 1;
        if (GetCell(x + 1, y) == paletteIndex) mask |= 2;
        if (GetCell(x, y + 1) == paletteIndex) mask |= 4;
        if (GetCell(x - 1, y) == paletteIndex) mask |= 8;
        return mask;
    }

    public void Resize(int width, int height)
    {
        if (width is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(height));

        // Capture the old dimensions before changing the row stride. This preserves
        // tile coordinates when width is edited in the Inspector.
        // Use the authored old row stride even when it is invalid. This allows
        // Diagnostics → Fix safe issues to preserve the overlapping top-left cells
        // while clamping oversized/undersized maps into supported dimensions.
        var oldWidth = Math.Max(1, Width);
        var oldHeight = Math.Max(1, Height);
        var oldCells = Cells ?? new List<int>();
        var resized = new List<int>(new int[checked(width * height)]);
        for (var y = 0; y < Math.Min(oldHeight, height); y++)
        for (var x = 0; x < Math.Min(oldWidth, width); x++)
        {
            var oldIndex = (long)y * oldWidth + x;
            if (oldIndex >= 0 && oldIndex < oldCells.Count) resized[y * width + x] = oldCells[(int)oldIndex];
        }
        Width = width;
        Height = height;
        Cells = resized;
        EnsureCellStorage();
    }

    public void EnsureCellStorage()
    {
        Cells ??= new List<int>();
        Palette ??= new List<string>();
        Width = Math.Clamp(Width, 1, 512);
        Height = Math.Clamp(Height, 1, 512);
        TileSize = Math.Clamp(TileSize, 1, 256);
        if (Palette.Count == 0)
            Palette.AddRange(new[] { "#00000000", "#587C43", "#8B6B45", "#5278A6", "#C5A85C" });
        if (Palette.Count > 256) Palette.RemoveRange(256, Palette.Count - 256);
        var count = checked(Width * Height);
        if (Cells.Count < count) Cells.AddRange(Enumerable.Repeat(0, count - Cells.Count));
        else if (Cells.Count > count) Cells.RemoveRange(count, Cells.Count - count);
    }
}
