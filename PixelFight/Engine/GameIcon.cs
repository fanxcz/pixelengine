using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PixelFight;

// Иконка окон: avatar.png (скрещенные мечи) или процедурная мини-копия.
public static class GameIcon
{
    public static Icon Get()
    {
        try
        {
            string[] candidates = {
                Path.Combine(AppContext.BaseDirectory, "avatar.png"),
                "avatar.png",
                Path.Combine("PixelFight", "avatar.png"),
                @"C:\Lfall\avatar.png",
            };
            foreach (var p in candidates)
                if (File.Exists(p))
                {
                    using var bmp = new Bitmap(p);
                    return Icon.FromHandle(bmp.GetHicon());
                }
        }
        catch { }
        return Procedural();
    }

    static Icon Procedural()
    {
        var b = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.FromArgb(20, 12, 18));
            using var blade = new SolidBrush(Color.FromArgb(215, 227, 244));
            using var gold = new SolidBrush(Color.FromArgb(201, 162, 39));
            using var wood = new SolidBrush(Color.FromArgb(91, 58, 30));
            foreach (float a in new[] { 45f, -45f })
            {
                var s = g.Save();
                g.TranslateTransform(32, 34);
                g.RotateTransform(a);
                g.FillRectangle(blade, -3, -26, 6, 38);
                g.FillPolygon(blade, new[] { new PointF(-3, -26), new PointF(3, -26), new PointF(0, -32) });
                g.FillRectangle(gold, -8, 12, 16, 4);
                g.FillRectangle(wood, -2, 16, 4, 9);
                g.Restore(s);
            }
            using var blood = new SolidBrush(Color.FromArgb(193, 18, 31));
            g.FillEllipse(blood, 8, 52, 6, 6);
            g.FillEllipse(blood, 50, 54, 5, 5);
            using var pen = new Pen(Color.FromArgb(201, 162, 39), 3);
            g.DrawRectangle(pen, 1, 1, 62, 62);
        }
        var icon = Icon.FromHandle(b.GetHicon());
        b.Dispose();
        return icon;
    }
}
