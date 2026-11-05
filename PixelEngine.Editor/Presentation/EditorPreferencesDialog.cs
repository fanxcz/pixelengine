using System.Drawing;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Preferences dialog bound to <see cref="EditorSettings"/>. Edits a working copy so Cancel
/// leaves the current session untouched, and validates every value before returning.
/// </summary>
internal sealed class EditorPreferencesDialog : Form
{
    private readonly EditorSettings _working;
    private readonly CheckBox _autoCheck = new() { Text = "Run the Lua syntax check when a project loads", AutoSize = true };
    private readonly CheckBox _saveBeforeRun = new() { Text = "Save the project before launching the player", AutoSize = true };
    private readonly CheckBox _confirmDiscard = new() { Text = "Ask before discarding unsaved changes", AutoSize = true };
    private readonly CheckBox _groupLogs = new() { Text = "Group repeated Output lines", AutoSize = true };
    private readonly NumericUpDown _maxOutput = new() { Minimum = 250, Maximum = 20_000, Increment = 250, Width = Theme.Px(110) };
    private readonly ComboBox _shell = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.Px(160) };
    private readonly NumericUpDown _terminalFont = new() { Minimum = 7, Maximum = 24, DecimalPlaces = 1, Increment = 0.5m, Width = Theme.Px(90) };
    private readonly NumericUpDown _scrollback = new() { Minimum = 500, Maximum = 50_000, Increment = 500, Width = Theme.Px(110) };
    private readonly NumericUpDown _scriptFont = new() { Minimum = 7, Maximum = 32, DecimalPlaces = 1, Increment = 0.5m, Width = Theme.Px(90) };
    private readonly CheckBox _wrap = new() { Text = "Wrap long lines", AutoSize = true };
    private readonly CheckBox _lineNumbers = new() { Text = "Show line numbers", AutoSize = true };
    private readonly NumericUpDown _gridSize = new() { Minimum = 1, Maximum = 512, Width = Theme.Px(90) };
    private readonly CheckBox _snap = new() { Text = "Snap to grid", AutoSize = true };
    private readonly CheckBox _showGrid = new() { Text = "Show grid", AutoSize = true };
    private readonly CheckBox _showColliders = new() { Text = "Show colliders", AutoSize = true };
    private readonly CheckBox _showLabels = new() { Text = "Show object labels", AutoSize = true };

    public EditorSettings Result => _working;

    private EditorPreferencesDialog(EditorSettings settings)
    {
        _working = settings;
        Text = "PixelEngine Preferences";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(Theme.Px(620), Theme.Px(660));
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = Theme.Body(Theme.FontBody);

        var content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(Theme.Px(18), Theme.Px(12), Theme.Px(18), Theme.Px(12)), BackColor = Theme.Window };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Window
        };
        content.Controls.Add(stack);

        stack.Controls.Add(Section("GENERAL"));
        foreach (var box in new[] { _autoCheck, _saveBeforeRun, _confirmDiscard, _groupLogs }) stack.Controls.Add(box);
        stack.Controls.Add(Row("Output history entries", _maxOutput));

        stack.Controls.Add(Section("TERMINAL"));
        _shell.Items.AddRange(["PowerShell", "Command Prompt"]);
        _shell.SelectedItem = _working.DefaultShell;
        stack.Controls.Add(Row("Default shell", _shell));
        stack.Controls.Add(Row("Font size (pt)", _terminalFont));
        stack.Controls.Add(Row("Scrollback lines", _scrollback));

        stack.Controls.Add(Section("SCRIPT EDITOR"));
        stack.Controls.Add(Row("Font size (pt)", _scriptFont));
        var scriptToggles = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, Theme.Px(4), 0, 0)
        };
        scriptToggles.Controls.AddRange([_wrap, _lineNumbers]);
        stack.Controls.Add(scriptToggles);

        stack.Controls.Add(Section("SCENE VIEW DEFAULTS"));
        stack.Controls.Add(Row("Grid size", _gridSize));
        foreach (var box in new[] { _snap, _showGrid, _showColliders, _showLabels }) stack.Controls.Add(box);

        stack.Controls.Add(Section("RECENT PROJECTS"));
        var recentRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, Theme.Px(2), 0, 0)
        };
        var recentCount = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.Body(Theme.FontBody),
            Text = RecentSummary(),
            Margin = new Padding(0, Theme.Px(8), Theme.Px(12), 0)
        };
        var clearRecent = new StudioButton("Clear list") { Width = Theme.Px(110), Kind = StudioButtonKind.Ghost };
        clearRecent.Click += (_, _) =>
        {
            // Only the working copy is touched: Cancel still restores the previous history.
            _working.RecentProjects.Clear();
            recentCount.Text = RecentSummary();
        };
        recentRow.Controls.AddRange([recentCount, clearRecent]);
        stack.Controls.Add(recentRow);

        var note = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Px(30),
            ForeColor = Theme.Muted,
            Font = Theme.Body(Theme.FontSmall),
            Text = $"Stored at {EditorSettings.SettingsFile}",
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(Theme.Px(18), 0, 0, 0)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.Px(54),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(Theme.Px(16), Theme.Px(10), Theme.Px(16), Theme.Px(10)),
            WrapContents = false,
            BackColor = Theme.Panel
        };
        var ok = new StudioButton("Save changes") { Width = Theme.Px(130), Kind = StudioButtonKind.Primary, DialogResult = DialogResult.OK };
        var cancel = new StudioButton("Cancel") { Width = Theme.Px(96), DialogResult = DialogResult.Cancel };
        var reset = new StudioButton("Restore defaults") { Width = Theme.Px(140), Kind = StudioButtonKind.Ghost };
        reset.Click += (_, _) => Apply(new EditorSettings());
        buttons.Controls.AddRange([ok, cancel, reset]);

        Controls.Add(content);
        Controls.Add(note);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        Apply(_working.Clone());

        // Validate on OK so an impossible combination never reaches disk.
        ok.Click += (_, _) => Collect();
        Shown += (_, _) => StudioControlStyler.Style(this);
    }

    /// <summary>Shows the preferences dialog; returns the edited settings on OK, otherwise null.</summary>
    public static EditorSettings? Pick(IWin32Window owner, EditorSettings current)
    {
        using var dialog = new EditorPreferencesDialog(current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Result : null;
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Accent,
        Font = Theme.Strong(Theme.FontSmall),
        Margin = new Padding(0, Theme.Px(14), 0, Theme.Px(6))
    };

    private static Panel Row(string label, Control editor)
    {
        var row = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(Theme.Px(520), 0), Margin = new Padding(0, Theme.Px(2), 0, Theme.Px(4)) };
        var caption = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Location = new Point(0, Theme.Px(6)), Font = Theme.Body(Theme.FontBody) };
        editor.Location = new Point(Theme.Px(230), 0);
        row.Controls.Add(editor);
        row.Controls.Add(caption);
        row.Height = Math.Max(editor.Height, caption.Height) + Theme.Px(4);
        return row;
    }

    private string RecentSummary()
    {
        var count = _working.RecentProjects.Count;
        return count == 0 ? "No recent projects" : $"{count} recent project(s) remembered";
    }

    private void Apply(EditorSettings settings)
    {
        _autoCheck.Checked = settings.AutoCheckScripts;
        _saveBeforeRun.Checked = settings.SaveBeforeRun;
        _confirmDiscard.Checked = settings.ConfirmDiscardChanges;
        _groupLogs.Checked = settings.GroupRepeatedLogs;
        _maxOutput.Value = Math.Clamp(settings.MaxOutputEntries, (int)_maxOutput.Minimum, (int)_maxOutput.Maximum);
        _shell.SelectedItem = settings.DefaultShell == "Command Prompt" ? "Command Prompt" : "PowerShell";
        _terminalFont.Value = Math.Clamp((decimal)settings.TerminalFontSize, _terminalFont.Minimum, _terminalFont.Maximum);
        _scrollback.Value = Math.Clamp(settings.TerminalScrollback, (int)_scrollback.Minimum, (int)_scrollback.Maximum);
        _scriptFont.Value = Math.Clamp((decimal)settings.ScriptFontSize, _scriptFont.Minimum, _scriptFont.Maximum);
        _wrap.Checked = settings.ScriptWordWrap;
        _lineNumbers.Checked = settings.ScriptShowLineNumbers;
        _gridSize.Value = Math.Clamp((decimal)settings.SceneGridSize, _gridSize.Minimum, _gridSize.Maximum);
        _snap.Checked = settings.SceneSnap;
        _showGrid.Checked = settings.SceneShowGrid;
        _showColliders.Checked = settings.SceneShowColliders;
        _showLabels.Checked = settings.SceneShowLabels;
    }

    private void Collect()
    {
        _working.AutoCheckScripts = _autoCheck.Checked;
        _working.SaveBeforeRun = _saveBeforeRun.Checked;
        _working.ConfirmDiscardChanges = _confirmDiscard.Checked;
        _working.GroupRepeatedLogs = _groupLogs.Checked;
        _working.MaxOutputEntries = (int)_maxOutput.Value;
        _working.DefaultShell = _shell.SelectedItem?.ToString() ?? "PowerShell";
        _working.TerminalFontSize = (float)_terminalFont.Value;
        _working.TerminalScrollback = (int)_scrollback.Value;
        _working.ScriptFontSize = (float)_scriptFont.Value;
        _working.ScriptWordWrap = _wrap.Checked;
        _working.ScriptShowLineNumbers = _lineNumbers.Checked;
        _working.SceneGridSize = (float)_gridSize.Value;
        _working.SceneSnap = _snap.Checked;
        _working.SceneShowGrid = _showGrid.Checked;
        _working.SceneShowColliders = _showColliders.Checked;
        _working.SceneShowLabels = _showLabels.Checked;
        _working.Normalize();
    }
}

internal static class EditorSettingsExtensions
{
    /// <summary>Shallow copy used so the dialog can be cancelled without side effects.</summary>
    public static EditorSettings Clone(this EditorSettings source) => new()
    {
        Version = source.Version,
        AutoCheckScripts = source.AutoCheckScripts,
        SaveBeforeRun = source.SaveBeforeRun,
        ConfirmDiscardChanges = source.ConfirmDiscardChanges,
        GroupRepeatedLogs = source.GroupRepeatedLogs,
        MaxOutputEntries = source.MaxOutputEntries,
        DefaultShell = source.DefaultShell,
        TerminalFontSize = source.TerminalFontSize,
        TerminalScrollback = source.TerminalScrollback,
        ScriptFontSize = source.ScriptFontSize,
        ScriptWordWrap = source.ScriptWordWrap,
        ScriptShowLineNumbers = source.ScriptShowLineNumbers,
        SceneGridSize = source.SceneGridSize,
        SceneSnap = source.SceneSnap,
        SceneShowGrid = source.SceneShowGrid,
        SceneShowColliders = source.SceneShowColliders,
        SceneShowLabels = source.SceneShowLabels,
        RecentProjects = new List<string>(source.RecentProjects ?? new List<string>())
    };
}
