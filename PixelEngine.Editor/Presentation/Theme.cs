using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Central design tokens for the PixelEngine Studio shell. Colours, metrics and fonts are
/// declared once so every surface (chrome, docks, dialogs, terminal, canvas) reads from the
/// same palette instead of hard-coding its own greys. Sizes are authored in logical DIPs at
/// 96 DPI and scaled by <see cref="DpiScale"/> so nothing is clipped on high-DPI displays.
/// </summary>
internal static class Theme
{
    // ── Surfaces ────────────────────────────────────────────────────────────────
    /// <summary>Application background (near-black).</summary>
    public static readonly Color Window = Color.FromArgb(0x0B, 0x0D, 0x12);
    /// <summary>Dock and panel surface.</summary>
    public static readonly Color Panel = Color.FromArgb(0x14, 0x18, 0x21);
    /// <summary>Slightly lifted surface used for toolbars, headers and tab strips.</summary>
    public static readonly Color Toolbar = Color.FromArgb(0x19, 0x1E, 0x29);
    /// <summary>Recessed surface for lists, inputs and viewports.</summary>
    public static readonly Color Viewport = Color.FromArgb(0x0F, 0x12, 0x19);
    /// <summary>Deepest surface, used for the built-in terminal.</summary>
    public static readonly Color Terminal = Color.FromArgb(0x08, 0x0A, 0x0F);
    /// <summary>Selected tab surface.</summary>
    public static readonly Color TabActive = Color.FromArgb(0x1E, 0x24, 0x31);
    /// <summary>Row/tab hover surface.</summary>
    public static readonly Color Hover = Color.FromArgb(0x22, 0x29, 0x38);
    /// <summary>Row/tab selection surface.</summary>
    public static readonly Color Selection = Color.FromArgb(0x24, 0x33, 0x4D);
    /// <summary>Hairline border.</summary>
    public static readonly Color Border = Color.FromArgb(0x26, 0x2D, 0x3A);
    /// <summary>Stronger divider.</summary>
    public static readonly Color BorderStrong = Color.FromArgb(0x33, 0x3C, 0x4D);

    // ── Accents & semantic colours ──────────────────────────────────────────────
    public static readonly Color Accent = Color.FromArgb(0x6E, 0x8B, 0xFF);
    public static readonly Color AccentBright = Color.FromArgb(0x93, 0xA8, 0xFF);
    public static readonly Color AccentDark = Color.FromArgb(0x3A, 0x4C, 0xA8);
    public static readonly Color Violet = Color.FromArgb(0x9B, 0x7C, 0xFF);
    public static readonly Color Success = Color.FromArgb(0x4A, 0xDE, 0x9B);
    public static readonly Color Warning = Color.FromArgb(0xF2, 0xC1, 0x4E);
    public static readonly Color Danger = Color.FromArgb(0xF8, 0x71, 0x71);

    // ── Text ────────────────────────────────────────────────────────────────────
    public static readonly Color Text = Color.FromArgb(0xE6, 0xEA, 0xF2);
    public static readonly Color TextStrong = Color.FromArgb(0xF7, 0xF9, 0xFC);
    public static readonly Color Muted = Color.FromArgb(0x8A, 0x95, 0xAB);
    public static readonly Color Dim = Color.FromArgb(0x5F, 0x6A, 0x80);

    // ── Canvas ──────────────────────────────────────────────────────────────────
    /// <summary>Selection outline inside the 2D scene view.</summary>
    public static readonly Color SelectionOutline = Color.FromArgb(0xFF, 0xC8, 0x6B);
    /// <summary>Grid majors.</summary>
    public static readonly Color GridMajor = Color.FromArgb(0x3A, 0x2F, 0x46, 0x5E);
    /// <summary>Grid minors.</summary>
    public static readonly Color GridMinor = Color.FromArgb(0x1E, 0x2F, 0x46, 0x5E);

