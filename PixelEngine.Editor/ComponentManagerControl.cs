using System.ComponentModel;
using System.Windows.Forms;
using PixelEngine.Serialization;

namespace PixelEngine.Editor;

/// <summary>Object component stack with an editable inspector and deterministic ordering.</summary>
internal sealed class ComponentManagerControl : UserControl
{
    private readonly ComboBox _types = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.Px(150) };
    private readonly Button _add = new() { Text = "+ Add", Width = Theme.Px(66) };
    private readonly Button _up = new() { Text = "↑", Width = Theme.Px(34) };
    private readonly Button _down = new() { Text = "↓", Width = Theme.Px(34) };
    private readonly Button _reset = new() { Text = "Reset", Width = Theme.Px(58) };
    private readonly Button _duplicate = new() { Text = "Copy", Width = Theme.Px(56) };
    private readonly ToolTip _toolTips = new();
    private readonly Button _remove = new() { Text = "Remove", Width = Theme.Px(66) };
    private readonly ListBox _components = new();
    private readonly PropertyGrid _properties = new();
    private LicObjectData? _target;
    private bool _binding;

    public event EventHandler? ComponentsChanged;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTips.Dispose();
        base.Dispose(disposing);
    }

    public ComponentManagerControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        _types.Items.AddRange(["SpriteRenderer", "Rigidbody2D", "BoxCollider2D", "CircleCollider2D", "AudioSource", "Animator", "CameraFollow", "Script"]);
        _types.SelectedIndex = 0;
        _toolTips.SetToolTip(_up, "Move component up");
        _toolTips.SetToolTip(_down, "Move component down");
        _toolTips.SetToolTip(_reset, "Reset selected component to its defaults");
        _toolTips.SetToolTip(_duplicate, "Duplicate selected component, including extension fields");
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.Px(40), Padding = new Padding(Theme.Px(4)), BackColor = Theme.Toolbar, WrapContents = false, AutoScroll = true };
        tools.Controls.AddRange([_types, _add, _up, _down, _duplicate, _reset, _remove]);
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Panel, FixedPanel = FixedPanel.Panel1, SplitterWidth = Theme.Px(6) };
        _components.Dock = DockStyle.Fill;
        _components.BackColor = Theme.Viewport;
        _components.ForeColor = Theme.Text;
        _components.BorderStyle = BorderStyle.None;
        _components.IntegralHeight = false;
        _components.ContextMenuStrip = BuildComponentContextMenu();
        _components.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = _components.IndexFromPoint(e.Location);
            if (index >= 0) _components.SelectedIndex = index;
            else _components.ClearSelected();
        };
        _components.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.D) { e.SuppressKeyPress = true; DuplicateComponent(); }
            else if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; RemoveComponent(); }
            else if (e.Alt && e.KeyCode is Keys.Up or Keys.Down) { e.SuppressKeyPress = true; MoveComponent(e.KeyCode == Keys.Up ? -1 : 1); }
        };
        _properties.Dock = DockStyle.Fill;
        _properties.ToolbarVisible = false;
        _properties.BackColor = Theme.Panel;
        _properties.ViewBackColor = Theme.Panel;
        _properties.ViewForeColor = Theme.Text;
        _properties.CategoryForeColor = Theme.Muted;
        _properties.HelpBackColor = Theme.Panel;
        _properties.HelpForeColor = Theme.Text;
        _properties.LineColor = Theme.Border;
        // DPI-aware proportions: the splitter distance must be applied after the control has a
        // handle, otherwise WinForms clamps it against the design-time size and clips the list.
        split.HandleCreated += (_, _) =>
        {
            var desired = Math.Max(Theme.Px(120), split.Height * 2 / 5);
            if (desired < split.Height - Theme.Px(140))
                split.SplitterDistance = desired;
        };
        split.Panel1MinSize = Theme.Px(90);
        split.Panel2MinSize = Theme.Px(120);
        split.Panel1.Controls.Add(_components);
        split.Panel2.Controls.Add(_properties);
        Controls.Add(split);
        Controls.Add(tools);

        _add.Click += (_, _) => AddComponent();
        _remove.Click += (_, _) => RemoveComponent();
        _reset.Click += (_, _) => ResetComponent();
        _duplicate.Click += (_, _) => DuplicateComponent();
        _up.Click += (_, _) => MoveComponent(-1);
        _down.Click += (_, _) => MoveComponent(1);
        _components.SelectedIndexChanged += (_, _) =>
        {
            if (!_binding) _properties.SelectedObject = _components.SelectedItem is LicComponentData component ? new ComponentInspectorAdapter(component) : null;
            UpdateButtons();
        };
        _properties.PropertyValueChanged += (_, _) =>
        {
            _components.Invalidate();
            ComponentsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Bind(LicObjectData? target)
    {
        _binding = true;
        _target = target;
        _components.BeginUpdate();
        _components.Items.Clear();
        if (target is not null)
        {
            target.Components ??= new List<LicComponentData>();
            foreach (var component in target.Components) _components.Items.Add(component);
        }
        if (_components.Items.Count > 0) _components.SelectedIndex = 0;
        else _properties.SelectedObject = null;
        _properties.SelectedObject = _components.SelectedItem is LicComponentData selected ? new ComponentInspectorAdapter(selected) : null;
        _components.EndUpdate();
        _types.Enabled = target is not null;
        _add.Enabled = target is not null;
        _binding = false;
        UpdateButtons();
    }

    private ContextMenuStrip BuildComponentContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer() };
        var duplicate = new ToolStripMenuItem("Duplicate", null, (_, _) => DuplicateComponent());
        var reset = new ToolStripMenuItem("Reset to defaults", null, (_, _) => ResetComponent());
        var moveUp = new ToolStripMenuItem("Move up", null, (_, _) => MoveComponent(-1));
        var moveDown = new ToolStripMenuItem("Move down", null, (_, _) => MoveComponent(1));
        var remove = new ToolStripMenuItem("Remove", null, (_, _) => RemoveComponent());
        menu.Items.AddRange([duplicate, reset, moveUp, moveDown, new ToolStripSeparator(), remove]);
        menu.Opening += (_, _) =>
        {
            var index = _components.SelectedIndex;
            var selected = _target is not null && index >= 0 && _components.SelectedItem is LicComponentData;
            duplicate.Enabled = reset.Enabled = remove.Enabled = selected;
            moveUp.Enabled = selected && index > 0;
            moveDown.Enabled = selected && index < _components.Items.Count - 1;
        };
        return menu;
    }

    private void DuplicateComponent()
    {
        if (_target is null || _components.SelectedItem is not LicComponentData current) return;
        var index = _target.Components.IndexOf(current);
        if (index < 0) return;
        var duplicate = LicProjectOperations.CloneComponentData(current);
        _target.Components.Insert(index + 1, duplicate);
        _binding = true;
        _components.Items.Insert(index + 1, duplicate);
        _components.SelectedIndex = index + 1;
        _properties.SelectedObject = new ComponentInspectorAdapter(duplicate);
        _binding = false;
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
        UpdateButtons();
    }

    private void ResetComponent()
    {
        if (_target is null || _components.SelectedItem is not LicComponentData current) return;
        var index = _target.Components.IndexOf(current);
        if (index < 0) return;
        if (MessageBox.Show(this, $"Reset {current.Type} to its default values?", "Reset component", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var replacement = LicComponentData.Create(current.Type);
        replacement.ExtraFields = current.ExtraFields;
        _target.Components[index] = replacement;
        _binding = true;
        _components.Items[index] = replacement;
        _components.SelectedIndex = index;
        _properties.SelectedObject = new ComponentInspectorAdapter(replacement);
        _binding = false;
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
        UpdateButtons();
    }

    private void AddComponent()
    {
        if (_target is null || _types.SelectedItem is not string type) return;
        if (_target.Components.Any(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"{type} is already attached to this object.", "Component", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var component = LicComponentData.Create(type);
        _target.Components.Add(component);
        _components.Items.Add(component);
        _components.SelectedItem = component;
        _properties.SelectedObject = new ComponentInspectorAdapter(component);
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
        UpdateButtons();
    }

    private void RemoveComponent()
    {
        if (_target is null || _components.SelectedItem is not LicComponentData component) return;
        var index = _components.SelectedIndex;
        _target.Components.Remove(component);
        _components.Items.Remove(component);
        _properties.SelectedObject = null;
        if (_components.Items.Count > 0) _components.SelectedIndex = Math.Clamp(index, 0, _components.Items.Count - 1);
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
        UpdateButtons();
    }

    private void MoveComponent(int offset)
    {
        if (_target is null) return;
        var index = _components.SelectedIndex;
        var next = index + offset;
        if (index < 0 || next < 0 || next >= _target.Components.Count) return;
        var component = _target.Components[index];
        _target.Components.RemoveAt(index);
        _target.Components.Insert(next, component);
        _binding = true;
        _components.Items.RemoveAt(index);
        _components.Items.Insert(next, component);
        _components.SelectedIndex = next;
        _binding = false;
        _properties.SelectedObject = new ComponentInspectorAdapter(component);
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var index = _components.SelectedIndex;
        var hasSelection = index >= 0 && _target is not null;
        _remove.Enabled = hasSelection;
        _reset.Enabled = hasSelection;
        _duplicate.Enabled = hasSelection;
        _up.Enabled = hasSelection && index > 0;
        _down.Enabled = hasSelection && index < _components.Items.Count - 1;
    }
}


/// <summary>Shows only the properties that apply to the currently selected component type.</summary>
internal sealed class ComponentInspectorAdapter : CustomTypeDescriptor
{
    private static readonly IReadOnlyDictionary<string, string[]> VisibleProperties = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["SpriteRenderer"] = ["Enabled", "SortingLayer", "SortingOrder", "Texture", "Tint"],
        ["Rigidbody2D"] = ["Enabled", "Mass", "GravityScale", "UseGravity", "LinearDamping", "IsStatic", "IsKinematic", "FreezePositionX", "FreezePositionY"],
        ["BoxCollider2D"] = ["Enabled", "BoxSizeX", "BoxSizeY", "ColliderOffsetX", "ColliderOffsetY", "IsTrigger", "CollisionLayer", "CollisionMask", "Friction", "Restitution", "CastsShadows"],
        ["CircleCollider2D"] = ["Enabled", "Radius", "ColliderOffsetX", "ColliderOffsetY", "IsTrigger", "CollisionLayer", "CollisionMask", "Friction", "Restitution", "CastsShadows"],
        ["AudioSource"] = ["Enabled", "Clip", "Volume", "Pitch", "Pan", "AudioBus", "Loop", "PlayOnAwake"],
        ["Animator"] = ["Enabled", "Animation", "Speed", "SpriteFrameWidth", "SpriteFrameHeight", "SpriteFrameCount", "SpriteFramesPerSecond", "SpriteSheetColumns", "SpriteFirstFrame", "SpriteLoop", "SpritePingPong"],
        ["CameraFollow"] = ["Enabled", "CameraTarget", "CameraOffsetX", "CameraOffsetY", "CameraSmoothSpeed", "CameraDeadZoneX", "CameraDeadZoneY"],
        ["Script"] = ["Enabled", "ScriptName"]
    };

    public LicComponentData Data { get; }

    public ComponentInspectorAdapter(LicComponentData data)
        : base(TypeDescriptor.GetProvider(data).GetTypeDescriptor(data))
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public override string? GetClassName() => Data.Type;
    public override string? GetComponentName() => Data.Type;
    public override object? GetPropertyOwner(PropertyDescriptor? pd) => Data;

    public override PropertyDescriptorCollection GetProperties() => FilterProperties(null);
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => FilterProperties(attributes);

    private PropertyDescriptorCollection FilterProperties(Attribute[]? attributes)
    {
        var allowed = VisibleProperties.TryGetValue(Data.Type, out var properties)
            ? properties.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(["Enabled"], StringComparer.OrdinalIgnoreCase);
        var source = TypeDescriptor.GetProperties(Data, attributes ?? Array.Empty<Attribute>());
        return new PropertyDescriptorCollection(source.Cast<PropertyDescriptor>()
            .Where(property => allowed.Contains(property.Name))
            .ToArray(), readOnly: true);
    }
}
