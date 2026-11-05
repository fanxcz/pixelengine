using System.Drawing;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>Shared dark, low-contrast ToolStrip palette inspired by modern IDEs.</summary>
internal static class StudioToolStripStyle
{
    public static ToolStripProfessionalRenderer CreateRenderer()
        => new(new StudioProfessionalColorTable());
}

internal sealed class StudioProfessionalColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => Theme.Panel;
    public override Color MenuStripGradientEnd => Theme.Panel;
    public override Color ToolStripGradientBegin => Theme.Toolbar;
    public override Color ToolStripGradientMiddle => Theme.Toolbar;
    public override Color ToolStripGradientEnd => Theme.Toolbar;
    public override Color ToolStripDropDownBackground => Theme.Panel;
    public override Color ImageMarginGradientBegin => Theme.Panel;
    public override Color ImageMarginGradientMiddle => Theme.Panel;
    public override Color ImageMarginGradientEnd => Theme.Panel;
    public override Color MenuItemSelected => Theme.Selection;
    public override Color MenuItemBorder => Theme.AccentDark;
    public override Color MenuBorder => Theme.Border;
    public override Color ToolStripBorder => Theme.Border;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Panel;
    public override Color ButtonSelectedHighlight => Theme.Selection;
    public override Color ButtonSelectedHighlightBorder => Theme.Border;
    public override Color ButtonPressedHighlight => Theme.AccentDark;
    public override Color ButtonPressedHighlightBorder => Theme.Accent;
    public override Color ButtonCheckedHighlight => Theme.Selection;
    public override Color ButtonCheckedHighlightBorder => Theme.AccentDark;
    public override Color CheckBackground => Theme.Selection;
    public override Color CheckSelectedBackground => Theme.Selection;
    public override Color CheckPressedBackground => Theme.AccentDark;
}
