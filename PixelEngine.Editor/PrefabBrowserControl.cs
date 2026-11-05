using System.Windows.Forms;
using PixelEngine.Serialization;

namespace PixelEngine.Editor;

/// <summary>Prefab library dock. Instances are linked by prefab name; template updates can propagate to instances.</summary>
internal sealed class PrefabBrowserControl : UserControl
{
    private readonly TextBox _filter = new() { Width = Theme.Px(160), BorderStyle = BorderStyle.FixedSingle };
    private readonly ListBox _items = new();
    private readonly Label _details = new() { Dock = DockStyle.Bottom, Height = Theme.Px(48), ForeColor = Theme.Muted, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = new Padding(Theme.Px(6), 0, Theme.Px(6), 0) };
    private readonly Button _create = new() { Text = "Create from object" };
    private readonly Button _duplicate = new() { Text = "Duplicate" };
    private readonly Button _rename = new() { Text = "Rename" };
    private readonly Button _editTemplate = new() { Text = "Edit Template" };
    private readonly Button _update = new() { Text = "Update from object" };
    private readonly Button _propagate = new() { Text = "Apply to instances" };
    private readonly Button _instantiate = new() { Text = "Instantiate" };
    private readonly Button _remove = new() { Text = "Delete" };
    private List<LicPrefabData> _prefabs = new();
    private bool _hasProject;

    public Func<bool>? CanCreateProvider { get; set; }
    public Func<bool>? CanUpdateProvider { get; set; }

    /// <summary>Returns the number of objects linked to a prefab across all project scenes.</summary>
    public Func<LicPrefabData, int>? InstanceCountProvider { get; set; }

    public event EventHandler? CreateRequested;
    public event Action<LicPrefabData, string>? RenameRequested;
    public event Action<LicPrefabData>? DuplicateRequested;
    public event Action<LicPrefabData>? EditTemplateRequested;
    public event Action<LicPrefabData>? UpdateRequested;
    public event Action<LicPrefabData>? PropagateRequested;
    public event Action<LicPrefabData>? InstantiateRequested;
    public event Action<LicPrefabData>? RemoveRequested;

