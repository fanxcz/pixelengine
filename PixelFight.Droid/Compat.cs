using System;
using MG = Microsoft.Maui.Graphics;

namespace PFMaui;

// Слой совместимости: игровой код (порт Game.cs) рисует как раньше,
// а внутри всё едет в MAUI ICanvas. Логика боя НЕ тронута.
public struct Color
{
    public int R, G, B;
    public float A;
    Color(int r, int g, int b, float a) { R = r; G = g; B = b; A = a; }
    public Color(float r, float g, float b, float a = 1) { R = (int)(r * 255); G = (int)(g * 255); B = (int)(b * 255); A = a; }
    public static Color FromArgb(int r, int g, int b) => new Color(r, g, b, 1);
    public static Color FromArgb(int a, int r, int g, int b) => new Color(r, g, b, a / 255f);
    public static Color Black => FromArgb(0, 0, 0);
    public static Color White => FromArgb(255, 255, 255);
    public static Color Red => FromArgb(255, 50, 50);
    public static Color Green => FromArgb(50, 200, 50);
    public static Color Blue => FromArgb(50, 100, 255);
    public static Color Yellow => FromArgb(255, 230, 50);
    public static Color Orange => FromArgb(255, 150, 25);
    public static Color Cyan => FromArgb(50, 230, 230);
    public static Color Gray => FromArgb(128, 128, 128);
    public static Color LightGreen => FromArgb(150, 255, 150);
    public static Color LightGray => FromArgb(200, 200, 200);
    public static Color Transparent => new Color(0, 0, 0, 0);
    public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    public static bool operator !=(Color a, Color b) => !(a == b);
    public override bool Equals(object o) => o is Color c && this == c;
    public override int GetHashCode() => (R * 31 + G) * 31 + B;
    public static implicit operator MG.Color(Color c) => new MG.Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A);
    public static implicit operator Br(Color c) => new Br(c);
}

public class Br : IDisposable
{
    public Color C;
    public Br(Color c) { C = c; }
    public void Dispose() { }
}

public static class Brushes
{
    public static readonly Br Black = new Br(Color.Black);
    public static readonly Br White = new Br(Color.White);
}

public class SolidBrush : Br
{
    public SolidBrush(Color c) : base(c) { }
}

public class LinGrad : Br
{
    public Color C2;
    public bool Vertical;
    public LinGrad(Color c1, Color c2, bool vertical) : base(c1) { C2 = c2; Vertical = vertical; }
}

public enum LineCap { Round, Square, Butt }
public enum DashStyle { Solid, Dash }
public enum TextRenderingHint { SystemDefault, SingleBitPerPixelGridFit }

public class Pen : IDisposable
{
    public Color C;
    public float W;
    public LineCap StartCap = LineCap.Butt, EndCap = LineCap.Butt;
    public DashStyle DashStyle = DashStyle.Solid;
    public Pen(Color c, float w) { C = c; W = Math.Max(1, w); }
    public Pen(Color c) : this(c, 1) { }
    public Pen(Br b, float w) : this(b.C, w) { }
    public void Dispose() { }
}

public enum FontStyle { Regular, Bold }

public static class FontFamily
{
    public const string GenericMonospace = "monospace";
}

public class Font
{
    public string Family;
    public float Size;
    public bool Bold;
    public Font(string family, float size) { Family = family; Size = size; }
    public Font(string family, float size, FontStyle style) : this(family, size) { Bold = style == FontStyle.Bold; }
}

public struct Pt
{
    public int X, Y;
    public Pt(int x, int y) { X = x; Y = y; }
}

public struct PtF
{
    public float X, Y;
    public PtF(float x, float y) { X = x; Y = y; }
}

public struct SizeF
{
    public float Width, Height;
    public SizeF(float w, float h) { Width = w; Height = h; }
}

public struct GRect
{
    public int X, Y, Width, Height;
    public GRect(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
    public bool Contains(Pt p) => Contains(p.X, p.Y);
}

// Коды клавиш, используемые игрой (значения не важны, только имена).
public enum Keys
{
    None, A, D, W, S, Q, E, F, R, T, P, C, H, O, V, L, J, K, N, M,
    D1, D2, D3, D4, D5, D6, D7, D8,
    Left, Right, Up, Down, Space, Enter, Escape,
    Oemcomma, OemPeriod, OemQuestion, F11,
}

// Обёртка над ICanvas с привычными игре методами (координаты — буфер 480x270).
public class Gfx
{
    readonly MG.ICanvas _c;
    public object TextRenderingHint { set { } }
    public object InterpolationMode { set { } }
    public object PixelOffsetMode { set { } }

