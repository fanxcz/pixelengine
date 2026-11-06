using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using PixelEngine.Animation;
using PixelEngine.Diagnostics;
using PixelEngine.Serialization;

namespace PixelEngine.Editor;

/// <summary>Authoring workspace for diagnostics, animations, lights, particles and tile maps.</summary>
public sealed class Stage8ToolsForm : Form
{
    private readonly LicProjectData _project;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ListView _diagnostics = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly ListBox _animations = new() { Dock = DockStyle.Fill, DisplayMember = nameof(LicAnimationClipData.Name) };
    private readonly PropertyGrid _animationProperties = new() { Dock = DockStyle.Top, Height = Theme.Px(196), ToolbarVisible = false };
    private readonly DataGridView _keyframes = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Panel _animationTimeline = new() { Dock = DockStyle.Fill, BackColor = Theme.Viewport, Cursor = Cursors.Cross };
    private readonly Label _timelineReadout = new() { Dock = DockStyle.Top, Height = Theme.Px(24), ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Text = "Timeline · click to move playhead" };
    private readonly ListBox _lights = new() { Dock = DockStyle.Fill, DisplayMember = nameof(LicLightData.Name) };
    private readonly PropertyGrid _lightProperties = new() { Dock = DockStyle.Fill, ToolbarVisible = false };
    private readonly NumericUpDown _ambientLight = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = .05m, Width = Theme.Px(76), TextAlign = HorizontalAlignment.Center };
    private readonly ListBox _emitters = new() { Dock = DockStyle.Fill, DisplayMember = nameof(LicParticleEmitterData.Name) };
    private readonly PropertyGrid _emitterProperties = new() { Dock = DockStyle.Fill, ToolbarVisible = false };
    private readonly ListBox _tileMaps = new() { Dock = DockStyle.Fill, DisplayMember = nameof(LicTileMapData.Name) };
    private readonly PropertyGrid _tileMapProperties = new() { Dock = DockStyle.Top, Height = Theme.Px(196), ToolbarVisible = false };
    private readonly DataGridView _tileGrid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = true, ColumnHeadersVisible = true };
    private readonly NumericUpDown _tileBrushIndex = new() { Minimum = 0, Maximum = 255, Width = Theme.Px(62), TextAlign = HorizontalAlignment.Center };
    private readonly CheckBox _tilePaintMode = new() { Text = "Paint mode", AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Checked = false, Margin = new Padding(8, 5, 4, 0) };
    private readonly Label _tileMapHint = new() { Dock = DockStyle.Top, Height = Theme.Px(26), ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _profilerTimer = new() { Interval = 500 };
    private readonly Label _profilerSummary = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(Theme.Px(18)), Font = Theme.Mono(11f) };
    private bool _buildingGrid;
    private int _boundTileWidth;
    private int _boundTileHeight;
    private float _timelinePlayhead;
    private Button? _removeAnimationButton;
    private Button? _addKeyframeButton;
    private Button? _removeLightButton;
    private Button? _removeEmitterButton;
    private Button? _removeTileMapButton;
    private Button? _fillTileMapButton;
    private Button? _floodFillButton;
    private Button? _generateCaveButton;

    public event EventHandler? ProjectChanged;

    public Stage8ToolsForm(LicProjectData project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _project.EditorData ??= new LicEditorData();
        _ambientLight.Value = (decimal)Math.Clamp(float.IsFinite(_project.EditorData.AmbientLight) ? _project.EditorData.AmbientLight : 1f, 0f, 1f);
        _ambientLight.ValueChanged += (_, _) => { _project.EditorData.AmbientLight = (float)_ambientLight.Value; MarkChanged(); };
        Text = "PixelEngine Studio Tools";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(Theme.Px(1120), Theme.Px(760));
        MinimumSize = new Size(Theme.Px(860), Theme.Px(560));
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body();
        BuildDiagnosticsTab();
        BuildAnimationTab();
        BuildObjectDataTab("Lighting", _lights, _lightProperties, AddLight, RemoveLight, _project.EditorData.Lights);
        BuildObjectDataTab("Particles", _emitters, _emitterProperties, AddEmitter, RemoveEmitter, _project.EditorData.ParticleEmitters);
        BuildTileMapTab();
        BuildProfilerTab();
        Controls.Add(_tabs);
        RefreshLists();
        UpdateActionButtons();
        _profilerTimer.Tick += (_, _) => RefreshProfiler();
        _profilerTimer.Start();
        FormClosed += (_, _) => _profilerTimer.Stop();
        Shown += (_, _) => StudioControlStyler.Style(this);
    }

    private void BuildDiagnosticsTab()
    {
        var page = new TabPage("Diagnostics") { BackColor = BackColor, ForeColor = ForeColor };
        _diagnostics.Columns.Add("Severity", Theme.Px(88));
        _diagnostics.Columns.Add("Code", 75);
        _diagnostics.Columns.Add("Location", 220);
        _diagnostics.Columns.Add("Message", 390);
        _diagnostics.Columns.Add("Suggested action", 420);
        var buttons = CreateButtonBar();
        buttons.Controls.Add(MakeButton("Run analysis", (_, _) => RunDiagnostics()));
        buttons.Controls.Add(MakeButton("Fix safe issues", (_, _) => ApplySafeFixes()));
        buttons.Controls.Add(MakeButton("Copy report", (_, _) => CopyDiagnostics()));
        buttons.Controls.Add(MakeButton("Export report…", (_, _) => ExportDiagnostics()));
        page.Controls.Add(_diagnostics);
        page.Controls.Add(buttons);
        _tabs.TabPages.Add(page);
        RunDiagnostics();
    }

    private void RunDiagnostics()
    {
        _diagnostics.BeginUpdate();
        _diagnostics.Items.Clear();
        var issues = ProjectDiagnostics.Analyze(_project);
        foreach (var issue in issues)
        {
            var row = new ListViewItem(issue.Severity.ToString());
            row.SubItems.Add(issue.Code);
            row.SubItems.Add(issue.Location);
            row.SubItems.Add(issue.Message);
            row.SubItems.Add(issue.Suggestion);
            row.ForeColor = issue.Severity switch
            {
                DiagnosticSeverity.Error => Theme.Danger,
                DiagnosticSeverity.Warning => Theme.Warning,
                _ => Theme.Success
            };
            _diagnostics.Items.Add(row);
        }
        if (issues.Count == 0)
        {
            var success = new ListViewItem("OK");
            success.SubItems.Add("—");
            success.SubItems.Add("Project");
            success.SubItems.Add("No issues found.");
            success.SubItems.Add("No action required.");
            success.ForeColor = Theme.Success;
            _diagnostics.Items.Add(success);
        }
        _diagnostics.EndUpdate();
    }

    private void ApplySafeFixes()
    {
        var fixes = ProjectDiagnostics.ApplySafeFixes(_project);
        RunDiagnostics();
        if (fixes.Count == 0)
        {
            MessageBox.Show(this, "No safe automatic repairs were needed. Missing references and duplicate names require manual review.", "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MarkChanged();
        MessageBox.Show(this, string.Join(Environment.NewLine, fixes), $"Applied {fixes.Count} safe repair(s)", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyDiagnostics()
    {
        var report = BuildDiagnosticsText();
        if (!string.IsNullOrWhiteSpace(report)) Clipboard.SetText(report);
    }

    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = "PixelEngine-Diagnostics.txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, BuildDiagnosticsText(), Encoding.UTF8);
            MessageBox.Show(this, "Diagnostics report exported.", "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Export report failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string BuildDiagnosticsText()
    {
        var builder = new StringBuilder();
        var issues = ProjectDiagnostics.Analyze(_project);
        foreach (var issue in issues)
            builder.AppendLine($"[{issue.Severity}] {issue.Code} {issue.Location}: {issue.Message} Suggested: {issue.Suggestion}");
        if (issues.Count == 0) builder.AppendLine("No issues found.");
        return builder.ToString();
    }

    private void BuildAnimationTab()
    {
        var page = new TabPage("Animation") { BackColor = BackColor, ForeColor = ForeColor };
        var split = ConfigureSplit(new SplitContainer { Dock = DockStyle.Fill, BackColor = BackColor }, 250);
        var left = new Panel { Dock = DockStyle.Fill };
        var buttons = CreateButtonBar();
        buttons.Controls.Add(MakeButton("New clip", (_, _) => AddAnimation()));
        _removeAnimationButton = MakeButton("Remove", (_, _) => RemoveAnimation());
        _addKeyframeButton = MakeButton("Add key", (_, _) => AddKeyframe());
        buttons.Controls.Add(_removeAnimationButton);
        buttons.Controls.Add(_addKeyframeButton);
        left.Controls.Add(_animations);
        left.Controls.Add(buttons);
        var right = new Panel { Dock = DockStyle.Fill };
        var keyFramePanel = new Panel { Dock = DockStyle.Fill };
        keyFramePanel.Controls.Add(_keyframes);
        keyFramePanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = Theme.Px(24), Text = "KEYFRAME VALUES · seconds / transform channels", ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft });
        var timelineHost = new Panel { Dock = DockStyle.Fill, Height = Theme.Px(96), BackColor = Theme.Viewport, Padding = new Padding(6, 2, 6, 4) };
        timelineHost.Controls.Add(_animationTimeline);
        timelineHost.Controls.Add(_timelineReadout);
        var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = BackColor, Margin = Padding.Empty };
        rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rightLayout.Controls.Add(_animationProperties, 0, 0);
        rightLayout.Controls.Add(timelineHost, 0, 1);
        rightLayout.Controls.Add(keyFramePanel, 0, 2);
        right.Controls.Add(rightLayout);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        page.Controls.Add(split);
        _tabs.TabPages.Add(page);
        _animations.SelectedIndexChanged += (_, _) => { BindAnimation(); UpdateActionButtons(); };
        _animationProperties.PropertyValueChanged += (_, _) => { _animations.Refresh(); _animationTimeline.Invalidate(); MarkChanged(); };
        _animationTimeline.Paint += DrawAnimationTimeline;
        _animationTimeline.MouseDown += ScrubAnimationTimeline;
        _keyframes.CellEndEdit += (_, _) => HandleKeyframeGridChanged();
        _keyframes.UserDeletedRow += (_, _) => HandleKeyframeGridChanged();
    }

    private void AddAnimation()
    {
        if (_project.EditorData!.Animations.Count >= 10_000)
        {
            MessageBox.Show(this, "This project reached the 10,000 animation-clip limit.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var scene = _project.Scenes.FirstOrDefault();
        var target = scene?.Objects.FirstOrDefault();
        var clip = new LicAnimationClipData
        {
            Name = UniqueName(_project.EditorData!.Animations.Select(x => x.Name), "Animation"),
            Scene = scene?.Name ?? string.Empty,
            Target = target?.Name ?? string.Empty,
            Duration = 1f,
            Loop = true,
            Keyframes = new List<LicAnimationKeyframeData>
            {
                new() { Time = 0f, X = target?.X, Y = target?.Y, Rotation = target?.Rotation },
                new() { Time = 1f, X = (target?.X ?? 0) + 32f, Y = target?.Y, Rotation = target?.Rotation }
            }
        };
        _project.EditorData.Animations.Add(clip);
        RefreshLists();
        _animations.SelectedItem = clip;
        MarkChanged();
    }

    private void RemoveAnimation()
    {
        if (_animations.SelectedItem is not LicAnimationClipData clip) return;
        _project.EditorData!.Animations.Remove(clip);
        RefreshLists();
        MarkChanged();
    }

    private void AddKeyframe()
    {
        if (_animations.SelectedItem is not LicAnimationClipData clip) return;
        if (clip.Keyframes.Count >= 100_000)
        {
            MessageBox.Show(this, "This animation reached the 100,000 keyframe limit.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var duration = float.IsFinite(clip.Duration) && clip.Duration > 0f ? clip.Duration : 1f;
        var time = Math.Clamp(_timelinePlayhead, 0f, duration);
        if (clip.Keyframes.Any(x => float.IsFinite(x.Time) && Math.Abs(x.Time - time) < 0.001f))
        {
            MessageBox.Show(this, "A keyframe already exists at the playhead position.", "Animation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        clip.Keyframes.Add(new LicAnimationKeyframeData { Time = time });
        BindAnimation();
        _timelinePlayhead = time;
        _timelineReadout.Text = $"Playhead · {time:0.000}s / {duration:0.000}s";
        _animationTimeline.Invalidate();
        MarkChanged();
    }

    private void BindAnimation()
    {
        _animationProperties.SelectedObject = _animations.SelectedItem;
        _timelinePlayhead = 0f;
        _keyframes.Columns.Clear();
        foreach (var (name, header) in new[] { ("Time", "Time"), ("X", "X"), ("Y", "Y"), ("Rotation", "Rotation"), ("ScaleX", "Scale X"), ("ScaleY", "Scale Y") })
            _keyframes.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header });
        _keyframes.Rows.Clear();
        if (_animations.SelectedItem is not LicAnimationClipData clip)
        {
            _timelineReadout.Text = "Timeline · select an animation clip";
            _animationTimeline.Invalidate();
            UpdateActionButtons();
            return;
        }
        foreach (var key in clip.Keyframes.OrderBy(x => x.Time))
            _keyframes.Rows.Add(key.Time, key.X, key.Y, key.Rotation, key.ScaleX, key.ScaleY);
        var duration = float.IsFinite(clip.Duration) && clip.Duration > 0f ? clip.Duration : 1f;
        _timelineReadout.Text = $"Playhead · {_timelinePlayhead:0.000}s / {duration:0.000}s · click timeline to scrub";
        _animationTimeline.Invalidate();
        UpdateActionButtons();
    }

    private void DrawAnimationTimeline(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(_animationTimeline.BackColor);
        if (_animations.SelectedItem is not LicAnimationClipData clip)
        {
            TextRenderer.DrawText(graphics, "Select a clip to view its keyframes", Font, _animationTimeline.ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var duration = float.IsFinite(clip.Duration) && clip.Duration > 0f ? clip.Duration : 1f;
        const float left = 32f;
        const float rightInset = 16f;
        var right = Math.Max(left + 1f, _animationTimeline.ClientSize.Width - rightInset);
        var y = Math.Max(30f, _animationTimeline.ClientSize.Height * .62f);
        using var axisPen = new Pen(Theme.Border, 1f);
        using var tickPen = new Pen(Theme.BorderSoft, 1f);
        using var keyBrush = new SolidBrush(Theme.Warning);
        using var playheadPen = new Pen(Theme.Accent, 2f);
        using var textBrush = new SolidBrush(Theme.Muted);
        graphics.DrawLine(axisPen, left, y, right, y);
        const int ticks = 10;
        for (var i = 0; i <= ticks; i++)
        {
            var ratio = i / (float)ticks;
            var x = left + (right - left) * ratio;
            graphics.DrawLine(tickPen, x, y - 12f, x, y + 10f);
            var label = (duration * ratio).ToString("0.##");
            graphics.DrawString(label, Font, textBrush, x - 10f, 4f);
        }
        foreach (var key in clip.Keyframes)
        {
            if (!float.IsFinite(key.Time)) continue;
            var ratio = Math.Clamp(key.Time / duration, 0f, 1f);
            var x = left + (right - left) * ratio;
            var marker = new[] { new PointF(x, y - 7f), new PointF(x + 6f, y), new PointF(x, y + 7f), new PointF(x - 6f, y) };
            graphics.FillPolygon(keyBrush, marker);
        }
        var playheadRatio = Math.Clamp(_timelinePlayhead / duration, 0f, 1f);
        var playheadX = left + (right - left) * playheadRatio;
        graphics.DrawLine(playheadPen, playheadX, 18f, playheadX, _animationTimeline.ClientSize.Height - 4f);
    }

    private void HandleKeyframeGridChanged()
    {
        var playhead = _timelinePlayhead;
        ReadKeyframesFromGrid();
        BindAnimation(); // Rebuild sorted rows so timeline markers and grid row indices stay aligned.
        if (_animations.SelectedItem is LicAnimationClipData clip)
        {
            var duration = float.IsFinite(clip.Duration) && clip.Duration > 0f ? clip.Duration : 1f;
            _timelinePlayhead = Math.Clamp(float.IsFinite(playhead) ? playhead : 0f, 0f, duration);
            _timelineReadout.Text = $"Playhead · {_timelinePlayhead:0.000}s / {duration:0.000}s";
        }
        _animationTimeline.Invalidate();
        MarkChanged();
    }

    private void ScrubAnimationTimeline(object? sender, MouseEventArgs e)
    {
        if (_animations.SelectedItem is not LicAnimationClipData clip) return;
        var duration = float.IsFinite(clip.Duration) && clip.Duration > 0f ? clip.Duration : 1f;
        const float left = 32f;
        var right = Math.Max(left + 1f, _animationTimeline.ClientSize.Width - 16f);
        var ratio = Math.Clamp((e.X - left) / (right - left), 0f, 1f);
        _timelinePlayhead = ratio * duration;
        _timelineReadout.Text = $"Playhead · {_timelinePlayhead:0.000}s / {duration:0.000}s";
        _animationTimeline.Invalidate();

        // The grid is bound to a stable time-sorted projection of the key list.
        // Find the nearest marker in one pass; do not sort once for every candidate.
        var keys = clip.Keyframes.OrderBy(x => x.Time).ToArray();
        var nearest = AnimationEvaluator.FindNearestKeyframeIndex(keys, _timelinePlayhead);
        if (nearest >= 0 && nearest < _keyframes.Rows.Count)
        {
            _keyframes.ClearSelection();
            _keyframes.Rows[nearest].Selected = true;
            if (nearest >= 0 && nearest < _keyframes.RowCount)
                _keyframes.FirstDisplayedScrollingRowIndex = Math.Max(0, nearest);
        }
    }

    private void ReadKeyframesFromGrid()
    {
        if (_animations.SelectedItem is not LicAnimationClipData clip) return;
        var keys = new List<LicAnimationKeyframeData>();
        foreach (DataGridViewRow row in _keyframes.Rows)
        {
            if (row.IsNewRow) continue;
            var time = ReadFloat(row.Cells[0].Value, null);
            if (time is null) continue;
            keys.Add(new LicAnimationKeyframeData
            {
                Time = time.Value,
                X = ReadFloat(row.Cells[1].Value, null), Y = ReadFloat(row.Cells[2].Value, null),
                Rotation = ReadFloat(row.Cells[3].Value, null), ScaleX = ReadFloat(row.Cells[4].Value, null),
                ScaleY = ReadFloat(row.Cells[5].Value, null)
            });
        }
        clip.Keyframes = keys.OrderBy(x => x.Time).ToList();
        _animationTimeline.Invalidate();
    }

    private void BuildObjectDataTab<T>(string title, ListBox list, PropertyGrid properties, Action add, Action remove, List<T> items) where T : class
    {
        var page = new TabPage(title) { BackColor = BackColor, ForeColor = ForeColor };
        var split = ConfigureSplit(new SplitContainer { Dock = DockStyle.Fill, BackColor = BackColor }, 280);
        var left = new Panel { Dock = DockStyle.Fill };
        var buttons = CreateButtonBar();
        buttons.Controls.Add(MakeButton("Add", (_, _) => add()));
        var removeButton = MakeButton("Remove", (_, _) => remove());
        buttons.Controls.Add(removeButton);
        if (string.Equals(title, "Lighting", StringComparison.OrdinalIgnoreCase)) _removeLightButton = removeButton;
        else if (string.Equals(title, "Particles", StringComparison.OrdinalIgnoreCase)) _removeEmitterButton = removeButton;
        left.Controls.Add(list);
        left.Controls.Add(buttons);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(properties);
        page.Controls.Add(split);
        if (string.Equals(title, "Lighting", StringComparison.OrdinalIgnoreCase))
        {
            var ambientBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.Px(42), BackColor = Theme.Toolbar, Padding = new Padding(8, 5, 4, 3), WrapContents = false };
            ambientBar.Controls.Add(new Label { Text = "Ambient light", AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(2, 5, 8, 0) });
            ambientBar.Controls.Add(_ambientLight);
            ambientBar.Controls.Add(new Label { Text = "0 = dark · 1 = fully lit", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(8, 5, 2, 0) });
            page.Controls.Add(ambientBar);
            split.Dock = DockStyle.Fill;
        }
        _tabs.TabPages.Add(page);
        list.SelectedIndexChanged += (_, _) => { properties.SelectedObject = list.SelectedItem; UpdateActionButtons(); };
        properties.PropertyValueChanged += (_, _) => { list.Refresh(); MarkChanged(); };
        list.Tag = items;
    }

    private void AddLight()
    {
        if (_project.EditorData!.Lights.Count >= 10_000)
        {
            MessageBox.Show(this, "This project reached the 10,000 light limit.", "Lighting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var scene = _project.Scenes.FirstOrDefault();
        var light = new LicLightData { Name = UniqueName(_project.EditorData!.Lights.Select(x => x.Name), "Light"), Scene = scene?.Name ?? "", X = 32, Y = 32 };
        _project.EditorData.Lights.Add(light); RefreshLists(); _lights.SelectedItem = light; MarkChanged();
    }
    private void RemoveLight() { if (_lights.SelectedItem is LicLightData item) { _project.EditorData!.Lights.Remove(item); RefreshLists(); MarkChanged(); } }
    private void AddEmitter()
    {
        if (_project.EditorData!.ParticleEmitters.Count >= 10_000)
        {
            MessageBox.Show(this, "This project reached the 10,000 particle-emitter limit.", "Particles", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var scene = _project.Scenes.FirstOrDefault();
        var emitter = new LicParticleEmitterData { Name = UniqueName(_project.EditorData!.ParticleEmitters.Select(x => x.Name), "Emitter"), Scene = scene?.Name ?? "", X = 48, Y = 48 };
        _project.EditorData.ParticleEmitters.Add(emitter); RefreshLists(); _emitters.SelectedItem = emitter; MarkChanged();
    }
    private void RemoveEmitter() { if (_emitters.SelectedItem is LicParticleEmitterData item) { _project.EditorData!.ParticleEmitters.Remove(item); RefreshLists(); MarkChanged(); } }

    private void BuildTileMapTab()
    {
        var page = new TabPage("TileMap") { BackColor = BackColor, ForeColor = ForeColor };
        var split = ConfigureSplit(new SplitContainer { Dock = DockStyle.Fill, BackColor = BackColor }, 250);
        var left = new Panel { Dock = DockStyle.Fill };
        var buttons = CreateButtonBar();
        buttons.Controls.Add(MakeButton("New map", (_, _) => AddTileMap()));
        _removeTileMapButton = MakeButton("Remove", (_, _) => RemoveTileMap());
        buttons.Controls.Add(_removeTileMapButton);
        left.Controls.Add(_tileMaps); left.Controls.Add(buttons);
        var right = new Panel { Dock = DockStyle.Fill };
        var tileTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.Px(68), WrapContents = true, BackColor = Theme.Toolbar, Padding = new Padding(4, 2, 4, 2) };
        tileTools.Controls.Add(new Label { Text = "Brush index", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 7, 2, 0) });
        tileTools.Controls.Add(_tileBrushIndex);
        tileTools.Controls.Add(_tilePaintMode);
        _fillTileMapButton = MakeButton("Fill map", (_, _) => FillTileMap());
        _floodFillButton = MakeButton("Flood fill", (_, _) => FloodFillTileMap());
        _generateCaveButton = MakeButton("Generate cave", (_, _) => GenerateTileMapCave());
        tileTools.Controls.AddRange([_fillTileMapButton, _floodFillButton, _generateCaveButton]);
        right.Controls.Add(_tileGrid); right.Controls.Add(_tileMapHint); right.Controls.Add(_tileMapProperties); right.Controls.Add(tileTools);
        split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right);
        page.Controls.Add(split); _tabs.TabPages.Add(page);
        _tileMaps.SelectedIndexChanged += (_, _) => { BindTileMap(); UpdateActionButtons(); };
        _tileBrushIndex.ValueChanged += (_, _) => UpdateActionButtons();
        _tileGrid.CurrentCellChanged += (_, _) => UpdateActionButtons();
        _tilePaintMode.CheckedChanged += (_, _) => UpdateActionButtons();
        _tileMapProperties.PropertyValueChanged += (_, _) => HandleTileMapPropertiesChanged();
        _tileGrid.CellEndEdit += (_, e) => WriteTileCell(e.RowIndex, e.ColumnIndex);
        _tileGrid.CellFormatting += (_, e) => FormatTileCell(e);
        _tileGrid.CellMouseClick += (_, e) =>
        {
            if (!_tilePaintMode.Checked || e.RowIndex < 0 || e.ColumnIndex < 0 || _tileMaps.SelectedItem is not LicTileMapData map) return;
            var brush = (int)_tileBrushIndex.Value;
            if (brush >= map.Palette.Count) return;
            map.SetCell(e.ColumnIndex, e.RowIndex, brush);
            _tileGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = brush;
            MarkChanged();
        };
    }

    private void AddTileMap()
    {
        if (_project.EditorData!.TileMaps.Count >= 64)
        {
            MessageBox.Show(this, "This project reached the 64 tile-map limit.", "TileMap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var scene = _project.Scenes.FirstOrDefault();
        var map = new LicTileMapData { Name = UniqueName(_project.EditorData!.TileMaps.Select(x => x.Name), "TileMap"), Scene = scene?.Name ?? "", Width = 20, Height = 12, TileSize = 16 };
        map.EnsureCellStorage();
        _project.EditorData.TileMaps.Add(map); RefreshLists(); _tileMaps.SelectedItem = map; MarkChanged();
    }
    private void RemoveTileMap() { if (_tileMaps.SelectedItem is LicTileMapData item) { _project.EditorData!.TileMaps.Remove(item); RefreshLists(); MarkChanged(); } }
    private void BindTileMap()
    {
        _tileMapProperties.SelectedObject = _tileMaps.SelectedItem;
        if (_tileMaps.SelectedItem is LicTileMapData map)
        {
            var safeWidth = Math.Clamp(map.Width, 1, 512);
            var safeHeight = Math.Clamp(map.Height, 1, 512);
            if (safeWidth != map.Width || safeHeight != map.Height) map.Resize(safeWidth, safeHeight);
            map.TileSize = Math.Clamp(map.TileSize, 1, 256);
            map.EnsureCellStorage();
            _boundTileWidth = map.Width;
            _boundTileHeight = map.Height;
            _tileBrushIndex.Maximum = Math.Max(0, map.Palette.Count - 1);
        }
        else
        {
            _boundTileWidth = 0;
            _boundTileHeight = 0;
            _tileBrushIndex.Maximum = 0;
            _tileBrushIndex.Value = 0;
        }
        BindTileMapGrid();
        UpdateActionButtons();
    }

    private void HandleTileMapPropertiesChanged()
    {
        if (_tileMaps.SelectedItem is not LicTileMapData map) return;
        var requestedWidth = Math.Clamp(map.Width, 1, 512);
        var requestedHeight = Math.Clamp(map.Height, 1, 512);
        var requestedTileSize = Math.Clamp(map.TileSize, 1, 256);

        if (_boundTileWidth > 0 && _boundTileHeight > 0 &&
            (requestedWidth != _boundTileWidth || requestedHeight != _boundTileHeight))
        {
            // PropertyGrid has already assigned the new width. Restore the old stride
            // before resizing so each row retains its original tile coordinates.
            map.Width = _boundTileWidth;
            map.Height = _boundTileHeight;
            map.Resize(requestedWidth, requestedHeight);
        }
        else
        {
            map.Width = requestedWidth;
            map.Height = requestedHeight;
            map.EnsureCellStorage();
        }
        map.TileSize = requestedTileSize;
        _boundTileWidth = map.Width;
        _boundTileHeight = map.Height;
        BindTileMapGrid();
        MarkChanged();
    }

    private void BindTileMapGrid()
    {
        _buildingGrid = true;
        try
        {
            _tileGrid.Columns.Clear(); _tileGrid.Rows.Clear();
            if (_tileMaps.SelectedItem is not LicTileMapData map) return;
            map.EnsureCellStorage();
            var visibleWidth = Math.Min(map.Width, 128);
            var visibleHeight = Math.Min(map.Height, 128);
            _tileMapHint.Text = map.Width > visibleWidth || map.Height > visibleHeight
                ? $"Showing {visibleWidth}×{visibleHeight} cells from top-left (map: {map.Width}×{map.Height}). Runtime supports the full map."
                : $"Grid: {map.Width}×{map.Height} · Enter a palette index (0 = empty).";
            for (var x = 0; x < visibleWidth; x++) _tileGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "C" + x, HeaderText = x.ToString(), Width = Theme.Px(36), SortMode = DataGridViewColumnSortMode.NotSortable });
            for (var y = 0; y < visibleHeight; y++)
            {
                var row = new object[visibleWidth];
                for (var x = 0; x < visibleWidth; x++) row[x] = map.GetCell(x, y);
                _tileGrid.Rows.Add(row);
                _tileGrid.Rows[y].HeaderCell.Value = y.ToString();
            }
        }
        finally { _buildingGrid = false; }
        UpdateActionButtons();
    }

    private void FillTileMap()
    {
        if (_tileMaps.SelectedItem is not LicTileMapData map) return;
        var brush = (int)_tileBrushIndex.Value;
        if (brush >= map.Palette.Count) return;
        map.Fill(brush);
        BindTileMapGrid();
        MarkChanged();
    }

    private void GenerateTileMapCave()
    {
        if (_tileMaps.SelectedItem is not LicTileMapData map) return;
        if (map.Palette.Count < 2)
        {
            MessageBox.Show(this, "Add a non-empty wall color to the tile palette first.", "TileMap", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        map.GenerateCave(1, .46, 5, Environment.TickCount);
        BindTileMapGrid();
        MarkChanged();
    }

    private void FloodFillTileMap()
    {
        if (_tileMaps.SelectedItem is not LicTileMapData map || _tileGrid.CurrentCell is not { } cell) return;
        var brush = (int)_tileBrushIndex.Value;
        if (brush >= map.Palette.Count) return;
        var changed = map.FloodFill(cell.ColumnIndex, cell.RowIndex, brush);
        if (changed > 0)
        {
            BindTileMapGrid();
            MarkChanged();
        }
    }

    private void FormatTileCell(DataGridViewCellFormattingEventArgs e)
    {
        if (_tileMaps.SelectedItem is not LicTileMapData map || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!int.TryParse(Convert.ToString(e.Value), out var index) || index < 0 || index >= map.Palette.Count) return;
        var color = PixelEngine.Graphics.ColorParser.Parse(map.Palette[index], Microsoft.Xna.Framework.Color.Transparent);
        var alpha = color.A / 255d;
        var baseColor = Color.FromArgb(32, 36, 44);
        var background = Color.FromArgb(
            (int)Math.Round(color.R * alpha + baseColor.R * (1d - alpha)),
            (int)Math.Round(color.G * alpha + baseColor.G * (1d - alpha)),
            (int)Math.Round(color.B * alpha + baseColor.B * (1d - alpha)));
        var cellStyle = e.CellStyle ?? new DataGridViewCellStyle();
        e.CellStyle = cellStyle;
        cellStyle.BackColor = background;
        var luminance = .2126 * background.R + .7152 * background.G + .0722 * background.B;
        cellStyle.ForeColor = luminance < 128 ? Color.White : Color.Black;
        cellStyle.SelectionBackColor = ControlPaint.Dark(background);
        e.FormattingApplied = true;
    }

    private void WriteTileCell(int row, int column)
    {
        if (_buildingGrid || _tileMaps.SelectedItem is not LicTileMapData map || row < 0 || column < 0) return;
        if (!int.TryParse(Convert.ToString(_tileGrid.Rows[row].Cells[column].Value), out var value))
        {
            _tileGrid.Rows[row].Cells[column].Value = map.GetCell(column, row);
            return;
        }
        if (value < 0 || value >= map.Palette.Count)
        {
            _tileGrid.Rows[row].Cells[column].Value = map.GetCell(column, row);
            return;
        }
        map.SetCell(column, row, value);
        MarkChanged();
        UpdateActionButtons();
    }

    private void UpdateActionButtons()
    {
        var hasAnimation = _animations.SelectedItem is LicAnimationClipData;
        if (_removeAnimationButton is not null) _removeAnimationButton.Enabled = hasAnimation;
        if (_addKeyframeButton is not null) _addKeyframeButton.Enabled = hasAnimation;
        if (_removeLightButton is not null) _removeLightButton.Enabled = _lights.SelectedItem is LicLightData;
        if (_removeEmitterButton is not null) _removeEmitterButton.Enabled = _emitters.SelectedItem is LicParticleEmitterData;

        var map = _tileMaps.SelectedItem as LicTileMapData;
        var hasMap = map is not null;
        var validBrush = map is not null && map.Palette.Count > 0 && (int)_tileBrushIndex.Value < map.Palette.Count;
        if (_removeTileMapButton is not null) _removeTileMapButton.Enabled = hasMap;
        if (_fillTileMapButton is not null) _fillTileMapButton.Enabled = validBrush;
        if (_floodFillButton is not null) _floodFillButton.Enabled = validBrush && _tileGrid.CurrentCell is not null;
        if (_generateCaveButton is not null) _generateCaveButton.Enabled = map is not null && map.Palette.Count > 1;
        _tileBrushIndex.Enabled = hasMap && map!.Palette.Count > 0;
        _tilePaintMode.Enabled = validBrush;
    }

    private void BuildProfilerTab()
    {
        var page = new TabPage("Profiler") { BackColor = BackColor, ForeColor = ForeColor };
        _profilerSummary.ForeColor = Theme.Text;
        _profilerSummary.Text = "Collecting editor metrics…";
        page.Controls.Add(_profilerSummary);
        _tabs.TabPages.Add(page);
        RefreshProfiler();
    }

    private void RefreshProfiler()
    {
        if (IsDisposed) return;
        try
        {
            using var process = Process.GetCurrentProcess();
            var objectCount = _project.Scenes.Sum(x => x.Objects.Count);
            var componentCount = _project.Scenes.Sum(x => x.Objects.Sum(o => o.Components.Count));
            var bytes = _project.Assets.Sum(x => (long)(x.Base64?.Length ?? 0) * 3 / 4);
            var text = new StringBuilder();
            text.AppendLine("PIXELENGINE EDITOR PROFILER");
            text.AppendLine("────────────────────────────────────");
            text.AppendLine($"Editor working set : {process.WorkingSet64 / 1024d / 1024d:0.0} MB");
            text.AppendLine($"Managed heap       : {GC.GetTotalMemory(false) / 1024d / 1024d:0.0} MB");
            text.AppendLine($"Process threads    : {process.Threads.Count}");
            text.AppendLine($"Scenes             : {_project.Scenes.Count}");
            text.AppendLine($"Scene objects      : {objectCount}");
            text.AppendLine($"Authoring comps    : {componentCount}");
            text.AppendLine($"Assets payload     : {bytes / 1024d:0.0} KB");
            text.AppendLine($"Animations         : {_project.EditorData?.Animations.Count ?? 0}");
            text.AppendLine($"2D lights          : {_project.EditorData?.Lights.Count ?? 0}");
            text.AppendLine($"Particle emitters  : {_project.EditorData?.ParticleEmitters.Count ?? 0}");
            text.AppendLine($"Tile maps          : {_project.EditorData?.TileMaps.Count ?? 0}");
            text.AppendLine();
            text.AppendLine();
            text.AppendLine("MONOGAME PLAYER RUNTIME");
            text.AppendLine("────────────────────────────────────");
            if (RuntimeProfilerStore.TryReadLatest(TimeSpan.FromSeconds(8), out var runtime) && runtime is not null)
            {
                var sample = runtime.Snapshot;
                text.AppendLine($"Runtime status     : Running · {(DateTime.UtcNow - runtime.TimestampUtc):g} ago");
                text.AppendLine($"Runtime FPS        : {sample.FramesPerSecond:0.0}");
                text.AppendLine($"Frame / Update     : {sample.FrameMilliseconds:0.00} / {sample.UpdateMilliseconds:0.00} ms");
                text.AppendLine($"Draw time          : {sample.DrawMilliseconds:0.00} ms");
                text.AppendLine($"Runtime objects    : {sample.ObjectCount}");
                text.AppendLine($"Runtime components : {sample.ComponentCount}");
                text.AppendLine($"Runtime memory     : {sample.ManagedMemoryBytes / 1024d / 1024d:0.0} MB");
                text.AppendLine($"Runtime frames     : {sample.FrameCount}");
            }
            else
            {
                text.AppendLine("Runtime status     : No active player telemetry");
                text.AppendLine("Start a game with F5. Samples refresh while the player is running.");
            }
            _profilerSummary.Text = text.ToString();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { /* Process may exit while the profiler reads process metadata. */ }
    }

    private void RefreshLists()
    {
        RefreshList(_animations, _project.EditorData!.Animations);
        RefreshList(_lights, _project.EditorData.Lights);
        RefreshList(_emitters, _project.EditorData.ParticleEmitters);
        RefreshList(_tileMaps, _project.EditorData.TileMaps);
    }

    private static void RefreshList<T>(ListBox list, IEnumerable<T> items)
    {
        var selected = list.SelectedItem;
        list.BeginUpdate(); list.Items.Clear();
        foreach (var item in items)
            if (item is not null) list.Items.Add(item);
        if (selected is not null)
            for (var i = 0; i < list.Items.Count; i++) if (ReferenceEquals(list.Items[i], selected)) { list.SelectedIndex = i; break; }
        list.EndUpdate();
    }

    private void MarkChanged() => ProjectChanged?.Invoke(this, EventArgs.Empty);
    private static float? ReadFloat(object? value, float? fallback) => float.TryParse(Convert.ToString(value), out var number) && float.IsFinite(number) ? number : fallback;
    private static int ReadInt(object? value, int fallback) => int.TryParse(Convert.ToString(value), out var number) ? number : fallback;
    private static string UniqueName(IEnumerable<string> names, string basis)
    {
        var existing = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(basis)) return basis;
        for (var i = 1; ; i++) { var value = $"{basis} {i}"; if (!existing.Contains(value)) return value; }
    }

    /// <summary>
    /// Applies DPI-scaled panel minimums and defers the splitter distance until the control has a
    /// handle; assigning it in the initializer lets WinForms clamp it against the 1000×600 design
    /// size, which clipped panels on high-DPI displays.
    /// </summary>
    private static SplitContainer ConfigureSplit(SplitContainer split, int designDistance)
    {
        split.Panel1MinSize = Theme.Px(170);
        split.Panel2MinSize = Theme.Px(220);
        split.SplitterWidth = Theme.Px(6);
        split.HandleCreated += (_, _) =>
        {
            var desired = Theme.Px(designDistance);
            var maximum = split.Height - split.Panel2MinSize - split.SplitterWidth;
            if (maximum > split.Panel1MinSize) split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, maximum);
        };
        return split;
    }

    private static FlowLayoutPanel CreateButtonBar() => new() { Dock = DockStyle.Top, Height = Theme.Px(40), WrapContents = false, AutoScroll = true, BackColor = Theme.Toolbar, Padding = new Padding(4) };
    private static Button MakeButton(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Theme.PanelAlt, ForeColor = Theme.Text, Margin = new Padding(3) };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.Click += click;
        return button;
    }
}
