using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Applies the Studio palette to WinForms' native controls. WinForms renders many controls with
/// themed system colours that ignore <see cref="Control.BackColor"/>, so a dark editor needs both
/// the documented dark-mode opt-in (`SetPreferredAppMode`, Windows 10 1809+) and an explicit
/// traversal that restyles every child control, including controls created lazily by dialogs.
/// </summary>
internal static class StudioControlStyler
{
    private enum PreferredAppMode
    {
        AppModeDefault = 0,
        AppModeAllowDark = 1,
        AppModeForceDark = 2,
        AppModeForceLight = 3,
        AppModeMax = 4
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Enables immersive dark mode for the whole process. Must run before the first window is created.</summary>
    public static void EnableApplicationDarkMode()
    {
        try { SetPreferredAppMode((int)PreferredAppMode.AppModeForceDark); }
        catch (DllNotFoundException) { /* Not Windows 10 1809+; explicit control styling still applies. */ }
        catch (EntryPointNotFoundException) { }
        catch (BadImageFormatException) { }
        // Application.SetColorMode is .NET 9+; on .NET 8 the per-control styling below is the
        // dark-mode path, so the editor does not depend on a newer runtime.
    }

    /// <summary>Applies the dark title bar / immersive dark mode to a top-level window.</summary>
    public static void ApplyWindowChrome(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            var enabled = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 20H1+, 19 on 1809/1903.
            if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Removes the native bevel from a control so owner-drawn/tinted surfaces show cleanly.</summary>
    public static void ApplyExplorerTheme(Control control)
    {
        if (!control.IsHandleCreated) return;
        try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Recursively styles a control tree with the Studio palette.</summary>
    public static void Style(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.IsDisposed) return;
        root.SuspendLayout();
        try
        {
            StyleControl(root);
            foreach (Control child in root.Controls) Style(child);
        }
        finally
        {
            root.ResumeLayout(false);
        }
    }

    /// <summary>Styles a single control based on its concrete type. Idempotent.</summary>
    public static void StyleControl(Control control)
    {
        switch (control)
        {
            case StudioButton or StudioTabControl or StudioSectionHeader or StudioCard or SplitContainer or CodeEditorControl or EditorTerminalControl:
                return; // fully owner-drawn
            case Form form:
                if (form is not (EditorPromptDialog or EditorCommandPaletteDialog or ScriptReferenceDialog or GoToLineForm))
                {
                    form.BackColor = form.BackColor == default ? Theme.Window : form.BackColor;
                    form.ForeColor = Theme.Text;
                }
                return;
            case MenuStrip menu:
                menu.BackColor = Theme.Panel;
                menu.ForeColor = Theme.Text;
                menu.Font = Theme.Body(Theme.FontBody);
                menu.Renderer = StudioToolStripStyle.CreateRenderer();
                menu.Padding = new Padding(Theme.Px(8), Theme.Px(3), Theme.Px(8), Theme.Px(3));
                return;
            case StatusStrip status:
                status.BackColor = Theme.Panel;
                status.ForeColor = Theme.Muted;
                status.Font = Theme.Body(Theme.FontSmall);
                status.Renderer = StudioToolStripStyle.CreateRenderer();
                status.SizingGrip = false;
                foreach (ToolStripItem item in status.Items) item.ForeColor = Theme.Muted;
                return;
            case ToolStrip strip:
                strip.BackColor = strip is MenuStrip ? Theme.Panel : Theme.Toolbar;
                strip.ForeColor = Theme.Text;
                strip.Renderer = StudioToolStripStyle.CreateRenderer();
                strip.Font = Theme.Body(Theme.FontBody);
                strip.Padding = new Padding(Theme.Px(6), Theme.Px(2), Theme.Px(6), Theme.Px(2));
                foreach (ToolStripItem item in strip.Items) StyleToolStripItem(item);
                return;
            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.Appearance = TabAppearance.Normal;
                tabs.SizeMode = TabSizeMode.Fixed;
                tabs.Font = Theme.Body(Theme.FontBody);
                tabs.BackColor = Theme.Panel;
                tabs.ForeColor = Theme.Text;
                foreach (TabPage page in tabs.TabPages) StyleTabPage(tabs, page);
                return;
            case TreeView tree:
                tree.BackColor = Theme.Viewport;
                tree.ForeColor = Theme.Text;
                tree.BorderStyle = BorderStyle.None;
                tree.Font = Theme.Body(Theme.FontControl);
                tree.LineColor = Theme.WithAlpha(Theme.Border, 200);
                tree.ItemHeight = Theme.Px(23);
                tree.Indent = Theme.Px(16);
                tree.ShowLines = false;
                tree.ShowPlusMinus = true;
                tree.ShowRootLines = false;
                tree.FullRowSelect = true;
                tree.HideSelection = false;
                tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
                if (tree.Tag is not "studio-styled")
                {
                    tree.Tag = "studio-styled";
                    tree.BeforeExpand += (_, _) => { };
                    var workaround = new DarkTreeViewDrawer(tree);
                    tree.Disposed += (_, _) => workaround.Dispose();
                }
                ApplyExplorerTheme(tree);
                return;
            case ListView list:
                list.BackColor = Theme.Viewport;
                list.ForeColor = Theme.Text;
                list.BorderStyle = BorderStyle.None;
                list.Font = Theme.Body(Theme.FontBody);
                list.FullRowSelect = true;
                list.HideSelection = false;
                list.GridLines = false;
                list.OwnerDraw = false;
                ApplyExplorerTheme(list);
                return;
            case ListBox box:
                box.BackColor = Theme.Viewport;
                box.ForeColor = Theme.Text;
                box.BorderStyle = BorderStyle.None;
                box.Font = Theme.Body(Theme.FontBody);
                box.IntegralHeight = false;
                return;
            case PropertyGrid grid:
                grid.BackColor = Theme.Panel;
                grid.ViewBackColor = Theme.Panel;
                grid.ViewForeColor = Theme.Text;
                grid.ViewBorderColor = Theme.Border;
                grid.CategoryForeColor = Theme.Muted;
                grid.CategorySplitterColor = Theme.Border;
                grid.HelpBackColor = Theme.Panel;
                grid.HelpForeColor = Theme.Muted;
                grid.HelpBorderColor = Theme.Border;
                grid.CommandsBackColor = Theme.Panel;
                grid.CommandsForeColor = Theme.Text;
                grid.CommandsBorderColor = Theme.Border;
                grid.LineColor = Theme.Border;
                grid.SelectedItemWithFocusBackColor = Theme.Selection;
                grid.SelectedItemWithFocusForeColor = Theme.TextStrong;
                grid.DisabledItemForeColor = Theme.Dim;
                grid.CanShowVisualStyleGlyphs = false;
                grid.Font = Theme.Body(Theme.FontBody);
                return;
            case TextBoxBase text:
                text.BackColor = Theme.Viewport;
                text.ForeColor = Theme.Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                text.Font = Theme.Body(Theme.FontBody);
                if (text is RichTextBox rich)
                {
                    rich.BorderStyle = BorderStyle.None;
                    rich.DetectUrls = false;
                }
                return;
            case ComboBox combo:
                combo.BackColor = Theme.Viewport;
                combo.ForeColor = Theme.Text;
                combo.FlatStyle = FlatStyle.Flat;
                combo.Font = Theme.Body(Theme.FontBody);
                ApplyExplorerTheme(combo);
                return;
            case Button button:
                button.BackColor = Theme.Toolbar;
                button.ForeColor = Theme.Text;
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.FlatAppearance.BorderColor = Theme.BorderStrong;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Theme.Hover;
                button.FlatAppearance.MouseDownBackColor = Theme.Selection;
                button.Font = Theme.Body(Theme.FontBody);
                return;
            case CheckBox or RadioButton:
                control.BackColor = Theme.Panel;
                control.ForeColor = Theme.Text;
                control.Font = Theme.Body(Theme.FontBody);
                return;
            case Label label:
                label.ForeColor = label.ForeColor == Theme.Text || label.ForeColor == default ? Theme.Text : label.ForeColor;
                label.BackColor = label.BackColor == default ? Theme.Panel : label.BackColor;
                return;
            case Panel or UserControl:
                if (control.BackColor == default || control.BackColor == SystemColors.Control)
                    control.BackColor = Theme.Panel;
                if (control.ForeColor == default || control.ForeColor == Control.DefaultForeColor)
                    control.ForeColor = Theme.Text;
                return;
            case PictureBox picture:
                picture.BackColor = picture.BackColor == default ? Theme.Viewport : picture.BackColor;
                return;
            case ProgressBar bar:
                bar.BackColor = Theme.Viewport;
                bar.ForeColor = Theme.Accent;
                return;
            case NumericUpDown numeric:
                numeric.BackColor = Theme.Viewport;
                numeric.ForeColor = Theme.Text;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                numeric.Font = Theme.Body(Theme.FontBody);
                return;
            case DataGridView grid2:
                grid2.BackgroundColor = Theme.Viewport;
                grid2.GridColor = Theme.Border;
                grid2.BorderStyle = BorderStyle.None;
                grid2.EnableHeadersVisualStyles = false;
                grid2.ColumnHeadersDefaultCellStyle.BackColor = Theme.Toolbar;
                grid2.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
                grid2.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Selection;
                grid2.DefaultCellStyle.BackColor = Theme.Viewport;
                grid2.DefaultCellStyle.ForeColor = Theme.Text;
                grid2.DefaultCellStyle.SelectionBackColor = Theme.Selection;
                grid2.DefaultCellStyle.SelectionForeColor = Theme.TextStrong;
                grid2.RowHeadersVisible = false;
                return;
            case TrackBar track:
                track.BackColor = Theme.Panel;
                return;
        }
    }

    /// <summary>Styles a tab page and its nesting for owner-drawn tab controls.</summary>
    public static void StyleTabPage(TabControl owner, TabPage page)
    {
        page.BackColor = owner.BackColor;
        page.ForeColor = Theme.Text;
        if (owner is TabControl and not StudioTabControl)
        {
            owner.DrawItem -= LegacyTabPainter;
            owner.DrawItem += LegacyTabPainter;
        }
    }

    private static void LegacyTabPainter(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
        var selected = e.Index == tabs.SelectedIndex;
        var bounds = e.Bounds;
        using var fill = new SolidBrush(selected ? Theme.TabActive : Theme.Panel);
        e.Graphics.FillRectangle(fill, bounds);
        var text = tabs.TabPages[e.Index].Text;
        TextRenderer.DrawText(e.Graphics, text, tabs.Font, Rectangle.Inflate(bounds, -Theme.Px(6), 0),
            selected ? Theme.TextStrong : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, bounds.Left + Theme.Px(4), bounds.Bottom - Theme.Px(2), Math.Max(1, bounds.Width - Theme.Px(8)), Theme.Px(2));
        }
        using var divider = new Pen(Theme.Border);
        e.Graphics.DrawLine(divider, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    private static void StyleToolStripItem(ToolStripItem item)
    {
        item.ForeColor = item.Enabled ? Theme.Text : Theme.Dim;
        item.Font = Theme.Body(Theme.FontBody);
        if (item is ToolStripDropDownItem dropDown)
        {
            dropDown.DropDown.BackColor = Theme.Toolbar;
            dropDown.DropDown.ForeColor = Theme.Text;
            dropDown.DropDown.Renderer = StudioToolStripStyle.CreateRenderer();
            dropDown.DropDown.Font = Theme.Body(Theme.FontBody);
            foreach (ToolStripItem child in dropDown.DropDownItems) StyleToolStripItem(child);
            if (dropDown.HasDropDownItems)
                dropDown.DropDown.Opened += (_, _) => PadDropDown(dropDown.DropDown);
        }
        if (item is ToolStripTextBox textBox)
        {
            textBox.BackColor = Theme.Viewport;
            textBox.ForeColor = Theme.Text;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        if (item is ToolStripComboBox combo)
        {
            combo.BackColor = Theme.Viewport;
            combo.ForeColor = Theme.Text;
            combo.FlatStyle = FlatStyle.Flat;
        }
        if (item is ToolStripLabel label)
        {
            label.BackColor = Color.Transparent;
            if (label.ForeColor == Control.DefaultForeColor) label.ForeColor = Theme.Muted;
        }
    }

    /// <summary>Adds breathing room to drop-down menus so items do not touch the menu edge.</summary>
    private static void PadDropDown(ToolStripDropDown dropDown)
    {
        dropDown.Padding = new Padding(Theme.Px(3));
        foreach (ToolStripItem item in dropDown.Items)
        {
            if (item is ToolStripSeparator) continue;
            item.Padding = new Padding(Theme.Px(4), Theme.Px(3), Theme.Px(4), Theme.Px(3));
        }
    }
}

/// <summary>
/// Owns the owner-draw handler for tree views. Implemented as a small disposable observer so the
/// static styler does not have to keep per-control state.
/// </summary>
internal sealed class DarkTreeViewDrawer : IDisposable
{
    private readonly TreeView _tree;

    public DarkTreeViewDrawer(TreeView tree)
    {
        _tree = tree;
        _tree.DrawNode += OnDrawNode;
        _tree.BeforeSelect += OnBeforeSelect;
    }

    private void OnBeforeSelect(object? sender, TreeViewCancelEventArgs e)
    {
        // Keep selection colours in the palette instead of the OS highlight.
        _tree.Invalidate();
    }

    private void OnDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        var graphics = e.Graphics;
        var bounds = e.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var rowWidth = Math.Max(bounds.Width + bounds.Left, Math.Max(0, _tree.ClientSize.Width - bounds.Left));
        var row = new Rectangle(0, bounds.Y, Math.Max(1, _tree.ClientSize.Width), bounds.Height);
        var selected = (e.State & TreeNodeStates.Selected) != 0;
        var hot = (e.State & TreeNodeStates.Hot) != 0;
        using (var fill = new SolidBrush(selected ? Theme.Selection : hot ? Theme.Hover : Theme.Viewport))
            graphics.FillRectangle(fill, row);
        if (selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            graphics.FillRectangle(accent, row.X, row.Y + 1, Theme.Px(2), Math.Max(1, row.Height - 2));
        }
        var textBounds = new Rectangle(bounds.X, row.Y, Math.Max(1, rowWidth - bounds.X), row.Height);
        TextRenderer.DrawText(graphics, e.Node.Text, _tree.Font, textBounds,
            selected ? Theme.TextStrong : _tree.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    public void Dispose()
    {
        _tree.DrawNode -= OnDrawNode;
        _tree.BeforeSelect -= OnBeforeSelect;
    }
}
