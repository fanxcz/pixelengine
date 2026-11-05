using System.Drawing;
using System.Windows.Forms;

namespace PixelEngine.Editor;

internal sealed record EditorCommand(string Id, string Title, string Category, string Shortcut);

/// <summary>Searchable command launcher inspired by the Godot command palette.</summary>
internal sealed class EditorCommandPaletteDialog : Form
{
    private readonly TextBox _query = new();
    private readonly ListBox _results = new();
    private readonly List<EditorCommand> _commands;

    public string? SelectedCommandId => (_results.SelectedItem as CommandRow)?.Command.Id;

    private sealed record CommandRow(EditorCommand Command)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Command.Shortcut)
            ? $"{Command.Title}    ·    {Command.Category}"
            : $"{Command.Title}    ·    {Command.Category}    ·    {Command.Shortcut}";
    }

    public EditorCommandPaletteDialog(IEnumerable<EditorCommand> commands)
    {
        _commands = commands?.ToList() ?? new List<EditorCommand>();
        Text = "Command Palette";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 430);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        KeyPreview = true;

        var heading = new Label
        {
            Text = "COMMAND PALETTE",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(8, 6, 0, 0),
            ForeColor = Theme.Muted,
            BackColor = Theme.Panel
        };
        _query.Dock = DockStyle.Top;
        _query.Margin = new Padding(8);
        _query.BorderStyle = BorderStyle.FixedSingle;
        _query.BackColor = Theme.Viewport;
        _query.ForeColor = Theme.Text;
        _query.Font = new Font("Segoe UI", 11f);
        _query.PlaceholderText = "Search commands…";

        _results.Dock = DockStyle.Fill;
        _results.BorderStyle = BorderStyle.None;
        _results.BackColor = Theme.Viewport;
        _results.ForeColor = Theme.Text;
        _results.Font = new Font("Segoe UI", 10f);
        _results.IntegralHeight = false;
        _results.DoubleClick += (_, _) => Accept();
        _results.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
        };
        _query.TextChanged += (_, _) => RefreshResults();
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                if (_results.Items.Count > 0) { _results.SelectedIndex = Math.Min(_results.SelectedIndex + 1, _results.Items.Count - 1); _results.Focus(); }
            }
            else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
        };
        Controls.Add(_results);
        Controls.Add(_query);
        Controls.Add(heading);
        RefreshResults();
        Shown += (_, _) => { _query.Focus(); _query.SelectAll(); };
    }

    public static string? Pick(IWin32Window owner, IEnumerable<EditorCommand> commands)
    {
        using var dialog = new EditorCommandPaletteDialog(commands);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedCommandId : null;
    }

    private void RefreshResults()
    {
        var query = _query.Text.Trim();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = _commands
            .Where(command => words.Length == 0 || words.All(word =>
                command.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                command.Category.Contains(word, StringComparison.OrdinalIgnoreCase) ||
                command.Shortcut.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(command => command.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(command => new CommandRow(command))
            .ToArray();
        _results.BeginUpdate();
        _results.Items.Clear();
        _results.Items.AddRange(filtered);
        if (_results.Items.Count > 0) _results.SelectedIndex = 0;
        _results.EndUpdate();
    }

    private void Accept()
    {
        if (_results.SelectedItem is not CommandRow) return;
        DialogResult = DialogResult.OK;
        Close();
    }
}
