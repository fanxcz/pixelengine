using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>Shared dark renderer for every ToolStrip, MenuStrip, ContextMenuStrip and StatusStrip.</summary>
internal static class StudioToolStripStyle
{
    public static ToolStripProfessionalRenderer CreateRenderer()
        => new(new StudioProfessionalColorTable()) { RoundedEdges = false };
}

internal sealed class StudioProfessionalColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => Theme.Panel;
    public override Color MenuStripGradientEnd => Theme.Panel;
    public override Color ToolStripGradientBegin => Theme.Toolbar;
    public override Color ToolStripGradientMiddle => Theme.Toolbar;
    public override Color ToolStripGradientEnd => Theme.Toolbar;
    public override Color StatusStripGradientBegin => Theme.Panel;
    public override Color StatusStripGradientEnd => Theme.Panel;
    public override Color ToolStripDropDownBackground => Theme.Toolbar;
    public override Color ImageMarginGradientBegin => Theme.Toolbar;
    public override Color ImageMarginGradientMiddle => Theme.Toolbar;
    public override Color ImageMarginGradientEnd => Theme.Toolbar;
    public override Color MenuItemSelected => Theme.Selection;
    public override Color MenuItemSelectedGradientBegin => Theme.Selection;
    public override Color MenuItemSelectedGradientEnd => Theme.Selection;
    public override Color MenuItemPressedGradientBegin => Theme.Selection;
    public override Color MenuItemPressedGradientMiddle => Theme.Selection;
    public override Color MenuItemPressedGradientEnd => Theme.Selection;
    public override Color MenuItemBorder => Theme.AccentDark;
    public override Color MenuBorder => Theme.BorderStrong;
    public override Color ToolStripBorder => Theme.Border;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
    public override Color ButtonSelectedHighlight => Theme.Selection;
    public override Color ButtonSelectedHighlightBorder => Theme.AccentDark;
    public override Color ButtonSelectedGradientBegin => Theme.Hover;
    public override Color ButtonSelectedGradientMiddle => Theme.Hover;
    public override Color ButtonSelectedGradientEnd => Theme.Hover;
    public override Color ButtonPressedHighlight => Theme.Selection;
    public override Color ButtonPressedHighlightBorder => Theme.Accent;
    public override Color ButtonPressedGradientBegin => Theme.Selection;
    public override Color ButtonPressedGradientMiddle => Theme.Selection;
    public override Color ButtonPressedGradientEnd => Theme.Selection;
    public override Color ButtonCheckedHighlight => Theme.Selection;
    public override Color ButtonCheckedHighlightBorder => Theme.Accent;
    public override Color ButtonCheckedGradientBegin => Theme.Selection;
    public override Color ButtonCheckedGradientMiddle => Theme.Selection;
    public override Color ButtonCheckedGradientEnd => Theme.Selection;
    public override Color CheckBackground => Theme.Selection;
    public override Color CheckSelectedBackground => Theme.Selection;
    public override Color CheckPressedBackground => Theme.AccentDark;
    public override Color GripDark => Theme.Border;
    public override Color GripLight => Theme.Toolbar;
    public override Color OverflowButtonGradientBegin => Theme.Toolbar;
    public override Color OverflowButtonGradientMiddle => Theme.Toolbar;
    public override Color OverflowButtonGradientEnd => Theme.Toolbar;
}

/// <summary>
/// Owner-drawn flat toolbar renderer. ProfessionalRenderer gives every button a bevel that
/// reads as Windows-XP chrome; this renderer paints roundless, accent-underlined active items.
/// </summary>
internal sealed class StudioToolStripRenderer : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border, 1f);
        e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripButton button) { base.OnRenderButtonBackground(e); return; }
        var bounds = new Rectangle(Point.Empty, button.Size);
        var active = button.Pressed || button.Selected || button.Checked;
        if (!active) return;
        var radius = Theme.RadiusSmall;
        Theme.PaintCard(e.Graphics, bounds, button.Pressed ? Theme.Selection : Theme.Hover,
            Theme.WithAlpha(Theme.BorderStrong, 190), radius);
        if (button.Checked && button.Enabled)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, bounds.X + radius, bounds.Bottom - Theme.Px(2), Math.Max(1, bounds.Width - radius * 2), Theme.Px(2));
        }
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Pressed && !e.Item.Selected) return;
        Theme.PaintCard(e.Graphics, new Rectangle(Point.Empty, e.Item.Size),
            e.Item.Pressed ? Theme.Selection : Theme.Hover, Theme.WithAlpha(Theme.BorderStrong, 190), Theme.RadiusSmall);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed) return;
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        bounds.Inflate(-Theme.Px(3), -1);
        Theme.PaintCard(e.Graphics, bounds, e.Item.Pressed ? Theme.Selection : Theme.Hover, Theme.WithAlpha(Theme.BorderStrong, 140), Theme.RadiusSmall);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var text = e.Text;
        if (string.IsNullOrEmpty(text)) { base.OnRenderItemText(e); return; }
        var color = !e.Item.Enabled ? Theme.Dim
            : e.Item.Selected || e.Item.Pressed ? Theme.TextStrong
            : e.Item.ForeColor;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var bounds = e.TextRectangle;
        // Centre short toolbar labels inside their own padded button body.
        if (e.Item is ToolStripButton or ToolStripDropDownButton)
        {
            bounds = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            flags |= TextFormatFlags.HorizontalCenter;
        }
        TextRenderer.DrawText(e.Graphics, text, e.TextFont, bounds, color, e.ToolStrip?.BackColor ?? Theme.Toolbar, flags);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Border, 1f);
        if (e.Vertical)
            e.Graphics.DrawLine(pen, e.Item.Width / 2, 3, e.Item.Width / 2, Math.Max(3, e.Item.Height - 3));
        else
            e.Graphics.DrawLine(pen, 6, y, Math.Max(6, e.Item.Width - 6), y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? Theme.Dim : Theme.Muted;
        var bounds = e.ArrowRectangle;
        using var pen = new Pen(e.ArrowColor, Math.Max(1.4f, Theme.Px(1) * 1.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = bounds.X + bounds.Width / 2f;
        var cy = bounds.Y + bounds.Height / 2f;
        var half = Theme.Px(3);
        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [new PointF(cx - half, cy - half * .4f), new PointF(cx, cy + half * .7f), new PointF(cx + half, cy - half * .4f)]);
        e.Graphics.SmoothingMode = previous;
    }

    protected override void OnRenderToolStripStatusLabelBackground(ToolStripItemRenderEventArgs e) { }

    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e) { }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Panel);
        e.Graphics.FillRectangle(brush, e.ToolStripPanel.Bounds);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) bounds = new Rectangle(Point.Empty, e.Item.Size);
        var previous = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.AccentBright, Math.Max(1.6f, Theme.Px(1) * 1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = bounds.X + bounds.Width / 2f;
        var cy = bounds.Y + bounds.Height / 2f;
        var w = Theme.Px(4);
        var h = Theme.Px(3);
        e.Graphics.DrawLines(pen,
        [
            new PointF(cx - w, cy),
            new PointF(cx - w * .25f, cy + h),
            new PointF(cx + w, cy - h)
        ]);
        e.Graphics.SmoothingMode = previous;
    }
}
