using System.Windows.Forms;

namespace PixelEngine.Editor;

internal sealed record ScriptReferenceEntry(string Category, string Signature, string Description, string Example)
{
    public override string ToString() => $"{Signature}    ·    {Category}";
}

/// <summary>Searchable in-editor API reference. Selecting an entry can insert its signature at the caret.</summary>
internal sealed class ScriptReferenceDialog : Form
{
    private readonly TextBox _search = new();
    private readonly ListBox _entries = new();
    private readonly RichTextBox _details = new();
    private readonly Button _insert = new() { Text = "Insert at caret", Width = 112, Height = 28, DialogResult = DialogResult.OK };
    private readonly Button _close = new() { Text = "Close", Width = 72, Height = 28, DialogResult = DialogResult.Cancel };
    private readonly List<ScriptReferenceEntry> _source;

    private ScriptReferenceDialog(string language)
    {
        Text = language.Equals("lua", StringComparison.OrdinalIgnoreCase) ? "Lua API Reference" : "C# Script Reference";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        MinimumSize = new System.Drawing.Size(680, 430);
        ClientSize = new System.Drawing.Size(820, 520);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;

        _source = BuildEntries(language);
        _search.Dock = DockStyle.Top;
        _search.Height = 30;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = Theme.Viewport;
        _search.ForeColor = Theme.Text;
        _search.Font = new System.Drawing.Font("Segoe UI", 10f);
        _search.PlaceholderText = "Search API, category or description…";

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 290, BackColor = Theme.Panel };
        _entries.Dock = DockStyle.Fill;
        _entries.BorderStyle = BorderStyle.None;
        _entries.BackColor = Theme.Viewport;
        _entries.ForeColor = Theme.Text;
        _entries.Font = new System.Drawing.Font("Cascadia Mono", 9f);
        _entries.IntegralHeight = false;
        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.BorderStyle = BorderStyle.None;
        _details.BackColor = Theme.Viewport;
        _details.ForeColor = Theme.Text;
        _details.Font = new System.Drawing.Font("Cascadia Mono", 9f);
        _details.DetectUrls = false;
        split.Panel1.Controls.Add(_entries);
        split.Panel2.Padding = new Padding(8);
        split.Panel2.Controls.Add(_details);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6), BackColor = Theme.Panel };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_insert);
        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(_search);

        _search.TextChanged += (_, _) => RefreshEntries();
        _entries.SelectedIndexChanged += (_, _) => UpdateDetails();
        _entries.DoubleClick += (_, _) => { if (_entries.SelectedItem is not null) DialogResult = DialogResult.OK; };
        _entries.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DialogResult = DialogResult.OK; }
        };
        AcceptButton = _insert;
        CancelButton = _close;
        RefreshEntries();
        Shown += (_, _) => { _search.Focus(); _search.SelectAll(); };
    }

    public static string? Pick(IWin32Window owner, string language)
    {
        using var dialog = new ScriptReferenceDialog(language);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog._entries.SelectedItem is ScriptReferenceEntry entry
            ? entry.Example
            : null;
    }

    private void RefreshEntries()
    {
        var query = _search.Text.Trim();
        var rows = _source.Where(entry => string.IsNullOrWhiteSpace(query) ||
            entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Signature.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _entries.BeginUpdate();
        _entries.Items.Clear();
        _entries.Items.AddRange(rows);
        if (_entries.Items.Count > 0) _entries.SelectedIndex = 0;
        _entries.EndUpdate();
        if (_entries.Items.Count == 0) _details.Text = "No matching API entry.";
    }

    private void UpdateDetails()
    {
        if (_entries.SelectedItem is not ScriptReferenceEntry entry) return;
        _details.Text = $"{entry.Category}\r\n\r\n{entry.Signature}\r\n\r\n{entry.Description}\r\n\r\nExample:\r\n{entry.Example}";
    }

    private static List<ScriptReferenceEntry> BuildEntries(string language)
    {
        if (!language.Equals("lua", StringComparison.OrdinalIgnoreCase))
            return new List<ScriptReferenceEntry>
            {
                new("C#", "C# source editor", "C# scripts are retained in LIC and receive syntax coloring. They are not executed by the current runtime because arbitrary C# needs a separately designed isolation model.", "// C# runtime execution is not enabled yet."),
                new("Editor", "Ctrl+F7 — Check Lua Scripts", "Validates Lua scripts in the current project.", ""),
                new("Editor", "F5 — Run Preview", "Launches the current project in the MonoGame sandbox process.", "")
            };

        return new List<ScriptReferenceEntry>
        {
            new("Lifecycle", "function start()", "Called once when the script instance is initialized.", "function start()\n    api.log('started')\nend"),
            new("Lifecycle", "function update(dt)", "Called on each runtime update. dt is elapsed time in seconds.", "function update(dt)\n    -- update gameplay here\nend"),
            new("API", "api.log(message)", "Writes a message to the runtime output/console.", "api.log('Hello from PixelEngine')"),
            new("API", "api.key(name)", "Returns true while the named keyboard key is held.", "if api.key('right') then\n    self.vx = 120\nend"),
            new("API", "api.action(name)", "Returns true while a built-in or rebound keyboard/gamepad action is held.", "if api.action('jump') then\n    self.vy = -280\nend"),
            new("API", "api.action_pressed(name)", "Returns true for one frame when an action becomes pressed.", "if api.action_pressed('interact') then\n    api.log('interact')\nend"),
            new("API", "api.axis(name)", "Reads move_x/move_y (keyboard plus left stick), look_x/look_y (right stick) or a trigger.", "local moveX = api.axis('move_x')\nself.vx = moveX * 180"),
            new("API", "api.bind_key(action, keyName)", "Adds a keyboard binding to an input action using a MonoGame Keys name.", "api.bind_key('interact', 'F')"),
            new("API", "api.time()", "Returns elapsed runtime time in seconds.", "local elapsed = api.time()"),
            new("API", "api.rand(min, max)", "Returns a random number in the given range.", "local speed = api.rand(80, 140)"),
            new("API", "api.find(name)", "Finds a scene object and returns its script proxy, or nil.", "local target = api.find('Enemy')\nif target ~= nil then\n    target.hit()\nend"),
            new("API", "api.spawn(kind, options)", "Creates an object at runtime. Available kinds depend on the compatibility runtime.", "local bullet = api.spawn('circle', { x = self.x, y = self.y })"),
            new("API", "api.destroy(name)", "Marks an object for destruction by name.", "api.destroy('TemporaryEffect')"),
            new("API", "api.purge()", "Removes destroyed objects from the runtime scene collection.", "api.purge()"),
            new("API", "api.cam(x, y, zoom)", "Sets the runtime camera position and zoom.", "api.cam(self.x, self.y, 1.2)"),
            new("API", "api.bg(color1, color2)", "Sets the scene background colors.", "api.bg('#152033', '#334A62')"),
            new("API", "api.save(key, value)", "Stores a string in a bounded project save file under the user's LocalAppData folder; falls back to memory if unavailable.", "api.save('coins', '120')"),
            new("API", "api.load(key)", "Reads a string from the runtime save store.", "local coins = api.load('coins')"),
            new("API", "api.mousex() / api.mousey()", "Returns the mouse position in world coordinates.", "local aimX = api.mousex()\nlocal aimY = api.mousey()"),
            new("API", "api.click()", "Returns true on the mouse click frame.", "if api.click() then\n    api.log('clicked')\nend"),
            new("API", "api.pick()", "Returns the name of the object under the pointer, or an empty string.", "local picked = api.pick()"),
            new("API", "api.play(assetName)", "Plays an embedded WAV asset through the MonoGame runtime audio service. AudioSource components can also auto-play and loop.", "api.play('hit.wav')"),
            new("Object", "self.x / self.y / self.w / self.h", "Current object's position and dimensions.", "self.x = self.x + 40 * dt"),
            new("Object", "self.vx / self.vy", "Current object's velocity values.", "self.vx = 120\nself.vy = -280"),
            new("Object", "self.Get(key) / self.Set(key, value)", "Per-object script memory helpers.", "self.Set('phase', 'attack')\nlocal phase = self.Get('phase')"),
            new("Object", "self.hit() / self.destroy()", "Visual hit feedback and object destruction helpers.", "self.hit()"),
            new("Editor", "Ctrl+Space — Completion", "Show contextual API, language and project object/asset suggestions.", ""),
            new("Editor", "Ctrl+/ — Toggle comment", "Comments or uncomments the selected lines.", ""),
            new("Editor", "F2 / Shift+F2 — Bookmarks", "Toggle a line bookmark and navigate between bookmarks.", ""),
            new("Editor", "Ctrl+F / Ctrl+H — Find / Replace", "Opens the find/replace panel.", ""),
            new("Editor", "Ctrl+Shift+I — Reindent", "Applies conservative block indentation without changing statements.", ""),
            new("Editor", "Ctrl+Shift+O — Outline", "Opens the script function outline selector.", ""),
            new("Editor", "Ctrl+L — Go to line", "Navigates directly to a line number.", "")
        };
    }
}
