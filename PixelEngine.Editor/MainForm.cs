using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PixelEngine.Scripting;
using PixelEngine.Serialization;

namespace PixelEngine.Editor;

public sealed class MainForm : Form
{
    private readonly TreeView _hierarchy = new();
    private readonly PropertyGrid _inspector = new();
    private readonly SceneCanvas _sceneCanvas = new();
    private readonly ListBox _output = new();
    private readonly CodeEditorControl _codeEditor = new();
    private readonly AssetBrowserControl _assetBrowser = new();
    private readonly PrefabBrowserControl _prefabBrowser = new();
    private readonly ComponentManagerControl _componentManager = new();
    private readonly TabControl _centerTabs = new();
    private readonly TabControl _scriptTabs = new();
    private readonly TabControl _inspectorTabs = new();
    private readonly TabControl _explorerTabs = new();
    private readonly TabControl _bottomTabs = new();
    private TabPage? _sceneDocumentTab;
    private readonly ListView _problemList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.None, MultiSelect = false };
    private readonly TextBox _projectSearchBox = new() { BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Search scripts, objects, assets…" };
    private readonly ListView _projectSearchResults = new() { View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.None, MultiSelect = false };
    private readonly EditorTerminalControl _terminal = new();
    private readonly SplitContainer _rootSplit = new();
    private readonly SplitContainer _sideSplit = new();
    private readonly SplitContainer _centerSplit = new();
    private readonly SplitContainer _leftDockSplit = new();
    private readonly List<ToolStripButton> _workspaceModeButtons = new();
    private readonly ListView _signalsList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.None };
    private readonly ListView _scriptFileList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.None, MultiSelect = false };
    private readonly ToolStripTextBox _sceneTreeSearch = new() { AutoSize = false, Width = 126, ToolTipText = "Find a scene node; press Enter to jump to the next match" };
    private readonly Panel _gameHost = new() { BackColor = Theme.Viewport };
    private readonly Label _gamePlaceholder = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.Muted,
        Font = new Font("Segoe UI", 11f),
        Text = "GAME PREVIEW\n\nThe game runs in its own resizable window.\nPress F5 to launch · Shift+F5 to stop."
    };
    private readonly ComboBox _outputFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
    private readonly TextBox _outputSearch = new() { Width = 190, PlaceholderText = "Filter output…" };
    private readonly List<EditorLogEntry> _outputEntries = new();
    private readonly System.Windows.Forms.Timer _workerLogFlushTimer = new() { Interval = 1200 };
    private readonly System.Windows.Forms.Timer _layoutSaveTimer = new() { Interval = 500 };
    private string? _lastWorkerLogMessage;
    private int _lastWorkerLogRepeatCount;
    private DateTime _lastWorkerLogAt;
    private bool _refreshingOutput;
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight, Text = "Ready" };
    private ToolStripButton? _undoButton;
    private ToolStripButton? _redoButton;
    private ToolStripButton? _saveButton;
    private ToolStripButton? _playButton;
    private ToolStripButton? _stopButton;
    private ToolStripButton? _checkScriptsButton;
    private ToolStripButton? _buildButton;
    private ToolStripButton? _newSceneButton;
    private ToolStripButton? _newScriptButton;
    private ToolStripButton? _fitSceneButton;
    private ToolStripDropDownButton? _createObjectButton;
    private Button? _gameRunButton;
    private Button? _gameStopButton;
    private Button? _clearOutputButton;
    private Button? _copyOutputButton;
    private Button? _exportOutputButton;
    private readonly ToolStripComboBox _sceneSelector = new() { AutoSize = false, Width = 210, DropDownStyle = ComboBoxStyle.DropDownList, ToolTipText = "Active scene" };
    private readonly EditorHistory _history = new();
    private readonly System.Windows.Forms.Timer _scriptHistoryTimer = new() { Interval = 650 };
    private LicScriptData? _selectedScript;
    private LicProjectData? _project;
    private string? _projectPath;
    private Process? _previewProcess;
    private readonly List<ToolStripButton> _sceneToolButtons = new();
    private bool _suppressTreeEvents;
    private bool _updatingSceneSelector;
    private bool _loadingScriptText;
    private bool _updatingScriptTabs;
    private bool _refreshingSearch;
    private bool _isDirty;

    public MainForm(string? initialProject)
    {
        Text = "PixelEngine 2.0 — Studio";
        MinimumSize = new Size(1180, 720);
        Size = new Size(1580, 980);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        BuildShell();
        WireEvents();
        if (!string.IsNullOrWhiteSpace(initialProject) && File.Exists(initialProject)) OpenProject(initialProject);
        else OpenRecentDefault();
        if (_project is null)
        {
            _assetBrowser.Bind(null);
            _prefabBrowser.Bind(null);
        }
        UpdateProjectActionButtons();
        UpdateHistoryButtons();
    }

    private void BuildShell()
    {
        var menu = BuildMenu();
        var workspaceBar = BuildWorkspaceBar();
        var toolbar = BuildToolbar();
        var status = new StatusStrip { BackColor = Theme.Panel, ForeColor = Theme.Muted, SizingGrip = false, Dock = DockStyle.Fill };
        status.Renderer = StudioToolStripStyle.CreateRenderer();
        status.Items.Add(new ToolStripStatusLabel("● PixelEngine Studio") { ForeColor = Theme.Accent });
        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        status.Items.Add(new ToolStripStatusLabel("MonoGame · LIC · Lua") { ForeColor = Theme.Muted });
        status.Items.Add(_status);

        ConfigureTreeAndPropertyGrid();
        // Configure the nested vertical dock before parenting controls into its panels.
        ConfigureSplit(_leftDockSplit, Orientation.Horizontal, new Size(285, 555), 330, 210, 155);
        var explorerPanel = CreateExplorerPanel();
        var centerPanel = CreateCenterPanel();
        var inspectorPanel = CreateInspectorPanel();
        var bottomPanel = CreateBottomPanel();

        // Godot-style dock layout: Scene + FileSystem on the left, viewport in the
        // center, Inspector/Node/Signals on the right and Output/Problems/Terminal below.
        ConfigureSplit(_rootSplit, Orientation.Horizontal, new Size(1500, 760), 555, 420, 145);
        ConfigureSplit(_sideSplit, Orientation.Vertical, new Size(1500, 555), 285, 220, 560);
        ConfigureSplit(_centerSplit, Orientation.Vertical, new Size(1210, 555), 900, 480, 260);

        _sideSplit.Panel1.Controls.Add(explorerPanel);
        _sideSplit.Panel2.Controls.Add(_centerSplit);
        _centerSplit.Panel1.Controls.Add(centerPanel);
        _centerSplit.Panel2.Controls.Add(inspectorPanel);
        _rootSplit.Panel1.Controls.Add(_sideSplit);
        _rootSplit.Panel2.Controls.Add(bottomPanel);

        // A TableLayoutPanel gives the top chrome a deterministic order. This avoids
        // the toolbar/menu docking inversion that made earlier layouts look improvised.
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));  // menu
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));  // workspace modes
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));  // main toolbar
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // docks
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 23f));  // status

        menu.Dock = DockStyle.Fill;
        menu.AutoSize = false;
        menu.Height = 28;
        menu.Padding = new Padding(8, 2, 8, 2);
        MainMenuStrip = menu;
        workspaceBar.Dock = DockStyle.Fill;
        toolbar.Dock = DockStyle.Fill;
        toolbar.Height = 40;
        _rootSplit.Dock = DockStyle.Fill;

        shell.Controls.Add(menu, 0, 0);
        shell.Controls.Add(workspaceBar, 0, 1);
        shell.Controls.Add(toolbar, 0, 2);
        shell.Controls.Add(_rootSplit, 0, 3);
        shell.Controls.Add(status, 0, 4);
        Controls.Add(shell);

        _workerLogFlushTimer.Tick += (_, _) => FlushRepeatedWorkerLog();
        _layoutSaveTimer.Tick += (_, _) => { _layoutSaveTimer.Stop(); SaveDockLayout(); };
        _rootSplit.SplitterMoved += (_, _) => ScheduleDockLayoutSave();
        _sideSplit.SplitterMoved += (_, _) => ScheduleDockLayoutSave();
        _centerSplit.SplitterMoved += (_, _) => ScheduleDockLayoutSave();
        _leftDockSplit.SplitterMoved += (_, _) => ScheduleDockLayoutSave();
        Shown += (_, _) =>
        {
            ApplyInitialSplitDistances();
            _sceneCanvas.FitScene();
            UpdateWorkspaceModeButtons();
        };
    }

    private Control CreateExplorerPanel()
    {
        _explorerTabs.Dock = DockStyle.Fill;
        _explorerTabs.Appearance = TabAppearance.FlatButtons;
        _explorerTabs.SizeMode = TabSizeMode.Fixed;
        _explorerTabs.ItemSize = new Size(58, 27);
        StyleTabs(_explorerTabs, 58);

        var sceneTab = new TabPage("Scene") { BackColor = Theme.Panel, Padding = Padding.Empty };
        sceneTab.Controls.Add(CreateSceneTreePanel());
        var scriptsTab = new TabPage("Scripts") { BackColor = Theme.Panel, Padding = Padding.Empty };
        scriptsTab.Controls.Add(CreateScriptsPanel());
        var prefabsTab = new TabPage("Prefabs") { BackColor = Theme.Panel, Padding = Padding.Empty };
        prefabsTab.Controls.Add(_prefabBrowser);
        var searchTab = new TabPage("Search") { BackColor = Theme.Panel, Padding = Padding.Empty };
        searchTab.Controls.Add(CreateProjectSearchPanel());
        _explorerTabs.TabPages.AddRange([sceneTab, scriptsTab, prefabsTab, searchTab]);

        var fileSystemPanel = WrapPanel("FILE SYSTEM", _assetBrowser);
        _leftDockSplit.Panel1.Controls.Add(_explorerTabs);
        _leftDockSplit.Panel2.Controls.Add(fileSystemPanel);
        return _leftDockSplit;
    }

    private Control CreateScriptsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 32, BackColor = Theme.Toolbar,
            ForeColor = Theme.Text, GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2), LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        };
        toolbar.Renderer = StudioToolStripStyle.CreateRenderer();
        var create = new ToolStripButton("＋ Script") { ForeColor = Theme.Text, ToolTipText = "Create a Lua script" };
        create.Click += (_, _) => AddScript();
        toolbar.Items.Add(create);
        toolbar.Items.Add(new ToolStripLabel("PROJECT SCRIPTS") { ForeColor = Theme.Muted });

        _scriptFileList.Dock = DockStyle.Fill;
        _scriptFileList.BackColor = Theme.Viewport;
        _scriptFileList.ForeColor = Theme.Text;
        _scriptFileList.Columns.Add("Name", 150);
        _scriptFileList.Columns.Add("Language", 68);
        _scriptFileList.ItemActivate += (_, _) => OpenSelectedScriptFile();
        panel.Controls.Add(_scriptFileList);
        panel.Controls.Add(toolbar);
        return panel;
    }

    private void RefreshScriptFileList()
    {
        if (_scriptFileList.IsDisposed) return;
        _scriptFileList.BeginUpdate();
        _scriptFileList.Items.Clear();
        foreach (var script in _project?.Scripts ?? Enumerable.Empty<LicScriptData>())
        {
            var row = new ListViewItem(script.Name) { Tag = script };
            row.SubItems.Add(script.Language);
            _scriptFileList.Items.Add(row);
        }
        _scriptFileList.EndUpdate();
    }

    private void OpenSelectedScriptFile()
    {
        if (_scriptFileList.SelectedItems.Count == 0 || _scriptFileList.SelectedItems[0].Tag is not LicScriptData script) return;
        OpenScript(script);
        _centerTabs.SelectedIndex = 2;
    }

    private Control CreateSceneTreePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 32,
            BackColor = Theme.Toolbar,
            ForeColor = Theme.Text,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2),
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
        };
        toolbar.Renderer = StudioToolStripStyle.CreateRenderer();

        var add = new ToolStripDropDownButton("+")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Add a node to the active scene",
            Font = new Font("Segoe UI Semibold", 11f),
            Padding = new Padding(7, 1, 7, 1)
        };
        foreach (var kind in new[] { "rect", "circle", "text", "poly", "button" })
            add.DropDownItems.Add(kind.ToUpperInvariant(), null, (_, _) => AddObject(kind));
        toolbar.Items.Add(add);
        toolbar.Items.Add(new ToolStripSeparator());
        var expand = new ToolStripButton("Expand") { ForeColor = Theme.Text, ToolTipText = "Expand all scene nodes" };
        expand.Click += (_, _) => _hierarchy.ExpandAll();
        var collapse = new ToolStripButton("Collapse") { ForeColor = Theme.Text, ToolTipText = "Collapse all scene nodes" };
        collapse.Click += (_, _) => _hierarchy.CollapseAll();
        toolbar.Items.Add(expand);
        toolbar.Items.Add(collapse);
        toolbar.Items.Add(new ToolStripSeparator());
        _sceneTreeSearch.BackColor = Theme.Viewport;
        _sceneTreeSearch.ForeColor = Theme.Text;
        _sceneTreeSearch.Font = new Font("Segoe UI", 8.5f);
        _sceneTreeSearch.TextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SelectNextHierarchyMatch(_sceneTreeSearch.Text);
        };
        toolbar.Items.Add(_sceneTreeSearch);

        _hierarchy.Dock = DockStyle.Fill;
        panel.Controls.Add(_hierarchy);
        panel.Controls.Add(toolbar);
        return panel;
    }

    private void SelectNextHierarchyMatch(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        var nodes = new List<TreeNode>();
        void Collect(TreeNodeCollection collection)
        {
            foreach (TreeNode node in collection)
            {
                nodes.Add(node);
                Collect(node.Nodes);
            }
        }
        Collect(_hierarchy.Nodes);
        if (nodes.Count == 0) return;
        var start = _hierarchy.SelectedNode is null ? 0 : (nodes.IndexOf(_hierarchy.SelectedNode) + 1) % nodes.Count;
        for (var offset = 0; offset < nodes.Count; offset++)
        {
            var node = nodes[(start + offset) % nodes.Count];
            if (!node.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.Expand();
            _hierarchy.SelectedNode = node;
            node.EnsureVisible();
            _hierarchy.Focus();
            SetStatus("Scene node: " + node.Text);
            return;
        }
        SetStatus("No scene node matches: " + query);
    }

    private void OpenExplorerTab(int index)
    {
        if (_sideSplit.Panel1Collapsed) _sideSplit.Panel1Collapsed = false;
        switch (index)
        {
            case 0: // Scene tree
                _explorerTabs.SelectedIndex = 0;
                _hierarchy.Focus();
                break;
            case 1: // FileSystem / assets
                _leftDockSplit.Panel2Collapsed = false;
                _assetBrowser.Focus();
                ScheduleDockLayoutSave();
                break;
            case 2: // Prefabs
                _explorerTabs.SelectedIndex = 2;
                _prefabBrowser.Focus();
                break;
            case 3: // Search
                _explorerTabs.SelectedIndex = 3;
                _projectSearchBox.Focus();
                _projectSearchBox.SelectAll();
                break;
            case 4: // Scripts
                _explorerTabs.SelectedIndex = 1;
                _scriptFileList.Focus();
                break;
            default:
                return;
        }
    }

    private void FocusProjectSearch()
    {
        OpenExplorerTab(3);
        _projectSearchBox.Focus();
        _projectSearchBox.SelectAll();
    }

    private void OpenBottomTab(int index)
    {
        if (_rootSplit.Panel2Collapsed) _rootSplit.Panel2Collapsed = false;
        if (index >= 0 && index < _bottomTabs.TabPages.Count)
            _bottomTabs.SelectedIndex = index;
        if (index == 2) _terminal.FocusCommandInput();
        else _bottomTabs.Focus();
    }

    private Control CreateProjectSearchPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(6) };
        var heading = new Label { Dock = DockStyle.Top, Height = 25, Text = "SEARCH", ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 8.5f), TextAlign = ContentAlignment.MiddleLeft };
        _projectSearchBox.Dock = DockStyle.Top;
        _projectSearchBox.Height = 28;
        _projectSearchBox.BackColor = Theme.Viewport;
        _projectSearchBox.ForeColor = Theme.Text;
        _projectSearchBox.Font = new Font("Segoe UI", 9.5f);
        _projectSearchResults.Dock = DockStyle.Fill;
        _projectSearchResults.BackColor = Theme.Viewport;
        _projectSearchResults.ForeColor = Theme.Text;
        _projectSearchResults.Columns.Add("Name", 155);
        _projectSearchResults.Columns.Add("Type", 65);
        _projectSearchResults.Columns.Add("Location", 210);
        _projectSearchResults.DoubleClick += (_, _) => NavigateToSearchResult();
        _projectSearchBox.TextChanged += (_, _) => RefreshProjectSearchResults();
        panel.Controls.Add(_projectSearchResults);
        panel.Controls.Add(_projectSearchBox);
        panel.Controls.Add(heading);
        return panel;
    }

    private void RefreshProjectSearchResults()
    {
        if (_refreshingSearch || _projectSearchResults.IsDisposed) return;
        _refreshingSearch = true;
        try
        {
            _projectSearchResults.BeginUpdate();
            _projectSearchResults.Items.Clear();
            var query = _projectSearchBox.Text.Trim();
            if (_project is null || query.Length == 0)
            {
                _projectSearchResults.EndUpdate();
                return;
            }

            const int maximumResults = 500;
            void Add(string name, string kind, string location, object target, string? preview = null)
            {
                if (_projectSearchResults.Items.Count >= maximumResults) return;
                var item = new ListViewItem(name) { Tag = new EditorSearchHit(kind, target, preview ?? string.Empty) };
                item.SubItems.Add(kind);
                item.SubItems.Add(location);
                _projectSearchResults.Items.Add(item);
            }

            foreach (var script in _project.Scripts)
            {
                var nameMatch = script.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchingLine = nameMatch ? string.Empty : (script.Code ?? string.Empty).Split('\n').FirstOrDefault(line => line.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (nameMatch || matchingLine is not null)
                {
                    var location = nameMatch ? "Scripts" : "Scripts · " + (matchingLine ?? string.Empty).Trim();
                    Add(script.Name, "Script", location, script, matchingLine);
                }
            }
            foreach (var scene in _project.Scenes)
            {
                if (scene.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) Add(scene.Name, "Scene", "Scenes", scene);
                foreach (var obj in scene.Objects)
                {
                    if (obj.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(obj.Text) && obj.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(obj.Script) && obj.Script.Contains(query, StringComparison.OrdinalIgnoreCase)))
                        Add(obj.Name, "Object", scene.Name + " / " + obj.Kind, obj);
                }
            }
            foreach (var asset in _project.Assets)
                if (asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) Add(asset.Name, "Asset", "FileSystem / " + asset.Type.ToUpperInvariant(), asset);
            foreach (var prefab in _project.Prefabs)
                if (prefab.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) Add(prefab.Name, "Prefab", "Prefabs", prefab);

            if (_projectSearchResults.Items.Count == 0)
            {
                var empty = new ListViewItem("No matches found") { ForeColor = Theme.Muted };
                empty.SubItems.Add(string.Empty);
                empty.SubItems.Add(string.Empty);
                _projectSearchResults.Items.Add(empty);
            }
            _projectSearchResults.EndUpdate();
        }
        finally { _refreshingSearch = false; }
    }

    private void NavigateToSearchResult()
    {
        if (_projectSearchResults.SelectedItems.Count == 0 || _projectSearchResults.SelectedItems[0].Tag is not EditorSearchHit hit) return;
        switch (hit.Target)
        {
            case LicScriptData script:
                OpenScript(script);
                _centerTabs.SelectedIndex = 2;
                SetStatus("Opened script: " + script.Name);
                break;
            case LicObjectData obj:
                RefreshProjectView(selectTag: obj);
                _centerTabs.SelectedIndex = 0;
                SetStatus("Selected object: " + obj.Name);
                break;
            case LicSceneData scene:
                RefreshProjectView(selectTag: scene);
                _centerTabs.SelectedIndex = 0;
                SetStatus("Selected scene: " + scene.Name);
                break;
            case LicAssetData asset:
                OpenExplorerTab(1);
                _assetBrowser.SelectAsset(asset);
                break;
            case LicPrefabData prefab:
                OpenExplorerTab(2);
                _prefabBrowser.SelectPrefab(prefab);
                break;
        }
    }

    private void ScheduleDockLayoutSave()
    {
        if (!IsHandleCreated || IsDisposed) return;
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
    }

    private void ResetDockLayout()
    {
        try
        {
            _rootSplit.Panel2Collapsed = false;
            _sideSplit.Panel1Collapsed = false;
            _leftDockSplit.Panel2Collapsed = false;
            _centerSplit.Panel2Collapsed = false;
            ApplyDefaultSplitDistances();
            SaveDockLayout();
            SetStatus("Dock layout reset");
        }
        catch (InvalidOperationException ex) { Log("Could not reset dock layout: " + ex.Message); }
    }

    private void ApplyDefaultSplitDistances()
    {
        // Godot-like proportions: compact Scene/FileSystem dock, wide viewport,
        // readable Inspector and a bottom dock sized for logs/debugging.
        SetSplitDistance(_rootSplit, (int)(_rootSplit.Height * .745));
        var hierarchyWidth = Math.Clamp((int)(_sideSplit.Width * .205), 235, 315);
        SetSplitDistance(_sideSplit, hierarchyWidth);
        var inspectorWidth = Math.Clamp((int)(_centerSplit.Width * .235), 275, 360);
        SetSplitDistance(_centerSplit, _centerSplit.Width - inspectorWidth - _centerSplit.SplitterWidth);
        SetSplitDistance(_leftDockSplit, (int)(_leftDockSplit.Height * .53));
    }

    private static string DockLayoutFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PixelEngine", "EditorLayout.v4.json");

    private void ApplyInitialSplitDistances()
    {
        try
        {
            ApplyDefaultSplitDistances();
            if (File.Exists(DockLayoutFile))
            {
                var saved = JsonSerializer.Deserialize<EditorDockLayout>(File.ReadAllText(DockLayoutFile));
                if (saved is not null)
                {
                    SetSplitDistance(_rootSplit, saved.RootDistance);
                    SetSplitDistance(_sideSplit, saved.HierarchyDistance);
                    SetSplitDistance(_centerSplit, saved.CenterDistance);
                    if (saved.FileSystemDistance > 0) SetSplitDistance(_leftDockSplit, saved.FileSystemDistance);
                    _sideSplit.Panel1Collapsed = saved.HierarchyHidden;
                    _leftDockSplit.Panel2Collapsed = saved.FileSystemHidden;
                    _centerSplit.Panel2Collapsed = saved.InspectorHidden;
                    _rootSplit.Panel2Collapsed = saved.BottomDockHidden;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Log("Could not restore dock layout; using defaults: " + ex.Message);
            ApplyDefaultSplitDistances();
        }
    }

    private void SaveDockLayout()
    {
        try
        {
            if (_rootSplit.IsDisposed || _sideSplit.IsDisposed || _centerSplit.IsDisposed || _leftDockSplit.IsDisposed ||
                _rootSplit.Height <= 0 || _sideSplit.Width <= 0 || _centerSplit.Width <= 0 || _leftDockSplit.Height <= 0) return;
            var folder = Path.GetDirectoryName(DockLayoutFile)!;
            Directory.CreateDirectory(folder);
            var layout = new EditorDockLayout
            {
                RootDistance = _rootSplit.SplitterDistance,
                HierarchyDistance = _sideSplit.SplitterDistance,
                CenterDistance = _centerSplit.SplitterDistance,
                FileSystemDistance = _leftDockSplit.SplitterDistance,
                HierarchyHidden = _sideSplit.Panel1Collapsed,
                FileSystemHidden = _leftDockSplit.Panel2Collapsed,
                InspectorHidden = _centerSplit.Panel2Collapsed,
                BottomDockHidden = _rootSplit.Panel2Collapsed
            };
            File.WriteAllText(DockLayoutFile, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Could not save dock layout: " + ex.Message);
        }
    }

    private static void ConfigureSplit(SplitContainer split, Orientation orientation, Size initialSize,
        int distance, int panel1Minimum, int panel2Minimum)
    {
        split.Dock = DockStyle.Fill;
        split.Orientation = orientation;
        split.SplitterWidth = 6;
        split.BorderStyle = BorderStyle.None;
        split.Size = initialSize;
        split.Panel1MinSize = panel1Minimum;
        split.Panel2MinSize = panel2Minimum;
        var extent = orientation == Orientation.Vertical ? initialSize.Width : initialSize.Height;
        split.SplitterDistance = Math.Clamp(distance, panel1Minimum, Math.Max(panel1Minimum, extent - panel2Minimum - split.SplitterWidth));
        split.BackColor = Theme.Window;
    }

    private static void SetSplitDistance(SplitContainer split, int desired)
    {
        var extent = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        var usable = extent - split.SplitterWidth;
        if (usable < split.Panel1MinSize + split.Panel2MinSize) return;
        var maximum = usable - split.Panel2MinSize;
        split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, maximum);
    }

    private void ConfigureTreeAndPropertyGrid()
    {
        _hierarchy.BackColor = Theme.Panel;
        _hierarchy.ForeColor = Theme.Text;
        _hierarchy.BorderStyle = BorderStyle.None;
        _hierarchy.HideSelection = false;
        _hierarchy.FullRowSelect = true;
        _hierarchy.ShowLines = true;
        _hierarchy.ShowRootLines = false;
        _hierarchy.Indent = 18;
        _hierarchy.ItemHeight = 24;
        _hierarchy.Font = new Font("Segoe UI", 9.25f);

        _inspector.Dock = DockStyle.Fill;
        _inspector.BackColor = Theme.Panel;
        _inspector.ViewBackColor = Theme.Panel;
        _inspector.ViewForeColor = Theme.Text;
        _inspector.CategoryForeColor = Theme.Muted;
        _inspector.HelpBackColor = Theme.Panel;
        _inspector.HelpForeColor = Theme.Text;
        _inspector.LineColor = Color.FromArgb(58, 64, 75);
        _inspector.ToolbarVisible = true;
        _inspector.HelpVisible = true;
        _inspector.PropertySort = PropertySort.CategorizedAlphabetical;

        _output.BackColor = Theme.Viewport;
        _output.ForeColor = Theme.Text;
        _output.BorderStyle = BorderStyle.None;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            RenderMode = ToolStripRenderMode.Professional,
            Font = new Font("Segoe UI", 9.25f),
            Padding = new Padding(8, 4, 8, 4),
            GripStyle = ToolStripGripStyle.Hidden
        };
        menu.Renderer = StudioToolStripStyle.CreateRenderer();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(MenuItem("New Project", Keys.Control | Keys.N, (_, _) => NewProject()));
        file.DropDownItems.Add(MenuItem("Open .lic…", Keys.Control | Keys.O, (_, _) => OpenDialog()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Save", Keys.Control | Keys.S, (_, _) => SaveProject()));
        file.DropDownItems.Add(MenuItem("Save As…", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveProjectAs()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Exit", Keys.Alt | Keys.F4, (_, _) => Close()));

        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add(MenuItem("Undo", Keys.Control | Keys.Z, (_, _) => Undo()));
        edit.DropDownItems.Add(MenuItem("Redo", Keys.Control | Keys.Y, (_, _) => Redo()));
        edit.DropDownItems.Add(MenuItem("Command Palette…", Keys.Control | Keys.Shift | Keys.P, (_, _) => ShowCommandPalette()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(MenuItem("Duplicate Object", Keys.Control | Keys.Shift | Keys.D, (_, _) => DuplicateSelectedObject()));
        edit.DropDownItems.Add(MenuItem("Delete Selected", Keys.Delete, (_, _) => DeleteSelectedObject()));

        var node = new ToolStripMenuItem("Node");
        node.DropDownItems.Add(MenuItem("Create Empty Rect", Keys.Control | Keys.Shift | Keys.A, (_, _) => AddObject("rect")));
        node.DropDownItems.Add(MenuItem("Create Circle", Keys.None, (_, _) => AddObject("circle")));
        node.DropDownItems.Add(MenuItem("Create Text", Keys.Control | Keys.Shift | Keys.T, (_, _) => AddObject("text")));
        node.DropDownItems.Add(MenuItem("Create Polygon", Keys.None, (_, _) => AddObject("poly")));
        node.DropDownItems.Add(MenuItem("Create Button", Keys.None, (_, _) => AddObject("button")));
        node.DropDownItems.Add(MenuItem("Rename Selected", Keys.F2, (_, _) => RenameSelectedObject()));
        node.DropDownItems.Add(MenuItem("Focus Selected (F in Scene View)", Keys.None, (_, _) => FocusSelectedObject()));
        node.DropDownItems.Add(MenuItem("Center Selected X", Keys.None, (_, _) => AlignSelectedObject(true, false)));
        node.DropDownItems.Add(MenuItem("Center Selected Y", Keys.None, (_, _) => AlignSelectedObject(false, true)));
        node.DropDownItems.Add(MenuItem("Create Script…", Keys.None, (_, _) => AddScript()));
        node.DropDownItems.Add(new ToolStripSeparator());
        node.DropDownItems.Add(MenuItem("Create Prefab from Selected", Keys.None, (_, _) => CreatePrefabFromSelection()));
        node.DropDownItems.Add(MenuItem("Revert Selected Prefab Instance", Keys.None, (_, _) => RevertSelectedPrefabInstance()));
        node.DropDownItems.Add(MenuItem("Unpack Selected Prefab Instance", Keys.None, (_, _) => UnpackSelectedPrefabInstance()));

        var scenes = new ToolStripMenuItem("Scene");
        scenes.DropDownItems.Add(MenuItem("New Scene", Keys.Control | Keys.Shift | Keys.N, (_, _) => AddScene()));
        scenes.DropDownItems.Add(MenuItem("Duplicate Active Scene", Keys.Control | Keys.Alt | Keys.D, (_, _) => DuplicateActiveScene()));
        scenes.DropDownItems.Add(MenuItem("Delete Active Scene", Keys.Control | Keys.Shift | Keys.Delete, (_, _) => DeleteActiveScene()));
        scenes.DropDownItems.Add(new ToolStripSeparator());
        scenes.DropDownItems.Add(MenuItem("Set Active as Startup Scene", Keys.None, (_, _) => SetActiveAsStartupScene()));

        var view = new ToolStripMenuItem("View");
        view.DropDownItems.Add(MenuItem("Toggle Scene / FileSystem Dock", Keys.Control | Keys.B, (_, _) =>
        {
            _sideSplit.Panel1Collapsed = !_sideSplit.Panel1Collapsed;
            SaveDockLayout();
        }));
        view.DropDownItems.Add(MenuItem("Toggle FileSystem Dock", Keys.Control | Keys.Shift | Keys.E, (_, _) =>
        {
            _leftDockSplit.Panel2Collapsed = !_leftDockSplit.Panel2Collapsed;
            SaveDockLayout();
        }));
        view.DropDownItems.Add(MenuItem("Show Project Scripts", Keys.Control | Keys.Alt | Keys.S, (_, _) => OpenExplorerTab(4)));
        view.DropDownItems.Add(MenuItem("Quick Open / Search Files", Keys.Control | Keys.P, (_, _) => FocusProjectSearch()));
        view.DropDownItems.Add(MenuItem("Show Search", Keys.Control | Keys.Shift | Keys.F, (_, _) => FocusProjectSearch()));
        view.DropDownItems.Add(MenuItem("Show Terminal", Keys.Control | Keys.Oemtilde, (_, _) => OpenBottomTab(2)));
        view.DropDownItems.Add(MenuItem("Show Problems", Keys.Control | Keys.Shift | Keys.M, (_, _) => { RunProjectDiagnostics(); OpenBottomTab(1); }));
        view.DropDownItems.Add(MenuItem("Toggle Inspector", Keys.Control | Keys.D2, (_, _) =>
        {
            _centerSplit.Panel2Collapsed = !_centerSplit.Panel2Collapsed;
            SaveDockLayout();
        }));
        view.DropDownItems.Add(MenuItem("Toggle Bottom Dock", Keys.Control | Keys.D3, (_, _) =>
        {
            _rootSplit.Panel2Collapsed = !_rootSplit.Panel2Collapsed;
            SaveDockLayout();
        }));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(MenuItem("2D Scene View", Keys.Control | Keys.D4, (_, _) => _centerTabs.SelectedIndex = 0));
        view.DropDownItems.Add(MenuItem("Game View", Keys.Control | Keys.D5, (_, _) => _centerTabs.SelectedIndex = 1));
        view.DropDownItems.Add(MenuItem("Script Editor", Keys.Control | Keys.D6, (_, _) => _centerTabs.SelectedIndex = 2));

        var window = new ToolStripMenuItem("Window");
        window.DropDownItems.Add(MenuItem("Reset Dock Layout", Keys.Control | Keys.Shift | Keys.L, (_, _) => ResetDockLayout()));
        window.DropDownItems.Add(MenuItem("Fit Scene", Keys.Control | Keys.Alt | Keys.F, (_, _) => _sceneCanvas.FitScene()));

        var project = new ToolStripMenuItem("Project");
        project.DropDownItems.Add(MenuItem("Import Assets…", Keys.None, (_, _) => ImportAssets()));
        project.DropDownItems.Add(MenuItem("Check Lua Scripts", Keys.Control | Keys.F7, (_, _) => CheckScripts()));
        project.DropDownItems.Add(MenuItem("Run Preview", Keys.F5, (_, _) => RunPreview()));
        project.DropDownItems.Add(MenuItem("Stop Preview", Keys.Shift | Keys.F5, (_, _) => StopPreview()));
        project.DropDownItems.Add(new ToolStripSeparator());
        project.DropDownItems.Add(MenuItem("Build Solution", Keys.None, (_, _) => BuildSolution()));

        var tools = new ToolStripMenuItem("Tools");
        tools.DropDownItems.Add(MenuItem("Diagnostics & Stage 8 Tools…", Keys.Control | Keys.F8, (_, _) => OpenStage8Tools()));
        tools.DropDownItems.Add(MenuItem("Run Project Diagnostics", Keys.None, (_, _) => RunProjectDiagnostics()));
        tools.DropDownItems.Add(MenuItem("Build Editor", Keys.None, (_, _) => RunEditorBuild()));
        tools.DropDownItems.Add(MenuItem("Run Smoke Tests", Keys.None, (_, _) => RunSmokeTests()));

        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add(MenuItem("About PixelEngine", Keys.None, (_, _) =>
            MessageBox.Show(this, "PixelEngine 2.0 Studio\nCustom LIC editor with script tools, component metadata, prefabs, asset browser and undo/redo.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        menu.Items.AddRange([file, edit, node, scenes, view, window, project, tools, help]);
        return menu;
    }

    private void OpenStage8Tools()
    {
        if (_project is null)
        {
            MessageBox.Show(this, "Open or create a LIC project first.", "Studio Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var changed = false;
        using var form = new Stage8ToolsForm(_project);
        form.ProjectChanged += (_, _) =>
        {
            changed = true;
            _isDirty = true;
            UpdateWindowTitle();
            _sceneCanvas.Invalidate();
        };
        form.ShowDialog(this);
        if (changed)
        {
            if (TrySaveProject())
            {
                RefreshProjectView();
                Log("Stage 8 authoring data saved.");
            }
            else
            {
                Log("Stage 8 authoring changes remain unsaved; use File → Save.");
            }
        }
    }

    private void RunProjectDiagnostics()
    {
        if (_project is null)
        {
            SetStatus("Open a project before running diagnostics.");
            return;
        }
        var issues = PixelEngine.Diagnostics.ProjectDiagnostics.Analyze(_project);
        _problemList.BeginUpdate();
        _problemList.Items.Clear();
        foreach (var issue in issues)
        {
            var row = new ListViewItem(issue.Severity.ToString()) { Tag = issue };
            row.SubItems.Add(issue.Code);
            row.SubItems.Add(issue.Location);
            row.SubItems.Add(issue.Message + (string.IsNullOrWhiteSpace(issue.Suggestion) ? string.Empty : "  ·  " + issue.Suggestion));
            row.ForeColor = issue.Severity == PixelEngine.Diagnostics.DiagnosticSeverity.Error ? Theme.Danger
                : issue.Severity == PixelEngine.Diagnostics.DiagnosticSeverity.Warning ? Theme.Warning : Theme.Text;
            _problemList.Items.Add(row);
        }
        if (issues.Count == 0)
        {
            var clean = new ListViewItem("OK") { ForeColor = Theme.Success };
            clean.SubItems.Add("—");
            clean.SubItems.Add("Project");
            clean.SubItems.Add("No diagnostics issues found.");
            _problemList.Items.Add(clean);
        }
        _problemList.EndUpdate();
        foreach (var issue in issues)
            Log($"[{issue.Severity}] {issue.Code} {issue.Location}: {issue.Message} — {issue.Suggestion}");
        var errors = issues.Count(x => x.Severity == PixelEngine.Diagnostics.DiagnosticSeverity.Error);
        var warnings = issues.Count(x => x.Severity == PixelEngine.Diagnostics.DiagnosticSeverity.Warning);
        SetStatus($"Diagnostics: {errors} error(s), {warnings} warning(s)");
        OpenBottomTab(1);
    }

    private ToolStrip BuildWorkspaceBar()
    {
        var bar = new ToolStrip
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 34,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(10, 2, 10, 2),
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            Font = new Font("Segoe UI Semibold", 9f)
        };
        bar.Renderer = StudioToolStripStyle.CreateRenderer();

        AddWorkspaceModeButton(bar, "2D", 0, true, "2D scene editor");
        AddWorkspaceModeButton(bar, "3D", -1, false, "3D workspace is not available in this 2D runtime yet");
        bar.Items.Add(new ToolStripSeparator { Margin = new Padding(8, 3, 8, 3) });
        AddWorkspaceModeButton(bar, "Script", 2, true, "Open script workspace");
        AddWorkspaceModeButton(bar, "Game", 1, true, "Open game preview workspace");
        bar.Items.Add(new ToolStripSeparator { Margin = new Padding(8, 3, 8, 3) });
        var files = new ToolStripButton("FileSystem")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Show the FileSystem dock",
            Margin = new Padding(3, 1, 3, 1),
            Padding = new Padding(8, 3, 8, 3)
        };
        files.Click += (_, _) => OpenExplorerTab(1);
        bar.Items.Add(files);
        // ToolStripLabel does not expose Spring (that property belongs to ToolStripStatusLabel).
        // Right alignment keeps the workspace identity anchored without relying on a StatusStrip-only API.
        bar.Items.Add(new ToolStripLabel("PIXELENGINE 2D")
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 8f),
            Margin = new Padding(8, 1, 4, 1)
        });
        return bar;
    }

    private void AddWorkspaceModeButton(ToolStrip bar, string text, int pageIndex, bool enabled, string tooltip)
    {
        var button = new ToolStripButton(text)
        {
            CheckOnClick = false,
            Checked = pageIndex == 0,
            Tag = pageIndex,
            Enabled = enabled,
            ForeColor = Theme.Text,
            ToolTipText = tooltip,
            Font = new Font("Segoe UI Semibold", 9f),
            Margin = new Padding(3, 1, 3, 1),
            Padding = new Padding(10, 3, 10, 3)
        };
        button.Click += (_, _) =>
        {
            if (pageIndex < 0)
            {
                SetStatus("The current PixelEngine runtime is 2D-only; 3D workspace is not available yet.");
                return;
            }
            _centerTabs.SelectedIndex = pageIndex;
            UpdateWorkspaceModeButtons();
        };
        _workspaceModeButtons.Add(button);
        bar.Items.Add(button);
    }

    private void UpdateWorkspaceModeButtons()
    {
        var selected = _centerTabs.SelectedIndex;
        foreach (var button in _workspaceModeButtons)
        {
            var active = button.Tag is int pageIndex && pageIndex >= 0 && pageIndex == selected;
            button.Checked = active;
            button.BackColor = active ? Theme.Selection : Theme.Panel;
            button.ForeColor = active ? Theme.Accent : Theme.Text;
        }
    }

    private ToolStrip BuildToolbar()
    {
        // Keep the global toolbar focused on project-level actions. Scene manipulation
        // controls live in the Scene workspace so buttons no longer compete for one row.
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            BackColor = Theme.Toolbar,
            ForeColor = Theme.Text,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(10, 4, 10, 4),
            CanOverflow = true,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            Font = new Font("Segoe UI", 9.25f)
        };
        toolbar.Renderer = StudioToolStripStyle.CreateRenderer();
        toolbar.ImageScalingSize = new Size(16, 16);

        AddToolbarGroupLabel(toolbar, "PROJECT");
        toolbar.Items.Add(StyledToolButton("New", "Create a new LIC project (Ctrl+N)", (_, _) => NewProject()));
        toolbar.Items.Add(StyledToolButton("Open", "Open a LIC project (Ctrl+O)", (_, _) => OpenDialog()));
        _saveButton = StyledToolButton("Save", "Save project (Ctrl+S)", (_, _) => SaveProject());
        toolbar.Items.Add(_saveButton);

        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(7, 3, 7, 3) });
        AddToolbarGroupLabel(toolbar, "EDIT");
        _undoButton = StyledToolButton("Undo", "Undo (Ctrl+Z)", (_, _) => Undo());
        _redoButton = StyledToolButton("Redo", "Redo (Ctrl+Y)", (_, _) => Redo());
        toolbar.Items.Add(_undoButton);
        toolbar.Items.Add(_redoButton);

        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(7, 3, 7, 3) });
        AddToolbarGroupLabel(toolbar, "RUN");
        _playButton = StyledToolButton("▶ Play", "Run MonoGame preview (F5)");
        _playButton.ForeColor = Theme.Success;
        _playButton.Click += (_, _) => RunPreview();
        _stopButton = StyledToolButton("■ Stop", "Stop preview (Shift+F5)");
        _stopButton.ForeColor = Theme.Danger;
        _stopButton.Click += (_, _) => StopPreview();
        toolbar.Items.Add(_playButton);
        toolbar.Items.Add(_stopButton);

        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(7, 3, 7, 3) });
        AddToolbarGroupLabel(toolbar, "SCENE");
        toolbar.Items.Add(new ToolStripLabel("Active:") { ForeColor = Theme.Muted, Margin = new Padding(2, 0, 4, 0) });
        _sceneSelector.AutoSize = false;
        _sceneSelector.Width = 190;
        _sceneSelector.Height = 27;
        _sceneSelector.Font = new Font("Segoe UI", 9f);
        toolbar.Items.Add(_sceneSelector);
        _newSceneButton = StyledToolButton("+ Scene", "Add a scene", (_, _) => AddScene());
        toolbar.Items.Add(_newSceneButton);
        _createObjectButton = new ToolStripDropDownButton("+ Object")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Create an object in the active scene",
            Font = new Font("Segoe UI", 9.25f),
            Margin = new Padding(3, 1, 3, 1),
            Padding = new Padding(7, 3, 7, 3)
        };
        foreach (var kind in new[] { "rect", "circle", "text", "poly", "button" })
        {
            var item = new ToolStripMenuItem(kind.ToUpperInvariant());
            item.Click += (_, _) => AddObject(kind);
            _createObjectButton.DropDownItems.Add(item);
        }
        toolbar.Items.Add(_createObjectButton);

        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(7, 3, 7, 3) });
        AddToolbarGroupLabel(toolbar, "TOOLS");
        _newScriptButton = StyledToolButton("+ Script", "Create a Lua script", (_, _) => AddScript());
        _checkScriptsButton = StyledToolButton("Check", "Check Lua scripts", (_, _) => CheckScripts());
        _buildButton = StyledToolButton("Build", "Build the PixelEngine solution", (_, _) => BuildSolution());
        toolbar.Items.Add(_newScriptButton);
        toolbar.Items.Add(_checkScriptsButton);
        toolbar.Items.Add(_buildButton);
        return toolbar;
    }

    private static void AddToolbarGroupLabel(ToolStrip toolbar, string text)
    {
        toolbar.Items.Add(new ToolStripLabel(text)
        {
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 7.5f),
            Margin = new Padding(2, 2, 5, 1),
            Padding = Padding.Empty
        });
    }

    private static ToolStripButton StyledToolButton(string text, string tooltip, EventHandler? action = null)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.25f),
            ToolTipText = tooltip,
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(7, 3, 7, 3),
            AutoSize = false,
            Width = Math.Max(48, TextRenderer.MeasureText(text, new Font("Segoe UI", 9.25f)).Width + 18)
        };
        if (action is not null) button.Click += action;
        return button;
    }

    private ToolStrip BuildSceneToolbar()
    {
        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 38,
            BackColor = Theme.Toolbar,
            ForeColor = Theme.Text,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(8, 3, 8, 3),
            CanOverflow = true,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            Font = new Font("Segoe UI", 9f)
        };
        toolbar.Renderer = StudioToolStripStyle.CreateRenderer();
        AddToolbarGroupLabel(toolbar, "TRANSFORM");
        foreach (var tool in new[] { SceneTool.Select, SceneTool.Move, SceneTool.Rotate, SceneTool.Scale })
        {
            var button = new ToolStripButton(tool.ToString())
            {
                ForeColor = Theme.Text,
                CheckOnClick = true,
                Checked = tool == SceneTool.Select,
                Tag = tool,
                ToolTipText = tool switch
                {
                    SceneTool.Select => "Select objects (Q)",
                    SceneTool.Move => "Move objects (W)",
                    SceneTool.Rotate => "Rotate objects (E)",
                    SceneTool.Scale => "Scale objects (R)",
                    _ => tool.ToString()
                },
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(2, 1, 2, 1),
                Padding = new Padding(7, 3, 7, 3)
            };
            button.Click += (_, _) => SetSceneTool(tool);
            _sceneToolButtons.Add(button);
            toolbar.Items.Add(button);
        }

        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 3, 6, 3) });
        AddToolbarGroupLabel(toolbar, "SNAPPING");
        var snap = new ToolStripButton("Snap")
        {
            ForeColor = Theme.Text,
            CheckOnClick = true,
            Checked = true,
            ToolTipText = "Snap moved objects to the grid",
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(6, 3, 6, 3)
        };
        snap.CheckedChanged += (_, _) => _sceneCanvas.SnapToGrid = snap.Checked;
        toolbar.Items.Add(snap);
        var gridSize = new ToolStripComboBox
        {
            AutoSize = false,
            Width = 66,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ToolTipText = "Grid snap size",
            Margin = new Padding(2, 1, 5, 1)
        };
        gridSize.Items.AddRange(["4", "8", "16", "32", "64"]);
        gridSize.SelectedItem = "16";
        gridSize.SelectedIndexChanged += (_, _) =>
        {
            if (float.TryParse(gridSize.SelectedItem?.ToString(), out var size))
            {
                _sceneCanvas.GridSize = size;
                _sceneCanvas.Invalidate();
            }
        };
        toolbar.Items.Add(gridSize);

        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 3, 6, 3) });
        AddToolbarGroupLabel(toolbar, "OVERLAYS");
        var showGrid = new ToolStripButton("Grid") { ForeColor = Theme.Text, CheckOnClick = true, Checked = true, ToolTipText = "Show scene grid", Padding = new Padding(6, 3, 6, 3) };
        showGrid.CheckedChanged += (_, _) => { _sceneCanvas.ShowGrid = showGrid.Checked; _sceneCanvas.Invalidate(); };
        toolbar.Items.Add(showGrid);
        var labels = new ToolStripButton("Labels") { ForeColor = Theme.Text, CheckOnClick = true, Checked = false, ToolTipText = "Show object labels", Padding = new Padding(6, 3, 6, 3) };
        labels.CheckedChanged += (_, _) => { _sceneCanvas.ShowObjectLabels = labels.Checked; _sceneCanvas.Invalidate(); };
        toolbar.Items.Add(labels);
        var colliders = new ToolStripButton("Colliders") { ForeColor = Theme.Text, CheckOnClick = true, Checked = true, ToolTipText = "Show collider shapes", Padding = new Padding(6, 3, 6, 3) };
        colliders.CheckedChanged += (_, _) => { _sceneCanvas.ShowColliders = colliders.Checked; _sceneCanvas.Invalidate(); };
        toolbar.Items.Add(colliders);
        toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 3, 6, 3) });
        _fitSceneButton = StyledToolButton("Fit Scene", "Fit the complete scene in the viewport");
        _fitSceneButton.Click += (_, _) => _sceneCanvas.FitScene();
        toolbar.Items.Add(_fitSceneButton);
        return toolbar;
    }

    private void SetSceneTool(SceneTool tool)
    {
        _sceneCanvas.ActiveTool = tool;
        foreach (var button in _sceneToolButtons)
            button.Checked = button.Tag is SceneTool buttonTool && buttonTool == tool;
        _sceneCanvas.Invalidate();
    }

    private Control CreateCenterPanel()
    {
        _centerTabs.Dock = DockStyle.Fill;
        _centerTabs.Appearance = TabAppearance.FlatButtons;
        _centerTabs.SizeMode = TabSizeMode.Fixed;
        StyleTabs(_centerTabs, 154);
        var sceneTab = new TabPage("Scene") { BackColor = Theme.Viewport, ForeColor = Theme.Text };
        _sceneDocumentTab = sceneTab;
        var sceneWorkspace = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Viewport };
        _sceneCanvas.Dock = DockStyle.Fill;
        _sceneCanvas.ContextMenuStrip = BuildSceneContextMenu();
        sceneWorkspace.Controls.Add(_sceneCanvas);
        sceneWorkspace.Controls.Add(BuildSceneToolbar());
        sceneTab.Controls.Add(sceneWorkspace);
        var gameTab = new TabPage("Game") { BackColor = Theme.Viewport, ForeColor = Theme.Text };
        _gameHost.Dock = DockStyle.Fill;
        var previewInfo = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Viewport, Padding = new Padding(28) };
        var previewCard = new Panel { Width = 520, Height = 250, BackColor = Theme.Panel, Padding = new Padding(22), Anchor = AnchorStyles.None };
        var previewTitle = new Label { Dock = DockStyle.Top, Height = 38, Text = "GAME PREVIEW", ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 15f), TextAlign = ContentAlignment.MiddleCenter };
        _gamePlaceholder.Dock = DockStyle.Fill;
        _gamePlaceholder.Text = "The game opens in a separate Windows window.\n\nF5  Run project\nShift+F5  Stop project\n\nUse this tab for preview status; the Scene workspace stays available in the editor.";
        var previewButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = false, Anchor = AnchorStyles.None };
        _gameRunButton = SmallToolButton("▶ Run game", "Launch game in its own window");
        _gameStopButton = SmallToolButton("■ Stop game", "Stop the running game");
        _gameRunButton.Click += (_, _) => RunPreview();
        _gameStopButton.Click += (_, _) => StopPreview();
        previewButtons.Controls.AddRange([_gameRunButton, _gameStopButton]);
        previewCard.Controls.Add(_gamePlaceholder);
        previewCard.Controls.Add(previewButtons);
        previewCard.Controls.Add(previewTitle);
        previewInfo.Controls.Add(previewCard);
        previewInfo.Resize += (_, _) => previewCard.Location = new Point(Math.Max(0, (previewInfo.ClientSize.Width - previewCard.Width) / 2), Math.Max(0, (previewInfo.ClientSize.Height - previewCard.Height) / 2));
        _gameHost.Controls.Add(previewInfo);
        gameTab.Controls.Add(_gameHost);
        var scriptTab = new TabPage("Script") { BackColor = Theme.Viewport, ForeColor = Theme.Text };
        var scriptWorkspace = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Viewport };
        _scriptTabs.Dock = DockStyle.Top;
        _scriptTabs.Height = 28;
        _scriptTabs.Appearance = TabAppearance.FlatButtons;
        StyleTabs(_scriptTabs, 142);
        _scriptTabs.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingScriptTabs && _scriptTabs.SelectedTab?.Tag is LicScriptData script) SetActiveScript(script);
        };
        _scriptTabs.MouseUp += (_, e) =>
        {
            var index = Enumerable.Range(0, _scriptTabs.TabPages.Count).FirstOrDefault(i => _scriptTabs.GetTabRect(i).Contains(e.Location), -1);
            if (index < 0) return;
            var page = _scriptTabs.TabPages[index];
            if (e.Button == MouseButtons.Middle)
            {
                CloseScriptTab(page);
                return;
            }
            if (e.Button != MouseButtons.Right) return;
            var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer() };
            menu.Items.Add("Close", null, (_, _) => CloseScriptTab(page));
            menu.Items.Add("Close other scripts", null, (_, _) => CloseOtherScriptTabs(page));
            menu.Items.Add("Close all", null, (_, _) => CloseAllScriptTabs());
            menu.Show(_scriptTabs, e.Location);
        };
        _codeEditor.Dock = DockStyle.Fill;
        scriptWorkspace.Controls.Add(_codeEditor);
        scriptWorkspace.Controls.Add(_scriptTabs);
        scriptTab.Controls.Add(scriptWorkspace);
        _centerTabs.TabPages.AddRange([sceneTab, gameTab, scriptTab]);
        _centerTabs.SelectedIndexChanged += (_, _) => UpdateWorkspaceModeButtons();
        _centerTabs.Dock = DockStyle.Fill;
        return _centerTabs;
    }

    private ContextMenuStrip BuildSceneContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer() };
        var create = new ToolStripMenuItem("Create");
        foreach (var kind in new[] { "rect", "circle", "text", "poly", "button" })
            create.DropDownItems.Add(kind.ToUpperInvariant(), null, (_, _) => AddObjectAtMouse(kind));
        menu.Items.Add(create);
        menu.Items.Add(new ToolStripSeparator());
        var focus = new ToolStripMenuItem("Focus selected", null, (_, _) => FocusSelectedObject());
        var rename = new ToolStripMenuItem("Rename selected…", null, (_, _) => RenameSelectedObject());
        var duplicate = new ToolStripMenuItem("Duplicate selected", null, (_, _) => DuplicateSelectedObject());
        var delete = new ToolStripMenuItem("Delete selected", null, (_, _) => DeleteSelectedObject());
        var centerX = new ToolStripMenuItem("Center X", null, (_, _) => AlignSelectedObject(true, false));
        var centerY = new ToolStripMenuItem("Center Y", null, (_, _) => AlignSelectedObject(false, true));
        menu.Items.AddRange([focus, rename, duplicate, delete, new ToolStripSeparator(), centerX, centerY]);
        menu.Opening += (_, _) =>
        {
            create.Enabled = GetActiveScene() is not null;
            var hasSelection = GetActiveScene() is not null && _sceneCanvas.SelectedObject is LicObjectData;
            focus.Enabled = rename.Enabled = duplicate.Enabled = delete.Enabled = centerX.Enabled = centerY.Enabled = hasSelection;
        };
        return menu;
    }

    private void AddObjectAtMouse(string kind)
    {
        var point = _sceneCanvas.MouseWorldPosition;
        AddObject(kind, point.X, point.Y);
    }

    private void FocusSelectedObject()
    {
        if (_sceneCanvas.SelectedObject is LicObjectData selected)
        {
            _sceneCanvas.FocusObject(selected);
            SetStatus($"Focused: {selected.Name}");
        }
    }

    private void RenameSelectedObject()
    {
        if (_sceneCanvas.SelectedObject is not LicObjectData selected) return;
        var scene = GetActiveScene();
        if (scene is null) return;
        var newName = EditorPromptDialog.Ask(this, "Rename Object", "Object name:", selected.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        if (scene.Objects.Any(x => !ReferenceEquals(x, selected) && string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "An object with this name already exists in the scene.", "Rename object", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var oldName = selected.Name;
        selected.Name = newName;
        foreach (var script in _project?.Scripts ?? Enumerable.Empty<LicScriptData>())
        {
            // Rewrite only exact api.find("OldName") references; avoid broad text replacement.
            script.Code = System.Text.RegularExpressions.Regex.Replace(script.Code,
                "(api\\.find\\(\\s*)(['\"])" + System.Text.RegularExpressions.Regex.Escape(oldName) + "\\2",
                match => match.Groups[1].Value + match.Groups[2].Value + newName + match.Groups[2].Value,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        CommitHistory();
        RefreshProjectView(selectTag: selected);
        Log($"Renamed object '{oldName}' to '{newName}'.");
    }

    private void AlignSelectedObject(bool alignX, bool alignY)
    {
        if (_sceneCanvas.SelectedObject is not LicObjectData selected) return;
        if (alignX) selected.X = 0;
        if (alignY) selected.Y = 0;
        _inspector.Refresh();
        _sceneCanvas.Invalidate();
        CommitHistory();
        Log($"Aligned '{selected.Name}' to world origin {(alignX && alignY ? "X/Y" : alignX ? "X" : "Y")}.");
    }

    private Control CreateInspectorPanel()
    {
        _inspectorTabs.Dock = DockStyle.Fill;
        _inspectorTabs.Appearance = TabAppearance.FlatButtons;
        _inspectorTabs.SizeMode = TabSizeMode.Fixed;
        StyleTabs(_inspectorTabs, 82);

        var inspectorTab = new TabPage("Inspector") { BackColor = Theme.Panel, Padding = Padding.Empty };
        _inspector.Dock = DockStyle.Fill;
        _inspector.Margin = Padding.Empty;
        inspectorTab.Controls.Add(_inspector);

        var nodeTab = new TabPage("Node") { BackColor = Theme.Panel, Padding = Padding.Empty };
        _componentManager.Dock = DockStyle.Fill;
        nodeTab.Controls.Add(_componentManager);

        var signalsTab = new TabPage("Signals") { BackColor = Theme.Panel, Padding = Padding.Empty };
        signalsTab.Controls.Add(CreateSignalsPanel());
        _inspectorTabs.TabPages.AddRange([inspectorTab, nodeTab, signalsTab]);
        return _inspectorTabs;
    }

    private Control CreateSignalsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(7) };
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "AVAILABLE RUNTIME CALLBACKS",
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 8f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _signalsList.Dock = DockStyle.Fill;
        _signalsList.BackColor = Theme.Viewport;
        _signalsList.ForeColor = Theme.Text;
        _signalsList.Columns.Add("Signal / Callback", 145);
        _signalsList.Columns.Add("Description", 190);
        foreach (var entry in new[]
        {
            ("OnCollisionEnter2D", "Called when a solid contact begins."),
            ("OnCollisionStay2D", "Called while solid contact continues."),
            ("OnCollisionExit2D", "Called when solid contact ends."),
            ("OnTriggerEnter2D", "Called when an object enters a trigger.")
        })
        {
            var row = new ListViewItem(entry.Item1);
            row.SubItems.Add(entry.Item2);
            _signalsList.Items.Add(row);
        }
        var note = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            Text = "These callbacks are exposed to C# Component subclasses. Visual signal connections and Lua event binding are not yet supported.",
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(_signalsList);
        panel.Controls.Add(note);
        panel.Controls.Add(heading);
        return panel;
    }

    private Control CreateBottomPanel()
    {
        _bottomTabs.Dock = DockStyle.Fill;
        _bottomTabs.Appearance = TabAppearance.FlatButtons;
        StyleTabs(_bottomTabs, 98);
        var consoleTab = new TabPage("Output") { BackColor = Theme.Panel, Name = "Output" };
        var problemsTab = new TabPage("Problems") { BackColor = Theme.Panel, Name = "Problems" };
        var terminalTab = new TabPage("Terminal") { BackColor = Theme.Terminal, Name = "Terminal" };

        var outputPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        var outputTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(5, 4, 5, 2), WrapContents = false, AutoScroll = true, BackColor = Theme.Toolbar };
        _outputFilter.Items.AddRange(["All", "Errors", "Warnings", "Player", "Editor"]);
        _outputFilter.SelectedIndex = 0;
        _outputFilter.Height = 24;
        _outputSearch.Height = 24;
        _outputSearch.BackColor = Theme.Viewport;
        _outputSearch.ForeColor = Theme.Text;
        _clearOutputButton = SmallToolButton("Clear", "Clear output log");
        _copyOutputButton = SmallToolButton("Copy", "Copy selected output line");
        _exportOutputButton = SmallToolButton("Export…", "Save output log to a text file");
        outputTools.Controls.AddRange([_outputFilter, _outputSearch, _clearOutputButton, _copyOutputButton, _exportOutputButton]);
        _output.Dock = DockStyle.Fill;
        _output.IntegralHeight = false;
        outputPanel.Controls.Add(_output);
        outputPanel.Controls.Add(outputTools);
        consoleTab.Controls.Add(outputPanel);

        _outputFilter.SelectedIndexChanged += (_, _) => RefreshOutputList();
        _outputSearch.TextChanged += (_, _) => RefreshOutputList();
        _clearOutputButton.Click += (_, _) =>
        {
            FlushRepeatedWorkerLog();
            _outputEntries.Clear();
            _output.Items.Clear();
            SetStatus("Output cleared");
            UpdateOutputButtons();
        };
        _copyOutputButton.Click += (_, _) => { if (_output.SelectedItem is string line) Clipboard.SetText(line); };
        _exportOutputButton.Click += (_, _) => ExportOutputLog();
        _output.SelectedIndexChanged += (_, _) => UpdateOutputButtons();
        UpdateOutputButtons();
        _output.DoubleClick += (_, _) => { if (_output.SelectedItem is string line) Clipboard.SetText(line); };

        _problemList.Dock = DockStyle.Fill;
        _problemList.BackColor = Theme.Viewport;
        _problemList.ForeColor = Theme.Text;
        _problemList.Columns.Add("Severity", 78);
        _problemList.Columns.Add("Code", 84);
        _problemList.Columns.Add("Location", 250);
        _problemList.Columns.Add("Message", 640);
        _problemList.DoubleClick += (_, _) => NavigateToSelectedProblem();
        problemsTab.Controls.Add(_problemList);

        _terminal.WorkingDirectoryProvider = () => FindRepositoryRoot() ?? Path.GetDirectoryName(_projectPath) ?? Environment.CurrentDirectory;
        _terminal.CommandStarted += command =>
        {
            SetStatus("Running: " + command);
            if (command.Contains("dotnet build", StringComparison.OrdinalIgnoreCase)) ClearBuildProblems();
        };
        _terminal.OutputLineReceived += (line, _) => CaptureBuildProblem(line);
        _terminal.CommandCompleted += (_, exitCode) => SetStatus(exitCode == 0 ? "Terminal command completed" : $"Terminal command failed ({exitCode})");
        terminalTab.Controls.Add(_terminal);

        _bottomTabs.TabPages.AddRange([consoleTab, problemsTab, terminalTab]);
        _bottomTabs.Dock = DockStyle.Fill;
        return _bottomTabs;
    }

    private void ClearBuildProblems()
    {
        for (var i = _problemList.Items.Count - 1; i >= 0; i--)
            if (_problemList.Items[i].Tag is BuildProblem) _problemList.Items.RemoveAt(i);
    }

    private void CaptureBuildProblem(string line)
    {
        var normalizedLine = line.Trim();
        var match = Regex.Match(normalizedLine,
            @"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<message>.*?)(?:\s+\[.*\])?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return;

        var file = match.Groups["file"].Value.Trim();
        try
        {
            if (!Path.IsPathRooted(file) && FindRepositoryRoot() is { } root)
                file = Path.GetFullPath(Path.Combine(root, file));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            Log("Could not normalize compiler source path: " + ex.Message);
            return;
        }
        var lineNumber = int.TryParse(match.Groups["line"].Value, out var parsedLine) ? parsedLine : 1;
        var column = int.TryParse(match.Groups["column"].Value, out var parsedColumn) ? parsedColumn : 1;
        var severity = match.Groups["severity"].Value;
        var isError = severity.Equals("error", StringComparison.OrdinalIgnoreCase);
        var message = match.Groups["message"].Value.Trim();
        var code = match.Groups["code"].Value;
        var row = new ListViewItem(severity) { Tag = new BuildProblem(file, lineNumber, column, severity, code, message) };
        row.SubItems.Add(code);
        row.SubItems.Add($"{Path.GetFileName(file)}:{lineNumber}:{column}");
        row.SubItems.Add(message);
        row.ForeColor = isError ? Theme.Danger : Theme.Warning;
        _problemList.Items.Add(row);
        SetStatus($"Build {severity}: {code} · {Path.GetFileName(file)}:{lineNumber}");
    }

    private void NavigateToSelectedProblem()
    {
        if (_problemList.SelectedItems.Count == 0) return;
        if (_problemList.SelectedItems[0].Tag is BuildProblem buildProblem)
        {
            try
            {
                if (File.Exists(buildProblem.FilePath))
                    Process.Start(new ProcessStartInfo(buildProblem.FilePath) { UseShellExecute = true });
                else SetStatus("Compiler source file no longer exists: " + buildProblem.FilePath);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            { Log("Could not open compiler source file: " + ex.Message); }
            return;
        }
        if (_problemList.SelectedItems[0].Tag is not PixelEngine.Diagnostics.ProjectDiagnostic issue || _project is null) return;
        var scene = _project.Scenes.FirstOrDefault(item => issue.Location.Contains("Scene:" + item.Name, StringComparison.OrdinalIgnoreCase));
        if (scene is not null)
        {
            var objectMatch = scene.Objects.FirstOrDefault(item => issue.Location.Contains("Object:" + item.Name, StringComparison.OrdinalIgnoreCase));
            object selectedTarget = objectMatch is not null ? objectMatch : scene;
            RefreshProjectView(selectTag: selectedTarget);
            _centerTabs.SelectedIndex = 0;
            return;
        }
        var script = _project.Scripts.FirstOrDefault(item => issue.Location.Contains(item.Name, StringComparison.OrdinalIgnoreCase));
        if (script is not null)
        {
            OpenScript(script);
            _centerTabs.SelectedIndex = 2;
        }
        else if (issue.Location.StartsWith("Asset:", StringComparison.OrdinalIgnoreCase))
            OpenExplorerTab(1);
    }

    private static Button SmallToolButton(string text, string tooltip) => new()
    {
        Text = text, AutoSize = true, Height = 24, FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Padding = new Padding(7, 0, 7, 0),
        Margin = new Padding(2), AccessibleDescription = tooltip
    };

    private void RefreshOutputList()
    {
        if (_refreshingOutput || _output.IsDisposed) return;
        _refreshingOutput = true;
        try
        {
            var filter = _outputFilter.SelectedItem?.ToString() ?? "All";
            var query = _outputSearch.Text.Trim();
            _output.BeginUpdate();
            _output.Items.Clear();
            foreach (var entry in _outputEntries)
            {
                if (filter != "All" && !string.Equals(entry.Category, filter, StringComparison.OrdinalIgnoreCase)) continue;
                var line = $"[{entry.Timestamp:HH:mm:ss}] {entry.Message}";
                if (query.Length > 0 && !line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                _output.Items.Add(line);
            }
            if (_output.Items.Count > 0) _output.TopIndex = _output.Items.Count - 1;
            _output.EndUpdate();
            UpdateOutputButtons();
        }
        finally { _refreshingOutput = false; }
    }

    private void UpdateOutputButtons()
    {
        if (_clearOutputButton is not null) _clearOutputButton.Enabled = _outputEntries.Count > 0;
        if (_copyOutputButton is not null) _copyOutputButton.Enabled = _output.SelectedItem is string;
        if (_exportOutputButton is not null) _exportOutputButton.Enabled = _outputEntries.Count > 0;
    }

    private void ExportOutputLog()
    {
        using var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log", DefaultExt = "txt", FileName = "PixelEngine-output.txt", Title = "Export output log" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllLines(dialog.FileName, _outputEntries.Select(entry => $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Category}] {entry.Message}"));
            SetStatus("Output log exported");
            Log("Output log exported: " + dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Export output log failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void StyleTabs(TabControl tabs, int width)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(Math.Max(52, width), 28);
        tabs.Font = new Font("Segoe UI", 8.5f);
        tabs.HotTrack = true;
        tabs.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            var selected = e.Index == tabs.SelectedIndex;
            var bounds = e.Bounds;
            using var background = new SolidBrush(selected ? Theme.TabActive : Theme.Panel);
            e.Graphics.FillRectangle(background, bounds);
            var text = tabs.TabPages[e.Index].Text;
            var textColor = selected ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(e.Graphics, text, tabs.Font, Rectangle.Inflate(bounds, -7, -1), textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                e.Graphics.FillRectangle(accent, bounds.Left + 2, bounds.Bottom - 2, bounds.Width - 4, 2);
            }
            else
            {
                using var border = new Pen(Theme.Border);
                e.Graphics.DrawLine(border, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            }
        };
    }

    private Control WrapPanel(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(8, 5, 8, 8), Margin = Padding.Empty };
        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 29,
            Text = title,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 8.25f),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Theme.Panel,
            Padding = new Padding(2, 0, 0, 0)
        };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(header);
        return panel;
    }

    private void WireEvents()
    {
        _assetBrowser.UsageCountProvider = CountAssetUsages;
        _prefabBrowser.InstanceCountProvider = CountPrefabInstances;
        _prefabBrowser.CanCreateProvider = () => _sceneCanvas.SelectedObject is not null;
        _prefabBrowser.CanUpdateProvider = () => _sceneCanvas.SelectedObject is not null;
        _hierarchy.AfterSelect += (_, e) =>
        {
            if (!_suppressTreeEvents && e.Node is not null)
                ApplySelectedNode(e.Node);
        };
        _inspector.PropertyValueChanged += (_, _) =>
        {
            _sceneCanvas.Invalidate();
            CommitHistory();
        };
        _sceneCanvas.ObjectSelected += SelectObjectFromScene;
        _sceneCanvas.ObjectModified += () =>
        {
            _inspector.Refresh();
            _sceneCanvas.Invalidate();
        };
        _sceneCanvas.ObjectDragFinished += CommitHistory;
        _sceneSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingSceneSelector && _project is not null && _sceneSelector.SelectedIndex >= 0)
                ActivateScene(_sceneSelector.SelectedIndex, selectHierarchy: true);
        };
        _codeEditor.CodeChanged += (_, _) =>
        {
            if (_loadingScriptText || _selectedScript is null) return;
            _selectedScript.Code = _codeEditor.Code;
            MarkScriptTabDirty(_selectedScript, true);
            _isDirty = true;
            UpdateWindowTitle();
            _codeEditor.SetDiagnostic(null);
            _scriptHistoryTimer.Stop();
            _scriptHistoryTimer.Start();
            SetStatus("Script modified · not saved");
        };
        _scriptHistoryTimer.Tick += (_, _) =>
        {
            _scriptHistoryTimer.Stop();
            CommitHistory();
        };
        _componentManager.ComponentsChanged += (_, _) =>
        {
            _sceneCanvas.Invalidate();
            CommitHistory();
        };
        _assetBrowser.ImportRequested += (_, _) => ImportAssets();
        _assetBrowser.AssignRequested += AssignAssetToSelectedObject;
        _assetBrowser.RenameRequested += RenameAsset;
        _assetBrowser.FilesDropped += ImportAssetPaths;
        _assetBrowser.ExportRequested += ExportAsset;
        _assetBrowser.RemoveRequested += RemoveAsset;
        _prefabBrowser.CreateRequested += (_, _) => CreatePrefabFromSelection();
        _prefabBrowser.DuplicateRequested += DuplicatePrefab;
        _prefabBrowser.EditTemplateRequested += SelectPrefabTemplate;
        _prefabBrowser.RenameRequested += RenamePrefab;
        _prefabBrowser.UpdateRequested += UpdatePrefabFromSelection;
        _prefabBrowser.PropagateRequested += PropagatePrefabTemplate;
        _prefabBrowser.InstantiateRequested += InstantiatePrefab;
        _prefabBrowser.RemoveRequested += RemovePrefab;
    }

    private static ToolStripMenuItem MenuItem(string text, Keys shortcut, EventHandler action)
    {
        var item = new ToolStripMenuItem(text);

        // WinForms rejects bare printable keys (for example Keys.F) as menu
        // shortcuts. Those keys are handled by the focused editor/viewport instead.
        // Keep modifier shortcuts (Ctrl+F, Ctrl+Shift+F) and navigation/function
        // keys (F2, F5, Delete, etc.) as normal ToolStrip shortcuts.
        var keyCode = (int)(shortcut & Keys.KeyCode);
        var modifiers = shortcut & Keys.Modifiers;
        var isBarePrintableKey = modifiers == Keys.None &&
            ((keyCode >= (int)Keys.A && keyCode <= (int)Keys.Z) ||
             (keyCode >= (int)Keys.D0 && keyCode <= (int)Keys.D9));

        if (shortcut != Keys.None && !isBarePrintableKey)
            item.ShortcutKeys = shortcut;

        item.Click += action;
        return item;
    }

    private static ToolStripButton ToolButton(string text, EventHandler action)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = Theme.Text };
        button.Click += action;
        return button;
    }

    private void OpenRecentDefault()
    {
        var root = FindRepositoryRoot();
        var candidate = root is null ? null : Path.Combine(root, "Lic", "demo.lic");
        if (candidate is not null && File.Exists(candidate)) OpenProject(candidate);
        else Log("Open a project using File → Open .lic.");
    }

    private void NewProject()
    {
        if (!ConfirmPendingChanges("create a new project")) return;
        _project = new LicProjectData { Name = "NewPixelGame", StartScene = 0 };
        _project.Scenes.Add(new LicSceneData
        {
            Name = "Main",
            Objects = new List<LicObjectData>
            {
                new() { Name = "Player", Kind = "rect", X = 0, Y = -24, Width = 12, Height = 20, Color = "#5D8FE8", Follow = true, Components = [LicComponentData.Create("SpriteRenderer"), LicComponentData.Create("Rigidbody2D"), LicComponentData.Create("BoxCollider2D")] },
                new() { Name = "Ground", Kind = "rect", X = 0, Y = 40, Width = 150, Height = 12, Color = "#6E9B58", Gravity = false, Components = [LicComponentData.Create("SpriteRenderer"), LicComponentData.Create("BoxCollider2D")] }
            }
        });
        _projectPath = null;
        _sceneCanvas.ActiveSceneIndex = 0;
        _history.Reset(_project);
        _isDirty = true; // A newly created project has not been written to disk yet.
        UpdateHistoryButtons();
        RefreshProjectView(resetActiveScene: true);
        UpdateWindowTitle();
        Log("Created a new LIC project. Save it to disk before closing.");
    }

    private void OpenDialog()
    {
        using var dialog = new OpenFileDialog { Filter = "PixelEngine projects (*.lic)|*.lic|JSON files (*.json)|*.json|All files (*.*)|*.*", Title = "Open PixelEngine project" };
        if (dialog.ShowDialog(this) == DialogResult.OK && ConfirmPendingChanges("open another project"))
            OpenProject(dialog.FileName);
    }

    private void OpenProject(string path)
    {
        try
        {
            StopPreview();
            _project = LicProjectReader.Load(path);
            _projectPath = Path.GetFullPath(path);
            _history.Reset(_project);
            _isDirty = false;
            UpdateHistoryButtons();
            RefreshProjectView(resetActiveScene: true);
            var objectCount = _project.Scenes.Sum(x => x.Objects.Count);
            Log($"Loaded {_project.Name}: {_project.Scenes.Count} scene(s), {objectCount} object(s), {_project.Scripts.Count} script(s), {_project.Assets.Count} asset(s), {_project.Prefabs.Count} prefab(s).");
            foreach (var script in _project.Scripts.Where(x => string.Equals(x.Language, "lua", StringComparison.OrdinalIgnoreCase)))
            {
                var error = LicLuaBehaviour.ValidateCode(script.Code);
                if (!string.IsNullOrEmpty(error)) Log($"Lua syntax error in {script.Name}: {error}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Project open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log("Open failed: " + ex.Message);
        }
    }

    private void SaveProject() => TrySaveProject();

    private bool TrySaveProject()
    {
        if (_project is null) return true;
        _scriptHistoryTimer.Stop();
        CommitHistory();
        if (string.IsNullOrWhiteSpace(_projectPath)) return SaveProjectAsCore();
        return WriteProjectFile(_projectPath);
    }

    private void SaveProjectAs() => SaveProjectAsCore();

    private bool SaveProjectAsCore()
    {
        if (_project is null) return true;
        _scriptHistoryTimer.Stop();
        CommitHistory();
        using var dialog = new SaveFileDialog
        {
            Filter = "PixelEngine projects (*.lic)|*.lic",
            DefaultExt = "lic",
            AddExtension = true,
            FileName = (_project.Name ?? "MyGame") + ".lic",
            Title = "Save PixelEngine project"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        var previousPath = _projectPath;
        var targetPath = dialog.FileName;
        _projectPath = targetPath;
        if (WriteProjectFile(targetPath)) return true;
        _projectPath = previousPath;
        UpdateWindowTitle();
        return false;
    }

    private bool WriteProjectFile(string path)
    {
        if (_project is null) return true;
        try
        {
            LicProjectWriter.Save(path, _project);
            _isDirty = false;
            ClearScriptDirtyIndicators();
            UpdateWindowTitle();
            Log("Saved " + path);
            SetStatus("Saved");
            UpdateProjectActionButtons();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool ConfirmPendingChanges(string action)
    {
        if (!_isDirty || _project is null) return true;
        var result = MessageBox.Show(this,
            $"Save changes to '{_project.Name}' before you {action}?",
            "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return result switch
        {
            DialogResult.Yes => TrySaveProject(),
            DialogResult.No => true,
            _ => false
        };
    }

    private void UpdateProjectActionButtons()
    {
        var hasProject = _project is not null;
        var hasScene = hasProject && _project!.Scenes.Count > 0;
        var running = false;
        try { running = _previewProcess is { HasExited: false }; }
        catch (InvalidOperationException) { }

        if (_saveButton is not null) _saveButton.Enabled = hasProject;
        if (_playButton is not null) _playButton.Enabled = hasScene;
        if (_checkScriptsButton is not null) _checkScriptsButton.Enabled = hasProject;
        if (_stopButton is not null) _stopButton.Enabled = running;
        if (_newSceneButton is not null) _newSceneButton.Enabled = hasProject;
        if (_newScriptButton is not null) _newScriptButton.Enabled = hasProject;
        if (_createObjectButton is not null) _createObjectButton.Enabled = hasScene;
        if (_fitSceneButton is not null) _fitSceneButton.Enabled = hasScene;
        foreach (var sceneToolButton in _sceneToolButtons) sceneToolButton.Enabled = hasScene;
        if (_gameRunButton is not null) _gameRunButton.Enabled = hasScene;
        if (_gameStopButton is not null) _gameStopButton.Enabled = running;
        if (_sceneSelector is not null) _sceneSelector.Enabled = hasScene;
    }

    private void RefreshProjectView(bool resetActiveScene = false, object? selectTag = null)
    {
        var previousActiveScene = _sceneCanvas.ActiveSceneIndex;
        var previousTag = selectTag ?? _hierarchy.SelectedNode?.Tag;
        _suppressTreeEvents = true;
        _hierarchy.BeginUpdate();
        _hierarchy.Nodes.Clear();
        if (_project is not null)
        {
            // The Scene dock contains scene roots and their nodes only. Scripts,
            // prefabs and embedded assets live in their own docks, like Godot's editor.
            for (var i = 0; i < _project.Scenes.Count; i++)
            {
                var scene = _project.Scenes[i];
                var startupMarker = i == _project.StartScene ? " ★" : string.Empty;
                var sceneNode = _hierarchy.Nodes.Add(scene.Name + startupMarker);
                sceneNode.Tag = scene;
                foreach (var item in scene.Objects)
                {
                    var objectNode = sceneNode.Nodes.Add(item.Name);
                    objectNode.Tag = item;
                }
                sceneNode.Expand();
            }
            if (_project.Scenes.Count > 0)
            {
                var startupScene = _project.Scenes[Math.Clamp(_project.StartScene, 0, _project.Scenes.Count - 1)];
                var selectedNode = FindNodeByTag(_hierarchy.Nodes, previousTag ?? startupScene);
                if (selectedNode is null) selectedNode = FindNodeByTag(_hierarchy.Nodes, startupScene);
                _hierarchy.SelectedNode = selectedNode;
            }
        }
        _hierarchy.EndUpdate();
        _suppressTreeEvents = false;

        _sceneCanvas.Project = _project;
        _sceneCanvas.ActiveSceneIndex = _project is null || _project.Scenes.Count == 0
            ? 0
            : Math.Clamp(resetActiveScene ? _project.StartScene : previousActiveScene, 0, _project.Scenes.Count - 1);
        _assetBrowser.Bind(_project?.Assets);
        _prefabBrowser.Bind(_project?.Prefabs);
        RefreshScriptFileList();
        _codeEditor.SetProjectSymbols(
            _project?.Scenes.SelectMany(scene => scene.Objects).Select(item => item.Name),
            _project?.Assets.Where(asset => string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase)).Select(asset => asset.Name));
        RebuildScriptTabs(_selectedScript?.Name);
        RefreshSceneSelector();
        UpdateSceneDocumentTitle();
        if (_hierarchy.SelectedNode is not null) ApplySelectedNode(_hierarchy.SelectedNode);
        else
        {
            _inspector.SelectedObject = null;
            _componentManager.Bind(null);
        }
        UpdateWindowTitle();
        UpdateProjectActionButtons();
        _prefabBrowser.RefreshActionState();
        RefreshProjectSearchResults();
        SetStatus(_projectPath ?? "Unsaved project");
        _sceneCanvas.Invalidate();
        if (resetActiveScene && _sceneCanvas.IsHandleCreated)
            _sceneCanvas.BeginInvoke(new Action(_sceneCanvas.FitScene));
    }

    private void ApplySelectedNode(TreeNode node)
    {
        if (_project is null) return;
        if (node.Tag is LicSceneData selectedScene)
            _sceneCanvas.ActiveSceneIndex = _project.Scenes.IndexOf(selectedScene);
        else if (node.Tag is LicObjectData selectedObject)
        {
            var sceneIndex = _project.Scenes.FindIndex(scene => scene.Objects.Contains(selectedObject));
            if (sceneIndex >= 0) _sceneCanvas.ActiveSceneIndex = sceneIndex;
        }
        RefreshSceneSelector();
        UpdateSceneDocumentTitle();
        var inspectObject = node.Tag is LicPrefabData prefab ? prefab.Object : node.Tag as LicObjectData;
        _inspector.SelectedObject = inspectObject ?? node.Tag;
        _sceneCanvas.SelectedObject = node.Tag as LicObjectData;
        _componentManager.Bind(inspectObject);
        if (node.Tag is LicPrefabData selectedPrefab)
        {
            _inspectorTabs.SelectedIndex = 1;
            _centerTabs.SelectedIndex = 0;
            SetStatus($"Editing prefab template: {selectedPrefab.Name}");
        }
        else if (node.Tag is LicScriptData script)
        {
            OpenScript(script);
            _centerTabs.SelectedIndex = 2;
        }
        else if (node.Tag is LicObjectData or LicSceneData) _centerTabs.SelectedIndex = 0;
        _sceneCanvas.Invalidate();
        _prefabBrowser.RefreshActionState();
    }

    private void SelectObjectFromScene(LicObjectData? item)
    {
        if (item is null)
        {
            _sceneCanvas.SelectedObject = null;
            var scene = GetActiveScene();
            var sceneNode = FindNodeByTag(_hierarchy.Nodes, scene);
            if (sceneNode is not null) _hierarchy.SelectedNode = sceneNode;
            else
            {
                _inspector.SelectedObject = scene;
                _componentManager.Bind(null);
            }
            _sceneCanvas.Invalidate();
            _prefabBrowser.RefreshActionState();
            return;
        }
        var node = FindNodeByTag(_hierarchy.Nodes, item);
        if (node is not null) _hierarchy.SelectedNode = node;
        else _sceneCanvas.SelectedObject = item;
        _prefabBrowser.RefreshActionState();
    }

    private void MarkScriptTabDirty(LicScriptData script, bool dirty)
    {
        var page = _scriptTabs.TabPages.Cast<TabPage>().FirstOrDefault(x =>
            x.Tag is LicScriptData item && string.Equals(item.Name, script.Name, StringComparison.OrdinalIgnoreCase));
        if (page is not null) page.Text = script.Name + (dirty ? "  ●" : string.Empty);
    }

    private void ClearScriptDirtyIndicators()
    {
        foreach (TabPage page in _scriptTabs.TabPages)
            if (page.Tag is LicScriptData script) page.Text = script.Name;
    }

    private void OpenScript(LicScriptData script)
    {
        if (_project is null) return;
        var page = _scriptTabs.TabPages.Cast<TabPage>().FirstOrDefault(x =>
            x.Tag is LicScriptData item && string.Equals(item.Name, script.Name, StringComparison.OrdinalIgnoreCase));
        if (page is null)
        {
            page = new TabPage(script.Name) { Tag = script, ToolTipText = script.Name, BackColor = Theme.Panel };
            _updatingScriptTabs = true;
            _scriptTabs.TabPages.Add(page);
            _scriptTabs.SelectedTab = page;
            _updatingScriptTabs = false;
        }
        else
        {
            page.Tag = script;
            _updatingScriptTabs = true;
            _scriptTabs.SelectedTab = page;
            _updatingScriptTabs = false;
        }
        SetActiveScript(script);
    }

    private void CloseScriptTab(TabPage page)
    {
        if (!_scriptTabs.TabPages.Contains(page)) return;
        var wasSelected = _scriptTabs.SelectedTab == page;
        _updatingScriptTabs = true;
        _scriptTabs.TabPages.Remove(page);
        page.Dispose();
        _updatingScriptTabs = false;
        if (wasSelected)
        {
            if (_scriptTabs.SelectedTab?.Tag is LicScriptData script) SetActiveScript(script);
            else ClearActiveScript();
        }
    }

    private void CloseOtherScriptTabs(TabPage keep)
    {
        var others = _scriptTabs.TabPages.Cast<TabPage>().Where(x => x != keep).ToArray();
        _updatingScriptTabs = true;
        foreach (var page in others) { _scriptTabs.TabPages.Remove(page); page.Dispose(); }
        _scriptTabs.SelectedTab = keep;
        _updatingScriptTabs = false;
        if (keep.Tag is LicScriptData script) SetActiveScript(script);
    }

    private void CloseAllScriptTabs()
    {
        _updatingScriptTabs = true;
        foreach (TabPage page in _scriptTabs.TabPages.Cast<TabPage>().ToArray()) page.Dispose();
        _scriptTabs.TabPages.Clear();
        _updatingScriptTabs = false;
        ClearActiveScript();
    }

    private void ClearActiveScript()
    {
        _selectedScript = null;
        _loadingScriptText = true;
        _codeEditor.Code = string.Empty;
        _codeEditor.EditorReadOnly = true;
        _loadingScriptText = false;
    }

    private void SetActiveScript(LicScriptData script)
    {
        _selectedScript = script;
        _codeEditor.SetDiagnostic(null);
        _loadingScriptText = true;
        _codeEditor.Code = script.Code;
        _codeEditor.Language = script.Language;
        _codeEditor.EditorReadOnly = false;
        _loadingScriptText = false;
    }

    private void RebuildScriptTabs(string? preferredName)
    {
        if (_project is null) return;
        var names = _scriptTabs.TabPages.Cast<TabPage>()
            .Select(x => (x.Tag as LicScriptData)?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(preferredName) && _project.Scripts.Any(x => string.Equals(x.Name, preferredName, StringComparison.OrdinalIgnoreCase)) &&
            !names.Contains(preferredName, StringComparer.OrdinalIgnoreCase)) names.Add(preferredName);

        _updatingScriptTabs = true;
        _scriptTabs.TabPages.Clear();
        foreach (var name in names)
        {
            var script = _project.Scripts.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (script is not null) _scriptTabs.TabPages.Add(new TabPage(script.Name) { Tag = script, ToolTipText = script.Name, BackColor = Theme.Panel });
        }
        var preferred = _scriptTabs.TabPages.Cast<TabPage>().FirstOrDefault(x =>
            string.Equals((x.Tag as LicScriptData)?.Name, preferredName, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) _scriptTabs.SelectedTab = preferred;
        else if (_scriptTabs.TabPages.Count > 0) _scriptTabs.SelectedIndex = 0;
        _updatingScriptTabs = false;
        if (_scriptTabs.SelectedTab?.Tag is LicScriptData active) SetActiveScript(active);
        else
        {
            _selectedScript = null;
            _loadingScriptText = true;
            _codeEditor.Code = string.Empty;
            _codeEditor.EditorReadOnly = true;
            _loadingScriptText = false;
        }
    }

    private void RefreshSceneSelector()
    {
        _updatingSceneSelector = true;
        try
        {
            _sceneSelector.Items.Clear();
            if (_project is null) return;
            for (var i = 0; i < _project.Scenes.Count; i++)
            {
                var scene = _project.Scenes[i];
                var suffix = i == _project.StartScene ? "  ★ Startup" : string.Empty;
                _sceneSelector.Items.Add(scene.Name + suffix);
            }
            if (_project.Scenes.Count > 0) _sceneSelector.SelectedIndex = Math.Clamp(_sceneCanvas.ActiveSceneIndex, 0, _project.Scenes.Count - 1);
        }
        finally { _updatingSceneSelector = false; }
    }

    private void UpdateSceneDocumentTitle()
    {
        if (_sceneDocumentTab is null) return;
        if (_project is null || _project.Scenes.Count == 0)
        {
            _sceneDocumentTab.Text = "Scene";
            return;
        }
        var index = Math.Clamp(_sceneCanvas.ActiveSceneIndex, 0, _project.Scenes.Count - 1);
        _sceneDocumentTab.Text = _project.Scenes[index].Name;
    }

    private void ActivateScene(int sceneIndex, bool selectHierarchy)
    {
        if (_project is null || _project.Scenes.Count == 0) return;
        sceneIndex = Math.Clamp(sceneIndex, 0, _project.Scenes.Count - 1);
        _sceneCanvas.ActiveSceneIndex = sceneIndex;
        UpdateSceneDocumentTitle();
        _sceneCanvas.SelectedObject = null;
        _inspector.SelectedObject = _project.Scenes[sceneIndex];
        _componentManager.Bind(null);
        _centerTabs.SelectedIndex = 0;
        RefreshSceneSelector();
        if (selectHierarchy)
        {
            var node = FindNodeByTag(_hierarchy.Nodes, _project.Scenes[sceneIndex]);
            if (node is not null) _hierarchy.SelectedNode = node;
        }
        _sceneCanvas.Invalidate();
        SetStatus($"Scene: {_project.Scenes[sceneIndex].Name}");
    }

    private void AddScene()
    {
        if (_project is null) return;
        var index = LicProjectOperations.AddScene(_project);
        CommitHistory();
        RefreshProjectView(selectTag: _project.Scenes[index]);
        ActivateScene(index, selectHierarchy: true);
        Log($"Created scene: {_project.Scenes[index].Name}");
    }

    private void DuplicateActiveScene()
    {
        if (_project is null) return;
        try
        {
            var index = LicProjectOperations.DuplicateScene(_project, _sceneCanvas.ActiveSceneIndex);
            CommitHistory();
            RefreshProjectView(selectTag: _project.Scenes[index]);
            ActivateScene(index, selectHierarchy: true);
            Log($"Duplicated scene: {_project.Scenes[index].Name}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Duplicate scene", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void DeleteActiveScene()
    {
        if (_project is null || _project.Scenes.Count <= 1)
        {
            MessageBox.Show(this, "A project must keep at least one scene.", "Delete scene", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var scene = GetActiveScene();
        if (scene is null || MessageBox.Show(this, $"Delete scene '{scene.Name}' and all its objects?", "Delete scene", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var nextIndex = LicProjectOperations.RemoveScene(_project, _sceneCanvas.ActiveSceneIndex);
        CommitHistory();
        RefreshProjectView(selectTag: _project.Scenes[nextIndex]);
        ActivateScene(nextIndex, selectHierarchy: true);
        Log($"Deleted scene: {scene.Name}");
    }

    private void SetActiveAsStartupScene()
    {
        if (_project is null || _project.Scenes.Count == 0) return;
        LicProjectOperations.SetStartScene(_project, _sceneCanvas.ActiveSceneIndex);
        CommitHistory();
        RefreshProjectView(selectTag: _project.Scenes[_sceneCanvas.ActiveSceneIndex]);
        Log($"Startup scene set to: {_project.Scenes[_project.StartScene].Name}");
    }

    private LicSceneData? GetActiveScene()
    {
        if (_project is null || _project.Scenes.Count == 0) return null;
        return _project.Scenes[Math.Clamp(_sceneCanvas.ActiveSceneIndex, 0, _project.Scenes.Count - 1)];
    }

    private void AddObject(string kind) => AddObject(kind, _sceneCanvas.ViewCenter.X, _sceneCanvas.ViewCenter.Y);

    private void AddObject(string kind, float x, float y)
    {
        var scene = GetActiveScene();
        if (scene is null) return;
        var baseName = kind == "text" ? "Text" : kind == "circle" ? "Circle" : "GameObject";
        var suffix = 1;
        var name = baseName;
        while (scene.Objects.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) name = baseName + " " + suffix++;
        var item = new LicObjectData
        {
            Name = name,
            Kind = kind,
            X = x,
            Y = y,
            Width = kind == "text" || kind == "button" ? 48 : 16,
            Height = kind == "text" || kind == "button" ? 12 : 16,
            Color = kind switch { "circle" => "#50C6A8", "text" => "#F4E7B2", "poly" => "#A88CDE", "button" => "#D77C62", _ => "#6D91D8" },
            Text = kind == "text" ? "New Text" : kind == "button" ? "Button" : string.Empty,
            Points = kind == "poly" ? new List<float> { x - 10, y + 8, x, y - 10, x + 10, y + 8, float.NaN, float.NaN } : new List<float>(),
            Gravity = false,
            Solid = false,
            Components = [LicComponentData.Create("SpriteRenderer")]
        };
        scene.Objects.Add(item);
        CommitHistory();
        RefreshProjectView(selectTag: item);
        Log($"Created {kind}: {name}");
    }

    private void DuplicateSelectedObject()
    {
        if (_project is null || _sceneCanvas.SelectedObject is not LicObjectData selected) return;
        var scene = GetActiveScene();
        if (scene is null) return;
        var json = LicProjectWriter.Serialize(new LicProjectData { Scenes = [new LicSceneData { Objects = [selected] }] });
        var clone = LicProjectReader.Parse(json).Scenes[0].Objects[0];
        var baseName = selected.Name + " Copy";
        var name = baseName;
        for (var suffix = 1; scene.Objects.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++) name = baseName + " " + suffix;
        clone.Name = name;
        clone.X += 12;
        clone.Y += 12;
        scene.Objects.Add(clone);
        CommitHistory();
        RefreshProjectView(selectTag: clone);
    }

    private void DeleteSelectedObject()
    {
        if (_sceneCanvas.SelectedObject is not LicObjectData selected) return;
        var scene = GetActiveScene();
        if (scene is null || !scene.Objects.Remove(selected)) return;
        CommitHistory();
        RefreshProjectView(selectTag: scene);
        Log("Deleted object: " + selected.Name);
    }

    private void AddScript()
    {
        if (_project is null) return;
        var name = "NewScript.lua";
        var index = 1;
        while (_project.Scripts.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) name = $"NewScript{index++}.lua";
        var script = new LicScriptData
        {
            Name = name,
            Language = "lua",
            Code = "function start()\n    api.log('Hello from PixelEngine')\nend\n\nfunction update(dt)\n    -- Write gameplay code here\nend\n"
        };
        _project.Scripts.Add(script);
        CommitHistory();
        RefreshProjectView(selectTag: script);
        Log("Created script: " + script.Name);
    }

    private void CreatePrefabFromSelection()
    {
        if (_project is null || _sceneCanvas.SelectedObject is not LicObjectData selected)
        {
            MessageBox.Show(this, "Select an object in the Scene or Hierarchy first.", "Create prefab", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var prefab = LicProjectOperations.CreatePrefab(_project, selected);
        CommitHistory();
        _prefabBrowser.Bind(_project.Prefabs);
        RefreshProjectView(selectTag: prefab);
        Log($"Created prefab: {prefab.Name}");
    }

    private void UpdatePrefabFromSelection(LicPrefabData prefab)
    {
        if (_project is null || _sceneCanvas.SelectedObject is not LicObjectData selected)
        {
            MessageBox.Show(this, "Select a scene object first, then choose a prefab and click Update from object.", "Update prefab", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var updatedInstances = LicProjectOperations.UpdatePrefabFromObject(_project, prefab, selected, updateInstances: true);
        CommitHistory();
        RefreshProjectView(selectTag: selected);
        Log($"Updated prefab '{prefab.Name}' and propagated to {updatedInstances} linked instance(s).");
    }

    private void SelectPrefabTemplate(LicPrefabData prefab)
    {
        // Prefabs have their own dock now; they are intentionally no longer inserted
        // into the Scene tree. Bind the prefab payload directly to the Node inspector.
        OpenExplorerTab(2);
        _inspectorTabs.SelectedIndex = 1;
        _inspector.SelectedObject = prefab.Object;
        _componentManager.Bind(prefab.Object);
        _sceneCanvas.SelectedObject = null;
        _centerTabs.SelectedIndex = 0;
        _sceneCanvas.Invalidate();
        SetStatus($"Editing prefab template: {prefab.Name}. Use Apply to instances to propagate changes.");
    }

    private void PropagatePrefabTemplate(LicPrefabData prefab)
    {
        if (_project is null || !_project.Prefabs.Contains(prefab)) return;
        var updated = LicProjectOperations.UpdatePrefabFromObject(_project, prefab, prefab.Object, updateInstances: true);
        CommitHistory();
        RefreshProjectView(selectTag: prefab);
        Log($"Applied prefab template '{prefab.Name}' to {updated} linked instance(s).");
    }

    private void DuplicatePrefab(LicPrefabData prefab)
    {
        if (_project is null) return;
        try
        {
            var duplicate = LicProjectOperations.DuplicatePrefab(_project, prefab);
            CommitHistory();
            RefreshProjectView(selectTag: duplicate);
            Log($"Duplicated prefab '{prefab.Name}' as '{duplicate.Name}'.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Duplicate prefab", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RenamePrefab(LicPrefabData prefab, string newName)
    {
        if (_project is null) return;
        try
        {
            var oldName = prefab.Name;
            LicProjectOperations.RenamePrefab(_project, prefab, newName);
            CommitHistory();
            RefreshProjectView(selectTag: prefab);
            Log($"Renamed prefab '{oldName}' to '{prefab.Name}' and updated linked instances.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Rename prefab", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RenameAsset(LicAssetData asset, string newName)
    {
        if (_project is null) return;
        try
        {
            var oldName = asset.Name;
            LicAssetOperations.RenameAsset(_project, asset, newName);
            CommitHistory();
            RefreshProjectView(selectTag: asset);
            Log($"Renamed asset '{oldName}' to '{asset.Name}' and updated scene/prefab references.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Rename asset", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void AssignAssetToSelectedObject(LicAssetData asset)
    {
        if (_project is null || _sceneCanvas.SelectedObject is not LicObjectData selected)
        {
            MessageBox.Show(this, "Select a scene object in the Scene Tree or Scene View first.", "Assign asset", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase))
        {
            selected.Kind = "sprite";
            selected.Sprite = asset.Name;
            var renderer = selected.Components.FirstOrDefault(x => string.Equals(x.Type, "SpriteRenderer", StringComparison.OrdinalIgnoreCase));
            if (renderer is null)
            {
                renderer = LicComponentData.Create("SpriteRenderer");
                selected.Components.Add(renderer);
            }
            renderer.Texture = asset.Name;
            renderer.Enabled = true;
            _inspectorTabs.SelectedIndex = 1;
            Log($"Assigned texture '{asset.Name}' to '{selected.Name}'.");
        }
        else if (string.Equals(asset.Type, "wav", StringComparison.OrdinalIgnoreCase))
        {
            var audio = selected.Components.FirstOrDefault(x => string.Equals(x.Type, "AudioSource", StringComparison.OrdinalIgnoreCase));
            if (audio is null)
            {
                audio = LicComponentData.Create("AudioSource");
                selected.Components.Add(audio);
            }
            audio.Clip = asset.Name;
            _inspectorTabs.SelectedIndex = 1;
            Log($"Assigned WAV clip '{asset.Name}' to AudioSource on '{selected.Name}'. PlayOnAwake is supported; use api.play(\"{asset.Name}\") in Lua for explicit playback.");
        }
        CommitHistory();
        _inspector.Refresh();
        _componentManager.Bind(selected);
        _sceneCanvas.Invalidate();
    }

    private int CountAssetUsages(LicAssetData asset)
    {
        if (_project is null) return 0;
        var objects = _project.Scenes.SelectMany(scene => scene.Objects)
            .Concat(_project.Prefabs.Select(prefab => prefab.Object));
        return objects.Sum(item =>
        {
            var count = string.Equals(item.Sprite, asset.Name, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            count += (item.Components ?? new List<LicComponentData>()).Count(component =>
                string.Equals(component.Texture, asset.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(component.Clip, asset.Name, StringComparison.OrdinalIgnoreCase));
            return count;
        });
    }

    private int CountPrefabInstances(LicPrefabData prefab) => _project?.Scenes
        .SelectMany(scene => scene.Objects)
        .Count(item => string.Equals(item.Prefab, prefab.Name, StringComparison.OrdinalIgnoreCase)) ?? 0;

    private void RevertSelectedPrefabInstance()
    {
        if (_project is null || _sceneCanvas.SelectedObject is not LicObjectData selected)
        {
            MessageBox.Show(this, "Select a linked prefab instance in the Scene Tree or Scene View.", "Revert prefab", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var previousName = selected.Prefab;
        if (!LicProjectOperations.RevertPrefabInstance(_project, selected))
        {
            MessageBox.Show(this, $"'{selected.Name}' is not linked to an existing prefab template.", "Revert prefab", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        CommitHistory();
        RefreshProjectView(selectTag: selected);
        Log($"Reverted '{selected.Name}' to prefab '{previousName}', preserving its transform.");
    }

    private void UnpackSelectedPrefabInstance()
    {
        if (_project is null || _sceneCanvas.SelectedObject is not LicObjectData selected)
        {
            MessageBox.Show(this, "Select a linked prefab instance in the Scene Tree or Scene View.", "Unpack prefab", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var prefabName = selected.Prefab;
        if (!LicProjectOperations.UnpackPrefabInstance(selected))
        {
            MessageBox.Show(this, "The selected object is not linked to a prefab.", "Unpack prefab", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CommitHistory();
        RefreshProjectView(selectTag: selected);
        Log($"Unpacked '{selected.Name}' from prefab '{prefabName}'. It is now an independent object.");
    }

    private void InstantiatePrefab(LicPrefabData prefab)
    {
        var scene = GetActiveScene();
        if (scene is null) return;
        var instance = LicProjectOperations.InstantiatePrefab(scene, prefab);
        CommitHistory();
        RefreshProjectView(selectTag: instance);
        Log($"Instantiated prefab: {prefab.Name}");
    }

    private void RemovePrefab(LicPrefabData prefab)
    {
        if (_project is null) return;
        var linked = CountPrefabInstances(prefab);
        if (MessageBox.Show(this, $"Remove prefab '{prefab.Name}' from the project library? {linked} linked instance(s) will be unpacked and kept as independent objects.", "Remove prefab", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var unpacked = LicProjectOperations.RemovePrefabAndUnpackInstances(_project, prefab);
        CommitHistory();
        RefreshProjectView();
        Log($"Removed prefab '{prefab.Name}'. Unpacked {unpacked} instance(s) without deleting them.");
    }

    private void ImportAssets()
    {
        if (_project is null) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Supported assets (*.png;*.wav)|*.png;*.wav|PNG images (*.png)|*.png|WAV audio (*.wav)|*.wav",
            Multiselect = true,
            Title = "Import project assets"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ImportAssetPaths(dialog.FileNames);
    }

    private void ImportAssetPaths(string[] paths)
    {
        if (_project is null || paths.Length == 0) return;
        var imported = 0;
        foreach (var path in paths)
        {
            try
            {
                var asset = LicAssetOperations.ImportFile(_project, path);
                imported++;
                Log($"Imported {asset.Name} ({asset.Type}).");
            }
            catch (Exception ex) { Log($"Asset import failed ({Path.GetFileName(path)}): {ex.Message}"); }
        }
        if (imported > 0)
        {
            CommitHistory();
            RefreshProjectView();
            Log($"Asset import complete: {imported} file(s).");
        }
    }

    private void ExportAsset(LicAssetData asset)
    {
        try
        {
            using var dialog = new SaveFileDialog { FileName = asset.Name, Filter = "All files (*.*)|*.*", Title = "Export asset" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dialog.FileName, LicAssetOperations.GetBytes(asset));
            Log("Exported asset: " + dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export asset", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RemoveAsset(LicAssetData asset)
    {
        if (_project is null) return;
        var usages = CountAssetUsages(asset);
        var message = usages > 0
            ? $"'{asset.Name}' has {usages} reference(s). Remove it and clear those references from scene objects and prefab templates?"
            : $"Remove '{asset.Name}' from the project?";
        if (MessageBox.Show(this, message, "Remove asset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var removedReferences = LicAssetOperations.RemoveAsset(_project, asset, clearReferences: true);
        CommitHistory();
        RefreshProjectView();
        Log($"Removed asset '{asset.Name}' and cleared {removedReferences} reference(s).");
    }

    private static TreeNode? FindNodeByTag(TreeNodeCollection nodes, object? tag)
    {
        if (tag is null) return null;
        foreach (TreeNode node in nodes)
        {
            if (ReferenceEquals(node.Tag, tag)) return node;
            var child = FindNodeByTag(node.Nodes, tag);
            if (child is not null) return child;
        }
        return null;
    }

    private void CheckScripts()
    {
        if (_project is null) return;
        var luaScripts = _project.Scripts.Where(x => string.Equals(x.Language, "lua", StringComparison.OrdinalIgnoreCase)).ToArray();
        var errors = 0;
        foreach (var script in luaScripts)
        {
            var error = LicLuaBehaviour.ValidateCode(script.Code);
            if (string.IsNullOrEmpty(error)) Log($"Lua OK: {script.Name}");
            else
            {
                errors++;
                Log($"Lua ERROR: {script.Name}: {error}");
                if (ReferenceEquals(script, _selectedScript)) _codeEditor.SetDiagnostic(error);
            }
            if (ReferenceEquals(script, _selectedScript) && string.IsNullOrEmpty(error)) _codeEditor.SetDiagnostic(null);
        }
        foreach (var script in _project.Scripts.Where(x => string.Equals(x.Language, "csharp", StringComparison.OrdinalIgnoreCase)))
            Log($"C# source retained but not executed: {script.Name}");
        Log($"Lua check complete: {luaScripts.Length - errors}/{luaScripts.Length} passed.");
    }

    private void RunPreview()
    {
        if (_project is null) return;
        if (!TrySaveProject())
        {
            SetStatus("Preview cancelled because the project was not saved.");
            return;
        }
        StopPreview();
        var root = FindRepositoryRoot();
        var sandbox = root is null ? null : Path.Combine(root, "PixelEngine.Sandbox", "PixelEngine.Sandbox.csproj");
        if (sandbox is null || !File.Exists(sandbox))
        {
            MessageBox.Show(this, "PixelEngine.Sandbox.csproj was not found. Run the editor from the source repository folder.", "Sandbox not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            // Do not reparent the MonoGame HWND. The player is a normal independent
            // desktop window with its own border, resize/maximize controls and input focus.
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("run");
            start.ArgumentList.Add("--project");
            start.ArgumentList.Add(sandbox);
            start.ArgumentList.Add("--");
            var projectPath = _projectPath;
            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            {
                MessageBox.Show(this, "Save the project to disk before starting the Game preview.", "Project path required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            start.ArgumentList.Add(projectPath);
            _previewProcess = Process.Start(start) ?? throw new InvalidOperationException("Could not start MonoGame Player.");
            _previewProcess.EnableRaisingEvents = true;
            var trackedProcess = _previewProcess;
            trackedProcess.Exited += (_, _) =>
            {
                int exitCode;
                try { exitCode = trackedProcess.ExitCode; } catch { exitCode = -1; }
                LogFromWorker($"[Player] Game process exited (code {exitCode}).");
                if (!IsDisposed && IsHandleCreated)
                {
                    try { BeginInvoke(new Action(UpdateProjectActionButtons)); }
                    catch (InvalidOperationException) { }
                }
                if (exitCode != 0 && !IsDisposed && IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed)
                                MessageBox.Show(this,
                                    $"The game player exited with code {exitCode}.\n\nCheck Output and this log for details:\n{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelEngine", "Logs", "player-startup.log")}",
                                    "PixelEngine Player stopped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                    catch (InvalidOperationException) { /* The editor may be closing. */ }
                }
            };
            _previewProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogFromWorker("[Player] " + e.Data); };
            _previewProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogFromWorker("[Player:Error] " + e.Data); };
            _previewProcess.BeginOutputReadLine();
            _previewProcess.BeginErrorReadLine();
            _gamePlaceholder.Text = "The game is running in a separate Windows window.\n\nF5  Restart game\nShift+F5  Stop game\n\nYou can keep editing the scene while the game window is open.";
            Log("Launched separate MonoGame game window: " + Path.GetFileName(_projectPath));
            SetStatus("Game running in separate window");
            UpdateProjectActionButtons();
            // Keep the editor on its current workspace; do not switch to a fake embedded Game tab.
        }
        catch (Exception ex)
        {
            Log("Preview launch failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Could not launch preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopPreview()
    {
        FlushRepeatedWorkerLog();
        try
        {
            if (_previewProcess is { HasExited: false })
            {
                _previewProcess.Kill(entireProcessTree: true);
                _previewProcess.WaitForExit(2500);
                Log("Stopped separate game window and preview process tree.");
            }
        }
        catch (Exception ex) { Log("Stop preview: " + ex.Message); }
        finally
        {
            _previewProcess?.Dispose();
            _previewProcess = null;
            _gamePlaceholder.Text = "The game opens in a separate Windows window.\n\nF5  Run project\nShift+F5  Stop project\n\nUse this tab for preview status; the Scene workspace stays available in the editor.";
            if (!IsDisposed)
            {
                SetStatus("Preview stopped");
                UpdateProjectActionButtons();
            }
        }
    }

    private void BuildSolution()
    {
        var root = FindRepositoryRoot();
        var solution = root is null ? null : Path.Combine(root, "PixelEngine.sln");
        if (solution is null || !File.Exists(solution))
        {
            MessageBox.Show(this, "PixelEngine.sln was not found. Open the editor from the source repository folder.", "Build", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _terminal.WorkingDirectoryProvider = () => root!;
        OpenBottomTab(2);
        _terminal.Execute(@"dotnet build .\PixelEngine.sln -c Debug");
    }

    private void RunSmokeTests()
    {
        var root = FindRepositoryRoot();
        if (root is null || !File.Exists(Path.Combine(root, "PixelEngine.SmokeTests", "PixelEngine.SmokeTests.csproj")))
        {
            MessageBox.Show(this, "Smoke test project was not found in the source repository.", "Tests", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _terminal.WorkingDirectoryProvider = () => root;
        OpenBottomTab(2);
        _terminal.Execute(@"dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release -- .");
    }

    private void RunEditorBuild()
    {
        var root = FindRepositoryRoot();
        if (root is null) return;
        _terminal.WorkingDirectoryProvider = () => root;
        OpenBottomTab(2);
        _terminal.Execute(@"dotnet build .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug");
    }

    private void CommitHistory()
    {
        if (_project is null) return;
        if (_history.Commit(_project))
        {
            _isDirty = true;
            UpdateWindowTitle();
        }
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        if (_undoButton is not null)
        {
            _undoButton.Enabled = _history.CanUndo;
            _undoButton.ToolTipText = _history.CanUndo ? $"Undo ({_history.UndoCount} step(s))" : "Nothing to undo";
        }
        if (_redoButton is not null)
        {
            _redoButton.Enabled = _history.CanRedo;
            _redoButton.ToolTipText = _history.CanRedo ? $"Redo ({_history.RedoCount} step(s))" : "Nothing to redo";
        }
    }

    private void Undo()
    {
        if (_centerTabs.SelectedIndex == 2 && _selectedScript is not null && _codeEditor.CanUndoText)
        {
            _codeEditor.UndoText();
            SetStatus("Script text undo");
            return;
        }
        if (_project is null) return;
        _scriptHistoryTimer.Stop();
        CommitHistory();
        if (!_history.Undo(out var restored) || restored is null) { SetStatus("Nothing to undo"); return; }
        _project = restored;
        _isDirty = true;
        UpdateHistoryButtons();
        RefreshProjectView(resetActiveScene: true);
        Log("Undo");
    }

    private void Redo()
    {
        if (_centerTabs.SelectedIndex == 2 && _selectedScript is not null && _codeEditor.CanRedoText)
        {
            _codeEditor.RedoText();
            SetStatus("Script text redo");
            return;
        }
        if (_project is null) return;
        _scriptHistoryTimer.Stop();
        CommitHistory();
        if (!_history.Redo(out var restored) || restored is null) { SetStatus("Nothing to redo"); return; }
        _project = restored;
        _isDirty = true;
        UpdateHistoryButtons();
        RefreshProjectView(resetActiveScene: true);
        Log("Redo");
    }

    private void ShowCommandPalette()
    {
        var commands = new[]
        {
            new EditorCommand("save", "Save Project", "File", "Ctrl+S"),
            new EditorCommand("undo", "Undo", "Edit", "Ctrl+Z"),
            new EditorCommand("redo", "Redo", "Edit", "Ctrl+Y"),
            new EditorCommand("check", "Check Lua Scripts", "Project", "Ctrl+F7"),
            new EditorCommand("run", "Run Preview", "Project", "F5"),
            new EditorCommand("stop", "Stop Preview", "Project", "Shift+F5"),
            new EditorCommand("new-scene", "Create Scene", "Scene", "Ctrl+Shift+N"),
            new EditorCommand("new-script", "Create Lua Script", "Script", ""),
            new EditorCommand("new-rect", "Create Rectangle", "Node", "Ctrl+Shift+A"),
            new EditorCommand("new-circle", "Create Circle", "Node", ""),
            new EditorCommand("new-text", "Create Text", "Node", "Ctrl+Shift+T"),
            new EditorCommand("prefab", "Create Prefab From Selected Object", "Node", ""),
            new EditorCommand("instantiate-prefab", "Instantiate Selected Prefab", "Prefab", ""),
            new EditorCommand("apply-prefab", "Apply Selected Prefab Template to Instances", "Prefab", ""),
            new EditorCommand("revert-prefab", "Revert Selected Prefab Instance", "Node", ""),
            new EditorCommand("unpack-prefab", "Unpack Selected Prefab Instance", "Node", ""),
            new EditorCommand("import-assets", "Import Assets", "Assets", ""),
            new EditorCommand("build", "Build Solution", "Project", "Ctrl+Shift+B"),
            new EditorCommand("build-editor", "Build Editor Only", "Project", ""),
            new EditorCommand("tests", "Run Smoke Tests", "Project", ""),
            new EditorCommand("explorer", "Show Explorer", "View", "Ctrl+B"),
            new EditorCommand("search", "Search in Project", "View", "Ctrl+Shift+F"),
            new EditorCommand("quick-open", "Quick Open File / Asset", "View", "Ctrl+P"),
            new EditorCommand("terminal", "Open Integrated Terminal", "View", "Ctrl+`"),
            new EditorCommand("problems", "Run Diagnostics / Show Problems", "View", "Ctrl+Shift+M")
        };
        var command = EditorCommandPaletteDialog.Pick(this, commands);
        switch (command)
        {
            case "save": SaveProject(); break;
            case "undo": Undo(); break;
            case "redo": Redo(); break;
            case "check": CheckScripts(); break;
            case "run": RunPreview(); break;
            case "stop": StopPreview(); break;
            case "new-scene": AddScene(); break;
            case "new-script": AddScript(); break;
            case "new-rect": AddObject("rect"); break;
            case "new-circle": AddObject("circle"); break;
            case "new-text": AddObject("text"); break;
            case "prefab": CreatePrefabFromSelection(); break;
            case "instantiate-prefab": if (_prefabBrowser.SelectedPrefab is { } selectedPrefab) InstantiatePrefab(selectedPrefab); break;
            case "apply-prefab": if (_prefabBrowser.SelectedPrefab is { } applyPrefab) PropagatePrefabTemplate(applyPrefab); break;
            case "revert-prefab": RevertSelectedPrefabInstance(); break;
            case "unpack-prefab": UnpackSelectedPrefabInstance(); break;
            case "import-assets": ImportAssets(); break;
            case "build": BuildSolution(); break;
            case "build-editor": RunEditorBuild(); break;
            case "tests": RunSmokeTests(); break;
            case "explorer": OpenExplorerTab(0); break;
            case "search": FocusProjectSearch(); break;
            case "quick-open": FocusProjectSearch(); break;
            case "terminal": OpenBottomTab(2); _terminal.FocusCommandInput(); break;
            case "problems": RunProjectDiagnostics(); break;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.P)) { ShowCommandPalette(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.L)) { ResetDockLayout(); return true; }
        if (keyData == (Keys.Control | Keys.B)) { _sideSplit.Panel1Collapsed = !_sideSplit.Panel1Collapsed; SaveDockLayout(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.F) || keyData == (Keys.Control | Keys.P)) { FocusProjectSearch(); return true; }
        if (keyData == (Keys.Control | Keys.Oemtilde)) { OpenBottomTab(2); _terminal.FocusCommandInput(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.M)) { RunProjectDiagnostics(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.B)) { BuildSolution(); return true; }
        if (keyData == (Keys.Control | Keys.W) && _centerTabs.SelectedIndex == 2 && _scriptTabs.SelectedTab is { } scriptPage)
        { CloseScriptTab(scriptPage); return true; }
        if (_sceneCanvas.ContainsFocus && keyData == Keys.F) { FocusSelectedObject(); return true; }
        if (_sceneCanvas.ContainsFocus && keyData == Keys.F2) { RenameSelectedObject(); return true; }
        if (_sceneCanvas.ContainsFocus && keyData == (Keys.Control | Keys.D)) { DuplicateSelectedObject(); return true; }
        if (_sceneCanvas.ContainsFocus && keyData == Keys.Delete) { DeleteSelectedObject(); return true; }
        if (_sceneCanvas.ContainsFocus && (keyData is Keys.Q or Keys.W or Keys.E or Keys.R))
        {
            _sceneCanvas.ActiveTool = keyData switch { Keys.W => SceneTool.Move, Keys.E => SceneTool.Rotate, Keys.R => SceneTool.Scale, _ => SceneTool.Select };
            foreach (var button in _sceneToolButtons) button.Checked = button.Tag is SceneTool tool && tool == _sceneCanvas.ActiveTool;
            _sceneCanvas.Invalidate();
            return true;
        }
        if (_codeEditor.ContainsFocus && (keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)))
            return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateWindowTitle()
    {
        var name = _project?.Name ?? "PixelEngine 2.0";
        Text = $"{(_isDirty ? "● " : string.Empty)}{name} — PixelEngine 2.0 Studio";
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PixelEngine.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }

    private void LogFromWorker(string message)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke(new Action(() => { if (!IsDisposed) AddWorkerLog(message); }));
            else AddWorkerLog(message);
        }
        catch (InvalidOperationException) { /* The editor may be closing while the preview exits. */ }
    }

    private void AddWorkerLog(string message)
    {
        var now = DateTime.Now;
        if (_lastWorkerLogMessage is not null && string.Equals(_lastWorkerLogMessage, message, StringComparison.Ordinal) &&
            (now - _lastWorkerLogAt).TotalMilliseconds < 1400)
        {
            _lastWorkerLogRepeatCount++;
            _lastWorkerLogAt = now;
            _workerLogFlushTimer.Stop();
            _workerLogFlushTimer.Start();
            return;
        }
        FlushRepeatedWorkerLog();
        Log(message);
        _lastWorkerLogMessage = message;
        _lastWorkerLogAt = now;
        _lastWorkerLogRepeatCount = 0;
        _workerLogFlushTimer.Stop();
        _workerLogFlushTimer.Start();
    }

    private void FlushRepeatedWorkerLog()
    {
        _workerLogFlushTimer.Stop();
        if (_lastWorkerLogRepeatCount > 0 && _lastWorkerLogMessage is not null)
            Log(_lastWorkerLogMessage + $"  (repeated {_lastWorkerLogRepeatCount} more times; grouped)");
        _lastWorkerLogRepeatCount = 0;
        _lastWorkerLogMessage = null;
    }

    private void Log(string message)
    {
        var category = message.Contains("error", StringComparison.OrdinalIgnoreCase) || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? "Errors"
            : message.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "Warnings"
            : message.StartsWith("[Player", StringComparison.OrdinalIgnoreCase) ? "Player" : "Editor";
        _outputEntries.Add(new EditorLogEntry(DateTime.Now, category, message));
        while (_outputEntries.Count > 2000) _outputEntries.RemoveAt(0);
        RefreshOutputList();
    }

    private void SetStatus(string text) => _status.Text = text;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel && !ConfirmPendingChanges("close PixelEngine")) e.Cancel = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveDockLayout();
        _scriptHistoryTimer.Stop();
        _scriptHistoryTimer.Dispose();
        StopPreview();
        _workerLogFlushTimer.Stop();
        _workerLogFlushTimer.Dispose();
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Dispose();
        base.OnFormClosed(e);
    }
}

internal sealed record EditorLogEntry(DateTime Timestamp, string Category, string Message);
internal sealed record EditorSearchHit(string Kind, object Target, string Preview);
internal sealed record BuildProblem(string FilePath, int Line, int Column, string Severity, string Code, string Message);

internal sealed class EditorDockLayout
{
    public int RootDistance { get; set; }
    public int HierarchyDistance { get; set; }
    public int CenterDistance { get; set; }
    public int FileSystemDistance { get; set; }
    public bool HierarchyHidden { get; set; }
    public bool FileSystemHidden { get; set; }
    public bool InspectorHidden { get; set; }
    public bool BottomDockHidden { get; set; }
}

internal enum SceneTool { Select, Move, Rotate, Scale }

internal sealed class SceneCanvas : Panel
{
    private LicProjectData? _project;
    public LicProjectData? Project
    {
        get => _project;
        set
        {
            if (ReferenceEquals(_project, value)) return;
            _project = value;
            foreach (var bitmap in _spritePreviewCache.Values) bitmap?.Dispose();
            _spritePreviewCache.Clear();
            Invalidate();
        }
    }
    public int ActiveSceneIndex { get; set; }
    public object? SelectedObject { get; set; }
    public SceneTool ActiveTool { get; set; } = SceneTool.Select;
    public bool SnapToGrid { get; set; } = true;
    public bool ShowColliders { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool ShowObjectLabels { get; set; } = false;
    public float GridSize { get; set; } = 16f;
    public PointF MouseWorldPosition => ScreenToWorld(PointToClient(Cursor.Position));
    public PointF ViewCenter => _viewCenter;
    public event Action<LicObjectData?>? ObjectSelected;
    public event Action? ObjectModified;
    public event Action? ObjectDragFinished;

    private enum DragKind { None, Move, MoveX, MoveY, Resize, ResizeX, ResizeY, Rotate }
    private LicObjectData? _dragging;
    private DragKind _dragKind;
    private PointF _dragStartWorld;
    private float _startX, _startY, _startWidth, _startHeight, _startRotation, _startAngle;
    private bool _dragMoved;
    private bool _panning;
    private Point _lastPanPoint;
    private float _zoom = 4f;
    private PointF _viewCenter;
    private readonly Dictionary<string, Bitmap?> _spritePreviewCache = new(StringComparer.OrdinalIgnoreCase);

    public SceneCanvas()
    {
        DoubleBuffered = true;
        BackColor = Theme.Viewport;
        TabStop = true;
        Cursor = Cursors.Cross;
    }

    public void FitScene()
    {
        var scene = ActiveScene();
        if (scene is null || scene.Objects.Count == 0) { _viewCenter = PointF.Empty; _zoom = 4f; Invalidate(); return; }
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        foreach (var item in scene.Objects)
        {
            var radians = item.Rotation * MathF.PI / 180f;
            var halfW = Math.Max(1f, item.Width) * .5f;
            var halfH = Math.Max(1f, item.Height) * .5f;
            var extentX = MathF.Abs(MathF.Cos(radians)) * halfW + MathF.Abs(MathF.Sin(radians)) * halfH;
            var extentY = MathF.Abs(MathF.Sin(radians)) * halfW + MathF.Abs(MathF.Cos(radians)) * halfH;
            minX = Math.Min(minX, item.X - extentX);
            maxX = Math.Max(maxX, item.X + extentX);
            minY = Math.Min(minY, item.Y - extentY);
            maxY = Math.Max(maxY, item.Y + extentY);
        }
        _viewCenter = new PointF((minX + maxX) * .5f, (minY + maxY) * .5f);
        var spanX = Math.Max(1f, maxX - minX);
        var spanY = Math.Max(1f, maxY - minY);
        _zoom = Math.Clamp(Math.Min(Math.Max(100, Width - 100) / spanX, Math.Max(100, Height - 110) / spanY), .5f, 16f);
        Invalidate();
    }

    public void FocusObject(LicObjectData item)
    {
        _viewCenter = new PointF(item.X, item.Y);
        var fitWidth = Math.Max(12f, item.Width * 3f);
        var fitHeight = Math.Max(12f, item.Height * 3f);
        _zoom = Math.Clamp(Math.Min(Math.Max(120, Width - 120) / fitWidth, Math.Max(120, Height - 140) / fitHeight), 1f, 24f);
        SelectedObject = item;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _lastPanPoint = e.Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            var rightClickScene = ActiveScene();
            if (rightClickScene is null) return;
            var worldPoint = ScreenToWorld(e.Location);
            SelectedObject = rightClickScene.Objects.AsEnumerable().Reverse()
                .FirstOrDefault(item => HitTest(item, worldPoint));
            ObjectSelected?.Invoke(SelectedObject as LicObjectData);
            Invalidate();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        var scene = ActiveScene();
        if (scene is null) return;

        var selected = SelectedObject as LicObjectData;
        if (selected is not null && TryHitGizmo(selected, e.Location, out var handle))
        {
            BeginDrag(selected, handle, e.Location);
            return;
        }

        var world = ScreenToWorld(e.Location);
        _dragging = scene.Objects.AsEnumerable().Reverse().FirstOrDefault(item => HitTest(item, world));
        SelectedObject = _dragging;
        ObjectSelected?.Invoke(_dragging);
        _dragMoved = false;
        _dragKind = DragKind.None;
        if (_dragging is not null && ActiveTool == SceneTool.Move)
            BeginDrag(_dragging, DragKind.Move, e.Location);
        Invalidate();
    }

    private void UpdateHoverCursor(Point point)
    {
        if (SelectedObject is LicObjectData selected && TryHitGizmo(selected, point, out var handle))
        {
            Cursor = handle switch
            {
                DragKind.Rotate => Cursors.Hand,
                DragKind.MoveX => Cursors.SizeWE,
                DragKind.MoveY => Cursors.SizeNS,
                DragKind.ResizeX => Cursors.SizeWE,
                DragKind.ResizeY => Cursors.SizeNS,
                DragKind.Resize => Cursors.SizeNWSE,
                _ => Cursors.SizeAll
            };
        }
        else Cursor = Cursors.Cross;
    }

    private void BeginDrag(LicObjectData item, DragKind kind, Point location)
    {
        _dragging = item;
        _dragKind = kind;
        _dragStartWorld = ScreenToWorld(location);
        _startX = item.X;
        _startY = item.Y;
        _startWidth = item.Width;
        _startHeight = item.Height;
        _startRotation = item.Rotation;
        _startAngle = MathF.Atan2(_dragStartWorld.Y - item.Y, _dragStartWorld.X - item.X);
        _dragMoved = false;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning && (Control.MouseButtons & MouseButtons.Middle) != 0)
        {
            var panDx = e.X - _lastPanPoint.X;
            var panDy = e.Y - _lastPanPoint.Y;
            _lastPanPoint = e.Location;
            _viewCenter = new PointF(_viewCenter.X - panDx / _zoom, _viewCenter.Y - panDy / _zoom);
            Invalidate();
            return;
        }
        if (_dragging is null || _dragKind == DragKind.None || (Control.MouseButtons & MouseButtons.Left) == 0)
        {
            UpdateHoverCursor(e.Location);
            return;
        }
        var world = ScreenToWorld(e.Location);
        var dx = world.X - _dragStartWorld.X;
        var dy = world.Y - _dragStartWorld.Y;
        var rotation = _startRotation * MathF.PI / 180f;
        var localDx = dx * MathF.Cos(rotation) + dy * MathF.Sin(rotation);
        var localDy = -dx * MathF.Sin(rotation) + dy * MathF.Cos(rotation);
        switch (_dragKind)
        {
            case DragKind.Move:
                _dragging.X = Snap(_startX + dx);
                _dragging.Y = Snap(_startY + dy);
                break;
            case DragKind.MoveX:
                _dragging.X = Snap(_startX + dx);
                break;
            case DragKind.MoveY:
                _dragging.Y = Snap(_startY + dy);
                break;
            case DragKind.Resize:
                _dragging.Width = Math.Max(1f, Snap(_startWidth + localDx));
                _dragging.Height = Math.Max(1f, Snap(_startHeight + localDy));
                break;
            case DragKind.ResizeX:
                _dragging.Width = Math.Max(1f, Snap(_startWidth + localDx));
                break;
            case DragKind.ResizeY:
                _dragging.Height = Math.Max(1f, Snap(_startHeight + localDy));
                break;
            case DragKind.Rotate:
                var angle = MathF.Atan2(world.Y - _dragging.Y, world.X - _dragging.X);
                var deltaAngle = angle - _startAngle;
                while (deltaAngle > MathF.PI) deltaAngle -= MathF.Tau;
                while (deltaAngle < -MathF.PI) deltaAngle += MathF.Tau;
                var degrees = _startRotation + deltaAngle * 180f / MathF.PI;
                _dragging.Rotation = SnapToGrid ? MathF.Round(degrees / 15f) * 15f : degrees;
                break;
        }
        _dragMoved = Math.Abs(dx) + Math.Abs(dy) > .001f;
        ObjectModified?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning && e.Button == MouseButtons.Middle)
        {
            _panning = false;
            Capture = false;
            Cursor = Cursors.Cross;
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        Capture = false;
        _dragging = null;
        _dragKind = DragKind.None;
        Cursor = Cursors.Cross;
        if (_dragMoved) ObjectDragFinished?.Invoke();
        _dragMoved = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && _panning)
        {
            _panning = false;
            Cursor = Cursors.Cross;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var worldBefore = ScreenToWorld(e.Location);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15f : 1f / 1.15f), .35f, 24f);
        var worldAfter = ScreenToWorld(e.Location);
        _viewCenter = new PointF(_viewCenter.X + worldBefore.X - worldAfter.X,
                                 _viewCenter.Y + worldBefore.Y - worldAfter.Y);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ParseColor(ActiveScene()?.Background1) ?? Theme.Viewport);
        var center = new PointF(Width * .5f, Height * .53f);
        var gridStep = Math.Max(2f, GridSize * _zoom);
        var origin = WorldToScreen(0, 0);
        if (ShowGrid && gridStep >= 4f)
        {
            using var minorGridPen = new Pen(Color.FromArgb(28, 130, 144, 164), 1f);
            using var majorGridPen = new Pen(Color.FromArgb(52, 130, 144, 164), 1f);
            var offsetX = Mod(center.X - _viewCenter.X * _zoom, gridStep);
            var offsetY = Mod(center.Y - _viewCenter.Y * _zoom, gridStep);
            var lineIndex = 0;
            for (var x = offsetX; x < Width; x += gridStep, lineIndex++) g.DrawLine(lineIndex % 4 == 0 ? majorGridPen : minorGridPen, x, 0, x, Height);
            lineIndex = 0;
            for (var y = offsetY; y < Height; y += gridStep, lineIndex++) g.DrawLine(lineIndex % 4 == 0 ? majorGridPen : minorGridPen, 0, y, Width, y);
        }
        using var axisPen = new Pen(Color.FromArgb(90, 160, 174, 194), 1f);
        g.DrawLine(axisPen, origin.X, 0, origin.X, Height);
        g.DrawLine(axisPen, 0, origin.Y, Width, origin.Y);
        using var axisFont = new Font("Segoe UI", 7f);
        using var axisBrush = new SolidBrush(Color.FromArgb(150, 175, 190, 210));
        g.DrawString("0,0", axisFont, axisBrush, origin.X + 4, origin.Y + 4);

        var scene = ActiveScene();
        if (scene is not null)
        {
            foreach (var item in scene.Objects.OrderBy(item => item.Components?.FirstOrDefault(component => component.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase))?.SortingOrder ?? 0))
                DrawObject(g, item, center, _zoom, ReferenceEquals(item, SelectedObject));
            if (ShowColliders) foreach (var item in scene.Objects) DrawColliders(g, item, center, _zoom);
            if (SelectedObject is LicObjectData selected && scene.Objects.Contains(selected)) DrawGizmo(g, selected);
        }

        using var titleFont = new Font("Segoe UI Semibold", 9f);
        using var titleBrush = new SolidBrush(Color.FromArgb(205, 215, 222, 232));
        g.DrawString($"2D SCENE · {scene?.Name ?? "No scene"} · {scene?.Objects.Count ?? 0} objects · {_zoom:0.0}x · {ActiveTool}", titleFont, titleBrush, new PointF(14, 12));
        using var hintFont = new Font("Segoe UI", 8f);
        using var hintBrush = new SolidBrush(Color.FromArgb(155, 180, 190, 205));
        g.DrawString("Q Select · W Move · E Rotate · R Scale · MMB drag: pan · wheel: zoom · F: focus · F2: rename · Ctrl+D: duplicate", hintFont, hintBrush, new PointF(14, Height - 24));
        var mouseWorld = MouseWorldPosition;
        g.DrawString($"X {mouseWorld.X:0.0}   Y {mouseWorld.Y:0.0}   Grid {GridSize:0}   Zoom {_zoom:0.00}x", hintFont, hintBrush, new PointF(Math.Max(14, Width - 280), Height - 24));
    }

    private float Snap(float value) => SnapToGrid ? MathF.Round(value / Math.Max(1f, GridSize)) * Math.Max(1f, GridSize) : value;
    private static float Mod(float value, float divisor) => divisor <= 0 ? 0 : ((value % divisor) + divisor) % divisor;

    private bool TryHitGizmo(LicObjectData item, Point point, out DragKind handle)
    {
        handle = DragKind.None;
        var c = WorldToScreen(item.X, item.Y);
        var halfW = Math.Max(5f, item.Width * _zoom * .5f);
        var halfH = Math.Max(5f, item.Height * _zoom * .5f);
        bool Near(PointF p, float radius = 10f) => Distance(new PointF(point.X, point.Y), p) <= radius;
        switch (ActiveTool)
        {
            case SceneTool.Move:
                if (Near(new PointF(c.X + 68, c.Y))) handle = DragKind.MoveX;
                else if (Near(new PointF(c.X, c.Y - 68))) handle = DragKind.MoveY;
                else if (Near(c, 12)) handle = DragKind.Move;
                break;
            case SceneTool.Rotate:
                var radius = Math.Max(halfW, halfH) + 30;
                if (Near(new PointF(c.X, c.Y - radius), 13)) handle = DragKind.Rotate;
                break;
            case SceneTool.Scale:
                var corner = RotateOffset(c, halfW, halfH, item.Rotation);
                var right = RotateOffset(c, halfW, 0, item.Rotation);
                var bottom = RotateOffset(c, 0, halfH, item.Rotation);
                if (Near(corner, 11)) handle = DragKind.Resize;
                else if (Near(right, 10)) handle = DragKind.ResizeX;
                else if (Near(bottom, 10)) handle = DragKind.ResizeY;
                break;
        }
        return handle != DragKind.None;
    }

    private void DrawGizmo(System.Drawing.Graphics g, LicObjectData item)
    {
        var center = WorldToScreen(item.X, item.Y);
        var halfW = Math.Max(5f, item.Width * _zoom * .5f);
        var halfH = Math.Max(5f, item.Height * _zoom * .5f);
        using var selectionPen = new Pen(Color.FromArgb(255, 255, 204, 95), 1.4f) { DashStyle = DashStyle.Dash };
        var bounds = new RectangleF(center.X - halfW, center.Y - halfH, halfW * 2, halfH * 2);
        using (var state = g.SaveScope())
        {
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(item.Rotation);
            g.TranslateTransform(-center.X, -center.Y);
            g.DrawRectangle(selectionPen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        if (ActiveTool == SceneTool.Move)
        {
            DrawArrow(g, center, new PointF(center.X + 68, center.Y), Color.FromArgb(255, 210, 92, 92));
            DrawArrow(g, center, new PointF(center.X, center.Y - 68), Color.FromArgb(255, 100, 205, 135));
            using var brush = new SolidBrush(Color.FromArgb(255, 235, 215, 105));
            g.FillEllipse(brush, center.X - 5, center.Y - 5, 10, 10);
        }
        else if (ActiveTool == SceneTool.Rotate)
        {
            var radius = Math.Max(halfW, halfH) + 30;
            using var pen = new Pen(Color.FromArgb(255, 115, 170, 245), 2f);
            g.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using var handle = new SolidBrush(Color.FromArgb(255, 115, 170, 245));
            g.FillEllipse(handle, center.X - 6, center.Y - radius - 6, 12, 12);
        }
        else if (ActiveTool == SceneTool.Scale)
        {
            var corner = RotateOffset(center, halfW, halfH, item.Rotation);
            var right = RotateOffset(center, halfW, 0, item.Rotation);
            var bottom = RotateOffset(center, 0, halfH, item.Rotation);
            DrawScaleHandle(g, corner.X, corner.Y);
            DrawScaleHandle(g, right.X, right.Y);
            DrawScaleHandle(g, bottom.X, bottom.Y);
        }
    }

    private static void DrawArrow(System.Drawing.Graphics g, PointF start, PointF end, Color color)
    {
        using var pen = new Pen(color, 3f) { StartCap = LineCap.Round, EndCap = LineCap.ArrowAnchor };
        g.DrawLine(pen, start, end);
    }

    private static void DrawScaleHandle(System.Drawing.Graphics g, float x, float y)
    {
        using var brush = new SolidBrush(Color.FromArgb(255, 95, 170, 235));
        using var pen = new Pen(Color.White, 1f);
        var rect = new RectangleF(x - 5, y - 5, 10, 10);
        g.FillRectangle(brush, rect);
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void DrawColliders(System.Drawing.Graphics g, LicObjectData item, PointF center, float scale)
    {
        var components = item.Components ?? new List<LicComponentData>();
        var hasCollider = components.Any(x => x.Enabled &&
            (x.Type.Equals("BoxCollider2D", StringComparison.OrdinalIgnoreCase) || x.Type.Equals("CircleCollider2D", StringComparison.OrdinalIgnoreCase)));
        foreach (var component in components.Where(x => x.Enabled &&
                     (x.Type.Equals("BoxCollider2D", StringComparison.OrdinalIgnoreCase) || x.Type.Equals("CircleCollider2D", StringComparison.OrdinalIgnoreCase))))
        {
            var point = WorldToScreen(item.X, item.Y);
            var color = component.IsTrigger ? Color.FromArgb(220, 230, 165, 80) : Color.FromArgb(220, 65, 205, 190);
            using var pen = new Pen(color, 1.5f) { DashStyle = component.IsTrigger ? DashStyle.Dash : DashStyle.DashDot };
            using var state = g.SaveScope();
            g.TranslateTransform(point.X, point.Y);
            g.RotateTransform(item.Rotation);
            if (component.Type.Equals("CircleCollider2D", StringComparison.OrdinalIgnoreCase))
            {
                var diameter = Math.Max(1f, component.Radius * 2 * scale);
                g.DrawEllipse(pen, -diameter * .5f, -diameter * .5f, diameter, diameter);
            }
            else
            {
                var w = Math.Max(1f, item.Width * scale);
                var h = Math.Max(1f, item.Height * scale);
                g.DrawRectangle(pen, -w * .5f, -h * .5f, w, h);
            }
        }
        if (!hasCollider && item.Solid)
        {
            var point = WorldToScreen(item.X, item.Y);
            using var pen = new Pen(Color.FromArgb(145, 65, 205, 190), 1f) { DashStyle = DashStyle.Dot };
            using var state = g.SaveScope();
            g.TranslateTransform(point.X, point.Y);
            g.RotateTransform(item.Rotation);
            var w = item.Width * scale;
            var h = item.Height * scale;
            g.DrawRectangle(pen, -w * .5f, -h * .5f, w, h);
        }
    }

    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static PointF RotateOffset(PointF center, float localX, float localY, float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new PointF(center.X + localX * cos - localY * sin,
                          center.Y + localX * sin + localY * cos);
    }

    private PointF ScreenToWorld(Point location)
    {
        var center = new PointF(Width * .5f, Height * .53f);
        return new PointF((location.X - center.X) / _zoom + _viewCenter.X, (location.Y - center.Y) / _zoom + _viewCenter.Y);
    }

    private PointF WorldToScreen(float x, float y)
    {
        var center = new PointF(Width * .5f, Height * .53f);
        return new PointF(center.X + (x - _viewCenter.X) * _zoom, center.Y + (y - _viewCenter.Y) * _zoom);
    }

    private static bool HitTest(LicObjectData item, PointF point)
    {
        var radians = -item.Rotation * MathF.PI / 180f;
        var dx = point.X - item.X;
        var dy = point.Y - item.Y;
        var localX = dx * MathF.Cos(radians) - dy * MathF.Sin(radians);
        var localY = dx * MathF.Sin(radians) + dy * MathF.Cos(radians);
        var width = Math.Max(1f, item.Width);
        var height = Math.Max(1f, item.Height);
        if (string.Equals(item.Kind, "circle", StringComparison.OrdinalIgnoreCase))
        {
            var nx = localX / (width * .5f);
            var ny = localY / (height * .5f);
            return nx * nx + ny * ny <= 1f;
        }
        return Math.Abs(localX) <= width * .5f && Math.Abs(localY) <= height * .5f;
    }

    private LicSceneData? ActiveScene() => Project is null || Project.Scenes.Count == 0 ? null : Project.Scenes[Math.Clamp(ActiveSceneIndex, 0, Project.Scenes.Count - 1)];

    private void DrawObject(System.Drawing.Graphics g, LicObjectData item, PointF center, float scale, bool selected)
    {
        var x = center.X + (item.X - _viewCenter.X) * scale;
        var y = center.Y + (item.Y - _viewCenter.Y) * scale;
        var width = Math.Max(2f, item.Width * scale);
        var height = Math.Max(2f, item.Height * scale);
        var bounds = new RectangleF(x - width * .5f, y - height * .5f, width, height);
        var color = ParseColor(item.Color) ?? Color.White;
        using var brush = new SolidBrush(Color.FromArgb(210, color));
        using var pen = new Pen(selected ? Color.FromArgb(255, 255, 205, 90) : Color.FromArgb(180, color), selected ? 2f : 1f);
        using var state = g.SaveScope();
        g.TranslateTransform(x, y);
        g.RotateTransform(item.Rotation);
        g.TranslateTransform(-x, -y);
        switch (item.Kind)
        {
            case "circle": g.FillEllipse(brush, bounds); g.DrawEllipse(pen, bounds); break;
            case "text":
                using (var font = new Font("Consolas", Math.Max(7f, Math.Min(14f, height * .55f)))) g.DrawString(item.Text ?? item.Name, font, brush, x - width * .5f, y - height * .5f);
                break;
            case "poly": DrawPolygon(g, item, center, scale, pen); break;
            default:
                if (string.Equals(item.Kind, "sprite", StringComparison.OrdinalIgnoreCase) && TryGetSpritePreview(item.Sprite) is { } sprite)
                {
                    g.DrawImage(sprite, bounds);
                    if (selected) g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
                else
                {
                    g.FillRectangle(brush, bounds);
                    g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    if (string.Equals(item.Kind, "sprite", StringComparison.OrdinalIgnoreCase))
                    {
                        using var font = new Font("Segoe UI", 7f);
                        g.DrawString("NO SPRITE", font, Brushes.White, bounds.X + 2, bounds.Y + 2);
                    }
                }
                break;
        }
        if (selected)
        {
            using var selectionPen = new Pen(Color.FromArgb(255, 255, 205, 90), 1f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(selectionPen, bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6);
        }
        if (ShowObjectLabels)
        {
            using var labelFont = new Font("Segoe UI", 7.5f);
            using var labelBrush = new SolidBrush(Color.FromArgb(210, 220, 225, 235));
            g.DrawString(item.Name, labelFont, labelBrush, x - width * .5f, y + height * .5f + 2);
        }
    }

    private Bitmap? TryGetSpritePreview(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) || Project is null) return null;
        if (_spritePreviewCache.TryGetValue(assetName, out var cached)) return cached;
        Bitmap? bitmap = null;
        try
        {
            var asset = Project.Assets.FirstOrDefault(item => string.Equals(item.Name, assetName, StringComparison.OrdinalIgnoreCase));
            if (asset is not null && string.Equals(asset.Type, "png", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Convert.FromBase64String(asset.Base64);
                using var stream = new MemoryStream(bytes);
                using var source = Image.FromStream(stream);
                bitmap = new Bitmap(source);
            }
        }
        catch { bitmap = null; }
        _spritePreviewCache[assetName] = bitmap;
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var bitmap in _spritePreviewCache.Values) bitmap?.Dispose();
            _spritePreviewCache.Clear();
        }
        base.Dispose(disposing);
    }

    private void DrawPolygon(System.Drawing.Graphics g, LicObjectData item, PointF center, float scale, Pen pen)
    {
        if (item.Points is null || item.Points.Count < 4) return;
        var ring = new List<PointF>();
        for (var i = 0; i + 1 < item.Points.Count; i += 2)
        {
            var px = item.Points[i];
            var py = item.Points[i + 1];
            if (!float.IsFinite(px) || !float.IsFinite(py)) { DrawRing(g, ring, pen); ring.Clear(); continue; }
            ring.Add(new PointF(center.X + (px - _viewCenter.X) * scale, center.Y + (py - _viewCenter.Y) * scale));
        }
        DrawRing(g, ring, pen);
    }

    private static void DrawRing(System.Drawing.Graphics g, List<PointF> ring, Pen pen)
    {
        if (ring.Count >= 3) g.DrawPolygon(pen, ring.ToArray());
        else if (ring.Count == 2) g.DrawLine(pen, ring[0], ring[1]);
    }

    private static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return ColorTranslator.FromHtml(text); }
        catch { return null; }
    }
}

internal static class GraphicsExtensions
{
    public static GraphicsStateScope SaveScope(this System.Drawing.Graphics graphics) => new(graphics);
}

internal readonly struct GraphicsStateScope : IDisposable
{
    private readonly System.Drawing.Graphics _graphics;
    private readonly GraphicsState _state;
    public GraphicsStateScope(System.Drawing.Graphics graphics)
    {
        _graphics = graphics;
        _state = graphics.Save();
    }
    public void Dispose() => _graphics.Restore(_state);
}

internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(26, 27, 31);
    public static readonly Color Panel = Color.FromArgb(34, 35, 40);
    public static readonly Color Toolbar = Color.FromArgb(42, 43, 49);
    public static readonly Color Viewport = Color.FromArgb(20, 21, 25);
    public static readonly Color Terminal = Color.FromArgb(17, 18, 21);
    public static readonly Color TabActive = Color.FromArgb(29, 31, 36);
    public static readonly Color Selection = Color.FromArgb(48, 63, 82);
    public static readonly Color Border = Color.FromArgb(52, 54, 61);
    public static readonly Color Accent = Color.FromArgb(78, 156, 224);
    public static readonly Color AccentDark = Color.FromArgb(42, 101, 153);
    public static readonly Color Success = Color.FromArgb(120, 205, 145);
    public static readonly Color Warning = Color.FromArgb(230, 181, 95);
    public static readonly Color Danger = Color.FromArgb(235, 112, 112);
    public static readonly Color Text = Color.FromArgb(222, 226, 234);
    public static readonly Color Muted = Color.FromArgb(142, 151, 166);
}