    public PrefabBrowserControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        _filter.BackColor = Theme.Viewport;
        _filter.ForeColor = Theme.Text;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.Px(104), BackColor = Theme.Toolbar, Padding = new Padding(Theme.Px(5)), WrapContents = true, AutoScroll = true };
        tools.Controls.Add(new Label { Text = "Filter", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(Theme.Px(4), Theme.Px(8), Theme.Px(2), 0) });
        tools.Controls.Add(_filter);
        tools.Controls.AddRange([_create, _duplicate, _rename, _editTemplate, _update, _propagate, _instantiate, _remove]);
        _items.Dock = DockStyle.Fill;
        _items.BackColor = Theme.Viewport;
        _items.ForeColor = Theme.Text;
        Controls.Add(_items);
        Controls.Add(_details);
        Controls.Add(tools);
        _create.Click += (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty);
        _duplicate.Click += (_, _) => { if (SelectedPrefab is { } prefab) DuplicateRequested?.Invoke(prefab); };
        _rename.Click += (_, _) =>
        {
            if (SelectedPrefab is not { } prefab) return;
            var name = EditorPromptDialog.Ask(this, "Rename Prefab", "New prefab name:", prefab.Name);
            if (!string.IsNullOrWhiteSpace(name)) RenameRequested?.Invoke(prefab, name);
        };
        _items.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = _items.IndexFromPoint(e.Location);
            if (index < 0) return;
            _items.SelectedIndex = index;
            var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer() };
            menu.Items.Add("Edit template in Inspector", null, (_, _) => { if (SelectedPrefab is { } item) EditTemplateRequested?.Invoke(item); });
            menu.Items.Add("Instantiate", null, (_, _) => { if (SelectedPrefab is { } item) InstantiateRequested?.Invoke(item); });
            menu.Items.Add("Duplicate", null, (_, _) => { if (SelectedPrefab is { } item) DuplicateRequested?.Invoke(item); });
            menu.Items.Add("Rename", null, (_, _) => _rename.PerformClick());
            var updateItem = new ToolStripMenuItem("Update from selected scene object", null, (_, _) => { if (SelectedPrefab is { } item) UpdateRequested?.Invoke(item); });
            updateItem.Enabled = CanUpdateProvider?.Invoke() ?? true;
            menu.Items.Add(updateItem);
            var propagateItem = new ToolStripMenuItem("Apply template to linked instances", null, (_, _) => { if (SelectedPrefab is { } item) PropagateRequested?.Invoke(item); });
            propagateItem.Enabled = Math.Max(0, InstanceCountProvider?.Invoke(SelectedPrefab!) ?? 0) > 0;
            menu.Items.Add(propagateItem);
            menu.Items.Add("Copy prefab name", null, (_, _) => { if (SelectedPrefab is { } item) Clipboard.SetText(item.Name); });
            menu.Items.Add("Delete and unpack linked instances", null, (_, _) => { if (SelectedPrefab is { } item) RemoveRequested?.Invoke(item); });
            menu.Show(_items, e.Location);
        };
        _editTemplate.Click += (_, _) => { if (SelectedPrefab is { } prefab) EditTemplateRequested?.Invoke(prefab); };
        _update.Click += (_, _) => { if (SelectedPrefab is { } prefab) UpdateRequested?.Invoke(prefab); };
        _propagate.Click += (_, _) => { if (SelectedPrefab is { } prefab) PropagateRequested?.Invoke(prefab); };
        _instantiate.Click += (_, _) => { if (SelectedPrefab is { } prefab) InstantiateRequested?.Invoke(prefab); };
        _remove.Click += (_, _) => { if (SelectedPrefab is { } prefab) RemoveRequested?.Invoke(prefab); };
        _items.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F2 && SelectedPrefab is { } prefab)
            {
                e.SuppressKeyPress = true;
                var name = EditorPromptDialog.Ask(this, "Rename Prefab", "New prefab name:", prefab.Name);
                if (!string.IsNullOrWhiteSpace(name)) RenameRequested?.Invoke(prefab, name);
            }
            else if (e.Control && e.KeyCode == Keys.D && SelectedPrefab is { } duplicate)
            {
                e.SuppressKeyPress = true;
                DuplicateRequested?.Invoke(duplicate);
            }
            else if (e.KeyCode == Keys.Enter && SelectedPrefab is { } instantiate)
            {
                e.SuppressKeyPress = true;
                InstantiateRequested?.Invoke(instantiate);
            }
            else if (e.KeyCode == Keys.Delete && SelectedPrefab is { } remove)
            {
                e.SuppressKeyPress = true;
                RemoveRequested?.Invoke(remove);
            }
        };
        _items.SelectedIndexChanged += (_, _) => UpdateButtons();
        _filter.TextChanged += (_, _) => RefreshItems(SelectedPrefab?.Name);
        UpdateButtons();
    }

    public LicPrefabData? SelectedPrefab => _items.SelectedItem as LicPrefabData;

    public void Bind(List<LicPrefabData>? prefabs)
    {
        _hasProject = prefabs is not null;
        _prefabs = prefabs ?? new List<LicPrefabData>();
        RefreshItems(SelectedPrefab?.Name);
    }

    public void SelectPrefab(LicPrefabData? prefab)
    {
        if (prefab is null) return;
        _filter.Clear();
        RefreshItems(prefab.Name);
        _items.SelectedItem = _items.Items.Cast<object>().FirstOrDefault(item =>
            ReferenceEquals(item, prefab) || string.Equals((item as LicPrefabData)?.Name, prefab.Name, StringComparison.OrdinalIgnoreCase));
        if (_items.SelectedIndex >= 0) _items.TopIndex = Math.Max(0, _items.SelectedIndex);
        UpdateButtons();
    }

    public void RefreshActionState() => UpdateButtons();

    private void RefreshItems(string? selectedName)
    {
        _items.BeginUpdate();
        _items.Items.Clear();
        foreach (var prefab in _prefabs.Where(x => string.IsNullOrWhiteSpace(_filter.Text) || x.Name.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            _items.Items.Add(prefab);
            if (string.Equals(prefab.Name, selectedName, StringComparison.OrdinalIgnoreCase)) _items.SelectedItem = prefab;
        }
        _items.EndUpdate();
        if (_items.SelectedItem is null && _items.Items.Count > 0) _items.SelectedIndex = 0;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasPrefab = _hasProject && SelectedPrefab is not null;
        _create.Enabled = _hasProject && (CanCreateProvider?.Invoke() ?? true);
        _duplicate.Enabled = hasPrefab;
        _rename.Enabled = hasPrefab;
        _editTemplate.Enabled = hasPrefab;
        _update.Enabled = hasPrefab && (CanUpdateProvider?.Invoke() ?? true);
        _propagate.Enabled = hasPrefab && Math.Max(0, InstanceCountProvider?.Invoke(SelectedPrefab!) ?? 0) > 0;
        _instantiate.Enabled = hasPrefab;
        _remove.Enabled = hasPrefab;
        if (SelectedPrefab is { } prefab)
        {
            var body = prefab.Object;
            var instances = Math.Max(0, InstanceCountProvider?.Invoke(prefab) ?? 0);
            _details.Text = $"{prefab.Name}  ·  {body.Kind}  ·  {body.Width:0.#}×{body.Height:0.#}  ·  {body.Components.Count} component(s)  ·  {instances} linked instance(s). Select the prefab in Scene Tree to edit its template.";
        }
        else _details.Text = "Create a prefab from a selected scene object, or instantiate an existing template.";
    }
}
