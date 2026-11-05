using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Windows.Forms;

using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("PixelFightTests")]

namespace PixelFight;

// Процедурные 8-битные звуки. Только встроенный SoundPlayer, без файлов.
public static class Sfx
{
    static byte[] Wav(float freq, float dur, float noise, float slide)
    {
        int rate = 22050, n = (int)(rate * dur);
        var d = new byte[44 + n];
        void Str(int o, string s) { for (int i = 0; i < s.Length; i++) d[o + i] = (byte)s[i]; }
        Str(0, "RIFF"); BitConverter.GetBytes(36 + n).CopyTo(d, 4);
        Str(8, "WAVE"); Str(12, "fmt "); BitConverter.GetBytes(16).CopyTo(d, 16);
        BitConverter.GetBytes((short)1).CopyTo(d, 20); BitConverter.GetBytes((short)1).CopyTo(d, 22);
        BitConverter.GetBytes(rate).CopyTo(d, 24); BitConverter.GetBytes(rate).CopyTo(d, 28);
        BitConverter.GetBytes((short)1).CopyTo(d, 32); BitConverter.GetBytes((short)8).CopyTo(d, 34);
        Str(36, "data"); BitConverter.GetBytes(n).CopyTo(d, 40);
        var r = new Random();
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate, f = freq + slide * t;
            float env = 1f - (float)i / n; env *= env;
            float s = (float)Math.Sin(2 * Math.PI * f * t) * (1f - noise)
                    + ((float)r.NextDouble() * 2 - 1) * noise;
            d[44 + i] = (byte)(128 + 90 * s * env);
        }
        return d;
    }

    static byte[] _swing, _clang, _death, _jump, _horn, _drum, _drumAcc, _tick, _heal, _stomp, _cheer;
    static byte[][] _hits, _scale; // варианты удара + пентатоника для мелодии боя
    static readonly Random _sr = new Random();

    public static void Init()
    {
        _swing = Wav(900, 0.12f, 0.5f, -600);
        _hits = new[] { Wav(140, 0.18f, 0.55f, -60), Wav(110, 0.2f, 0.6f, -40), Wav(170, 0.15f, 0.5f, -80) };
        _clang = Wav(2200, 0.22f, 0.25f, -1200);
        _death = Wav(220, 0.5f, 0.3f, -160);
        _jump = Wav(300, 0.1f, 0.2f, 300);
        _horn = Wav(196, 0.7f, 0.1f, 20);
        _drum = Wav(70, 0.16f, 0.15f, -30);
        _drumAcc = Wav(60, 0.28f, 0.1f, -20);
        _tick = Wav(3000, 0.03f, 0.8f, 0);
        _heal = Wav(500, 0.25f, 0.1f, 600);
        _stomp = Wav(55, 0.4f, 0.4f, -25);
        _cheer = Wav(320, 0.6f, 0.7f, 120); // гул толпы
        float[] notes = { 220f, 261.6f, 293.7f, 329.6f, 392f, 440f, 523.2f, 587.3f };
        _scale = new byte[notes.Length][];
        for (int i = 0; i < notes.Length; i++) _scale[i] = Wav(notes[i], 0.3f, 0.1f, -40);
    }

    static void Play(byte[] w)
    {
        if (!Enabled) return;
        try { new SoundPlayer(new MemoryStream(w, false)).Play(); } catch { }
    }

    public static bool Enabled = true;

    public static void Swing() => Play(_swing);
    public static void Hit() { try { if (_hits != null) Play(_hits[_sr.Next(_hits.Length)]); } catch { } }
    public static void Clang() => Play(_clang);
    public static void Death() => Play(_death);
    public static void Jump() => Play(_jump);
    public static void Horn() => Play(_horn);
    public static void Drum(bool accent) => Play(accent ? _drumAcc : _drum);
    public static void Tick() => Play(_tick);
    public static void Heal() => Play(_heal);
    public static void Stomp() => Play(_stomp);
    public static void Cheer() { try { if (_cheer != null) Play(_cheer); } catch { } }
    public static void Pluck(int i)
    {
        try
        {
            if (_scale == null || _scale.Length == 0) return;
            Play(_scale[((i % _scale.Length) + _scale.Length) % _scale.Length]);
        }
        catch { }
    }
}

// Окно-мини-движок: цикл 60 FPS, backbuffer 384x216, ввод, звук.
class GameForm : Form
{
    public const int W = 480, H = 270;
    readonly Bitmap _buf;
    readonly Graphics _bg;
    readonly Game _game;
    readonly Timer _timer;
    readonly HashSet<Keys> _keys = new();
    Point _mouseBuf; // мышь в координатах буфера
    bool _lmb, _rmb;
    Rectangle _view; // куда рисуем буфер: целочисленный масштаб по центру (чётко)
    bool _full = true;

    public GameForm()
    {
        Text = "Bloody Pixels — чистый C#, без движка (F11 — окно/экран)";
        Icon = GameIcon.Get();
        ClientSize = new Size(W * 4, H * 4); // 1920x1080 Full HD, пиксель ровно x4
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized; // на весь экран
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross; // прицел-курсор
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);

        _buf = new Bitmap(W, H);
        _bg = Graphics.FromImage(_buf);
        _bg.InterpolationMode = InterpolationMode.NearestNeighbor;
        _bg.PixelOffsetMode = PixelOffsetMode.None;
        Sfx.Init();
        _game = new Game();
        _game.ToggleFullscreen = () => ToggleFull();

        _timer = new Timer { Interval = 16 };
        _timer.Tick += (s, e) =>
        {
            Pad.Poll(_keys, ref _mouseBuf, out bool padLmb, out bool padRmb);
            _game.Update(_keys, _mouseBuf, _lmb || padLmb, _rmb || padRmb, 1f / 60f);
            _game.Render(_bg);
            Invalidate();
        };
        _timer.Start();

        KeyDown += (s, e) => { _keys.Add(e.KeyCode); _game.NoteKey(e.KeyCode); if (e.KeyCode == Keys.Space) e.SuppressKeyPress = true; if (e.KeyCode == Keys.F11) ToggleFull(); };
        KeyUp += (s, e) => _keys.Remove(e.KeyCode);
        MouseMove += (s, e) => ToBuf(e.Location);
        MouseDown += (s, e) =>
        {
            ToBuf(e.Location);
            if (e.Button == MouseButtons.Left) _lmb = true;
            if (e.Button == MouseButtons.Right) _rmb = true;
        };
        MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) _lmb = false;
            if (e.Button == MouseButtons.Right) _rmb = false;
        };
    }

    void ToggleFull()
    {
        _full = !_full;
        if (_full) { FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Maximized; }
        else { FormBorderStyle = FormBorderStyle.Sizable; WindowState = FormWindowState.Normal; ClientSize = new Size(W * 3, H * 3); }
    }

    void UpdateView()
    {
        int s = Math.Max(1, Math.Min(ClientSize.Width / W, ClientSize.Height / H));
        int w = W * s, h = H * s;
        _view = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
    }

    void ToBuf(Point p)
    {
        UpdateView();
        int bx = _view.Width > 0 ? (p.X - _view.X) * W / _view.Width : 0;
        int by = _view.Height > 0 ? (p.Y - _view.Y) * H / _view.Height : 0;
        _mouseBuf = new Point(Math.Max(0, Math.Min(W - 1, bx)), Math.Max(0, Math.Min(H - 1, by)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        UpdateView();
        e.Graphics.Clear(Color.Black);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_buf, _view);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        base.OnFormClosed(e);
    }
}
