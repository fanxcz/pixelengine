using System.Buffers.Binary;
using System.Drawing;
using System.Windows.Forms;
using PixelEngine.Serialization;

namespace PixelEngine.Editor;

/// <summary>Project-local asset dock with filtering, preview, export, delete and assignment.</summary>
internal sealed class AssetBrowserControl : UserControl
{
    private readonly ListView _items = new() { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None };
    private readonly TextBox _search = new() { Width = Theme.Px(118), BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox _typeFilter = new() { Width = Theme.Px(98), DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _usedOnly = new() { Text = "Used only", AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Toolbar, Margin = new Padding(5, 7, 5, 0) };
    private readonly PictureBox _preview = new();
    private readonly Label _details = new();
    private readonly Button _import = new() { Text = "Import" };
    private readonly Button _rename = new() { Text = "Rename" };
    private readonly Button _assign = new() { Text = "Assign to selected" };
    private readonly Button _export = new() { Text = "Export" };
    private readonly Button _playAudio = new() { Text = "▶ Play WAV", Enabled = false };
    private readonly Button _stopAudio = new() { Text = "■ Stop", Enabled = false };
    private readonly Button _remove = new() { Text = "Remove" };
    private List<LicAssetData> _assets = new();
    private Bitmap? _previewBitmap;
    private System.Media.SoundPlayer? _soundPlayer;
    private MemoryStream? _soundStream;
    private bool _hasProject;

    /// <summary>Returns the number of scene and prefab references for an embedded asset.</summary>
    public Func<LicAssetData, int>? UsageCountProvider { get; set; }

    public event EventHandler? ImportRequested;
    public event Action<LicAssetData, string>? RenameRequested;
    public event Action<LicAssetData>? AssignRequested;
    public event Action<string[]>? FilesDropped;
    public event Action<LicAssetData>? ExportRequested;
    public event Action<LicAssetData>? RemoveRequested;

    public AssetBrowserControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        _typeFilter.Items.AddRange(["All types", "PNG", "WAV"]);
        _typeFilter.SelectedIndex = 0;

        // Compact two-row FileSystem toolbar: filters remain visible in a narrow dock,
        // while asset actions scroll horizontally instead of wrapping into tall stacks.
        var toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.Px(94), BackColor = Theme.Toolbar };
        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = Theme.Px(33), Padding = new Padding(Theme.Px(5), Theme.Px(3), Theme.Px(3), Theme.Px(1)),
            BackColor = Theme.Toolbar, WrapContents = false, AutoScroll = true
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = Theme.Px(60), Padding = new Padding(Theme.Px(4), Theme.Px(1), Theme.Px(3), Theme.Px(1)),
            BackColor = Theme.Toolbar, WrapContents = true, AutoScroll = true
        };
        var searchLabel = new Label { Text = "Filter", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 5, 2, 0) };
        _search.BackColor = Theme.Viewport;
        _search.ForeColor = Theme.Text;
        _search.Height = Theme.Px(24);
        _typeFilter.Height = Theme.Px(25);
        _usedOnly.Margin = new Padding(3, 5, 3, 0);
        filters.Controls.Add(searchLabel);
        filters.Controls.Add(_search);
        filters.Controls.Add(_typeFilter);
        filters.Controls.Add(_usedOnly);
        foreach (var button in new[] { _import, _rename, _assign, _export, _playAudio, _stopAudio, _remove })
        {
            button.Height = Theme.Px(26);
            button.Width = Theme.Px(button.Text switch
            {
                "Assign to selected" => 104,
                "▶ Play WAV" => 82,
                "■ Stop" => 58,
                _ => 66
            });
            button.Margin = new Padding(Theme.Px(2), Theme.Px(1), Theme.Px(2), Theme.Px(1));
            button.Padding = new Padding(Theme.Px(4), 0, Theme.Px(4), 0);
            actions.Controls.Add(button);
        }
        toolbar.Controls.Add(actions);
        toolbar.Controls.Add(filters);

        _items.Columns.Add("Name", 230);
        _items.Columns.Add("Type", 62);
        _items.Columns.Add("Size", 82, HorizontalAlignment.Right);
        _items.Columns.Add("Used by", 70, HorizontalAlignment.Right);
        _items.Dock = DockStyle.Fill;
        _items.AllowDrop = true;
        _items.DragEnter += (_, e) =>
        {
            if (_hasProject && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        _items.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0) FilesDropped?.Invoke(paths);
        };
        _items.BackColor = Theme.Viewport;
        _items.ForeColor = Theme.Text;

        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Panel, FixedPanel = FixedPanel.Panel2 };
        void UpdatePreviewDock()
        {
            if (!split.IsHandleCreated || split.ClientSize.Width <= 0) return;
            // In the narrow Godot-style FileSystem dock the list gets the whole width.
            // A preview pane appears automatically when the user widens the dock.
            var showPreview = split.ClientSize.Width >= 500;
            split.Panel2Collapsed = !showPreview;
            if (showPreview)
            {
                var max = split.ClientSize.Width - 150 - split.SplitterWidth;
                if (max >= 180)
                    split.SplitterDistance = Math.Clamp((int)(split.ClientSize.Width * .64), 180, max);
            }
        }
        split.HandleCreated += (_, _) => UpdatePreviewDock();
        split.Resize += (_, _) => UpdatePreviewDock();
        split.Panel1.Controls.Add(_items);
        var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Viewport, Padding = new Padding(Theme.Px(8)) };
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.FromArgb(25, 28, 34);
        _details.Dock = DockStyle.Bottom;
        _details.Height = Theme.Px(48);
        _details.ForeColor = Theme.Muted;
        _details.TextAlign = ContentAlignment.MiddleLeft;
        previewPanel.Controls.Add(_preview);
        previewPanel.Controls.Add(_details);
        split.Panel2.Controls.Add(previewPanel);

        Controls.Add(split);
        Controls.Add(toolbar);
        _import.Click += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        _rename.Click += (_, _) =>
        {
            if (SelectedAsset is not { } asset) return;
            var name = EditorPromptDialog.Ask(this, "Rename Asset", "New asset file name (including extension):", asset.Name);
            if (!string.IsNullOrWhiteSpace(name)) RenameRequested?.Invoke(asset, name);
        };
        _items.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _items.GetItemAt(e.X, e.Y);
            if (hit is null) return;
            hit.Selected = true;
            var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer() };
            menu.Items.Add("Assign to selected object", null, (_, _) => { if (SelectedAsset is { } item) AssignRequested?.Invoke(item); });
            menu.Items.Add("Copy asset name", null, (_, _) => { if (SelectedAsset is { } item) Clipboard.SetText(item.Name); });
            menu.Items.Add("Rename", null, (_, _) => _rename.PerformClick());
            menu.Items.Add("Export", null, (_, _) => { if (SelectedAsset is { } item) ExportRequested?.Invoke(item); });
            var playItem = new ToolStripMenuItem("Play WAV preview", null, (_, _) => PlayAudioPreview());
            var stopItem = new ToolStripMenuItem("Stop WAV preview", null, (_, _) => StopAudioPreview());
            playItem.Enabled = SelectedAsset is { } selected && string.Equals(selected.Type, "wav", StringComparison.OrdinalIgnoreCase);
            stopItem.Enabled = _soundPlayer is not null;
            menu.Items.Add(playItem);
            menu.Items.Add(stopItem);
            menu.Items.Add("Remove", null, (_, _) => { if (SelectedAsset is { } item) RemoveRequested?.Invoke(item); });
            menu.Show(_items, e.Location);
        };
        _assign.Click += (_, _) => { if (SelectedAsset is { } asset) AssignRequested?.Invoke(asset); };
        _export.Click += (_, _) => { if (SelectedAsset is { } asset) ExportRequested?.Invoke(asset); };
        _playAudio.Click += (_, _) => PlayAudioPreview();
        _stopAudio.Click += (_, _) => StopAudioPreview();
        _remove.Click += (_, _) => { if (SelectedAsset is { } asset) RemoveRequested?.Invoke(asset); };
        _items.SelectedIndexChanged += (_, _) => UpdatePreview();
        _items.ItemActivate += (_, _) => { if (SelectedAsset is { } asset) AssignRequested?.Invoke(asset); };
        _items.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F2 && SelectedAsset is { } asset)
            {
                e.SuppressKeyPress = true;
                var name = EditorPromptDialog.Ask(this, "Rename Asset", "New asset file name (including extension):", asset.Name);
                if (!string.IsNullOrWhiteSpace(name)) RenameRequested?.Invoke(asset, name);
            }
            else if (e.KeyCode == Keys.Delete && SelectedAsset is { } selected)
            {
                e.SuppressKeyPress = true;
                RemoveRequested?.Invoke(selected);
            }
        };
        _search.TextChanged += (_, _) => RefreshItems(SelectedAsset?.Name);
        _typeFilter.SelectedIndexChanged += (_, _) => RefreshItems(SelectedAsset?.Name);
        _usedOnly.CheckedChanged += (_, _) => RefreshItems(SelectedAsset?.Name);
        UpdateButtons();
    }

    public LicAssetData? SelectedAsset => _items.SelectedItems.Count > 0 ? _items.SelectedItems[0].Tag as LicAssetData : null;

    public void Bind(List<LicAssetData>? assets)
    {
        var selectedName = SelectedAsset?.Name;
        _hasProject = assets is not null;
        _assets = assets ?? new List<LicAssetData>();
        _import.Enabled = _hasProject;
        RefreshItems(selectedName);
    }

    public void SelectAsset(LicAssetData? asset)
    {
        if (asset is null) return;
        _search.Clear();
        _typeFilter.SelectedIndex = 0;
        _usedOnly.Checked = false;
        RefreshItems(asset.Name);
        foreach (ListViewItem item in _items.Items)
        {
            if (ReferenceEquals(item.Tag, asset) || string.Equals((item.Tag as LicAssetData)?.Name, asset.Name, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
                item.EnsureVisible();
                break;
            }
        }
    }

    private void RefreshItems(string? selectedName = null)
    {
        _items.BeginUpdate();
        _items.Items.Clear();
        var filtered = _assets.Where(asset =>
        {
            var queryMatches = string.IsNullOrWhiteSpace(_search.Text) || asset.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            var typeMatches = _typeFilter.SelectedIndex switch
            {
                1 => string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase),
                2 => string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
            var usageCount = Math.Max(0, UsageCountProvider?.Invoke(asset) ?? 0);
            return queryMatches && typeMatches && (!_usedOnly.Checked || usageCount > 0);
        });
        foreach (var asset in filtered)
        {
            var bytes = SafeSize(asset);
            var item = new ListViewItem(asset.Name) { Tag = asset };
            item.SubItems.Add(asset.Type.ToUpperInvariant());
            item.SubItems.Add(bytes < 0 ? "invalid" : FormatSize(bytes));
            item.SubItems.Add(Math.Max(0, UsageCountProvider?.Invoke(asset) ?? 0).ToString());
            _items.Items.Add(item);
            if (string.Equals(asset.Name, selectedName, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        _items.EndUpdate();
        if (_items.SelectedItems.Count == 0 && _items.Items.Count > 0) _items.Items[0].Selected = true;
        UpdatePreview();
    }

    private static int SafeSize(LicAssetData asset)
    {
        try { return LicAssetOperations.GetBytes(asset).Length; }
        catch { return -1; }
    }

    private static string FormatSize(int bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} KB" : $"{bytes / (1024.0 * 1024):0.00} MB";

    private void UpdateButtons()
    {
        var selectedAsset = SelectedAsset;
        var selected = selectedAsset is not null;
        _rename.Enabled = selected;
        _assign.Enabled = selected;
        _export.Enabled = selected;
        _playAudio.Enabled = selectedAsset is not null && string.Equals(selectedAsset.Type, "wav", StringComparison.OrdinalIgnoreCase);
        _stopAudio.Enabled = _soundPlayer is not null;
        _remove.Enabled = selected;
    }

    private void UpdatePreview()
    {
        StopAudioPreview();
        _preview.Image = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        var asset = SelectedAsset;
        UpdateButtons();
        if (asset is null)
        {
            _details.Text = "No assets match the current filter.";
            return;
        }

        byte[] bytes;
        try { bytes = LicAssetOperations.GetBytes(asset); }
        catch (Exception ex) { _details.Text = "Invalid asset: " + ex.Message; return; }

        if (string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream);
                _previewBitmap = new Bitmap(image);
                _preview.Image = _previewBitmap;
                _details.Text = $"{asset.Name}  ·  PNG  ·  {_previewBitmap.Width}×{_previewBitmap.Height}  ·  {FormatSize(bytes.Length)}  ·  used by {Math.Max(0, UsageCountProvider?.Invoke(asset) ?? 0)}";
            }
            catch (Exception ex) { _details.Text = "PNG preview failed: " + ex.Message; }
        }
        else if (string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase))
            _details.Text = $"{asset.Name}  ·  {DescribeWave(bytes)}  ·  {FormatSize(bytes.Length)}  ·  used by {Math.Max(0, UsageCountProvider?.Invoke(asset) ?? 0)}  ·  embedded in .lic";
        else _details.Text = $"{asset.Name}  ·  {asset.Type.ToUpperInvariant()}  ·  {FormatSize(bytes.Length)}  ·  used by {Math.Max(0, UsageCountProvider?.Invoke(asset) ?? 0)}  ·  embedded in .lic";
    }

    private void PlayAudioPreview()
    {
        var asset = SelectedAsset;
        if (asset is null || !string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase)) return;
        StopAudioPreview();
        try
        {
            _soundStream = new MemoryStream(LicAssetOperations.GetBytes(asset), writable: false);
            _soundPlayer = new System.Media.SoundPlayer(_soundStream);
            _soundPlayer.Load();
            _soundPlayer.Play();
            _details.Text = $"Playing preview: {asset.Name}  ·  {DescribeWave(_soundStream.ToArray())}";
        }
        catch (Exception ex)
        {
            StopAudioPreview();
            _details.Text = $"WAV preview failed: {ex.Message}";
        }
        UpdateButtons();
    }

    private void StopAudioPreview()
    {
        try { _soundPlayer?.Stop(); }
        catch { /* Ignore audio-device errors while stopping the editor preview. */ }
        _soundPlayer?.Dispose();
        _soundPlayer = null;
        _soundStream?.Dispose();
        _soundStream = null;
        if (!IsDisposed) UpdateButtons();
    }

    private static string DescribeWave(byte[] bytes)
    {
        if (bytes.Length < 12) return "WAV (header incomplete)";
        var channels = 0;
        var sampleRate = 0;
        var bitsPerSample = 0;
        var byteRate = 0;
        long dataBytes = 0;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkName = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var payload = offset + 8;
            if (chunkSize > int.MaxValue || payload + (long)chunkSize > bytes.Length) break;
            if (chunkName == "fmt " && chunkSize >= 16)
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(payload + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(payload + 4, 4));
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(payload + 8, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(payload + 14, 2));
            }
            else if (chunkName == "data") dataBytes = chunkSize;
            offset = checked(payload + (int)chunkSize + ((int)chunkSize & 1));
        }
        var duration = byteRate > 0 ? dataBytes / (double)byteRate : 0d;
        return $"WAV · {channels} ch · {sampleRate:N0} Hz · {bitsPerSample}-bit · {duration:0.00}s";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAudioPreview();
            _previewBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
