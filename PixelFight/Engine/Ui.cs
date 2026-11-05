using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PixelFight;

// Минимум immediate-mode GUI для движка: кнопки, панели, центрированный текст.
// Кадр: Ui.Begin(мышь, клик) в начале Render, дальше рисуем виджеты.
static class Ui
{
    public static int MX, MY;
    public static bool Clicked;

    static readonly Dictionary<Color, SolidBrush> _b = new();

    public static void Begin(int mx, int my, bool clicked) { MX = mx; MY = my; Clicked = clicked; }
    public static bool Hover(Rectangle r) => r.Contains(MX, MY);

    public static Brush Br(Color c)
    {
        if (!_b.TryGetValue(c, out var b)) { b = new SolidBrush(c); _b[c] = b; }
        return b;
    }

    public static void Rect(Graphics g, int x, int y, int w, int h, Color c) =>
        g.FillRectangle(Br(c), x, y, w, h);

    // Кнопка: тёмная плашка + рамка (жёлтая если active, белая при наведении).
    // Возвращает true, если по ней кликнули в этом кадре.
    public static bool Button(Graphics g, Rectangle r, string text, Font f, bool active)
    {
        bool hot = Hover(r);
        Rect(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(20, 16, 30));
        var bc = active ? Color.Yellow : hot ? Color.White : Color.FromArgb(140, 140, 160);
        Rect(g, r.X, r.Y, r.Width, 1, bc); Rect(g, r.X, r.Y + r.Height - 1, r.Width, 1, bc);
        Rect(g, r.X, r.Y, 1, r.Height, bc); Rect(g, r.X + r.Width - 1, r.Y, 1, r.Height, bc);
        if (g.MeasureString(text, f).Width > r.Width - 8)
        {
            while (text.Length > 1 && g.MeasureString(text + "…", f).Width > r.Width - 8)
                text = text.Substring(0, text.Length - 1);
            text += "…";
        }
        var s = g.MeasureString(text, f);
        g.DrawString(text, f, Br(active ? Color.Yellow : Color.White),
            r.X + (r.Width - s.Width) / 2, r.Y + (r.Height - s.Height) / 2);
        return hot && Clicked;
    }

    public static void Panel(Graphics g, Rectangle r)
    {
        Rect(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(220, 18, 14, 26));
        var bc = Color.FromArgb(210, 160, 70);
        Rect(g, r.X, r.Y, r.Width, 2, bc); Rect(g, r.X, r.Y + r.Height - 2, r.Width, 2, bc);
        Rect(g, r.X, r.Y, 2, r.Height, bc); Rect(g, r.X + r.Width - 2, r.Y, 2, r.Height, bc);
    }

    public static void CenterText(Graphics g, string t, Font f, Brush br, int y, int w)
    {
        var s = g.MeasureString(t, f);
        g.DrawString(t, f, br, (w - s.Width) / 2, y);
    }
}
