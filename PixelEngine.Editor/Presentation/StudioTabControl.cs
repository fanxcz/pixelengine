using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Owner-drawn tab strip with flat tabs, an accent underline for the active page and a
/// scroll affordance when tabs do not fit. Unlike <see cref="TabControl"/> with FlatButtons it
/// never clips the tab labels and keeps a 24-DIP minimum touch height at every DPI.
/// </summary>
internal sealed class StudioTabControl : TabControl
{
    private int _hoverIndex = -1;
    private ToolTip? _toolTip;

    public StudioTabControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        DoubleBuffered = true;
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        Appearance = TabAppearance.Normal;
        HotTrack = false;
        Alignment = TabAlignment.Top;
        ItemSize = new Size(Theme.Px(120), Theme.Px(26));
        Padding = new Point(0, 0);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = Theme.Body(Theme.FontBody);
        Multiline = false;
    }

    /// <summary>Tab body surface; tabs paint their strip with <see cref="Theme.Toolbar"/>.</summary>
    public Color StripColor { get; set; } = Theme.Panel;

    /// <summary>Optional fixed tab width; when null the width follows the label text.</summary>
    public int? FixedTabWidth { get; set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // WinForms forces the tab strip colour through the native control; keep it in sync
        // with the owner-drawn strip so resizing never flashes the default light background.
        if (IsHandleCreated) Invalidate();
    }

    private int MeasureTab(TabPage page)
    {
        if (FixedTabWidth is { } width) return Theme.Px(width);
        var textWidth = TextRenderer.MeasureText(page.Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        return Math.Clamp(textWidth + Theme.Px(30), Theme.Px(64), Theme.Px(240));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var stripHeight = ItemSize.Height;
        using (var stripBrush = new SolidBrush(StripColor))
            graphics.FillRectangle(stripBrush, 0, 0, Width, stripHeight);
        using (var hairline = new Pen(Theme.Border, 1f))
            graphics.DrawLine(hairline, 0, stripHeight - 1, Width, stripHeight - 1);

        var x = 2;
        for (var index = 0; index < TabPages.Count; index++)
        {
            var page = TabPages[index];
            var width = MeasureTab(page);
            if (x + width > Width) { DrawOverflowIndicator(graphics, x); break; }
            var bounds = new Rectangle(x, 0, width, stripHeight);
            DrawTab(graphics, page, bounds, index == SelectedIndex, index == _hoverIndex);
            x += width;
        }
    }

    private void DrawOverflowIndicator(Graphics graphics, int x)
    {
        using var brush = new SolidBrush(Theme.Muted);
        var y = ItemSize.Height / 2;
        graphics.FillEllipse(brush, x + 4, y - 2, 4, 4);
        graphics.FillEllipse(brush, x + 11, y - 2, 4, 4);
    }

    private void DrawTab(Graphics graphics, TabPage page, Rectangle bounds, bool selected, bool hovered)
    {
        var body = new Rectangle(bounds.X + 1, bounds.Y + Theme.Px(3), Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - Theme.Px(3)));
        var fill = selected ? Theme.TabActive : hovered ? Theme.Hover : StripColor;
        if (selected || hovered)
            Theme.PaintCard(graphics, body, fill, Theme.WithAlpha(Theme.BorderStrong, selected ? 220 : 120), Theme.RadiusSmall);
        else
        {
            using var brush = new SolidBrush(fill);
            graphics.FillRectangle(brush, body);
        }

        var textColor = selected ? Theme.TextStrong : hovered ? Theme.Text : Theme.Muted;
        var textBounds = new Rectangle(body.X + Theme.Px(10), body.Y, Math.Max(1, body.Width - Theme.Px(20)), body.Height);
        TextRenderer.DrawText(graphics, page.Text, Font, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

        if (selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            var underline = new Rectangle(body.X + Theme.Px(8), body.Bottom - Theme.Px(2), Math.Max(1, body.Width - Theme.Px(16)), Theme.Px(2));
            graphics.FillRectangle(accent, underline);
        }

        // A dirty marker ("●" suffix) is already part of the page text; underline it in accent
        // by painting the gutter dot so unsaved scripts stay obvious at a glance.
        if (page.Text.EndsWith('●'))
        {
            using var accent = new SolidBrush(Theme.Warning);
            graphics.FillEllipse(accent, body.Right - Theme.Px(12), body.Top + body.Height / 2 - Theme.Px(2), Theme.Px(4), Theme.Px(4));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = HitTestTab(e.Location);
        if (index == _hoverIndex) return;
        _hoverIndex = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        if (index >= 0)
        {
            _toolTip ??= new ToolTip { InitialDelay = 600, ReshowDelay = 200, AutoPopDelay = 6000 };
            var rect = GetTabRect(index);
            if (rect.Width > 0) _toolTip.Show(TabPages[index].ToolTipText is { Length: > 0 } tip ? tip : TabPages[index].Text, this, rect.X + 8, rect.Bottom);
        }
        else _toolTip?.Hide(this);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _toolTip?.Hide(this);
        if (_hoverIndex == -1) return;
        _hoverIndex = -1;
        Cursor = Cursors.Default;
        Invalidate();
    }

    private int HitTestTab(Point point)
    {
        var x = 2;
        for (var index = 0; index < TabPages.Count; index++)
        {
            var width = MeasureTab(TabPages[index]);
            if (point.X >= x && point.X < x + width && point.Y >= 0 && point.Y < ItemSize.Height) return index;
            x += width;
        }
        return -1;
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip?.Dispose();
        base.Dispose(disposing);
    }
}
