using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PixelFight;

// Геймпад XInput без библиотек (xinput1_4 / xinput9_1_0 из самой Windows).
// Стик левый — движение, правый — прицел, A — прыжок/ок, X — пинок,
// B — тычок, Y — зелье, LB — блок, RT — рывок, Start — пауза, Back — меню.
static class Pad
{
    [StructLayout(LayoutKind.Sequential)]
    struct XS { public int packet; public short buttons, lt, rt; public short lx, ly, rx, ry; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int Get1(int i, out XS s);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern int Get9(int i, out XS s);
    [StructLayout(LayoutKind.Sequential)]
    struct XVib { public short left, right; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    static extern int SetVib1(int i, ref XVib v);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
    static extern int SetVib9(int i, ref XVib v);

    static bool _vibOn;
    static long _vibUntil;

    // вибрация на удары (0..65535, ms) — тихо гаснет сама
    public static void Rumble(int l, int r, int ms = 120)
    {
        if (_noPad || !SaveData.Pad) return;
        try
        {
            var v = new XVib { left = (short)Math.Max(0, Math.Min(65535, l)), right = (short)Math.Max(0, Math.Min(65535, r)) };
            if (SetVib1(0, ref v) != 0) SetVib9(0, ref v);
            _vibOn = true;
            _vibUntil = Environment.TickCount + ms;
        }
        catch { _noPad = true; }
    }

    const short DP_L = 4, DP_R = 8, DP_U = 1, DP_D = 2;
    const short START = 16, BACK = 32, LB = 256, RB = 512;
    const short A = 4096, B = 8192, X = 16384;
    const int Y = 32768, LST = 64, RST = 128;

    static bool _noPad;
    static short _prevBt;

    static bool Btn(short b, int f) => (b & f) != 0;
    static float Ax(short v) => Math.Abs(v) < 8000 ? 0 : v / 32767f;

    // вливает геймпад в ввод формы: клавиши + мышь(буфер); клики — отдельными флагами
    public static void Poll(HashSet<Keys> keys, ref Point mouse, out bool padLmb, out bool padRmb)
    {
        padLmb = false; padRmb = false;
        if (_noPad || !SaveData.Pad) return;
        if (_vibOn && Environment.TickCount > _vibUntil)
        {
            _vibOn = false;
            try { var z = new XVib(); if (SetVib1(0, ref z) != 0) SetVib9(0, ref z); } catch { }
        }
        XS s = new XS();
        try
        {
            int r = Get1(0, out s);
            if (r != 0) r = Get9(0, out s);
            if (r != 0) return; // нет геймпада
        }
        catch { _noPad = true; return; }
        float lx = Ax(s.lx), ly = Ax(s.ly), rx = Ax(s.rx), ry = Ax(s.ry);
        if (lx < -0.3f || Btn(s.buttons, DP_L)) keys.Add(Binds.Left);
        if (lx > 0.3f || Btn(s.buttons, DP_R)) keys.Add(Binds.Right);
        if (Btn(s.buttons, A)) keys.Add(Binds.Jump); // прыжок (срабатывает по фронту в игре)
        if (Btn(s.buttons, X)) keys.Add(Binds.Kick);
        if (Btn(s.buttons, B)) keys.Add(Binds.Jab);
        if (Btn(s.buttons, Y) && !_wasY) keys.Add(Binds.Potion); // зелье по фронту
        if (Btn(s.buttons, START)) keys.Add(Binds.Pause);
        if (Btn(s.buttons, BACK)) keys.Add(Keys.Escape);
        if (Btn(s.buttons, A) && !Btn(_prevBt, A)) keys.Add(Keys.Enter); // A = ок в меню
        if (Btn(s.buttons, RB)) padLmb = true; // рывок
        if (Btn(s.buttons, LB)) padRmb = true; // блок
        mouse = new Point(
            Math.Max(0, Math.Min(Game.W - 1, mouse.X + (int)(rx * 9))),
            Math.Max(0, Math.Min(Game.H - 1, mouse.Y + (int)(ry * 9))));
        _prevBt = s.buttons;
        _wasY = Btn(s.buttons, Y);
    }

    static bool _wasY;
}
