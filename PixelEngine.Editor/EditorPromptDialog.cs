using System.Drawing;
using System.Windows.Forms;

namespace PixelEngine.Editor;

internal sealed class EditorPromptDialog : Form
{
    private readonly TextBox _value;
    public string Value => _value.Text.Trim();

    private EditorPromptDialog(string title, string label, string initialValue)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 132);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);

        var caption = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Location = new Point(12, 12) };
        _value = new TextBox { Text = initialValue, Location = new Point(12, 38), Width = 396, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Viewport, ForeColor = Theme.Text };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(248, 88), Width = 76, FlatStyle = FlatStyle.Flat, BackColor = Theme.Toolbar, ForeColor = Theme.Text };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(332, 88), Width = 76, FlatStyle = FlatStyle.Flat, BackColor = Theme.Toolbar, ForeColor = Theme.Text };
        Controls.AddRange([caption, _value, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { _value.Focus(); _value.SelectAll(); };
    }

    public static string? Ask(IWin32Window owner, string title, string label, string initialValue)
    {
        using var dialog = new EditorPromptDialog(title, label, initialValue);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Value.Length > 0 ? dialog.Value : null;
    }
}
