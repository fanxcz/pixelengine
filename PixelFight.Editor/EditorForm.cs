using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using PixelFight;

namespace PixelFightEditor;

// Точка входа редактора.
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new EditorForm());
    }
}

// Редактор в духе Unity: сверху Toolbar (Play/Pause/Step),
// слева Hierarchy, по центру Scene/Game, справа Inspector,
// снизу Project/Console. Тёмная тема, марионетки с суставами.
class EditorForm : Form
{
    // Unity-цвета
    static readonly Color Bg = Color.FromArgb(45, 45, 48);
    static readonly Color Panel = Color.FromArgb(56, 56, 60);
    static readonly Color Panel2 = Color.FromArgb(64, 64, 68);
    static readonly Color Accent = Color.FromArgb(0, 122, 204);
    static readonly Color Txt = Color.FromArgb(220, 220, 220);
    static readonly Color Dim = Color.FromArgb(150, 150, 155);

    readonly PixelFight.Game _game = new PixelFight.Game();
    readonly Bitmap _buf = new Bitmap(PixelFight.Game.W, PixelFight.Game.H);
    readonly Graphics _bg;
    readonly Timer _timer = new Timer { Interval = 16 };
    readonly PictureBox _view = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
    readonly HashSet<Keys> _keys = new HashSet<Keys>();
    Point _mouse = new Point(240, 135);
    bool _lmb, _rmb;

    // состояние редактора
    bool _playing = true;
    bool _sceneMode = true; // true = Scene (гизмо+перетаскивание), false = Game (играть)
    bool _build;
    int _tool;
    string _sel = "Player";
    bool _dragP, _dragE;
    int _fps, _frames;
    DateTime _fpsT = DateTime.Now;

    // виджеты
    TreeView _hier;
    Panel _insp;
    ListBox _console, _project;
    ToolStripStatusLabel _statusSel, _statusStat;
    ToolStripButton _btnPlay, _btnStep;
    TabControl _viewTabs;

    // общий движок: проект .lic (свои игры на C#/Lua), отдельным режимом от PixelFight
    LicProject _proj;
    Runtime _rt;
    bool _projMode, _running;
    string _licPath;
    TabControl _bottomTabs;
    ListBox _scriptList;
    RichTextBox _scriptBox;
    Label _scriptLang, _scriptMsg;
    string _editingScript;
    ToolStripButton _btnRun;

    // панорама свободной камеры
    bool _pan;
    Point _panAt;

