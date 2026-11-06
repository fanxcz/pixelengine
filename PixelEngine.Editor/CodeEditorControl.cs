using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PixelEngine.Editor;

/// <summary>
/// Lightweight Godot-inspired script editor built on WinForms RichTextBox.
/// It provides a line-number gutter, Lua/C# syntax colors, indentation,
/// line comments, duplicate/delete line, search and go-to-line without extra packages.
/// </summary>
internal sealed class CodeEditorControl : UserControl
{
    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripLabel _languageLabel = new("LUA");
    private readonly ToolStripComboBox _outlineSelector = new() { AutoSize = false, Width = Theme.Px(196), DropDownStyle = ComboBoxStyle.DropDownList, ToolTipText = "Script outline — jump to a function" };
    private readonly TextBox _findBox = new() { Width = Theme.Px(220), BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _replaceBox = new() { Width = Theme.Px(220), BorderStyle = BorderStyle.FixedSingle };
    private Button? _findNextButton;
    private Button? _findPreviousButton;
    private Button? _replaceCurrentButton;
    private Button? _replaceAllButton;
    private readonly ToolStripStatusLabel _positionLabel = new("Ln 1, Col 1");
    private readonly ToolStripStatusLabel _diagnosticStatus = new("No diagnostics");
    private readonly Label _completionHelp = new() { AutoSize = false, Width = 310, Height = 38, Padding = new Padding(8, 4, 8, 4), BackColor = Color.FromArgb(29, 33, 41), ForeColor = Theme.Muted, Text = "Select a completion to see its description." };
    private readonly EditorTextBox _editor = new();
    private readonly LineNumberGutter _gutter;
    private readonly Panel _findPanel = new();
    private readonly ListBox _completionList = new() { BorderStyle = BorderStyle.None, IntegralHeight = false, TabStop = true, Width = 310, Height = 205, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
    private readonly ToolStripDropDown _completionPopup = new() { AutoClose = false, Padding = Padding.Empty, Margin = Padding.Empty };
    private readonly System.Windows.Forms.Timer _highlightTimer = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _outlineTimer = new() { Interval = 350 };
    private readonly HashSet<int> _bookmarks = new();
    private readonly Dictionary<int, string> _diagnosticLines = new();
    private string[] _projectCompletions = Array.Empty<string>();
    private bool _settingCode;
    private bool _highlighting;
    private bool _updatingBracketMatch;
    private int _bracketA = -1;
    private int _bracketB = -1;
    private string _language = "lua";

    public event EventHandler? CodeChanged;

    public string Code
    {
        get => _editor.Text;
        set
        {
            _settingCode = true;
            try
            {
                _editor.Text = value ?? string.Empty;
            }
            finally
            {
                _settingCode = false;
            }

            // A newly opened script must not inherit the previous file's selection, scroll
            // position or bracket markers. Old bracket offsets can be valid in the new file and
            // cause the highlight pass to jump to an unrelated location.
            _bracketA = _bracketB = -1;
            _bookmarks.Clear();
            _diagnosticLines.Clear();
            _completionPopup.Close();
            if (_outlineSelector.Items.Count > 0) _outlineSelector.SelectedIndex = -1;
            _editor.Select(0, 0);
            _editor.ScrollToCaret();
            _gutter.Invalidate();
            ScheduleHighlight();
            UpdateStatus();
            UpdateOutline();
        }
    }

    public bool CanUndoText => _editor.CanUndo;
    public bool CanRedoText => _editor.CanRedo;

    public void UndoText()
    {
        if (_editor.CanUndo) _editor.Undo();
        _editor.Focus();
    }

    public void RedoText()
    {
        if (_editor.CanRedo) _editor.Redo();
        _editor.Focus();
    }

    /// <summary>Applies persisted preferences (font size and word wrap).</summary>
    public void ApplyPreferences(float fontSize, bool wordWrap, bool showLineNumbers = true)
    {
        var size = float.IsFinite(fontSize) ? Math.Clamp(fontSize, 7f, 32f) : 10f;
        _editor.Font = Theme.Mono(size);
        _editor.WordWrap = wordWrap;
        // Word wrap changes the gutter mapping, so the gutter is hidden rather than left stale.
        _gutter.Visible = showLineNumbers;
        _gutter.Width = Theme.Px(50);
        _gutter.Invalidate();
    }

    public bool EditorReadOnly
    {
        get => _editor.ReadOnly;
        set
        {
            _editor.ReadOnly = value;
            _editor.BackColor = value ? Color.FromArgb(27, 30, 37) : Theme.Viewport;
        }
    }

    public string Language
    {
        get => _language;
        set
        {
            _language = string.Equals(value, "csharp", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value, "cs", StringComparison.OrdinalIgnoreCase) ? "csharp" : "lua";
            _languageLabel.Text = _language == "lua" ? "LUA" : "C#";
            ScheduleHighlight();
            UpdateOutline();
        }
    }

    public CodeEditorControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Panel;
        _gutter = new LineNumberGutter(_editor, _bookmarks, _diagnosticLines);
        _completionPopup.AutoClose = false;
        _completionList.BackColor = Color.FromArgb(35, 40, 49);
        _completionList.ForeColor = Theme.Text;
        _completionList.Font = new Font("Cascadia Mono", 9.5f);
        _completionPopup.Items.Add(new ToolStripControlHost(_completionList)
        {
            AutoSize = false,
            Size = _completionList.Size,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        });
        _completionPopup.Items.Add(new ToolStripControlHost(_completionHelp)
        {
            AutoSize = false,
            Size = new Size(_completionList.Width, _completionHelp.Height),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        });
        BuildUi();
        _editor.ContextMenuStrip = BuildEditorContextMenu();

        _editor.TextChanged += (_, _) =>
        {
            if (!_settingCode && !_highlighting) CodeChanged?.Invoke(this, EventArgs.Empty);
            ScheduleHighlight();
            _gutter.Invalidate();
            UpdateStatus();
            UpdateOutline();
        };
        _editor.SelectionChanged += (_, _) => { _gutter.Invalidate(); UpdateStatus(); UpdateBracketMatch(); };
        _editor.MouseDown += (_, _) => { if (_completionPopup.Visible) _completionPopup.Close(); };
        _editor.LostFocus += (_, _) =>
        {
            if (_completionPopup.Visible && !_completionList.Focused) _completionPopup.Close();
        };
        _editor.MouseWheel += (_, e) =>
        {
            _gutter.Invalidate();
            if ((ModifierKeys & Keys.Control) == Keys.Control) ChangeFontSize(e.Delta > 0 ? 1f : -1f);
        };
        _editor.Scrolled += (_, _) =>
        {
            _gutter.Invalidate();
            ScheduleHighlight();
        };
        _editor.KeyUp += (_, _) => _gutter.Invalidate();
        _editor.Resize += (_, _) => _gutter.Invalidate();
        _editor.KeyDown += EditorKeyDown;
        _editor.KeyPress += EditorKeyPress;
        _completionList.DrawItem += DrawCompletionItem;
        _completionList.SelectedIndexChanged += (_, _) => UpdateCompletionHelp();
        _completionList.DoubleClick += (_, _) => AcceptCompletion();
        _completionList.MouseClick += (_, _) => { if (_completionList.SelectedItem is not null) AcceptCompletion(); };
        _completionList.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Tab)
            {
                e.SuppressKeyPress = true;
                AcceptCompletion();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _completionPopup.Close();
                _editor.Focus();
            }
            else if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                _completionPopup.Close();
                _editor.Focus();
                if (_editor.SelectionLength > 0) _editor.SelectedText = string.Empty;
                else if (_editor.SelectionStart > 0) _editor.Select(_editor.SelectionStart - 1, 1);
                _editor.SelectedText = string.Empty;
            }
        };
        _completionList.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar)) return;
            _completionPopup.Close();
            _editor.Focus();
            _editor.SelectedText = e.KeyChar.ToString();
            if ((char.IsLetterOrDigit(e.KeyChar) || e.KeyChar is '_' or '.') && IsHandleCreated)
                BeginInvoke(new Action(() => { if (!_editor.IsDisposed && _editor.Focused) ShowCompletion(explicitRequest: false); }));
            e.Handled = true;
        };
        _completionPopup.Closed += (_, _) => _gutter.Invalidate();
        _diagnosticStatus.IsLink = true;
        _diagnosticStatus.Click += (_, _) =>
        {
            if (_diagnosticLines.Count > 0) GoToLine(_diagnosticLines.Keys.Min());
        };
        _outlineSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_outlineSelector.SelectedItem is OutlineEntry entry) GoToLine(entry.Line);
        };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            HighlightSyntax();
        };
        _outlineTimer.Tick += (_, _) =>
        {
            _outlineTimer.Stop();
            RebuildOutline();
        };
        _findBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                FindNext();
            }
            else if (e.KeyCode == Keys.Escape) _findPanel.Visible = false;
        };
    }

    private ContextMenuStrip BuildEditorContextMenu()
    {
        var menu = new ContextMenuStrip { Renderer = StudioToolStripStyle.CreateRenderer() };
        menu.Items.Add("Undo", null, (_, _) => { if (_editor.CanUndo) _editor.Undo(); });
        menu.Items.Add("Redo", null, (_, _) => { if (_editor.CanRedo) _editor.Redo(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Cut", null, (_, _) => _editor.Cut());
        menu.Items.Add("Copy", null, (_, _) => _editor.Copy());
        menu.Items.Add("Paste", null, (_, _) => _editor.Paste());
        menu.Items.Add("Select all", null, (_, _) => _editor.SelectAll());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Toggle line comment", null, (_, _) => ToggleLineComment());
        menu.Items.Add("Duplicate line", null, (_, _) => DuplicateCurrentLine());
        menu.Items.Add("Delete line", null, (_, _) => DeleteCurrentLine());
        menu.Items.Add("Toggle bookmark", null, (_, _) => ToggleBookmark());
        menu.Items.Add("Reindent document", null, (_, _) => ReindentDocument());
        menu.Items.Add("API Reference…", null, (_, _) => ShowScriptReference());
        return menu;
    }

    private void BuildUi()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.AutoSize = false;
        _toolbar.Height = Theme.Px(40);
        _toolbar.Renderer = StudioToolStripStyle.CreateRenderer();
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.BackColor = Theme.Toolbar;
        _toolbar.ForeColor = Theme.Text;
        _toolbar.Font = new Font("Segoe UI", 9f);
        _toolbar.Padding = new Padding(8, 3, 8, 3);
        _toolbar.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        _toolbar.CanOverflow = true;

        var navigationLabel = new ToolStripLabel("NAVIGATE")
        {
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI Semibold", 7.5f),
            Margin = new Padding(2, 1, 5, 1)
        };
        _toolbar.Items.Add(navigationLabel);
        var findButton = new ToolStripButton("Find")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Find or replace (Ctrl+F)",
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(6, 3, 6, 3)
        };
        findButton.Click += (_, _) => ShowFind();
        _toolbar.Items.Add(findButton);
        var goToButton = new ToolStripButton("Go to line")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Go to line (Ctrl+L)",
            Margin = new Padding(2, 1, 4, 1),
            Padding = new Padding(6, 3, 6, 3)
        };
        goToButton.Click += (_, _) => GoToLineDialog();
        _toolbar.Items.Add(goToButton);
        _toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(5, 3, 5, 3) });
        _languageLabel.ForeColor = Theme.Accent;
        _languageLabel.Font = new Font("Segoe UI Semibold", 8.5f);
        _languageLabel.Margin = new Padding(3, 1, 5, 1);
        _toolbar.Items.Add(_languageLabel);
        _toolbar.Items.Add(new ToolStripSeparator { Margin = new Padding(5, 3, 5, 3) });
        _toolbar.Items.Add(new ToolStripLabel("SYMBOLS") { ForeColor = Theme.Muted, Font = new Font("Segoe UI Semibold", 7.5f), Margin = new Padding(2, 1, 5, 1) });
        _outlineSelector.Width = 210;
        _outlineSelector.Margin = new Padding(2, 1, 5, 1);
        _toolbar.Items.Add(_outlineSelector);

        var codeMenu = new ToolStripDropDownButton("Code")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Code tools and scripting reference",
            Margin = new Padding(4, 1, 2, 1),
            Padding = new Padding(7, 3, 7, 3)
        };
        var completeItem = new ToolStripMenuItem("Complete  ·  Ctrl+Space");
        completeItem.Click += (_, _) => ShowCompletion(explicitRequest: true);
        codeMenu.DropDownItems.Add(completeItem);
        var referenceItem = new ToolStripMenuItem("API Reference  ·  F1");
        referenceItem.Click += (_, _) => ShowScriptReference();
        codeMenu.DropDownItems.Add(referenceItem);
        var snippets = new ToolStripMenuItem("Insert Snippet");
        snippets.DropDownItems.Add("Lifecycle: start / update", null, (_, _) => InsertSnippet(_language == "lua"
            ? "function start()" + Environment.NewLine + "    " + Environment.NewLine + "end" + Environment.NewLine + Environment.NewLine + "function update(dt)" + Environment.NewLine + "    " + Environment.NewLine + "end"
            : "public void Start()" + Environment.NewLine + "{" + Environment.NewLine + "    " + Environment.NewLine + "}" + Environment.NewLine + Environment.NewLine + "public void Update(float dt)" + Environment.NewLine + "{" + Environment.NewLine + "    " + Environment.NewLine + "}"));
        snippets.DropDownItems.Add("If statement", null, (_, _) => InsertSnippet(_language == "lua" ? "if condition then" + Environment.NewLine + "    " + Environment.NewLine + "end" : "if (condition)" + Environment.NewLine + "{" + Environment.NewLine + "    " + Environment.NewLine + "}"));
        snippets.DropDownItems.Add("For loop", null, (_, _) => InsertSnippet(_language == "lua" ? "for i = 1, 10 do" + Environment.NewLine + "    " + Environment.NewLine + "end" : "for (var i = 0; i < 10; i++)" + Environment.NewLine + "{" + Environment.NewLine + "    " + Environment.NewLine + "}"));
        snippets.DropDownItems.Add("Find object (Lua)", null, (_, _) => InsertSnippet("local target = api.find(\"ObjectName\")" + Environment.NewLine + "if target ~= nil then" + Environment.NewLine + "    " + Environment.NewLine + "end"));
        codeMenu.DropDownItems.Add(snippets);
        _toolbar.Items.Add(codeMenu);

        var formatMenu = new ToolStripDropDownButton("Format / View")
        {
            ForeColor = Theme.Text,
            ToolTipText = "Formatting and editor display",
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(7, 3, 7, 3)
        };
        var reindentItem = new ToolStripMenuItem("Reindent document  ·  Ctrl+Shift+I");
        reindentItem.Click += (_, _) => ReindentDocument();
        formatMenu.DropDownItems.Add(reindentItem);
        var wrapItem = new ToolStripMenuItem("Word wrap") { CheckOnClick = true, Checked = false };
        wrapItem.CheckedChanged += (_, _) => _editor.WordWrap = wrapItem.Checked;
        formatMenu.DropDownItems.Add(wrapItem);
        formatMenu.DropDownItems.Add(new ToolStripSeparator());
        formatMenu.DropDownItems.Add("Decrease font size", null, (_, _) => ChangeFontSize(-1f));
        formatMenu.DropDownItems.Add("Increase font size", null, (_, _) => ChangeFontSize(1f));
        _toolbar.Items.Add(formatMenu);

        _findPanel.Dock = DockStyle.Top;
        _findPanel.Height = 62;
        _findPanel.BackColor = Color.FromArgb(39, 44, 53);
        _findPanel.Padding = new Padding(6, 3, 6, 3);
        var findRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, WrapContents = false, BackColor = _findPanel.BackColor };
        var findLabel = new Label { Text = "Find", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(2, 6, 6, 0) };
        _findNextButton = new Button { Text = "Next", Width = 54, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Toolbar };
        _findPreviousButton = new Button { Text = "Prev", Width = 54, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Toolbar };
        var findClose = new Button { Text = "×", Width = 28, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Toolbar };
        _findBox.Width = 220;
        _findBox.Height = 23;
        _findBox.BackColor = Theme.Viewport;
        _findBox.ForeColor = Theme.Text;
        findRow.Controls.AddRange([findLabel, _findBox, _findNextButton, _findPreviousButton, findClose]);
        var replaceRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28, WrapContents = false, BackColor = _findPanel.BackColor };
        var replaceLabel = new Label { Text = "Replace", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(2, 6, 6, 0) };
        _replaceCurrentButton = new Button { Text = "Replace", Width = 70, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Toolbar };
        _replaceAllButton = new Button { Text = "All", Width = 48, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Toolbar };
        _replaceBox.Width = 220;
        _replaceBox.Height = 23;
        _replaceBox.BackColor = Theme.Viewport;
        _replaceBox.ForeColor = Theme.Text;
        replaceRow.Controls.AddRange([replaceLabel, _replaceBox, _replaceCurrentButton, _replaceAllButton]);
        _findNextButton.Click += (_, _) => FindNext();
        _findPreviousButton.Click += (_, _) => FindPrevious();
        findClose.Click += (_, _) => _findPanel.Visible = false;
        _replaceCurrentButton.Click += (_, _) => ReplaceCurrent();
        _replaceAllButton.Click += (_, _) => ReplaceAll();
        _findBox.TextChanged += (_, _) => UpdateFindButtons();
        UpdateFindButtons();
        _findPanel.Controls.Add(replaceRow);
        _findPanel.Controls.Add(findRow);
        _findPanel.Visible = false;

        _editor.Dock = DockStyle.Fill;
        _editor.BorderStyle = BorderStyle.None;
        _editor.BackColor = Theme.Viewport;
        _editor.ForeColor = Color.FromArgb(220, 225, 235);
        _editor.Font = new Font("Cascadia Mono", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _editor.AcceptsTab = true;
        _editor.WordWrap = false;
        _editor.DetectUrls = false;
        _editor.HideSelection = false;
        _editor.ShortcutsEnabled = true;
        _editor.ScrollBars = RichTextBoxScrollBars.Both;

        var editingRow = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Viewport };
        _gutter.Dock = DockStyle.Left;
        _gutter.Width = 48;
        editingRow.Controls.Add(_editor);
        editingRow.Controls.Add(_gutter);

        var status = new StatusStrip { Dock = DockStyle.Bottom, BackColor = Theme.Toolbar, SizingGrip = false };
        status.Items.Add(new ToolStripStatusLabel("Lua / C#") { ForeColor = Theme.Muted });
        _diagnosticStatus.ForeColor = Theme.Muted;
        status.Items.Add(_diagnosticStatus);
        status.Items.Add(new ToolStripStatusLabel { Spring = true });
        _positionLabel.ForeColor = Theme.Muted;
        status.Items.Add(_positionLabel);

        Controls.Add(editingRow);
        Controls.Add(_findPanel);
        Controls.Add(status);
        Controls.Add(_toolbar);

    }

    public void SetProjectSymbols(IEnumerable<string>? objectNames, IEnumerable<string>? wavAssetNames)
    {
        var suggestions = new List<string>();
        foreach (var name in objectNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            suggestions.Add($"api.find(\"{EscapeLuaString(name)}\")");
        }
        foreach (var name in wavAssetNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            suggestions.Add($"api.play(\"{EscapeLuaString(name)}\")");
        }
        _projectCompletions = suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string EscapeLuaString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    public void SetDiagnostic(string? message)
    {
        _diagnosticLines.Clear();
        if (!string.IsNullOrWhiteSpace(message))
        {
            var match = Regex.Match(message, @"(?:line\s*|:)(\d+)(?:\b|:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var line = match.Success && int.TryParse(match.Groups[1].Value, out var parsed) ? Math.Max(1, parsed) - 1 : 0;
            _diagnosticLines[line] = message;
            _diagnosticStatus.Text = "● " + message;
            _diagnosticStatus.ForeColor = Color.FromArgb(235, 115, 115);
        }
        else
        {
            _diagnosticStatus.Text = "✓ No diagnostics";
            _diagnosticStatus.ForeColor = Color.FromArgb(125, 190, 150);
        }
        _gutter.Invalidate();
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.I)
        {
            e.SuppressKeyPress = true;
            ReindentDocument();
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.O)
        {
            e.SuppressKeyPress = true;
            _outlineSelector.Focus();
            _outlineSelector.ComboBox.DroppedDown = true;
        }
        else if (e.Control && (e.KeyCode == Keys.F || e.KeyCode == Keys.H))
        {
            e.SuppressKeyPress = true;
            ShowFind();
        }
        else if (e.Control && e.KeyCode == Keys.L)
        {
            e.SuppressKeyPress = true;
            GoToLineDialog();
        }
        else if (e.KeyCode == Keys.F1)
        {
            e.SuppressKeyPress = true;
            ShowScriptReference();
        }
        else if (e.KeyCode == Keys.F3)
        {
            e.SuppressKeyPress = true;
            if (e.Shift) FindPrevious(); else FindNext();
        }
        else if (e.Control && !e.Shift && e.KeyCode == Keys.D)
        {
            e.SuppressKeyPress = true;
            DuplicateCurrentLine();
        }
        else if (e.Control && e.KeyCode == Keys.Space)
        {
            e.SuppressKeyPress = true;
            ShowCompletion(explicitRequest: true);
        }
        else if (e.Control && e.KeyCode == Keys.OemQuestion)
        {
            e.SuppressKeyPress = true;
            ToggleLineComment();
        }
        else if (_completionPopup.Visible && e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            MoveCompletionSelection(1);
        }
        else if (_completionPopup.Visible && e.KeyCode == Keys.Up)
        {
            e.SuppressKeyPress = true;
            MoveCompletionSelection(-1);
        }
        else if (_completionPopup.Visible && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab))
        {
            e.SuppressKeyPress = true;
            AcceptCompletion();
        }
        else if (_completionPopup.Visible && e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            _completionPopup.Close();
        }
        else if (_completionPopup.Visible && (e.KeyCode is Keys.Back or Keys.Delete))
        {
            // Let RichTextBox perform the edit first, then refresh the filtered suggestions.
            if (IsHandleCreated)
                BeginInvoke(new Action(() => { if (!_editor.IsDisposed && _editor.Focused) ShowCompletion(explicitRequest: false); }));
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.F2)
        {
            e.SuppressKeyPress = true;
            ToggleBookmark();
        }
        else if (e.KeyCode == Keys.F2)
        {
            e.SuppressKeyPress = true;
            if (e.Shift) NavigateBookmark(-1); else NavigateBookmark(1);
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.K)
        {
            e.SuppressKeyPress = true;
            DeleteCurrentLine();
        }
        else if (e.Alt && (e.KeyCode is Keys.Up or Keys.Down))
        {
            e.SuppressKeyPress = true;
            MoveCurrentLine(e.KeyCode == Keys.Up ? -1 : 1);
        }
        else if (e.KeyCode == Keys.Home)
        {
            var line = _editor.GetLineFromCharIndex(_editor.SelectionStart);
            var lineStart = _editor.GetFirstCharIndexFromLine(line);
            if (lineStart >= 0)
            {
                var firstText = lineStart;
                while (firstText < _editor.TextLength && _editor.Text[firstText] is ' ' or '\t') firstText++;
                if (_editor.SelectionStart == firstText) _editor.SelectionStart = lineStart;
                else _editor.SelectionStart = firstText;
                e.SuppressKeyPress = true;
            }
        }
        else if (e.KeyCode == Keys.Back && _editor.SelectionLength == 0 && TryDeleteBracketPair())
        {
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Tab)
        {
            e.SuppressKeyPress = true;
            if (e.Shift) UnindentSelection();
            else if (_editor.SelectionLength > 0) IndentSelection();
            else InsertText("    ");
        }
        else if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            InsertIndentedNewLine();
        }
    }

    private void EditorKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_editor.ReadOnly || char.IsControl(e.KeyChar)) return;
        if ((e.KeyChar is '\'' or '"') && _editor.SelectionLength == 0 && _editor.SelectionStart < _editor.TextLength && _editor.Text[_editor.SelectionStart] == e.KeyChar)
        {
            _editor.SelectionStart++;
            e.Handled = true;
            return;
        }
        if (e.KeyChar is '(' or '[' or '{' or '\'' or '"')
        {
            var closing = e.KeyChar switch { '(' => ')', '[' => ']', '{' => '}', '\'' => '\'', '"' => '"', _ => e.KeyChar };
            var selectionStart = _editor.SelectionStart;
            var selectionLength = _editor.SelectionLength;
            var selected = selectionLength > 0 ? _editor.SelectedText : string.Empty;
            _editor.SelectedText = e.KeyChar + selected + closing;
            _editor.SelectionStart = selectionStart + 1;
            _editor.SelectionLength = selectionLength;
            e.Handled = true;
        }
        else if (e.KeyChar is ')' or ']' or '}' or '\'' or '"')
        {
            var caret = _editor.SelectionStart;
            if (_editor.SelectionLength == 0 && caret < _editor.TextLength && _editor.Text[caret] == e.KeyChar)
            {
                _editor.SelectionStart++;
                e.Handled = true;
            }
        }
        else if (e.KeyChar == '.')
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(() => { if (!_editor.IsDisposed && _editor.Focused) ShowCompletion(explicitRequest: false); }));
        }
        else if (char.IsLetterOrDigit(e.KeyChar) || e.KeyChar == '_')
        {
            // Offer completions while typing identifiers as well as after a member-access dot.
            // BeginInvoke lets RichTextBox insert the character before the token is inspected.
            if (IsHandleCreated)
                BeginInvoke(new Action(() =>
                {
                    if (!_editor.IsDisposed && _editor.Focused && _editor.SelectionLength == 0)
                        ShowCompletion(explicitRequest: false);
                }));
        }
        else if (_completionPopup.Visible)
        {
            _completionPopup.Close();
        }
    }

    private bool TryDeleteBracketPair()
    {
        var caret = _editor.SelectionStart;
        if (caret <= 0 || caret >= _editor.TextLength) return false;
        var left = _editor.Text[caret - 1];
        var right = _editor.Text[caret];
        if (!((left == '(' && right == ')') || (left == '[' && right == ']') || (left == '{' && right == '}') || (left == '\'' && right == '\'') || (left == '"' && right == '"'))) return false;
        _editor.Select(caret - 1, 2);
        _editor.SelectedText = string.Empty;
        return true;
    }

    private void ShowCompletion(bool explicitRequest)
    {
        if (_editor.ReadOnly || _editor.IsDisposed) return;
        var token = GetCurrentToken();
        if (!explicitRequest && token.Length < 2 && !token.EndsWith(".", StringComparison.Ordinal))
        {
            _completionPopup.Close();
            return;
        }
        var candidates = _language == "lua"
            ? new[] { "api.log(", "api.key(", "api.action(", "api.action_pressed(", "api.axis(", "api.bind_key(", "api.time(", "api.rand(", "api.find(", "api.spawn(", "api.destroy(", "api.purge(", "api.cam(", "api.bg(", "api.save(", "api.load(", "api.mousex(", "api.mousey(", "api.click(", "api.pick(", "api.play(", "self.name", "self.x", "self.y", "self.vx", "self.vy", "self.w", "self.h", "self.ground", "self.dead", "self.num", "self.maxnum", "self.attack", "self.face", "self.color", "self.text", "self.Get(", "self.Set(", "self.hit(", "self.destroy(", "function start()", "function update(dt)", "function", "local", "if", "then", "elseif", "else", "end", "for", "while", "return", "true", "false", "nil", "pairs(", "ipairs(", "math.floor(", "math.random(" }
            : new[] { "using", "namespace", "class", "public", "private", "protected", "static", "void", "var", "int", "float", "string", "bool", "if", "else", "for", "foreach", "while", "return", "new", "null", "this", "Console.WriteLine(", "Math.Clamp(", "Math.Max(", "Math.Min(", "List<", "Dictionary<", "ArgumentNullException.ThrowIfNull(" };
        var allCandidates = candidates.Concat(_language == "lua" ? _projectCompletions : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var filtered = allCandidates.Where(x => token.Length == 0 || x.StartsWith(token, StringComparison.OrdinalIgnoreCase)).Take(80).ToArray();
        if (filtered.Length == 0) { _completionPopup.Close(); return; }
        _completionList.BeginUpdate();
        _completionList.Items.Clear();
        _completionList.Items.AddRange(filtered);
        _completionList.SelectedIndex = 0;
        _completionList.EndUpdate();
        UpdateCompletionHelp();
        var caretPoint = _editor.GetPositionFromCharIndex(_editor.SelectionStart);
        var popupPoint = _editor.PointToScreen(new Point(caretPoint.X + 6, caretPoint.Y + _editor.Font.Height + 4));
        var workArea = Screen.FromControl(_editor).WorkingArea;
        popupPoint.X = Math.Clamp(popupPoint.X, workArea.Left + 4, Math.Max(workArea.Left + 4, workArea.Right - _completionList.Width - 8));
        popupPoint.Y = Math.Clamp(popupPoint.Y, workArea.Top + 4, Math.Max(workArea.Top + 4, workArea.Bottom - _completionList.Height - _completionHelp.Height - 12));
        if (_completionPopup.Visible) _completionPopup.Close();
        _completionPopup.Show(popupPoint);
        _editor.Focus();
    }

    private void DrawCompletionItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _completionList.Items.Count) return;
        e.DrawBackground();
        var text = _completionList.Items[e.Index]?.ToString() ?? string.Empty;
        var category = text.StartsWith("api.", StringComparison.Ordinal) || text.StartsWith("self.", StringComparison.Ordinal) ? "API" : "LANG";
        using var badgeBrush = new SolidBrush(category == "API" ? Color.FromArgb(75, 132, 115) : Color.FromArgb(76, 99, 139));
        using var textBrush = new SolidBrush(Theme.Text);
        using var badgeFont = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var textFont = new Font("Cascadia Mono", 9f);
        var badge = new RectangleF(e.Bounds.Left + 4, e.Bounds.Top + 4, 34, Math.Max(14, e.Bounds.Height - 8));
        e.Graphics.FillRectangle(badgeBrush, badge);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(category, badgeFont, Brushes.White, badge, format);
        e.Graphics.DrawString(text, textFont, textBrush, e.Bounds.Left + 46, e.Bounds.Top + 3);
        e.DrawFocusRectangle();
    }

    private void UpdateCompletionHelp()
    {
        var item = _completionList.SelectedItem?.ToString();
        _completionHelp.Text = item is null ? "Select a completion to see its description." : DescribeCompletion(item);
    }

    private string DescribeCompletion(string item)
    {
        if (item.StartsWith("api.find(\"", StringComparison.Ordinal)) return "Project object shortcut: find this named scene object from a Lua script.";
        if (item.StartsWith("api.play(\"", StringComparison.Ordinal)) return "Project asset shortcut: play this embedded WAV sound through the runtime audio helper.";
        if (item.StartsWith("api.", StringComparison.Ordinal))
        {
            var name = item[4..].TrimEnd('(');
            return name switch
            {
                "log" => "api.log(message) — write a message to the Output dock.",
                "key" => "api.key(name) — true while a keyboard key is held.",
                "action" => "api.action(name) — true while a named keyboard/gamepad action is held.",
                "action_pressed" => "api.action_pressed(name) — true on the frame a named action becomes pressed.",
                "axis" => "api.axis(name) — analog/digital move_x/move_y, look_x/look_y and trigger values.",
                "bind_key" => "api.bind_key(action, keyName) — add a keyboard binding using a MonoGame Keys name.",
                "time" => "api.time() — elapsed game time in seconds.",
                "rand" => "api.rand(min, max) — random floating-point value.",
                "find" => "api.find(name) — find an object and return its script proxy.",
                "spawn" => "api.spawn(kind, options) — create a runtime object.",
                "destroy" => "api.destroy(name) — destroy an object by name.",
                "purge" => "api.purge() — remove destroyed objects from the scene list.",
                "cam" => "api.cam(x, y, zoom) — set the camera position and zoom.",
                "bg" => "api.bg(color1, color2) — set the scene background colors.",
                "save" => "api.save(key, value) — persist a string in the per-project save file.",
                "load" => "api.load(key) — read a string from the per-project persistent save file.",
                "mousex" or "mousey" => "World-space mouse position on the selected axis.",
                "click" => "api.click() — true on a mouse click frame.",
                "pick" => "api.pick() — name of the object under the mouse, or an empty string.",
                "play" => "api.play(assetName) — play an embedded WAV asset through the MonoGame runtime audio service.",
                _ => "PixelEngine Lua API member. Press Ctrl+F7 to validate Lua syntax."
            };
        }
        if (item.StartsWith("self.", StringComparison.Ordinal))
            return "Current object's script proxy: transform, velocity, state, memory, hit and destroy helpers.";
        if (item.StartsWith("function", StringComparison.Ordinal)) return "Lua lifecycle function. start() runs once; update(dt) runs every frame.";
        return "Language keyword or standard-library completion.";
    }

    private string GetCurrentToken()
    {
        var caret = _editor.SelectionStart;
        var start = caret;
        while (start > 0)
        {
            var ch = _editor.Text[start - 1];
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')) break;
            start--;
        }
        return _editor.Text.Substring(start, caret - start);
    }

    private void MoveCompletionSelection(int offset)
    {
        if (_completionList.Items.Count == 0) return;
        var index = Math.Clamp(_completionList.SelectedIndex + offset, 0, _completionList.Items.Count - 1);
        _completionList.SelectedIndex = index;
    }

    private void AcceptCompletion()
    {
        if (!_completionPopup.Visible || _completionList.SelectedItem is not string completion) return;
        var caret = _editor.SelectionStart;
        var start = caret;
        while (start > 0)
        {
            var ch = _editor.Text[start - 1];
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')) break;
            start--;
        }
        _editor.Select(start, caret - start);
        _editor.SelectedText = completion;
        _completionPopup.Close();
        _editor.Focus();
    }

    private void ToggleBookmark()
    {
        var line = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        if (!_bookmarks.Add(line)) _bookmarks.Remove(line);
        _gutter.Invalidate();
    }

    private void NavigateBookmark(int direction)
    {
        if (_bookmarks.Count == 0) return;
        var current = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var ordered = _bookmarks.OrderBy(x => x).ToArray();
        var next = direction > 0 ? ordered.FirstOrDefault(x => x > current, ordered[0]) : ordered.LastOrDefault(x => x < current, ordered[^1]);
        var index = _editor.GetFirstCharIndexFromLine(next);
        if (index >= 0) { _editor.Select(index, 0); _editor.ScrollToCaret(); _editor.Focus(); }
    }

    private void ChangeFontSize(float delta)
    {
        var size = Math.Clamp(_editor.Font.Size + delta, 7f, 24f);
        var old = _editor.Font;
        _editor.Font = new Font(old.FontFamily, size, old.Style, GraphicsUnit.Point);
        old.Dispose();
        _gutter.Invalidate();
    }

    private void UpdateBracketMatch()
    {
        if (_updatingBracketMatch || _highlighting || _editor.IsDisposed) return;
        _updatingBracketMatch = true;
        var selectionStart = _editor.SelectionStart;
        var selectionLength = _editor.SelectionLength;
        try
        {
            if (_bracketA >= 0 && _bracketA < _editor.TextLength)
            {
                _editor.Select(_bracketA, 1);
                _editor.SelectionBackColor = Theme.Viewport;
            }
            if (_bracketB >= 0 && _bracketB < _editor.TextLength)
            {
                _editor.Select(_bracketB, 1);
                _editor.SelectionBackColor = Theme.Viewport;
            }
            _bracketA = _bracketB = -1;

            var text = _editor.Text;
            var probe = selectionStart < text.Length && "()[]{}".Contains(text[selectionStart])
                ? selectionStart : selectionStart - 1;
            if (probe >= 0 && probe < text.Length)
            {
                var ch = text[probe];
                var isOpening = ch is '(' or '[' or '{';
                var isClosing = ch is ')' or ']' or '}';
                if (isOpening || isClosing)
                {
                    var target = ch switch
                    {
                        '(' => ')', '[' => ']', '{' => '}',
                        ')' => '(', ']' => '[', '}' => '{', _ => '\0'
                    };
                    var depth = 0;
                    var step = isOpening ? 1 : -1;
                    for (var i = probe; i >= 0 && i < text.Length; i += step)
                    {
                        if (text[i] == ch) depth++;
                        else if (text[i] == target)
                        {
                            depth--;
                            if (depth == 0)
                            {
                                _bracketA = Math.Min(probe, i);
                                _bracketB = Math.Max(probe, i);
                                break;
                            }
                        }
                    }
                }
            }

            if (_bracketA >= 0 && _bracketB >= 0)
            {
                var matchColor = Color.FromArgb(74, 91, 121);
                _editor.Select(_bracketA, 1);
                _editor.SelectionBackColor = matchColor;
                _editor.Select(_bracketB, 1);
                _editor.SelectionBackColor = matchColor;
            }
        }
        finally
        {
            _editor.Select(Math.Clamp(selectionStart, 0, _editor.TextLength), Math.Clamp(selectionLength, 0, Math.Max(0, _editor.TextLength - selectionStart)));
            _updatingBracketMatch = false;
        }
    }

    private void InsertSnippet(string snippet)
    {
        if (_editor.ReadOnly) return;
        var start = _editor.SelectionStart;
        _editor.SelectedText = snippet;
        var placeholder = snippet.IndexOf("condition", StringComparison.Ordinal);
        if (placeholder < 0) placeholder = snippet.IndexOf("ObjectName", StringComparison.Ordinal);
        if (placeholder < 0) placeholder = snippet.IndexOf("    " + Environment.NewLine, StringComparison.Ordinal);
        if (placeholder >= 0)
        {
            var length = snippet.Contains("condition", StringComparison.Ordinal) ? 9
                : snippet.Contains("ObjectName", StringComparison.Ordinal) ? 9 : 0;
            _editor.Select(start + placeholder + (length == 0 ? 4 : 0), length);
        }
        else _editor.SelectionStart = start + snippet.Length;
        _editor.Focus();
    }

    private void MoveCurrentLine(int direction)
    {
        var line = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var target = line + direction;
        if (target < 0 || target >= _editor.Lines.Length) return;
        var start = _editor.GetFirstCharIndexFromLine(line);
        var targetStart = _editor.GetFirstCharIndexFromLine(target);
        if (start < 0 || targetStart < 0) return;
        var end = line + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(line + 1) : _editor.TextLength;
        var targetEnd = target + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(target + 1) : _editor.TextLength;
        if (end < start || targetEnd < targetStart) return;
        var lines = _editor.Text.Substring(start, end - start);
        var targetLines = _editor.Text.Substring(targetStart, targetEnd - targetStart);
        var selection = _editor.SelectionStart - start;
        var selectionStart = Math.Min(start, targetStart);
        var selectionEnd = Math.Max(end, targetEnd);
        _editor.Select(selectionStart, Math.Max(0, selectionEnd - selectionStart));
        var replacement = direction < 0 ? lines + targetLines : targetLines + lines;
        _editor.SelectedText = replacement;
        var newStart = direction < 0 ? targetStart : start + targetLines.Length;
        _editor.SelectionStart = Math.Clamp(newStart + selection, 0, _editor.TextLength);
        _editor.ScrollToCaret();
    }

    /// <summary>Applies predictable block indentation without changing tokens or line order.</summary>
    private void ReindentDocument()
    {
        if (_editor.ReadOnly || _editor.TextLength == 0) return;
        var source = _editor.Text;
        var hadFinalNewline = source.EndsWith("\n", StringComparison.Ordinal);
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) { lines[i] = string.Empty; continue; }
            var structural = StripLineComment(trimmed);
            if (_language == "lua")
            {
                if (IsLuaBlockCloser(structural)) depth = Math.Max(0, depth - 1);
                lines[i] = new string(' ', depth * 4) + trimmed;
                if (IsLuaBlockOpener(structural)) depth++;
            }
            else
            {
                if (structural.StartsWith("}", StringComparison.Ordinal)) depth = Math.Max(0, depth - 1);
                lines[i] = new string(' ', depth * 4) + trimmed;
                if (structural.EndsWith("{", StringComparison.Ordinal)) depth++;
            }
        }
        var formatted = string.Join(Environment.NewLine, lines);
        if (hadFinalNewline && !formatted.EndsWith(Environment.NewLine, StringComparison.Ordinal)) formatted += Environment.NewLine;
        if (string.Equals(source, formatted, StringComparison.Ordinal)) return;
        var oldSelection = _editor.SelectionStart;
        var oldLine = _editor.GetLineFromCharIndex(oldSelection);
        var lineStart = _editor.GetFirstCharIndexFromLine(oldLine);
        var column = oldSelection - Math.Max(0, lineStart);
        _editor.Text = formatted;
        var targetLine = Math.Clamp(oldLine, 0, Math.Max(0, _editor.Lines.Length - 1));
        var targetStart = _editor.GetFirstCharIndexFromLine(targetLine);
        if (targetStart >= 0)
        {
            var targetLength = _editor.Lines[targetLine].Length;
            _editor.Select(Math.Min(_editor.TextLength, targetStart + Math.Min(column, targetLength)), 0);
            _editor.ScrollToCaret();
        }
        _editor.Focus();
        SetDiagnostic(null);
    }

    private static string StripLineComment(string line)
    {
        var quote = '\0';
        var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (escaped) { escaped = false; continue; }
            if (quote != '\0' && ch == '\\') { escaped = true; continue; }
            if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
            if (ch is '\'' or '"') { quote = ch; continue; }
            if (line.AsSpan(i).StartsWith("--", StringComparison.Ordinal) || line.AsSpan(i).StartsWith("//", StringComparison.Ordinal))
                return line[..i].TrimEnd();
        }
        return line;
    }

    private static bool IsLuaBlockCloser(string line) =>
        Regex.IsMatch(line, @"^(?:end|until|else\b|elseif\b)", RegexOptions.CultureInvariant);

    private static bool IsLuaBlockOpener(string line)
    {
        if (line.Length == 0) return false;
        if (Regex.IsMatch(line, @"^(?:local\s+)?function\b|\bfunction\s*\(", RegexOptions.CultureInvariant)) return true;
        if (line == "repeat" || line == "do") return true;
        return line.EndsWith("then", StringComparison.Ordinal)
            || line.EndsWith("do", StringComparison.Ordinal);
    }

    private void InsertIndentedNewLine()
    {
        var caret = _editor.SelectionStart;
        var line = _editor.GetLineFromCharIndex(caret);
        var lineStart = _editor.GetFirstCharIndexFromLine(line);
        if (lineStart < 0) lineStart = 0;
        var currentLine = _editor.Text.Substring(lineStart, Math.Max(0, caret - lineStart));
        var indent = new string(currentLine.TakeWhile(ch => ch is ' ' or '\t').ToArray());
        var trimmed = currentLine.TrimEnd();
        var closesLuaBlock = _language == "lua" && (trimmed.StartsWith("end", StringComparison.Ordinal) || trimmed.StartsWith("else", StringComparison.Ordinal) || trimmed.StartsWith("elseif", StringComparison.Ordinal) || trimmed.StartsWith("until", StringComparison.Ordinal));
        var closesBraceBlock = _language == "csharp" && trimmed.StartsWith("}", StringComparison.Ordinal);
        if ((closesLuaBlock || closesBraceBlock) && indent.Length >= 4) indent = indent[..^4];
        var opensBlock = (_language == "lua" && (trimmed.EndsWith("then", StringComparison.Ordinal) || trimmed.EndsWith("do", StringComparison.Ordinal) || trimmed.StartsWith("function ", StringComparison.Ordinal)))
                         || trimmed.EndsWith("{", StringComparison.Ordinal);
        if (opensBlock) indent += "    ";
        var next = caret < _editor.TextLength ? _editor.Text[caret] : '\0';
        if (opensBlock && next == '}')
        {
            var insertion = Environment.NewLine + indent + Environment.NewLine + new string(indent.Take(Math.Max(0, indent.Length - 4)).ToArray());
            _editor.SelectedText = insertion;
            _editor.SelectionStart = caret + Environment.NewLine.Length + indent.Length;
        }
        else InsertText(Environment.NewLine + indent);
    }

    private void InsertText(string text)
    {
        _editor.SelectedText = text;
        _editor.Focus();
    }

    private void DuplicateCurrentLine()
    {
        var line = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var start = _editor.GetFirstCharIndexFromLine(line);
        if (start < 0) return;
        var end = line + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(line + 1) : _editor.TextLength;
        if (end < start) end = _editor.TextLength;
        var text = _editor.Text.Substring(start, end - start);
        _editor.SelectionStart = end;
        _editor.SelectionLength = 0;
        _editor.SelectedText = text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal) ? Environment.NewLine + text : text;
        _editor.SelectionStart = start + text.Length;
    }

    private void DeleteCurrentLine()
    {
        var line = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var start = _editor.GetFirstCharIndexFromLine(line);
        if (start < 0) return;
        var end = line + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(line + 1) : _editor.TextLength;
        if (end == start && start > 0) start--;
        _editor.Select(start, Math.Max(0, end - start));
        _editor.SelectedText = string.Empty;
    }

    private void ToggleLineComment()
    {
        var prefix = _language == "lua" ? "--" : "//";
        var firstLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var lastLine = _editor.GetLineFromCharIndex(_editor.SelectionStart + _editor.SelectionLength);
        var start = _editor.GetFirstCharIndexFromLine(firstLine);
        var end = lastLine + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(lastLine + 1) : _editor.TextLength;
        if (start < 0 || end < start) return;
        var block = _editor.Text.Substring(start, end - start);
        var lines = block.Split('\n');
        var shouldUncomment = lines.Where(x => !string.IsNullOrWhiteSpace(x)).All(x => x.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        for (var i = 0; i < lines.Length; i++)
        {
            var indentLength = lines[i].TakeWhile(ch => ch is ' ' or '\t').Count();
            if (shouldUncomment)
            {
                if (lines[i].Length >= indentLength + prefix.Length && lines[i].Substring(indentLength).StartsWith(prefix, StringComparison.Ordinal))
                {
                    lines[i] = lines[i].Remove(indentLength, prefix.Length);
                    if (lines[i].Length > indentLength && lines[i][indentLength] == ' ') lines[i] = lines[i].Remove(indentLength, 1);
                }
            }
            else lines[i] = lines[i].Insert(indentLength, prefix + " ");
        }
        _editor.Select(start, end - start);
        _editor.SelectedText = string.Join("\n", lines);
    }

    private void IndentSelection()
    {
        var startLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var selectionEnd = _editor.SelectionStart + _editor.SelectionLength;
        var endLine = _editor.GetLineFromCharIndex(selectionEnd);
        var endLineStart = _editor.GetFirstCharIndexFromLine(endLine);
        if (selectionEnd > _editor.SelectionStart && selectionEnd == endLineStart) endLine--;
        var start = _editor.GetFirstCharIndexFromLine(startLine);
        var end = endLine + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(endLine + 1) : _editor.TextLength;
        if (start < 0 || end < start) return;
        var originalSelectionStart = _editor.SelectionStart;
        var originalSelectionEnd = selectionEnd;
        var lines = _editor.Text.Substring(start, end - start).Split('\n');
        for (var i = 0; i < lines.Length; i++) lines[i] = "    " + lines[i];
        var replacement = string.Join("\n", lines);
        _editor.Select(start, end - start);
        _editor.SelectedText = replacement;
        var delta = replacement.Length - (end - start);
        _editor.Select(originalSelectionStart + 4, Math.Max(0, originalSelectionEnd - originalSelectionStart + delta - 4));
    }

    private void UnindentSelection()
    {
        var startLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
        var originalSelectionStart = _editor.SelectionStart;
        var originalSelectionEnd = _editor.SelectionStart + _editor.SelectionLength;
        var endLine = _editor.GetLineFromCharIndex(originalSelectionEnd);
        var endLineStart = _editor.GetFirstCharIndexFromLine(endLine);
        if (originalSelectionEnd > originalSelectionStart && originalSelectionEnd == endLineStart) endLine--;
        var start = _editor.GetFirstCharIndexFromLine(startLine);
        var end = endLine + 1 < _editor.Lines.Length ? _editor.GetFirstCharIndexFromLine(endLine + 1) : _editor.TextLength;
        if (start < 0 || end < start) return;
        var sourceLines = _editor.Text.Substring(start, end - start).Split('\n');
        var lines = sourceLines.ToArray();
        var removedBeforeStart = CountRemovedIndent(sourceLines, originalSelectionStart - start);
        var removedBeforeEnd = CountRemovedIndent(sourceLines, originalSelectionEnd - start);
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("    ", StringComparison.Ordinal)) lines[i] = lines[i][4..];
            else if (lines[i].StartsWith("\t", StringComparison.Ordinal)) lines[i] = lines[i][1..];
        }
        var replacement = string.Join("\n", lines);
        _editor.Select(start, end - start);
        _editor.SelectedText = replacement;
        var newStart = Math.Max(start, originalSelectionStart - removedBeforeStart);
        var newEnd = Math.Max(newStart, originalSelectionEnd - removedBeforeEnd);
        _editor.Select(newStart, Math.Max(0, newEnd - newStart));
    }

    private static int CountRemovedIndent(string[] lines, int offset)
    {
        var cursor = 0;
        var removed = 0;
        foreach (var line in lines)
        {
            var remove = line.StartsWith("    ", StringComparison.Ordinal) ? 4 : line.StartsWith("\t", StringComparison.Ordinal) ? 1 : 0;
            var withinLine = Math.Clamp(offset - cursor, 0, line.Length);
            removed += Math.Min(remove, withinLine);
            cursor += line.Length + 1;
            if (offset < cursor) break;
        }
        return removed;
    }

    private void ShowScriptReference()
    {
        var example = ScriptReferenceDialog.Pick(this, _language);
        if (!string.IsNullOrWhiteSpace(example)) InsertSnippet(example);
        _editor.Focus();
    }

    private void ShowFind()
    {
        _findPanel.Visible = true;
        _findBox.Focus();
        _findBox.SelectAll();
    }

    private void UpdateFindButtons()
    {
        var hasQuery = !string.IsNullOrWhiteSpace(_findBox.Text);
        if (_findNextButton is not null) _findNextButton.Enabled = hasQuery;
        if (_findPreviousButton is not null) _findPreviousButton.Enabled = hasQuery;
        if (_replaceCurrentButton is not null) _replaceCurrentButton.Enabled = hasQuery;
        if (_replaceAllButton is not null) _replaceAllButton.Enabled = hasQuery;
    }

    private void FindNext()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query)) return;
        var start = Math.Min(_editor.TextLength, _editor.SelectionStart + Math.Max(1, _editor.SelectionLength));
        var index = _editor.Find(query, start, RichTextBoxFinds.None);
        if (index < 0 && start > 0) index = _editor.Find(query, 0, RichTextBoxFinds.None);
        if (index >= 0)
        {
            _editor.Select(index, query.Length);
            _editor.ScrollToCaret();
            _editor.Focus();
        }
        else { /* no match: keep focus and selection unchanged */ }
    }

    private void FindPrevious()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query)) return;
        var start = Math.Max(0, _editor.SelectionStart - 1);
        var index = _editor.Find(query, 0, start, RichTextBoxFinds.Reverse);
        if (index >= 0)
        {
            _editor.Select(index, query.Length);
            _editor.ScrollToCaret();
            _editor.Focus();
        }
    }

    private void ReplaceCurrent()
    {
        if (_editor.SelectionLength == _findBox.Text.Length && _findBox.Text.Length > 0 &&
            string.Equals(_editor.SelectedText, _findBox.Text, StringComparison.OrdinalIgnoreCase))
        {
            _editor.SelectedText = _replaceBox.Text;
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query)) return;
        var source = _editor.Text;
        var count = Regex.Matches(source, Regex.Escape(query), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
        if (count == 0) return;
        var caret = _editor.SelectionStart;
        _editor.Text = Regex.Replace(source, Regex.Escape(query), _ => _replaceBox.Text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _editor.SelectionStart = Math.Min(caret, _editor.TextLength);
        _editor.Focus();
    }

    private void GoToLine(int zeroBasedLine)
    {
        var line = Math.Clamp(zeroBasedLine, 0, Math.Max(0, _editor.Lines.Length - 1));
        var index = _editor.GetFirstCharIndexFromLine(line);
        if (index < 0) return;
        _editor.SelectionStart = index;
        _editor.SelectionLength = 0;
        _editor.ScrollToCaret();
        _editor.Focus();
    }

    private void UpdateOutline()
    {
        if (IsDisposed) return;
        _outlineTimer.Stop();
        _outlineTimer.Start();
    }

    private void RebuildOutline()
    {
        if (_editor.IsDisposed || _outlineSelector.IsDisposed) return;
        var previous = _outlineSelector.SelectedItem as OutlineEntry;
        _outlineSelector.BeginUpdate();
        _outlineSelector.Items.Clear();
        _outlineSelector.Items.Add("(Script Outline)");
        var text = _editor.Text;
        var pattern = _language == "lua"
            ? @"^\s*(?:local\s+)?function\s+([A-Za-z_][\w\.]*)"
            : @"^\s*(?:(?:public|private|protected|internal|static|virtual|override|async|new|sealed|partial)\s+)*(?:[A-Za-z_][\w<>,.\[\]?]*\s+)([A-Za-z_]\w*)\s*\(";
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            var line = _editor.GetLineFromCharIndex(match.Index);
            var name = match.Groups[1].Value;
            _outlineSelector.Items.Add(new OutlineEntry(name, line));
        }
        _outlineSelector.SelectedIndex = 0;
        if (previous is not null)
        {
            for (var i = 1; i < _outlineSelector.Items.Count; i++)
            {
                if (_outlineSelector.Items[i] is OutlineEntry entry && entry.Name == previous.Name && entry.Line == previous.Line)
                { _outlineSelector.SelectedIndex = i; break; }
            }
        }
        _outlineSelector.EndUpdate();
    }

    private void GoToLineDialog()
    {
        using var dialog = new GoToLineForm(_editor.Lines.Length, _editor.GetLineFromCharIndex(_editor.SelectionStart) + 1);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        GoToLine(dialog.LineNumber - 1);
    }

    private void ScheduleHighlight()
    {
        if (_highlighting || IsDisposed) return;
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void HighlightSyntax()
    {
        if (_highlighting || _editor.IsDisposed || _editor.TextLength == 0 ||
            _editor.ClientSize.Width <= 0 || _editor.ClientSize.Height <= 0) return;

        // RichTextBox formats text by sending native selection/format messages. Selecting each
        // token in a large script thousands of times on the UI thread makes the whole window
        // appear frozen and repeatedly scrolls the document. Only format complete lines currently
        // visible in the viewport; scrolling schedules the next small formatting pass.
        var text = _editor.Text;
        var firstVisible = _editor.GetCharIndexFromPosition(new Point(1, 1));
        var lastVisible = _editor.GetCharIndexFromPosition(new Point(
            Math.Max(1, _editor.ClientSize.Width - 2),
            Math.Max(1, _editor.ClientSize.Height - 2)));
        var (rangeStart, rangeLength) = GetVisibleTextRange(text, firstVisible, lastVisible);
        if (rangeLength <= 0) return;

        _highlighting = true;
        var selectionStart = _editor.SelectionStart;
        var selectionLength = _editor.SelectionLength;
        var visibleText = text.Substring(rangeStart, rangeLength);
        try
        {
            _editor.Select(rangeStart, rangeLength);
            _editor.SelectionColor = Color.FromArgb(220, 225, 235);
            _editor.SelectionBackColor = Theme.Viewport;
            ApplyColor(visibleText, _language == "lua" ? @"\b(and|break|do|else|elseif|end|false|for|function|if|in|local|nil|not|or|repeat|return|then|true|until|while)\b" : @"\b(abstract|as|base|bool|break|byte|case|catch|char|class|const|continue|decimal|default|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|while)\b", Color.FromArgb(105, 165, 245), rangeStart);
            ApplyColor(visibleText, @"\b\d+(?:\.\d+)?\b", Color.FromArgb(185, 145, 235), rangeStart);
            ApplyColor(visibleText, "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'", Color.FromArgb(220, 170, 115), rangeStart);
            ApplyColor(visibleText, _language == "lua" ? @"\bfunction\s+([A-Za-z_][\w\.]*)" : @"\b([A-Za-z_][\w]*)\s*(?=\()", Color.FromArgb(105, 205, 170), rangeStart);
            ApplyColor(visibleText, _language == "lua" ? @"--[^\r\n]*" : @"//[^\r\n]*|/\*[\s\S]*?\*/", Color.FromArgb(125, 145, 135), rangeStart);

            var safeSelectionStart = Math.Clamp(selectionStart, 0, _editor.TextLength);
            var safeSelectionLength = Math.Clamp(selectionLength, 0, Math.Max(0, _editor.TextLength - safeSelectionStart));
            _editor.Select(safeSelectionStart, safeSelectionLength);
        }
        finally
        {
            _highlighting = false;
            _gutter.Invalidate();
            UpdateStatus();
            UpdateBracketMatch();
        }
    }

    // Kept pure so viewport-range behaviour is covered by the headless editor tests.
    internal static (int Start, int Length) GetVisibleTextRange(string text, int firstVisibleChar, int lastVisibleChar)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);

        var first = Math.Clamp(firstVisibleChar, 0, text.Length - 1);
        var last = Math.Clamp(lastVisibleChar, first, text.Length - 1);
        var start = text.LastIndexOf('\n', first);
        start = start < 0 ? 0 : start + 1;

        var end = text.IndexOf('\n', last);
        end = end < 0 ? text.Length : end + 1;
        return (start, Math.Max(0, end - start));
    }

    private void ApplyColor(string text, string pattern, Color color, int offset)
    {
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            if (match.Length == 0 || match.Index + match.Length > text.Length) continue;
            _editor.Select(offset + match.Index, match.Length);
            _editor.SelectionColor = color;
        }
    }

    private void UpdateStatus()
    {
        var index = _editor.SelectionStart;
        var line = _editor.GetLineFromCharIndex(index) + 1;
        var lineStart = _editor.GetFirstCharIndexFromLine(line - 1);
        var column = index - Math.Max(0, lineStart) + 1;
        var selectionInfo = _editor.SelectionLength > 0 ? $"    {_editor.SelectionLength} selected" : string.Empty;
        _positionLabel.Text = $"Ln {line}, Col {column}    {_editor.TextLength} chars{selectionInfo}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _highlightTimer.Dispose();
            _outlineTimer.Dispose();
            _completionPopup.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record OutlineEntry(string Name, int Line)
    {
        public override string ToString() => $"{Name}  ·  line {Line + 1}";
    }

    private sealed class EditorTextBox : RichTextBox
    {
        public event EventHandler? Scrolled;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is 0x0115 or 0x0114 or 0x020A or 0x020E) Scrolled?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class LineNumberGutter : Control
    {
        private readonly RichTextBox _editor;
        private readonly HashSet<int> _bookmarks;
        private readonly Dictionary<int, string> _diagnostics;
        public LineNumberGutter(RichTextBox editor, HashSet<int> bookmarks, Dictionary<int, string> diagnostics)
        {
            _editor = editor;
            _bookmarks = bookmarks;
            _diagnostics = diagnostics;
            Cursor = Cursors.Hand;
            MouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var charIndex = _editor.GetCharIndexFromPosition(new Point(1, e.Y));
                var line = Math.Max(0, _editor.GetLineFromCharIndex(charIndex));
                if (!_bookmarks.Add(line)) _bookmarks.Remove(line);
                Invalidate();
            };
            BackColor = Color.FromArgb(24, 27, 34);
            ForeColor = Color.FromArgb(105, 115, 130);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            using var font = new Font("Cascadia Mono", 9f);
            var firstChar = _editor.GetCharIndexFromPosition(new Point(1, 1));
            var firstLine = Math.Max(0, _editor.GetLineFromCharIndex(firstChar));
            var currentLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
            for (var line = firstLine; line < Math.Min(_editor.Lines.Length, firstLine + Height / Math.Max(1, _editor.Font.Height) + 3); line++)
            {
                var charIndex = _editor.GetFirstCharIndexFromLine(line);
                if (charIndex < 0) continue;
                var position = _editor.GetPositionFromCharIndex(charIndex);
                if (position.Y < -font.Height || position.Y > Height) continue;
                if (line == currentLine)
                {
                    using var currentBrush = new SolidBrush(Color.FromArgb(80, 125, 185));
                    e.Graphics.FillRectangle(currentBrush, 0, position.Y, Width - 2, _editor.Font.Height + 2);
                }
                if (_bookmarks.Contains(line))
                {
                    using var marker = new SolidBrush(Color.FromArgb(235, 190, 90));
                    e.Graphics.FillEllipse(marker, 5, position.Y + 5, 7, 7);
                }
                if (_diagnostics.ContainsKey(line))
                {
                    using var errorMarker = new SolidBrush(Color.FromArgb(235, 95, 95));
                    e.Graphics.FillEllipse(errorMarker, 17, position.Y + 5, 7, 7);
                }
                var text = (line + 1).ToString();
                var size = e.Graphics.MeasureString(text, font);
                using var lineBrush = new SolidBrush(line == currentLine
                    ? Color.FromArgb(225, 235, 250) : ForeColor);
                e.Graphics.DrawString(text, font, lineBrush, Width - size.Width - 8, position.Y + 1);
            }
            using var pen = new Pen(Color.FromArgb(48, 55, 68));
            e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }
    }
}

internal sealed class GoToLineForm : Form
{
    private readonly NumericUpDown _line;
    public int LineNumber => (int)_line.Value;
    public GoToLineForm(int maximum, int current)
    {
        Text = "Go to line";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(280, 105);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        var label = new Label { Text = $"Line number (1–{Math.Max(1, maximum)}):", AutoSize = true, Location = new Point(12, 12), ForeColor = Theme.Text };
        _line = new NumericUpDown { Minimum = 1, Maximum = Math.Max(1, maximum), Value = Math.Clamp(current, 1, Math.Max(1, maximum)), Location = new Point(12, 36), Width = 250 };
        var ok = new Button { Text = "Go", DialogResult = DialogResult.OK, Location = new Point(182, 70), Width = 80 };
        Controls.AddRange([label, _line, ok]);
        AcceptButton = ok;
    }
}