    public Gfx(MG.ICanvas canvas) { _c = canvas; }

    public void Clear(Color c)
    {
        _c.FillColor = c;
        _c.FillRectangle(0, 0, 10000, 10000);
    }

    public void FillRectangle(Br b, float x, float y, float w, float h)
    {
        if (b is LinGrad g)
        {
            var paint = new MG.LinearGradientPaint
            {
                StartPoint = new MG.Point(0.5f, 0),
                EndPoint = new MG.Point(0.5f, 1),
            };
            paint.AddOffset(0, b.C);
            paint.AddOffset(1, g.C2);
            _c.SetFillPaint(paint, new MG.RectF(x, y, w, h));
            _c.FillRectangle(x, y, w, h);
            _c.SetFillPaint(null, MG.RectF.Zero);
        }
        else
        {
            _c.FillColor = b.C;
            _c.FillRectangle(x, y, w, h);
        }
    }

    public void FillEllipse(Br b, float x, float y, float w, float h)
    {
        _c.FillColor = b.C;
        _c.FillEllipse(x, y, w, h);
    }

    public void FillPolygon(Br b, PtF[] pts)
    {
        var path = new MG.PathF();
        for (int i = 0; i < pts.Length; i++)
        {
            if (i == 0) path.MoveTo(pts[i].X, pts[i].Y);
            else path.LineTo(pts[i].X, pts[i].Y);
        }
        path.Close();
        _c.FillColor = b.C;
        _c.FillPath(path);
    }

    public void DrawPolygon(Pen p, PtF[] pts)
    {
        var path = new MG.PathF();
        for (int i = 0; i < pts.Length; i++)
        {
            if (i == 0) path.MoveTo(pts[i].X, pts[i].Y);
            else path.LineTo(pts[i].X, pts[i].Y);
        }
        path.Close();
        _c.StrokeColor = p.C;
        _c.StrokeSize = p.W;
        _c.DrawPath(path);
    }

    public void DrawEllipse(Pen p, float x, float y, float w, float h)
    {
        _c.StrokeColor = p.C;
        _c.StrokeSize = p.W;
        ApplyDash(p);
        _c.DrawEllipse(x, y, w, h);
        _c.StrokeDashPattern = null;
    }

    public void DrawLine(Pen p, float x1, float y1, float x2, float y2)
    {
        _c.StrokeColor = p.C;
        _c.StrokeSize = p.W;
        _c.StrokeLineCap = p.StartCap == LineCap.Round ? MG.LineCap.Round : MG.LineCap.Butt;
        ApplyDash(p);
        _c.DrawLine(x1, y1, x2, y2);
        _c.StrokeDashPattern = null;
    }

    void ApplyDash(Pen p)
    {
        if (p.DashStyle == DashStyle.Dash) _c.StrokeDashPattern = new float[] { 4, 3 };
    }

    MG.Font ToFont(Font f) =>
        new MG.Font(f.Family, f.Bold ? MG.FontWeights.Bold : MG.FontWeights.Normal);

    public void DrawString(string t, Font f, Br b, float x, float y)
    {
        if (string.IsNullOrEmpty(t)) return;
        _c.FontColor = b.C;
        _c.Font = ToFont(f);
        _c.FontSize = f.Size;
        _c.DrawString(t, x, y, MG.HorizontalAlignment.Left);
    }

    public void DrawString(string t, Font f, Br b, PtF p) => DrawString(t, f, b, p.X, p.Y);

    public SizeF MeasureString(string t, Font f)
    {
        if (string.IsNullOrEmpty(t)) return new SizeF(0, 0);
        var s = _c.GetStringSize(t, ToFont(f), f.Size);
        return new SizeF(s.Width, s.Height);
    }

    public void TranslateTransform(float x, float y) => _c.Translate(x, y);
    public void RotateTransform(float deg) => _c.Rotate(deg);
    public void ScaleTransform(float sx, float sy) => _c.Scale(sx, sy);
    public object Save() { _c.SaveState(); return new object(); }
    public void Restore(object st) { _c.RestoreState(); }
}