    public EditorForm()
    {
        Text = "PixelFight Engine — Unity-style editor  •  Scene/Game  •  марионетки: F пинок, E рука";
        Icon = PixelFight.GameIcon.Get();
        ClientSize = new Size(1380, 820);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BackColor = Bg;
        ForeColor = Txt;
        Font = new Font("Segoe UI", 9);

        _bg = Graphics.FromImage(_buf);
        _bg.InterpolationMode = InterpolationMode.NearestNeighbor;
        Sfx.Init();

        // --- меню ---
        var menu = new MenuStrip { BackColor = Panel, ForeColor = Txt };
        var file = new ToolStripMenuItem("File") { ForeColor = Txt };
        file.DropDownItems.Add("Скриншот на рабочий стол", null, (s, e) => Shot());
        file.DropDownItems.Add("Сохранить арену", null, (s, e) => { _game.SaveArena(); Log("Арена сохранена."); });
        file.DropDownItems.Add("Загрузить арену", null, (s, e) => { _game.LoadArena(); Log("Арена загружена: " + _game.DecorCount()); RefreshHier(); });
        file.DropDownItems.Add("Выход", null, (s, e) => Close());
        var edit = new ToolStripMenuItem("Puppet") { ForeColor = Txt };
        edit.DropDownItems.Add("Пинок игрока (F)", null, (s, e) => _game.KickPlayer());
        edit.DropDownItems.Add("Тычок рукой (E)", null, (s, e) => _game.JabPlayer());
        edit.DropDownItems.Add("Лечение всех", null, (s, e) => _game.Heal());
        var help = new ToolStripMenuItem("Help") { ForeColor = Txt };
        help.DropDownItems.Add("Управление", null, (s, e) =>
            MessageBox.Show("Scene: тяни бойцов мышью, клик ставит декор (Build).\nGame: мышь крутит оружейную руку, ЛКМ рывок, ПКМ блок, F пинок ногой, E тычок рукой, A/D движение, W прыжок.",
            "Как пользоваться", MessageBoxButtons.OK, MessageBoxIcon.Information));
        var proj = new ToolStripMenuItem("Project") { ForeColor = Txt };
        proj.DropDownItems.Add("New game (демо)", null, (s, e) => ProjNew());
        proj.DropDownItems.Add("New strategy Ages", null, (s, e) => ProjStrategy());
        proj.DropDownItems.Add("New strategy WORLD (карта GitHub)", null, (s, e) => ProjWorld());
        proj.DropDownItems.Add("New merchant (экономика)", null, (s, e) => ProjMerchant());
        proj.DropDownItems.Add("New tower defense", null, (s, e) => ProjTower());
        proj.DropDownItems.Add("New knights (battle)", null, (s, e) => ProjKnights());
        proj.DropDownItems.Add("Open .lic ...", null, (s, e) => ProjOpen());
        proj.DropDownItems.Add("Save .lic", null, (s, e) => ProjSave(false));
        proj.DropDownItems.Add("Save .lic as ...", null, (s, e) => ProjSave(true));
        proj.DropDownItems.Add("Import PNG asset ...", null, (s, e) => ProjImportPng());
        proj.DropDownItems.Add("Import WAV asset ...", null, (s, e) => ProjImportWav());
        proj.DropDownItems.Add("New platformer demo", null, (s, e) => ProjDemo());
        proj.DropDownItems.Add("Export .exe игры ...", null, (s, e) => ProjExport());
        proj.DropDownItems.Add("Close project", null, (s, e) => ProjClose());
        menu.Items.Add(file); menu.Items.Add(edit); menu.Items.Add(proj); menu.Items.Add(help);
        Controls.Add(menu);

        // --- тулбар Play/Pause/Step ---
        var tool = new ToolStrip { BackColor = Panel2, ForeColor = Txt, GripStyle = ToolStripGripStyle.Hidden };
        _btnPlay = new ToolStripButton("⏸ Пауза", null, (s, e) => TogglePlay()) { ForeColor = Txt };
        _btnStep = new ToolStripButton("⏭ Шаг", null, (s, e) => StepOnce()) { ForeColor = Txt };
        var btnFight = new ToolStripButton("▶ В бой", null, (s, e) => { _game.Restart(); Log("Рестарт матча."); }) { ForeColor = Txt };
        var btnBoom = new ToolStripButton("БУМ", null, (s, e) => _game.Boom()) { ForeColor = Txt };
        var btnShot = new ToolStripButton("Скриншот", null, (s, e) => Shot()) { ForeColor = Txt };
        tool.Items.Add(btnFight); tool.Items.Add(_btnPlay); tool.Items.Add(_btnStep);
        tool.Items.Add(new ToolStripSeparator());
        _btnRun = new ToolStripButton("▶ Run проект", null, (s, e) => ToggleRun()) { ForeColor = Txt };
        tool.Items.Add(_btnRun);
        tool.Items.Add(new ToolStripSeparator());
        tool.Items.Add(btnBoom); tool.Items.Add(btnShot);
        tool.Items.Add(new ToolStripLabel("  Scene/Game — вкладки по центру. Scene: тяни кукол. Game: играй.") { ForeColor = Dim });
        Controls.Add(tool);

        // --- раскладка: таблица (меню / тулбар / центр / низ / статус) — не разъезжается ---
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        Controls.SetChildIndex(menu, 1);
        root.Controls.Add(menu, 0, 0);
        root.Controls.Add(tool, 0, 1);

        // --- центр: Hierarchy | Scene/Game | Inspector (фиксированные бока, растягивающийся центр) ---
        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Bg, Margin = new Padding(0) };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
        root.Controls.Add(mid, 0, 2);
        // иерархия слева
        var leftP = new Panel { Dock = DockStyle.Fill, BackColor = Panel };
        var hierTitle = new Label { Text = "Hierarchy", Dock = DockStyle.Top, Height = 24, ForeColor = Dim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 4, 0, 0) };
        _hier = new TreeView { Dock = DockStyle.Fill, BackColor = Color.FromArgb(37, 37, 40), ForeColor = Txt, BorderStyle = BorderStyle.FixedSingle, ShowLines = false, HideSelection = false, FullRowSelect = true };
        _hier.AfterSelect += (s, e) => { _sel = e.Node.Name; BuildInspector(); };
        leftP.Controls.Add(_hier);
        leftP.Controls.Add(hierTitle);
        mid.Controls.Add(leftP, 0, 0);

        // центр: Scene/Game — ОДИН PictureBox, вкладки только переключают режим
        var centerP = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(0, 0, 0, 22) };
        _viewTabs = new TabControl { Dock = DockStyle.Top, Height = 26, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(90, 22) };
        var tabScene = new TabPage("🔧 Scene");
        var tabGame = new TabPage("🎮 Game");
        _viewTabs.TabPages.Add(tabScene); _viewTabs.TabPages.Add(tabGame);
        _viewTabs.SelectedIndexChanged += (s, e) => { _sceneMode = _viewTabs.SelectedIndex == 0; };
        var hint = new Label { Text = "Scene: тяни кукол, клик — декор (Build)  •  Game: мышь — рука, ЛКМ — рывок, ПКМ — блок, F — нога, E — рука", Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.White, BackColor = Color.FromArgb(30, 30, 30), TextAlign = ContentAlignment.MiddleCenter };
        var viewHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        viewHost.Controls.Add(_view);
        centerP.Controls.Add(viewHost);
        centerP.Controls.Add(hint);
        centerP.Controls.Add(_viewTabs);
        mid.Controls.Add(centerP, 1, 0);

        // инспектор справа
        var inspHost = new Panel { Dock = DockStyle.Fill, BackColor = Panel };
        inspHost.Controls.Add(new Label { Text = "Inspector", Dock = DockStyle.Top, Height = 24, ForeColor = Dim, Padding = new Padding(6, 4, 0, 0) });
        _insp = new Panel { Dock = DockStyle.Fill, BackColor = Panel, AutoScroll = true, Padding = new Padding(8) };
        inspHost.Controls.Add(_insp);
        mid.Controls.Add(inspHost, 2, 0);

        // --- низ: Project / Console ---
        var bottom = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, SizeMode = TabSizeMode.Fixed, ItemSize = new Size(140, 22) };
        var tabProj = new TabPage("Project — префабы") { BackColor = Panel };
        _project = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(37, 37, 40), ForeColor = Txt, BorderStyle = BorderStyle.None };
        _project.Items.AddRange(new object[] {
            "[Оружие] Меч — быстрый", "[Оружие] Топор — мощный", "[Оружие] Булава — отброс", "[Оружие] Копьё — длинное", "[Оружие] Кинжал — укол", "[Оружие] Кистень — сквозь блок", "[Оружие] Пика — длинная", "[Оружие] Молот — сокрушает", "[Оружие] Лук — стреляет", "[Оружие] Алебарда — притяжка",
            "[Декор] Ель", "[Декор] Берёза", "[Декор] Сакура", "[Декор] Камень", "[Декор] Знамя", "[Декор] Факел",
            "[Арена] Япония: тории", "[Арена] Германия: замок", "[Арена] Русь: кремль", "[Арена] Франция: собор", "[Арена] Англия: донжон", "[Арена] Орда: юрты", "[Арена] Своя (стройка)",
        });
        _project.DoubleClick += (s, e) => UsePrefab();
        var projBtn = new Button { Text = "Применить", Dock = DockStyle.Right, Width = 120, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
        projBtn.Click += (s, e) => UsePrefab();
        tabProj.Controls.Add(_project); tabProj.Controls.Add(projBtn);
        var tabCon = new TabPage("Console") { BackColor = Panel };
        _console = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 32), ForeColor = Color.LightGreen, BorderStyle = BorderStyle.None };
        tabCon.Controls.Add(_console);
        bottom.TabPages.Add(tabProj); bottom.TabPages.Add(tabCon);
        bottom.TabPages.Add(BuildScriptsTab());
        _bottomTabs = bottom;
        root.Controls.Add(bottom, 0, 3);

        // статусбар
        var status = new StatusStrip { BackColor = Panel2, ForeColor = Txt, Dock = DockStyle.Fill };
        _statusSel = new ToolStripStatusLabel("Player") { ForeColor = Txt };
        _statusStat = new ToolStripStatusLabel("") { ForeColor = Dim };
        status.Items.Add(_statusSel); status.Items.Add(new ToolStripStatusLabel(" | ") { ForeColor = Dim }); status.Items.Add(_statusStat);
        root.Controls.Add(status, 0, 4);

        // ввод
        _view.Paint += (s, e) => { e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor; e.Graphics.DrawImage(_buf, _view.ClientRectangle); };
        _view.MouseMove += (s, e) => { ToBuf(e.Location); DragMove(e); };
        _view.MouseDown += (s, e) =>
        {
            ToBuf(e.Location);
            if (_projMode && _rt != null && _rt.Scene != null && _rt.Scene.freeCam && e.Button == MouseButtons.Right)
            { _pan = true; _panAt = e.Location; return; }
            if (_sceneMode) SceneClick(e);
            else { if (e.Button == MouseButtons.Left) _lmb = true; if (e.Button == MouseButtons.Right) _rmb = true; }
        };
        _view.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) { _lmb = false; _dragP = _dragE = false; }
            if (e.Button == MouseButtons.Right) { _rmb = false; _pan = false; }
        };
        _view.MouseMove += (s, e) =>
        {
            if (_pan && _projMode && _rt != null && _rt.Scene != null && _rt.Scene.freeCam && _view.Width > 0)
            {
                _rt.camX -= (e.Location.X - _panAt.X) * PixelFight.Game.W / (float)_view.Width / (float)(3 * _rt.zoom);
                _rt.camY -= (e.Location.Y - _panAt.Y) * PixelFight.Game.H / (float)_view.Height / (float)(3 * _rt.zoom);
                _panAt = e.Location;
            }
        };
        _view.DoubleClick += (s, e) =>
        {
            if (_projMode && _rt != null && _rt.Scene != null && _rt.Scene.freeCam)
            { _rt.camX = _rt.Scene.camX0; _rt.camY = _rt.Scene.camY0; _rt.zoom = 1; Log("Камера сброшена."); }
        };
        MouseWheel += (s, e) =>
        {
            if (!_projMode || _rt == null || _rt.Scene == null || !_rt.Scene.freeCam) return;
            if (!_view.ClientRectangle.Contains(_view.PointToClient(Cursor.Position))) return;
            _rt.zoom = Math.Max(0.6f, Math.Min(4f, _rt.zoom * (e.Delta > 0 ? 1.12f : 0.89f)));
        };
        KeyDown += (s, e) => { _keys.Add(e.KeyCode); if (e.KeyCode == Keys.Space) e.SuppressKeyPress = true; };
        KeyUp += (s, e) => _keys.Remove(e.KeyCode);

        RefreshHier();
        BuildInspector();
        Log("Editor запущен. Scene — строй и тяни марионеток, Game — играй. F — пинок, E — рука.");

        _timer.Tick += (s, e) =>
        {
            if (_projMode && _rt != null && _proj != null)
            {
                // в Game-вкладке мышь идёт в игру (клики), в Scene — таскает объекты
                var wpos = _rt.ScreenToWorld(_mouse.X, _mouse.Y);
                if (_running) _rt.Update(_keys, 1f / 60f, wpos.X, wpos.Y, !_sceneMode && _lmb);
                else _rt.SetMouse(wpos.X, wpos.Y, false);
                _rt.Render(_bg, _sceneMode);
                _view.Invalidate();
                _frames++;
                if ((DateTime.Now - _fpsT).TotalSeconds >= 0.5)
                {
                    _fps = (int)(_frames / (DateTime.Now - _fpsT).TotalSeconds);
                    _frames = 0; _fpsT = DateTime.Now;
                    _statusSel.Text = "Sel: " + _sel;
                    _statusStat.Text = _fps + " fps • " + (_running ? "RUN" : "EDIT") + " • " + _proj.name + " • сцена " + _rt.sceneIdx + " • объектов " + _rt.objs.Count;
                }
                return;
            }
            if (_playing && !_game.Paused)
                _game.Update(_keys, _sceneMode ? new Point(240, 135) : _mouse, _sceneMode ? false : _lmb, _sceneMode ? false : _rmb, 1f / 60f);
            _game.Render(_bg);
            if (_sceneMode) DrawGizmos(_bg);
            _view.Invalidate();
            _frames++;
            if ((DateTime.Now - _fpsT).TotalSeconds >= 0.5)
            {
                _fps = (int)(_frames / (DateTime.Now - _fpsT).TotalSeconds);
                _frames = 0; _fpsT = DateTime.Now;
                _statusSel.Text = "Sel: " + _sel;
                _statusStat.Text = _fps + " fps • " + _game.GetStateName() + " • " + _game.GetStats() + " • декор " + _game.DecorCount();
                RefreshHierCounts();
            }
        };
        _timer.Start();
    }

    void TogglePlay() { _playing = !_playing; _btnPlay.Text = _playing ? "⏸ Пауза" : "▶ Play"; Log(_playing ? "Play." : "Пауза."); }
    void StepOnce()
    {
        _playing = false; _btnPlay.Text = "▶ Play";
        _game.Update(_keys, _mouse, _lmb, _rmb, 1f / 60f);
        Log("Шаг 1/60с.");
    }

    void ToBuf(Point p)
    {
        _mouse = new Point(
            Math.Max(0, Math.Min(PixelFight.Game.W - 1, p.X * PixelFight.Game.W / Math.Max(1, _view.Width))),
            Math.Max(0, Math.Min(PixelFight.Game.H - 1, p.Y * PixelFight.Game.H / Math.Max(1, _view.Height))));
    }

    // --- Scene-режим: выбор и перетаскивание кукол, стройка декора ---
    void SceneClick(MouseEventArgs e)
    {
        if (_projMode && _rt != null)
        {
            var w = _rt.ScreenToWorld(_mouse.X, _mouse.Y);
            if (e.Button != MouseButtons.Left) return;
            RuntimeObj best = null;
            float bd = 20;
            foreach (var o in _rt.objs)
            {
                if (o.dead) continue;
                float d = Math.Max(Math.Abs(o.x - w.X) - o.w / 2, Math.Abs(o.y - w.Y) - o.h / 2);
                if (d < bd) { bd = d; best = o; }
            }
            if (best != null)
            {
                int oi = _rt.objs.IndexOf(best);
                _sel = "PObj:" + _rt.sceneIdx + ":" + oi;
                _dragP = true;
                SelectInHier(_sel); BuildInspector();
            }
            return;
        }
        float wx = _game.WorldX(_mouse.X);
        float px = _game.GetPlayerX(), ex = _game.GetEnemyX();
        if (e.Button == MouseButtons.Right) { _game.RemoveDecorNear(wx, 25); Log("Декор убран у x=" + (int)wx); RefreshHier(); return; }
        if (e.Button != MouseButtons.Left) return;
        if (Math.Abs(wx - px) < 14) { _sel = "Player"; _dragP = true; SelectInHier("Player"); return; }
        if (Math.Abs(wx - ex) < 14) { _sel = "Enemy"; _dragE = true; SelectInHier("Enemy"); return; }
        if (_build)
        {
            _game.AddDecor(_tool, wx);
            Log("Декор " + _tool + " at x=" + (int)wx);
            RefreshHier();
        }
    }
    void DragMove(MouseEventArgs e)
    {
        if (_projMode && _rt != null)
        {
            if (!_sceneMode || e.Button != MouseButtons.Left || !_dragP) return;
            var o = ProjSelObj();
            if (o == null) { _dragP = false; return; }
            var w = _rt.ScreenToWorld(_mouse.X, _mouse.Y);
            o.x = w.X; o.y = w.Y; o.vx = 0; o.vy = 0;
            var p = _sel.Split(':');
            if (p.Length == 3 && int.TryParse(p[2], out int oi))
            {
                var defs = _proj.scenes[_rt.sceneIdx].objects;
                if (oi >= 0 && oi < defs.Count) { defs[oi].x = o.x; defs[oi].y = o.y; }
            }
            return;
        }
        if (!_sceneMode || e.Button != MouseButtons.Left) return;
        float wx = _game.WorldX(_mouse.X);
        if (_dragP) _game.SetPlayerX(wx);
        if (_dragE) _game.SetEnemyX(wx);
    }

    void DrawGizmos(Graphics g)
    {
        // рамка выбора вокруг кукол + маркеры декора + сетка земли
        void Box(float wx, Color c)
        {
            int sx = (int)((wx - _game.GetCamX()) * 3 + 240);
            using var p = new Pen(c, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(p, sx - 24, 60, 48, 130);
        }
        if (_sel == "Player") Box(_game.GetPlayerX(), Color.Yellow);
        if (_sel == "Enemy") Box(_game.GetEnemyX(), Color.Cyan);
        for (int i = 0; i < _game.DecorCount(); i++)
        {
            var d = _game.GetDecorAt(i);
            int sx = (int)((d.x - _game.GetCamX()) * 3 + 240);
            bool sel = _sel == "Decor" + i;
            g.FillRectangle(new SolidBrush(sel ? Color.Yellow : Color.FromArgb(0, 122, 204)), sx - 3, 176, 6, 6);
        }
        g.DrawString("SCENE", new Font(FontFamily.GenericMonospace, 8), Brushes.Yellow, 6, 6);
    }

    // --- Hierarchy ---
    void RefreshHier()
    {
        if (_projMode && _proj != null && _rt != null)
        {
            _hier.BeginUpdate();
            _hier.Nodes.Clear();
            var proot = _hier.Nodes.Add("Proj", "🎮 " + _proj.name + " (.lic)");
            for (int si = 0; si < _proj.scenes.Count; si++)
            {
                var scn = _proj.scenes[si];
                var sn = proot.Nodes.Add("PScene:" + si, "🌍 " + scn.name + (si == _rt.sceneIdx ? " ●" : ""));
                for (int oi = 0; oi < scn.objects.Count; oi++)
                {
                    var o = scn.objects[oi];
                    sn.Nodes.Add("PObj:" + si + ":" + oi, KindIcon(o.kind) + " " + o.name + (o.script != "" ? " 📜" : ""));
                }
            }
            var sc2 = proot.Nodes.Add("PScripts", "📜 Scripts (" + _proj.scripts.Count + ")");
            foreach (var s in _proj.scripts) sc2.Nodes.Add("PScript:" + s.name, (s.lang == "csharp" ? "C# " : "Lua ") + s.name);
            var am = proot.Nodes.Add("PAssets", "🖼 Assets (" + _proj.assets.Count + ")");
            foreach (var a in _proj.assets) am.Nodes.Add("PAsset:" + a.name, a.name);
            proot.Expand();
            SelectInHier(_sel);
            _hier.EndUpdate();
            return;
        }
        _hier.BeginUpdate();
        _hier.Nodes.Clear();
        var root = _hier.Nodes.Add("Scene", "🌍 Scene");
        root.Nodes.Add("Camera", "🎥 Main Camera");
        root.Nodes.Add("Player", "🤺 Player (марионетка)");
        root.Nodes.Add("Enemy", "🥷 Enemy (марионетка)");
        root.Nodes.Add("Arena", "🏟 Arena");
        var dec = root.Nodes.Add("DecorRoot", "🌲 Decor (" + _game.DecorCount() + ")");
        for (int i = 0; i < _game.DecorCount(); i++)
        {
            var d = _game.GetDecorAt(i);
            dec.Nodes.Add("Decor" + i, "— #" + i + " kind=" + d.kind + " x=" + d.x);
        }
        root.Nodes.Add("FX", "✨ FX / Time");
        root.Expand();
        SelectInHier(_sel);
        _hier.EndUpdate();
    }
    void RefreshHierCounts()
    {
        foreach (TreeNode n in _hier.Nodes.Find("DecorRoot", true))
            n.Text = "🌲 Decor (" + _game.DecorCount() + ")";
    }
    static string KindIcon(string k) => k switch { "circle" => "⭕", "sprite" => "🖼", "text" => "🔤", _ => "🟦" };
    void SelectInHier(string name)
    {
        foreach (TreeNode n in _hier.Nodes.Find(name, true)) { _hier.SelectedNode = n; return; }
        foreach (TreeNode n in _hier.Nodes.Find("Scene", true)) _hier.SelectedNode = n;
    }

    // --- Inspector ---
    Label H(string t) => new Label { Text = t, ForeColor = Color.Gold, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
    void Row(Control c) { c.Width = 250; _insp.Controls.Add(c); }
    TrackBar Slider(int min, int max, int val, Action<int> on)
    {
        var s = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), Width = 250, TickFrequency = (max - min) / 5 };
        s.Scroll += (a, b) => on(s.Value);
        return s;
    }

    void BuildInspector()
    {
        _insp.Controls.Clear();
        _insp.Controls.Add(new Label { Text = _sel, ForeColor = Txt, Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true });
        if (_projMode && _proj != null && (_sel == "Proj" || _sel.StartsWith("PScene") || _sel.StartsWith("PObj") || _sel.StartsWith("PScript") || _sel.StartsWith("PAsset")))
        {
            BuildProjInspector();
            return;
        }
        if (_sel == "Player" || _sel == "Enemy")
        {
            bool isP = _sel == "Player";
            _insp.Controls.Add(H(isP ? "Синяя кукла — руки/ноги с суставами" : "Красная кукла — ИИ или P2"));
            var hpL = new Label { Text = "HP: " + (isP ? _game.GetPlayerHP() : _game.GetEnemyHP()), ForeColor = Txt, AutoSize = true };
            Row(hpL);
            Row(Slider(1, (int)(isP ? _game.GetPlayerMaxHP() : _game.GetEnemyMaxHP()),
                (int)(isP ? _game.GetPlayerHP() : _game.GetEnemyHP()),
                v => { if (isP) _game.SetPlayerHP(v); else _game.SetEnemyHP(v); hpL.Text = "HP: " + v; }));
            var xL = new Label { Text = "X: " + (isP ? _game.GetPlayerX() : _game.GetEnemyX()), ForeColor = Txt, AutoSize = true };
            Row(xL);
            Row(Slider(-420, 420, (int)(isP ? _game.GetPlayerX() : _game.GetEnemyX()),
                v => { if (isP) _game.SetPlayerX(v); else _game.SetEnemyX(v); xL.Text = "X: " + v; }));
            _insp.Controls.Add(H("Оружие в правой руке"));
            var cb = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "Меч (быстрый)", "Топор (мощный)", "Булава (отброс)", "Копьё (длинное)", "Кинжал (укол)", "Кистень (сквозь блок)", "Пика (длинная)", "Молот (сокрушает)", "Лук (стреляет)", "Алебарда (притяжка)" });
            cb.SelectedIndex = isP ? _game.GetPlayerWeapon() : _game.GetEnemyWeapon();
            cb.SelectedIndexChanged += (s, e) => { if (isP) _game.SetPlayerWeapon(cb.SelectedIndex); else _game.SetEnemyWeapon(cb.SelectedIndex); Log(_sel + " оружие: " + cb.SelectedIndex); };
            Row(cb);
            _insp.Controls.Add(H("Конечности — удары"));
            var bk = new Button { Text = "🦵 Пинок ногой (F)", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            bk.Click += (s, e) => { if (isP) _game.KickPlayer(); else _game.KickEnemy(); };
            Row(bk);
            var bj = new Button { Text = "🤜 Тычок рукой (E)", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            bj.Click += (s, e) => { _game.JabPlayer(); };
            Row(bj);
            var bh = new Button { Text = "💚 Лечение", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            bh.Click += (s, e) => _game.Heal();
            Row(bh);
            if (isP)
            {
                var god = new CheckBox { Text = "Бессмертие", ForeColor = Txt, Checked = _game.GodMode, Width = 250 };
                god.CheckedChanged += (s, e) => _game.GodMode = god.Checked;
                Row(god);
            }
            else
            {
                _insp.Controls.Add(H("Злость ИИ, %"));
                Row(Slider(20, 300, (int)(_game.AggrMul * 100), v => _game.AggrMul = v / 100f));
            }
        }
        else if (_sel == "Camera")
        {
            _insp.Controls.Add(H("Камера едет за бойцами"));
            var cl = new Label { Text = "CamX: " + (int)_game.GetCamX(), ForeColor = Txt, AutoSize = true };
            Row(cl);
            Row(Slider(-340, 340, (int)_game.GetCamX(), v => { _game.SetCamX(v); cl.Text = "CamX: " + v; }));
            var bc = new Button { Text = "Центр (0)", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            bc.Click += (s, e) => { _game.SetCamX(0); BuildInspector(); };
            Row(bc);
            Row(new Label { Text = _game.GetStateName() + " • " + _game.GetStats(), ForeColor = Dim, Width = 250, Height = 36 });
        }
        else if (_sel == "Arena")
        {
            _insp.Controls.Add(H("Арена (страна)"));
            var cb = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "Япония: тории", "Германия: замок", "Русь: кремль", "Франция: собор", "Англия: донжон", "Орда: юрты", "Своя (стройка)" });
            cb.SelectedIndex = _game.GetArena();
            cb.SelectedIndexChanged += (s, e) => { _game.SetArena(cb.SelectedIndex); Log("Арена: " + cb.SelectedItem); };
            Row(cb);
            _insp.Controls.Add(H("Биом своей арены"));
            var cb2 = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            cb2.Items.AddRange(new object[] { "Япония", "Германия", "Русь" });
            cb2.SelectedIndex = _game.GetCustomBase();
            cb2.SelectedIndexChanged += (s, e) => { _game.SetCustomBase(cb2.SelectedIndex); _game.SetArena(6); };
            Row(cb2);
            foreach (var (t, a) in new[] { ("Играть на своей", (Action)(() => _game.SetArena(6))), ("Сохранить арену", () => { _game.SaveArena(); Log("Сохранено."); }), ("Загрузить арену", () => { _game.LoadArena(); RefreshHier(); }), ("Очистить декор", () => { _game.ClearArena(); RefreshHier(); }) })
            {
                var b = new Button { Text = t, Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => a();
                Row(b);
            }
        }
        else if (_sel.StartsWith("Decor"))
        {
            if (_sel == "DecorRoot")
            {
                _insp.Controls.Add(H("Стройка: инструмент ставит декор"));
                var build = new CheckBox { Text = "Build mode (ЛКМ ставит, ПКМ убирает)", ForeColor = Txt, Checked = _build, Width = 250, Height = 32 };
                build.CheckedChanged += (s, e) => _build = build.Checked;
                Row(build);
                string[] tools = { "Ель", "Берёза", "Сакура", "Камень", "Знамя", "Факел" };
                for (int i = 0; i < 6; i++)
                {
                    int ti = i;
                    var b = new Button { Text = (i == _tool ? "● " : "") + tools[i], Width = 250, Height = 26, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                    b.Click += (s, e) => { _tool = ti; _build = true; Log("Инструмент: " + tools[ti]); BuildInspector(); };
                    Row(b);
                }
            }
            else if (int.TryParse(_sel.Substring(5), out int idx) && idx < _game.DecorCount())
            {
                var d = _game.GetDecorAt(idx);
                _insp.Controls.Add(H("Декор #" + idx));
                var cb = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
                cb.Items.AddRange(new object[] { "Ель", "Берёза", "Сакура", "Камень", "Знамя", "Факел" });
                cb.SelectedIndex = d.kind;
                cb.SelectedIndexChanged += (s, e) => { _game.SetDecorAt(idx, cb.SelectedIndex, _game.GetDecorAt(idx).x); RefreshHier(); };
                Row(cb);
                var xl = new Label { Text = "X: " + d.x, ForeColor = Txt, AutoSize = true };
                Row(xl);
                Row(Slider(-420, 420, d.x, v => { _game.SetDecorAt(idx, _game.GetDecorAt(idx).kind, v); xl.Text = "X: " + v; }));
                var del = new Button { Text = "Удалить", Width = 250, Height = 28, BackColor = Color.FromArgb(120, 40, 40), ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                del.Click += (s, e) => { _game.RemoveDecorAt(idx); _sel = "DecorRoot"; RefreshHier(); BuildInspector(); };
                Row(del);
            }
        }
        else // FX
        {
            _insp.Controls.Add(H("Время и веселье"));
            Row(new Label { Text = "Время, %", ForeColor = Txt, AutoSize = true });
            Row(Slider(10, 200, (int)(_game.TimeMul * 100), v => _game.TimeMul = v / 100f));
            foreach (var (t, a) in new[] { ("+100 монет", (Action)(() => _game.GiveCoins(100))), ("Лечение всех", () => _game.Heal()), ("БУМ крови", () => _game.Boom()), ("Рестарт боя", () => _game.Restart()), ("Скриншот", () => Shot()) })
            {
                var b = new Button { Text = t, Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => a();
                Row(b);
            }
        }
    }

    NumericUpDown PNum(float val, float min, float max, Action<float> on)
    {
        var n = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Max(min, Math.Min(max, val)), Width = 250, BackColor = Color.FromArgb(37, 37, 40), ForeColor = Txt };
        n.ValueChanged += (s, e) => on((float)n.Value);
        return n;
    }
    TextBox PTxt(string val, Action<string> onApply)
    {
        var t = new TextBox { Text = val, Width = 250, BackColor = Color.FromArgb(37, 37, 40), ForeColor = Txt };
        t.Leave += (s, e) => onApply(t.Text);
        return t;
    }

    void BuildProjInspector()
    {
        if (_sel == "Proj")
        {
            _insp.Controls.Add(H("Игра (проект .lic)"));
            _insp.Controls.Add(H("Название"));
            Row(PTxt(_proj.name, v => { _proj.name = v; RefreshHier(); }));
            _insp.Controls.Add(H("Стартовая сцена"));
            Row(PNum(_proj.startScene, 0, Math.Max(0, _proj.scenes.Count - 1), v => _proj.startScene = (int)v));
            Row(new Label { Text = "Сцен: " + _proj.scenes.Count + " • скриптов: " + _proj.scripts.Count + " • ассетов: " + _proj.assets.Count, ForeColor = Dim, Width = 250, Height = 36 });
            foreach (var (t, a) in new[] { ("▶ Run", (Action)ToggleRun), ("Save .lic", () => ProjSave(false)), ("Import PNG", () => ProjImportPng()) })
            {
                var b = new Button { Text = t, Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => a();
                Row(b);
            }
            return;
        }
        if (_sel.StartsWith("PScene:") && int.TryParse(_sel.Substring(7), out int si) && si < _proj.scenes.Count)
        {
            var sc = _proj.scenes[si];
            _insp.Controls.Add(H("Сцена" + (si == _rt.sceneIdx ? " (играется ●)" : "")));
            _insp.Controls.Add(H("Название"));
            Row(PTxt(sc.name, v => { sc.name = v; RefreshHier(); }));
            _insp.Controls.Add(H("Гравитация"));
            Row(PNum(sc.gravity, 0, 3000, v => sc.gravity = v));
            _insp.Controls.Add(H("Земля Y"));
            Row(PNum(sc.groundY, -400, 400, v => sc.groundY = v));
            foreach (var (t, a) in new[] { ("Играть эту", (Action)(() => { _rt.Start(_proj, si); Log("Сцена: " + sc.name); })), ("+ rect", () => ProjAddObj(si, "rect")), ("+ circle", () => ProjAddObj(si, "circle")), ("+ text", () => ProjAddObj(si, "text")) })
            {
                var b = new Button { Text = t, Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => a();
                Row(b);
            }
            if (_proj.scenes.Count > 1)
            {
                var del = new Button { Text = "Удалить сцену", Width = 250, Height = 28, BackColor = Color.FromArgb(120, 40, 40), ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                del.Click += (s, e) => { _proj.scenes.RemoveAt(si); _rt.Start(_proj, 0); _sel = "Proj"; RefreshHier(); BuildInspector(); };
                Row(del);
            }
            return;
        }
        if (_sel.StartsWith("PObj:"))
        {
            var o = ProjSelObj();
            var p = _sel.Split(':');
            int oi = p.Length == 3 && int.TryParse(p[2], out int q) ? q : -1;
            if (o == null || oi < 0) return;
            var def = _proj.scenes[_rt.sceneIdx].objects[oi];
            _insp.Controls.Add(H("Объект (x,y — центр)"));
            _insp.Controls.Add(H("Имя (для api.find)"));
            Row(PTxt(def.name, v => { def.name = v; o.def.name = v; RefreshHier(); }));
            _insp.Controls.Add(H("Тип"));
            var cb = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "rect", "circle", "sprite", "text" });
            cb.SelectedItem = def.kind;
            cb.SelectedIndexChanged += (s, e) => { def.kind = cb.SelectedItem.ToString(); o.def.kind = def.kind; RefreshHier(); };
            Row(cb);
            _insp.Controls.Add(H("X / Y"));
            Row(PNum(def.x, -500, 500, v => { def.x = v; o.x = v; }));
            Row(PNum(def.y, -400, 400, v => { def.y = v; o.y = v; }));
            _insp.Controls.Add(H("W / H"));
            Row(PNum(def.w, 1, 400, v => { def.w = v; o.w = v; }));
            Row(PNum(def.h, 1, 400, v => { def.h = v; o.h = v; }));
            _insp.Controls.Add(H("Цвет #RRGGBB / текст"));
            Row(PTxt(def.color, v => { def.color = v; o.def.color = v; }));
            Row(PTxt(def.text, v => { def.text = v; o.def.text = v; }));
            if (_proj.assets.Count > 0)
            {
                _insp.Controls.Add(H("Спрайт"));
                var cs = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
                cs.Items.Add("");
                foreach (var a in _proj.assets) cs.Items.Add(a.name);
                cs.SelectedItem = def.sprite;
                cs.SelectedIndexChanged += (s, e) => { def.sprite = cs.SelectedItem.ToString(); o.def.sprite = def.sprite; if (def.kind != "sprite") { def.kind = "sprite"; o.def.kind = "sprite"; } };
                Row(cs);
            }
            foreach (var (t, get, set) in new[] {
                ("Твёрдый (на нём стоят)", def.solid, (Action<bool>)(v => { def.solid = v; o.def.solid = v; })),
                ("Гравитация", def.gravity, (Action<bool>)(v => { def.gravity = v; o.def.gravity = v; })),
                ("Камера за ним", def.follow, (Action<bool>)(v => { def.follow = v; o.def.follow = v; })) })
            {
                var f = new CheckBox { Text = t, ForeColor = Txt, Checked = get, Width = 250 };
                f.CheckedChanged += (s, e) => set(f.Checked);
                Row(f);
            }
            _insp.Controls.Add(H("Скрипт"));
            var sc2 = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            sc2.Items.Add("");
            foreach (var s in _proj.scripts) sc2.Items.Add(s.name);
            sc2.SelectedItem = def.script;
            sc2.SelectedIndexChanged += (s, e) =>
            {
                def.script = sc2.SelectedItem.ToString(); o.def.script = def.script;
                o.runner = null; o.error = ""; // перепривязка на следующем кадре
                RefreshHier();
            };
            Row(sc2);
            if (o.error != "") Row(new Label { Text = "⚠ " + o.error, ForeColor = Color.OrangeRed, Width = 250, Height = 48 });
            var del = new Button { Text = "Удалить объект", Width = 250, Height = 28, BackColor = Color.FromArgb(120, 40, 40), ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            del.Click += (s, e) => { _proj.scenes[_rt.sceneIdx].objects.RemoveAt(oi); _rt.Start(_proj, _rt.sceneIdx); _sel = "PScene:" + _rt.sceneIdx; RefreshHier(); BuildInspector(); };
            Row(del);
            return;
        }
        if (_sel == "PScripts")
        {
            _insp.Controls.Add(H("Скрипты: Lua — start()+update(dt), C# — тело = кадр"));
            var b1 = new Button { Text = "+ Lua", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            b1.Click += (s, e) => ScriptNew("lua");
            Row(b1);
            var b2 = new Button { Text = "+ C#", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            b2.Click += (s, e) => ScriptNew("csharp");
            Row(b2);
            return;
        }
        if (_sel.StartsWith("PScript:"))
        {
            string nm = _sel.Substring(8);
            var s = _proj.FindScript(nm);
            if (s == null) return;
            _insp.Controls.Add(H(s.lang == "csharp" ? "C#" : "Lua"));
            var b = new Button { Text = "Открыть в Scripts", Width = 250, Height = 28, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            b.Click += (se, e) =>
            {
                _bottomTabs.SelectedIndex = 2;
                for (int i = 0; i < _scriptList.Items.Count; i++)
                    if (_scriptList.Items[i].ToString().StartsWith(s.name + " ")) { _scriptList.SelectedIndex = i; break; }
            };
            Row(b);
            var del = new Button { Text = "Удалить скрипт", Width = 250, Height = 28, BackColor = Color.FromArgb(120, 40, 40), ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            del.Click += (se, e) => { _editingScript = null; _proj.scripts.Remove(s); RefreshScriptList(); RefreshHier(); _sel = "PScripts"; BuildInspector(); };
            Row(del);
            return;
        }
        // PAssets / PAsset
        _insp.Controls.Add(H("Ассеты PNG (внутри .lic)"));
        var bi = new Button { Text = "Import PNG", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
        bi.Click += (s, e) => ProjImportPng();
        Row(bi);
        if (_sel.StartsWith("PAsset:"))
        {
            string nm = _sel.Substring(7);
            var a = _proj.assets.Find(a => a.name == nm);
            if (a != null)
            {
                Row(new Label { Text = nm + " • " + (a.base64.Length * 3 / 4 / 1024) + " КБ • " + (a.type ?? "png"), ForeColor = Txt, Width = 250, Height = 30 });
                if ((a.type ?? "png") == "png")
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(a.base64);
                        using var ms = new MemoryStream(bytes, false);
                        using var bmp = new Bitmap(ms);
                        var pv = new PictureBox { Width = 250, Height = 120, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 32), Image = new Bitmap(bmp) };
                        Row(pv);
                    }
                    catch { }
                }
                else
                {
                    var pl = new Button { Text = "▶ Слушать", Width = 250, Height = 28, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                    pl.Click += (s, e) => _rt.PlayAsset(nm);
                    Row(pl);
                }
                var del = new Button { Text = "Удалить", Width = 250, Height = 28, BackColor = Color.FromArgb(120, 40, 40), ForeColor = Txt, FlatStyle = FlatStyle.Flat };
                del.Click += (s, e) => { _proj.assets.Remove(a); _rt.ReloadAssets(); RefreshHier(); _sel = "PAssets"; BuildInspector(); };
                Row(del);
            }
        }
    }

    void ProjAddObj(int si, string kind)
    {
        var d = new LicObject { name = kind + (_proj.scenes[si].objects.Count + 1), kind = kind, x = _rt.camX, y = _rt.camY - 40 };
        if (kind == "text") { d.text = "текст"; d.solid = false; d.gravity = false; }
        _proj.scenes[si].objects.Add(d);
        if (si == _rt.sceneIdx) { _rt.Start(_proj, si); _sel = "PObj:" + si + ":" + (_proj.scenes[si].objects.Count - 1); }
        RefreshHier(); BuildInspector();
    }

    void UsePrefab()
    {
        if (_project.SelectedItem == null) return;
        string s = _project.SelectedItem.ToString();
        if (s.StartsWith("[Оружие]")) { int w = s.Contains("Топор") ? 1 : s.Contains("Булава") ? 2 : s.Contains("Копь") ? 3 : s.Contains("Кинжал") ? 4 : s.Contains("Кистень") ? 5 : s.Contains("Пика") ? 6 : s.Contains("Молот") ? 7 : s.Contains("Лук") ? 8 : s.Contains("Алебарда") ? 9 : 0; _game.SetPlayerWeapon(w); Log("Оружие игрока: " + s); }
        else if (s.StartsWith("[Декор]")) { string[] t = { "Ель", "Берёза", "Сакура", "Камень", "Знамя", "Факел" }; _tool = Array.IndexOf(t, s.Substring(8).Trim()); _build = true; _sel = "DecorRoot"; BuildInspector(); Log("Кисть: " + s + " — кликай в Scene."); _viewTabs.SelectedIndex = 0; }
        else if (s.StartsWith("[Арена]")) { int a = s.Contains("Германия") ? 1 : s.Contains("Русь") ? 2 : s.Contains("Франция") ? 3 : s.Contains("Англия") ? 4 : s.Contains("Орда") ? 5 : s.Contains("Своя") ? 6 : 0; _game.SetArena(a); _sel = "Arena"; BuildInspector(); Log("Арена: " + s); }
    }

    // ---------- общий движок: проекты .lic, скрипты C#/Lua ----------

    TabPage BuildScriptsTab()
    {
        var tab = new TabPage("Scripts — C# / Lua") { BackColor = Panel };
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Bg, SplitterDistance = 200 };
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Panel };
        _scriptList = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(37, 37, 40), ForeColor = Txt, BorderStyle = BorderStyle.None };
        _scriptList.SelectedIndexChanged += (s, e) => ScriptSelect();
        var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30, BackColor = Panel, FlowDirection = FlowDirection.LeftToRight };
        foreach (var (t, a) in new[] { ("+Lua", (Action)(() => ScriptNew("lua"))), ("+C#", () => ScriptNew("csharp")), ("Del", () => ScriptDel()), ("Check", () => ScriptCheck()) })
        {
            var b = new Button { Text = t, Width = 58, Height = 24, BackColor = Panel2, ForeColor = Txt, FlatStyle = FlatStyle.Flat };
            b.Click += (s, e) => a();
            btns.Controls.Add(b);
        }
        left.Controls.Add(_scriptList); left.Controls.Add(btns);
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Panel };
        var top = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Panel };
        _scriptLang = new Label { Text = "—", Dock = DockStyle.Left, Width = 200, ForeColor = Dim, TextAlign = ContentAlignment.MiddleLeft };
        _scriptMsg = new Label { Text = "код применится при следующем ▶ Run", Dock = DockStyle.Fill, ForeColor = Dim, TextAlign = ContentAlignment.MiddleRight };
        top.Controls.Add(_scriptMsg); top.Controls.Add(_scriptLang);
        _scriptBox = new RichTextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, ScrollBars = RichTextBoxScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(30, 30, 32), ForeColor = Color.FromArgb(220, 220, 160), BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10), DetectUrls = false };
        _scriptBox.TextChanged += (s, e) => HighlightScript();
        right.Controls.Add(_scriptBox); right.Controls.Add(top);
        split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right);
        tab.Controls.Add(split);
        return tab;
    }

    void ScriptSaveBack()
    {
        if (_proj == null || _editingScript == null) return;
        var s = _proj.FindScript(_editingScript);
        if (s != null && s.code != _scriptBox.Text) { s.code = _scriptBox.Text; _scriptMsg.Text = "изменено • применится при ▶ Run"; }
    }
    void RefreshScriptList()
    {
        _scriptList.BeginUpdate();
        _scriptList.Items.Clear();
        if (_proj != null) foreach (var s in _proj.scripts) _scriptList.Items.Add(s.name + "  [" + s.lang + "]");
        _scriptList.EndUpdate();
    }
    void ScriptSelect()
    {
        ScriptSaveBack();
        _editingScript = null;
        if (_proj == null || _scriptList.SelectedItem == null) { _scriptBox.Text = ""; _scriptLang.Text = "—"; return; }
        string item = _scriptList.SelectedItem.ToString();
        string nm = item.Split(new[] { "  [" }, StringSplitOptions.None)[0];
        var s = _proj.FindScript(nm);
        if (s == null) return;
        _editingScript = s.name;
        _scriptBox.Text = s.code;
        _scriptLang.Text = s.name + "  •  " + (s.lang == "csharp" ? "C# (тело = кадр: self, api, dt, time)" : "Lua (start() + update(dt))");
        _scriptMsg.Text = "код применится при следующем ▶ Run";
    }
    void ScriptNew(string lang)
    {
        if (!EnsureProj()) return;
        ScriptSaveBack();
        string nm = lang == "csharp" ? "script" + _proj.scripts.Count + ".cs" : "script" + _proj.scripts.Count + ".lua";
        string code = lang == "csharp"
            ? "// " + nm + ": выполняется КАЖДЫЙ кадр\n// self.x, self.vx, self.ground ... api.log/key/spawn/find/destroy/rand/time\nself.vx = 0;\n"
            : "-- " + nm + ": start() один раз, update(dt) каждый кадр\nfunction update(dt)\n  self.vx = 0\nend\n";
        _proj.scripts.Add(new LicScript { name = nm, lang = lang, code = code });
        RefreshScriptList();
        _scriptList.SelectedIndex = _scriptList.Items.Count - 1;
        RefreshHier();
        Log("Скрипт: " + nm);
    }
    void ScriptDel()
    {
        if (_proj == null || _editingScript == null) return;
        _proj.scripts.RemoveAll(s => s.name == _editingScript);
        _editingScript = null; _scriptBox.Text = "";
        RefreshScriptList(); RefreshHier();
    }
    void ScriptCheck()
    {
        ScriptSaveBack();
        if (_proj == null || _editingScript == null) { Log("Check: нет скрипта."); return; }
        var s = _proj.FindScript(_editingScript);
        string err = PixelFight.ScriptFactory.CheckCode(s.lang, s.code);
        _scriptMsg.Text = err == "" ? "OK ✓" : "ОШИБКА — см. Console";
        _scriptMsg.ForeColor = err == "" ? Color.LightGreen : Color.OrangeRed;
        Log(err == "" ? "Check " + s.name + ": OK" : "Check " + s.name + ":\n" + err);
        if (err != "") GotoErrorLine(err);
    }

    void GotoErrorLine(string err)
    {
        var m = System.Text.RegularExpressions.Regex.Match(err, @"\((\d+),\d+\)|:\s*(\d+):\d+");
        if (!m.Success) return;
        string num = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        if (!int.TryParse(num, out int line) || line < 1) return;
        try
        {
            _bottomTabs.SelectedIndex = 2;
            int idx = _scriptBox.GetFirstCharIndexFromLine(Math.Min(line - 1, _scriptBox.Lines.Length - 1));
            int len = _scriptBox.Lines[Math.Min(line - 1, _scriptBox.Lines.Length - 1)].Length;
            _scriptBox.Select(idx, len);
            _scriptBox.ScrollToCaret();
            _scriptBox.Focus();
        }
        catch { }
    }

    bool _hlLock;
    void HighlightScript()
    {
        if (_hlLock || _proj == null || _editingScript == null) return;
        var s = _proj.FindScript(_editingScript);
        if (s == null) return;
        _hlLock = true;
        try
        {
            int pos = _scriptBox.SelectionStart, len = _scriptBox.SelectionLength;
            string[] kw = s.lang == "csharp"
                ? new[] { "var", "if", "else", "for", "while", "return", "class", "public", "void", "int", "float", "string", "bool", "new", "null", "true", "false", "using", "foreach" }
                : new[] { "function", "end", "if", "then", "else", "elseif", "for", "while", "do", "return", "local", "nil", "true", "false", "and", "or", "not", "in" };
            _scriptBox.SelectAll();
            _scriptBox.SelectionColor = Color.FromArgb(220, 220, 160);
            _scriptBox.SelectionBackColor = Color.FromArgb(30, 30, 32);
            string txt = _scriptBox.Text;
            foreach (var k in kw)
            {
                int at = 0;
                while ((at = txt.IndexOf(k, at, StringComparison.Ordinal)) >= 0)
                {
                    bool pre = at == 0 || !char.IsLetterOrDigit(txt[at - 1]) && txt[at - 1] != '_';
                    bool post = at + k.Length >= txt.Length || !char.IsLetterOrDigit(txt[at + k.Length]) && txt[at + k.Length] != '_';
                    if (pre && post) { _scriptBox.Select(at, k.Length); _scriptBox.SelectionColor = Color.FromArgb(120, 180, 255); }
                    at += k.Length;
                }
            }
            // комментарии и строки
            string cm = s.lang == "csharp" ? "//" : "--";
            foreach (var line in _scriptBox.Lines)
            {
                int li = txt.IndexOf(line, StringComparison.Ordinal);
                int ci = line.IndexOf(cm, StringComparison.Ordinal);
                if (ci >= 0) { _scriptBox.Select(li + ci, line.Length - ci); _scriptBox.SelectionColor = Color.FromArgb(110, 160, 110); }
            }
            _scriptBox.Select(pos, len);
            _scriptBox.SelectionColor = Color.FromArgb(220, 220, 160);
        }
        catch { }
        _hlLock = false;
    }

    bool EnsureProj()
    {
        if (_proj != null) return true;
        ProjNew();
        return true;
    }
    void EnterProjMode()
    {
        _projMode = true;
        _running = false;
        if (_btnRun != null) _btnRun.Text = "▶ Run проект";
        _rt = _rt ?? new Runtime();
        _rt.OnLog = s => Log("game: " + s);
        _rt.Start(_proj, 0);
        RefreshScriptList();
        RefreshHier();
        BuildInspector();
    }
    void ProjNew()
    {
        _proj = LicProject.NewSample();
        _proj.name = "MyGame";
        _licPath = null;
        _sel = "Proj";
        EnterProjMode();
        Log("Новый проект. ▶ Run — играть, Scripts — код Lua/C#.");
    }
    void ProjStrategy()
    {
        _proj = StrategyGame.Build();
        _licPath = null;
        _sel = "Proj";
        EnterProjMode();
        Log("Стратегия Ages: клик — выбрать/атаковать, ХОД — ход, НАБОР/МИР — кнопки. ▶ Run!");
    }
    void ProjWorld()
    {
        string[] candidates = {
            Path.Combine(AppContext.BaseDirectory, "world.lic"),
            "world.lic",
            Path.Combine("Lic", "world.lic"),
            Path.Combine("PixelFight", "world.lic"),
            @"C:\Lfall\Lic\world.lic",
            @"C:\Lfall\world.lic",
        };
        string found = null;
        foreach (var p in candidates) if (File.Exists(p)) { found = p; break; }
        if (found == null) { Log("Нет world.lic. Сгенерируй: dotnet run --project MapGen -- Lic/ne50.geojson Lic/world.lic"); return; }
        try
        {
            _proj = LicProject.Load(found);
            _licPath = found;
            _sel = "Proj";
            EnterProjMode();
            Log("Мир загружен (" + found + "). Кликни страну — играй за неё. ▶ Run!");
        }
        catch (Exception ex) { Log("World: " + ex.Message); }
    }
    // папка проектов по умолчанию: Lic/ рядом с корнем репозитория
    static string LicDir()
    {
        try
        {
            string root = FindRoot();
            string lic = root == null ? "Lic" : Path.Combine(root, "Lic");
            if (Directory.Exists(lic)) return Path.GetFullPath(lic);
        }
        catch { }
        return "";
    }
    void ProjOpen()
    {
        using var d = new OpenFileDialog { Filter = "PixelEngine проект (*.lic)|*.lic", InitialDirectory = LicDir() };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            _proj = LicProject.Load(d.FileName);
            _licPath = d.FileName;
            _sel = "Proj";
            EnterProjMode();
            Log("Открыт " + d.FileName + ": сцен " + _proj.scenes.Count + ", скриптов " + _proj.scripts.Count);
        }
        catch (Exception ex) { Log("Open: " + ex.Message); }
    }
    void ProjSave(bool saveAs)
    {
        if (_proj == null) return;
        ScriptSaveBack();
        if (saveAs || _licPath == null)
        {
            using var d = new SaveFileDialog { Filter = "PixelEngine проект (*.lic)|*.lic", FileName = _proj.name + ".lic", InitialDirectory = LicDir() };
            if (d.ShowDialog() != DialogResult.OK) return;
            _licPath = d.FileName;
        }
        try { _proj.Save(_licPath); Log("Сохранено " + _licPath); }
        catch (Exception ex) { Log("Save: " + ex.Message); }
    }
    void ProjExport()
    {
        if (_proj == null) { Log("Нечего экспортировать — открой проект."); return; }
        ScriptSaveBack();
        using var d = new FolderBrowserDialog { Description = "Папка для готовой игры (.exe + game.lic)" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            string root = FindRoot();
            if (root == null) { Log("Не нашёл исходники (PixelFight.Player)."); return; }
            Log("Сборка плеера...");
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet", "build PixelFight.Player\\PixelFight.Player.csproj -c Release --nologo -v q")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            string bin = Path.Combine(root, "PixelFight.Player", "bin", "Release", "net8.0-windows");
            using (var pr = System.Diagnostics.Process.Start(psi))
            {
                string log = pr.StandardOutput.ReadToEnd() + pr.StandardError.ReadToEnd();
                pr.WaitForExit(180000);
                if (pr.ExitCode != 0) { Log("Сборка не удалась:\n" + log); return; }
            }
            foreach (var f in Directory.GetFiles(bin))
                File.Copy(f, Path.Combine(d.SelectedPath, Path.GetFileName(f)), true);
            _proj.Save(Path.Combine(d.SelectedPath, "game.lic"));
            File.WriteAllText(Path.Combine(d.SelectedPath, "ЗАПУСК.txt"),
                _proj.name + " — игра на PixelEngine.\r\nДвойной клик PixelPlayer.exe. Нужен .NET 8.\r\nEsc — выход, F11 — окно/экран.");
            Log("Готово: " + d.SelectedPath + " (PixelPlayer.exe + game.lic)");
        }
        catch (Exception ex) { Log("Export: " + ex.Message); }
    }

    static string FindRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "PixelFight.Player"))) return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        if (Directory.Exists(@"C:\Lfall\PixelFight.Player")) return @"C:\Lfall";
        return null;
    }
    void ProjImportPng()
    {
        ImportAsset("Картинки (*.png)|*.png", "png");
    }
    void ProjImportWav()
    {
        ImportAsset("Звуки (*.wav)|*.wav", "wav");
    }
    void ImportAsset(string filter, string type)
    {
        if (!EnsureProj()) return;
        using var d = new OpenFileDialog { Filter = filter };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            byte[] bytes = File.ReadAllBytes(d.FileName);
            if (bytes.Length > 512 * 1024) { Log("Файл больше 512КБ — сожми."); return; }
            _proj.assets.Add(new LicAsset { name = Path.GetFileName(d.FileName), base64 = Convert.ToBase64String(bytes), type = type });
            _rt.ReloadAssets();
            RefreshHier();
            Log("Ассет: " + Path.GetFileName(d.FileName));
        }
        catch (Exception ex) { Log("Import: " + ex.Message); }
    }
    void ProjDemo()
    {
        _proj = LicProject.NewSample();
        _proj.name = "DemoGame";
        _licPath = null;
        _sel = "Proj";
        EnterProjMode();
        Log("Демо-платформер. ▶ Run!");
    }
    void ProjMerchant()
    {
        _proj = MerchantGame.Build();
        _licPath = null;
        _sel = "Proj";
        EnterProjMode();
        Log("Купец: клик — город/товар, КУПИТЬ/ПРОДАТЬ, ЕХАТЬ, МЕСЯЦ. Цель — 3000. ▶ Run!");
    }
    void ProjTower()
    {
        _proj = TowerDefense.Build();
        _licPath = null;
        _sel = "Proj";
        EnterProjMode();
        Log("Оборона: клик по траве — башня, ЛУК/ПУШКА — тип, ВОЛНА — досрочно. ▶ Run!");
    }
    void ProjKnights()
    {
        _proj = KnightsDefense.Build();
        _licPath = null;
        _sel = "Proj";
        EnterProjMode();
        Log("Рыцари: кнопки или 1-4 — выпуск, ДОХОД/СТЕНА — прокачка. Снеси лагерь! ▶ Run!");
    }
    void ProjClose()
    {
        ScriptSaveBack();
        _projMode = false; _running = false; _proj = null;
        _sel = "Player";
        RefreshHier(); BuildInspector();
        Log("Проект закрыт — снова превью PixelFight.");
    }
    void ToggleRun()
    {
        if (!_projMode) { Log("Открой Project → New/Open .lic, потом ▶ Run."); return; }
        ScriptSaveBack();
        if (!_running)
        {
            // проверка скриптов до запуска — ошибки сразу в консоль, а не молча в инспектор
            foreach (var s in _proj.scripts)
            {
                string err = PixelFight.ScriptFactory.CheckCode(s.lang, s.code);
                if (err != "") { Log("Скрипт " + s.name + ": " + err.Split('\n')[0]); }
            }
        }
        _running = !_running;
        if (_running) { _rt.Start(_proj, Math.Max(0, _rt.sceneIdx)); Log("▶ Run " + _proj.name); }
        else { _rt.Start(_proj, Math.Max(0, _rt.sceneIdx)); Log("⏹ Stop — сцена сброшена."); }
        _btnRun.Text = _running ? "⏹ Stop" : "▶ Run проект";
    }

    RuntimeObj ProjSelObj()
    {
        if (_rt == null || !_sel.StartsWith("PObj:")) return null;
        var p = _sel.Split(':');
        if (p.Length != 3 || !int.TryParse(p[1], out int si) || !int.TryParse(p[2], out int oi)) return null;
        if (si != _rt.sceneIdx || oi < 0 || oi >= _rt.objs.Count) return null;
        return _rt.objs[oi];
    }

    void Log(string t) { _console.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + t); _console.TopIndex = _console.Items.Count - 1; }

    void Shot()
    {
        try
        {
            using var bmp = new Bitmap(PixelFight.Game.W * 4, PixelFight.Game.H * 4);
            using (var gg = Graphics.FromImage(bmp))
            {
                gg.InterpolationMode = InterpolationMode.NearestNeighbor;
                using var small = new Bitmap(PixelFight.Game.W, PixelFight.Game.H);
                using (var gs = Graphics.FromImage(small)) _game.Render(gs);
                gg.DrawImage(small, 0, 0, bmp.Width, bmp.Height);
            }
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "bloody_" + DateTime.Now.ToString("HHmmss") + ".png");
            bmp.Save(path);
            Log("Скриншот: " + Path.GetFileName(path));
        }
        catch (Exception ex) { Log("Ошибка: " + ex.Message); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); base.OnFormClosed(e); }
}
