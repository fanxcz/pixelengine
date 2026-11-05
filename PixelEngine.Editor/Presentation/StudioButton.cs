using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Flat, rounded, theme-aware button used across the shell. Variants communicate intent
/// (primary action, secondary, danger, icon) and every state is owner-drawn so hover and
/// press feedback stay on-palette instead of using the OS gradient.
/// </summary>
internal sealed class StudioButton : Button
{
    private bool _hover;
    private bool _pressed;

    public StudioButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        AutoSize = false;
        Cursor = Cursors.Hand;
        Font = Theme.Body(Theme.FontControl);
        BackColor = Theme.Toolbar;
        ForeColor = Theme.Text;
        Height = Theme.Px(30);
        MinimumSize = new Size(Theme.Px(34), Theme.Px(26));
        Padding = new Padding(Theme.Px(6), 0, Theme.Px(6), 0);
        TextAlign = ContentAlignment.MiddleCenter;
        TabStop = true;
    }

    public StudioButton(string text) : this() { Text = text; }

    /// <summary>Visual intent of the button.</summary>
    [DefaultValue(StudioButtonKind.Secondary)]
    public StudioButtonKind Kind { get; set; } = StudioButtonKind.Secondary;

    /// <summary>When true the label and glyph are laid out from the left and padding grows.</summary>
    [DefaultValue(false)]
    public bool LeadingIcon { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    private (Color Fill, Color Border, Color Text) Palette()
    {
        if (!Enabled) return (Theme.Blend(BackColor, Theme.Window, .45f), Theme.WithAlpha(Theme.Border, 140), Theme.Dim);
        var accent = Kind switch
        {
            StudioButtonKind.Primary => Theme.Accent,
            StudioButtonKind.Success => Theme.Success,
            StudioButtonKind.Danger => Theme.Danger,
            StudioButtonKind.Ghost => Theme.Toolbar,
            _ => Theme.Toolbar
        };
        var fill = Kind switch
        {
            StudioButtonKind.Primary or StudioButtonKind.Success or StudioButtonKind.Danger =>
                _pressed ? Theme.Blend(accent, Theme.Window, .35f)
                : _hover ? Theme.Blend(accent, Color.White, .14f)
                : Theme.Blend(accent, Theme.Window, .12f),
            StudioButtonKind.Ghost => _pressed ? Theme.Selection : _hover ? Theme.Hover : Theme.Window,
            _ => _pressed ? Theme.Selection : _hover ? Theme.Hover : BackColor
        };
        var border = Kind switch
        {
            StudioButtonKind.Primary or StudioButtonKind.Success or StudioButtonKind.Danger => Theme.WithAlpha(accent, 210),
            StudioButtonKind.Ghost => Theme.WithAlpha(Theme.Border, 120),
            _ => Theme.WithAlpha(Theme.BorderStrong, _hover ? 230 : 170)
        };
        var text = Kind is StudioButtonKind.Primary or StudioButtonKind.Success or StudioButtonKind.Danger
            ? Theme.TextStrong
            : _hover || _pressed ? Theme.TextStrong : ForeColor;
        return (fill, border, text);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(Parent?.BackColor ?? Theme.Panel))
            graphics.FillRectangle(background, ClientRectangle);
        var (fill, border, textColor) = Palette();
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        Theme.PaintCard(graphics, bounds, fill, border, Theme.RadiusSmall);
        if (Focused && Enabled)
        {
            var focus = Rectangle.Inflate(bounds, -Theme.Px(3), -Theme.Px(3));
            using var pen = new Pen(Theme.WithAlpha(Theme.AccentBright, 170), 1f) { DashStyle = DashStyle.Dot };
            graphics.DrawPath(pen, Theme.RoundedRect(focus, Theme.RadiusSmall));
        }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                    (LeadingIcon ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis;
        var textBounds = Rectangle.Inflate(ClientRectangle, -Padding.Horizontal / 2 - Theme.Px(2), 0);
        TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor, flags);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { /* handled in OnPaint */ }
}

internal enum StudioButtonKind
{
    Secondary,
    Primary,
    Success,
    Danger,
    Ghost
}

/// <summary>Section header used above dock content: uppercase caption plus optional action area.</summary>
internal sealed class StudioSectionHeader : Control
{
    private string _text = string.Empty;

    public StudioSectionHeader()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = Theme.Px(26);
        Dock = DockStyle.Top;
        ForeColor = Theme.Muted;
        BackColor = Theme.Panel;
        Font = Theme.Strong(Theme.FontCaption);
    }

    public new string Text { get => _text; set { _text = value ?? string.Empty; Invalidate(); } }

    public Color Accent { get; set; } = Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        using var background = new SolidBrush(BackColor);
        graphics.FillRectangle(background, ClientRectangle);
        var dot = Theme.Px(3);
        using (var accentBrush = new SolidBrush(Accent))
            graphics.FillEllipse(accentBrush, Theme.Px(10), Height / 2 - dot / 2, dot * 2, dot * 2);
        var textBounds = new Rectangle(Theme.Px(22), 0, Math.Max(1, Width - Theme.Px(26)), Height);
        TextRenderer.DrawText(graphics, _text.ToUpperInvariant(), Font, textBounds, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Rounded container that groups related controls, similar to a properties card.</summary>
internal sealed class StudioCard : Panel
{
    public StudioCard()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Padding = new Padding(Theme.Px(10));
    }

    public Color CardFill { get; set; } = Theme.Panel;
    public Color CardBorder { get; set; } = Theme.Border;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Parent?.BackColor ?? Theme.Window);
        e.Graphics.FillRectangle(background, ClientRectangle);
        Theme.PaintCard(e.Graphics, new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CardFill, CardBorder, Theme.Radius);
    }
}
