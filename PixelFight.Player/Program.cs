using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using PixelFight;

namespace PixelPlayer;

// Плеер .lic-игр: запускает любой проект движка отдельным окном.
// Использование: PixelPlayer.exe [игра.lic]  (без аргумента — game.lic рядом с exe)
// Esc — выход, F11 — окно/экран.
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string lic = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "game.lic");
        if (!File.Exists(lic))
        {
            MessageBox.Show("Нет игры:\n" + lic + "\n\nПоложи .lic рядом или перетащи на exe.",
                "PixelPlayer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Application.Run(new PlayerForm(LicProject.Load(lic))); }
        catch (Exception ex)
        {
            MessageBox.Show("Не запустилось:\n" + ex.Message, "PixelPlayer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

class PlayerForm : Form
{
    readonly Runtime _rt = new Runtime();
    readonly Bitmap _buf = new Bitmap(Runtime.W, Runtime.H);
    readonly Graphics _bg;
    readonly Timer _timer = new Timer { Interval = 16 };
    readonly PictureBox _view = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black };
    readonly HashSet<Keys> _keys = new HashSet<Keys>();
    Point _mouse = new Point(240, 135);
    bool _lmb;
    bool _full = true;

    public PlayerForm(LicProject proj)
    {
        Text = proj.name + " — на PixelEngine";
        try { Icon = GameIcon.Get(); } catch { }
        ClientSize = new Size(Runtime.W * 3, Runtime.H * 3);
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

        _bg = Graphics.FromImage(_buf);
        _bg.InterpolationMode = InterpolationMode.NearestNeighbor;
        try { Sfx.Init(); } catch { }
        _rt.Start(proj, Math.Max(0, Math.Min(proj.scenes.Count - 1, proj.startScene)));
        _timer.Tick += (s, e) =>
        {
            var w = _rt.ScreenToWorld(_mouse.X, _mouse.Y);
            _rt.Update(_keys, 1f / 60f, w.X, w.Y, _lmb);
            _rt.Render(_bg, false);
            _view.Invalidate();
        };
        _timer.Start();
        Controls.Add(_view);
        _view.Paint += (s, e) =>
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            int sc = Math.Max(1, Math.Min(_view.Width / Runtime.W, _view.Height / Runtime.H));
            int w = Runtime.W * sc, h = Runtime.H * sc;
            e.Graphics.Clear(Color.Black);
            e.Graphics.DrawImage(_buf, (_view.Width - w) / 2, (_view.Height - h) / 2, w, h);
        };
        KeyDown += (s, e) =>
        {
            _keys.Add(e.KeyCode);
            if (e.KeyCode == Keys.Escape) Close();
            if (e.KeyCode == Keys.F11) ToggleFull();
            if (e.KeyCode == Keys.Space) e.SuppressKeyPress = true;
        };
        KeyUp += (s, e) => _keys.Remove(e.KeyCode);
        _view.MouseMove += (s, e) => ToBuf(e.Location);
        _view.MouseDown += (s, e) =>
        {
            ToBuf(e.Location);
            if (e.Button == MouseButtons.Left) _lmb = true;
            // колесо и панорама для freeCam-сцен
            if (FreeCam() && e.Button == MouseButtons.Right) { _pan = true; _panAt = e.Location; }
        };
        _view.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) _lmb = false;
            if (e.Button == MouseButtons.Right) _pan = false;
        };
        _view.MouseMove += (s, e) =>
        {
            if (_pan && FreeCam())
            {
                _rt.camX -= (e.Location.X - _panAt.X) * Runtime.W / Math.Max(1, _view.Width) / (float)(3 * _rt.zoom);
                _rt.camY -= (e.Location.Y - _panAt.Y) * Runtime.H / Math.Max(1, _view.Height) / (float)(3 * _rt.zoom);
                _panAt = e.Location;
            }
        };
        MouseWheel += (s, e) =>
        {
            if (!FreeCam()) return;
            _rt.zoom = Math.Max(0.6f, Math.Min(4f, _rt.zoom * (e.Delta > 0 ? 1.12f : 0.89f)));
        };
        _view.DoubleClick += (s, e) =>
        {
            if (!FreeCam()) return;
            var scn = _rt.Scene;
            _rt.camX = scn.camX0; _rt.camY = scn.camY0; _rt.zoom = 1;
        };
    }

    bool _pan;
    Point _panAt;
    bool FreeCam() => _rt.Scene != null && _rt.Scene.freeCam;

    void ToggleFull()
    {
        _full = !_full;
        if (_full) { FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; }
        else { FormBorderStyle = FormBorderStyle.Sizable; WindowState = FormWindowState.Normal; ClientSize = new Size(Runtime.W * 3, Runtime.H * 3); }
    }

    void ToBuf(Point p)
    {
        int sc = Math.Max(1, Math.Min(_view.Width / Runtime.W, _view.Height / Runtime.H));
        int w = Runtime.W * sc, h = Runtime.H * sc;
        int ox = (_view.Width - w) / 2, oy = (_view.Height - h) / 2;
        _mouse = new Point(
            Math.Max(0, Math.Min(Runtime.W - 1, (p.X - ox) * Runtime.W / Math.Max(1, w))),
            Math.Max(0, Math.Min(Runtime.H - 1, (p.Y - oy) * Runtime.H / Math.Max(1, h))));
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); base.OnFormClosed(e); }
}