    // ── Type scale (DIPs @ 96 DPI) ──────────────────────────────────────────────
    public const float FontCaption = 7.75f;
    public const float FontSmall = 8.25f;
    public const float FontBody = 9f;
    public const float FontControl = 9.25f;
    public const float FontSubtitle = 10.5f;
    public const float FontTitle = 14f;
    public const float FontHero = 20f;

    private static string? _uiFamily;
    private static string? _monoFamily;
    private static int _dpi = 96;

    /// <summary>Current device DPI, updated by the shell on construction and DPI changes.</summary>
    public static int Dpi => _dpi;

    /// <summary>Scale factor from logical DIPs to physical pixels for the current DPI.</summary>
    public static float DpiScale => Math.Clamp(_dpi / 96f, 1f, 4f);

    /// <summary>Primary UI family with graceful fallback if the system font is unavailable.</summary>
    public static string UiFamily => _uiFamily ??= PickFamily("Segoe UI Variable Text", "Segoe UI", "Tahoma");

    /// <summary>Semibold UI family used for headings and group labels.</summary>
    public static string UiStrongFamily => PickFamily("Segoe UI Variable Display Semibold", "Segoe UI Semibold", UiFamily);

    /// <summary>Monospace family for the terminal and script editor.</summary>
    public static string MonoFamily => _monoFamily ??= PickFamily("Cascadia Mono", "Cascadia Code", "Consolas", "Courier New");

    public static Font Caption(float size = FontCaption, FontStyle style = FontStyle.Regular) => new(UiFamily, size, style, GraphicsUnit.Point);
    public static Font Strong(float size = FontSmall, FontStyle style = FontStyle.Bold) => new(UiStrongFamily, size, style, GraphicsUnit.Point);
    public static Font Body(float size = FontBody) => new(UiFamily, size, FontStyle.Regular, GraphicsUnit.Point);
    public static Font Mono(float size = FontControl, FontStyle style = FontStyle.Regular) => new(MonoFamily, size, style, GraphicsUnit.Point);

    /// <summary>Sets the DPI factor explicitly (used before a control handle exists).</summary>
    public static void UseDpi(int dpi) => _dpi = Math.Clamp(dpi <= 0 ? 96 : dpi, 96, 384);

    /// <summary>Records the DPI reported by a control so subsequent metrics scale correctly.</summary>
    public static void TrackDpi(Control control)
    {
        try
        {
            using var graphics = control.CreateGraphics();
            _dpi = Math.Clamp((int)Math.Round(graphics.DpiY), 96, 384);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            _dpi = 96;
        }
    }

    /// <summary>Logical DIP → physical pixels.</summary>
    public static int Px(int logical) => (int)Math.Round(logical * DpiScale);

    /// <summary>Logical points → physical pixels (font sizes are authored in points).</summary>
    public static float Pt(float points) => points * DpiScale;

    /// <summary>Standard corner radius.</summary>
    public static int Radius => Px(6);

    /// <summary>Compact corner radius for small controls.</summary>
    public static int RadiusSmall => Px(4);

    /// <summary>Builds a rounded rectangle path used by owner-drawn controls and cards.</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Draws a rounded filled panel with an optional hairline border.</summary>
    public static void PaintCard(System.Drawing.Graphics graphics, Rectangle bounds, Color fill, Color border, int radius)
    {
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1)), radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        using var pen = new Pen(border, 1f);
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = smoothing;
    }

    /// <summary>Blends two colours; used for hover/pressed states and subtle gradients.</summary>
    public static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    /// <summary>Applies a uniform alpha to a colour.</summary>
    public static Color WithAlpha(Color color, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), color.R, color.G, color.B);

    private static string PickFamily(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                using var family = new FontFamily(candidate);
                if (string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            catch (ArgumentException) { /* Font is not installed on this machine; try the next candidate. */ }
        }
        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Segoe UI";
    }
}
