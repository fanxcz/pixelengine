using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using MoonSharp.Interpreter;

namespace PixelFight;

// Живой объект сцены движка (не путать с бойцами PixelFight).
public class RuntimeObj
{
    public LicObject def;
    public float x, y, vx, vy, w, h;
    public bool onGround, dead;
    public IScriptRunner runner;
    public DynObj proxy;
    public string error = ""; // ошибка скрипта (показ в инспекторе/консоли, скрипт стоп)
    public float num; // свободное число объекта (армия, очки, таймер — для скриптов)
    public float maxnum; // максимум для num (полоски урона замков)
    public float hitT, deadT; // вспышка попадания, время трупа
    public float walkPh, attackT, face = 1; // марионетка kind=knight: фаза ходьбы, замах, сторона
}

// Универсальный рантайм проекта .lic: физика, скрипты Lua/C#, камера, рендер.
// x,y — центр объекта; земля и платформы держат снизу.
public class Runtime
{
    public const int W = 480, H = 270;
    const int ZOOM = 3;
    public readonly Random rnd = new Random();
    public float time;
    public float camX, camY;
    public float zoom = 1; // зум камеры (колесо мыши при freeCam)
    public readonly List<string> logLines = new List<string>();
    public Action<string> OnLog;
    readonly Dictionary<string, Bitmap> _sprites = new Dictionary<string, Bitmap>();
    readonly HashSet<string> _keys = new HashSet<string>();
    static Font _f8;
    static readonly Dictionary<Color, SolidBrush> _br = new Dictionary<Color, SolidBrush>();

    public LicProject proj;
    public int sceneIdx;
    public readonly List<RuntimeObj> objs = new List<RuntimeObj>();

    static Brush B(Color c)
    {
        if (!_br.TryGetValue(c, out var b)) { b = new SolidBrush(c); _br[c] = b; }
        return b;
    }
    static Color ParseColor(string s)
    {
        try { return ColorTranslator.FromHtml(s); }
        catch { return Color.White; }
    }

    public LicScene Scene => proj.scenes[Math.Max(0, Math.Min(proj.scenes.Count - 1, sceneIdx))];

    public void Start(LicProject p, int scene)
    {
        float oz = zoom, ox = camX, oy = camY;
        bool had = proj == p;
        proj = p;
        sceneIdx = Math.Max(0, Math.Min(p.scenes.Count - 1, scene));
        objs.Clear();
        logLines.Clear();
        time = 0;
        LoadSprites();
        foreach (var d in Scene.objects) AddLive(d.Clone());
        camX = 0; camY = Scene.groundY - 22;
        if (Scene.lockCam) { camX = Scene.camX0; camY = Scene.camY0; }
        if (had && Scene.lockCam) { zoom = oz; camX = ox; camY = oy; } // вид не прыгает при рестарте
        else zoom = 1;
        var f = objs.Find(o => o.def.follow);
        if (f != null && !Scene.lockCam) camX = f.x;
    }

    public void ReloadAssets() => LoadSprites();

    // фон сцены из скриптов: api.bg('#8FC3E8', '#D8EBD8')
    public void SetBG(string c1, string c2)
    {
        try
        {
            if (!string.IsNullOrEmpty(c1)) Scene.bg1 = c1;
            if (!string.IsNullOrEmpty(c2)) Scene.bg2 = c2;
        }
        catch { }
    }

    // wav-ассет из .lic (без падений даже без звука)
    public void PlayAsset(string name)
    {
        try
        {
            if (proj?.assets == null) return;
            var a = proj.assets.Find(a => a.name == name);
            if (a == null || string.IsNullOrEmpty(a.base64)) return;
            byte[] bytes = Convert.FromBase64String(a.base64);
            using var ms = new MemoryStream(bytes, false);
            using var sp = new System.Media.SoundPlayer(ms);
            sp.Play();
        }
        catch (Exception ex) { Log("play " + name + ": " + ex.Message); }
    }
    void LoadSprites()
    {
        foreach (var im in _sprites.Values) im.Dispose();
        _sprites.Clear();
        if (proj.assets == null) return;
        foreach (var a in proj.assets)
        {
            try
            {
                if (string.IsNullOrEmpty(a.base64)) continue;
                if ((a.type ?? "png") == "wav") continue; // звук — лениво в PlayAsset
                byte[] bytes = Convert.FromBase64String(a.base64);
                using var ms = new MemoryStream(bytes, false);
                _sprites[a.name] = new Bitmap(ms);
            }
            catch (Exception ex) { Log("asset " + a.name + ": " + ex.Message); }
        }
    }

    RuntimeObj AddLive(LicObject d)
    {
        var o = new RuntimeObj { def = d, x = d.x, y = d.y, w = Math.Max(1, d.w), h = Math.Max(1, d.h) };
        o.proxy = new DynObj(this, o);
        objs.Add(o);
        return o;
    }

    // вызов скриптов объекта (ошибка -> в лог, скрипт останавливается)
    void BindScript(RuntimeObj o)
    {
        if (o.runner != null || string.IsNullOrEmpty(o.def.script)) return;
        var s = proj.FindScript(o.def.script);
        if (s == null) { o.error = "нет скрипта " + o.def.script; return; }
        try
        {
            o.runner = ScriptFactory.Create(s);
            o.runner.Start(o.proxy, new EngineApi(this));
        }
        catch (Exception ex) { o.error = ErrText(ex); Log(o.def.name + ": " + o.error); }
    }

    static string ErrText(Exception ex)
    {
        // у Lua — первая строка трейсбэка с номером строки скрипта
        try
        {
            if (ex is MoonSharp.Interpreter.ScriptRuntimeException)
            {
                try
                {
                    dynamic d = ex;
                    foreach (var w in d.CallStack)
                    {
                        try
                        {
                            string loc = (string)w.Location.FormatLocation(w.OwningScript);
                            if (!string.IsNullOrEmpty(loc)) return ex.Message + " @ " + loc;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return ex.Message;
    }

    public void Log(string s)
    {
        logLines.Add(s);
        if (logLines.Count > 200) logLines.RemoveAt(0);
        OnLog?.Invoke(s);
    }

    // мышь для стратегий/квестов: редактор кладёт мировые координаты каждый кадр
    public float mouseWX, mouseWY;
    public bool mouseClicked;
    bool _prevLmb;
    public void SetMouse(float wx, float wy, bool lmb)
    {
        mouseWX = wx; mouseWY = wy;
        mouseClicked = lmb && !_prevLmb;
        _prevLmb = lmb;
    }

    public bool IsKey(string name)
    {
        name = (name ?? "").Trim().ToLowerInvariant();
        return _keys.Contains(name);
    }
    static string KeyName(Keys k) => k switch
    {
        Keys.Left => "left", Keys.Right => "right", Keys.Up => "up", Keys.Down => "down",
        Keys.Space => "space", Keys.Enter => "enter", Keys.Escape => "esc",
        Keys.A => "a", Keys.D => "d", Keys.W => "w", Keys.S => "s",
        Keys.F => "f", Keys.E => "e", Keys.Q => "q",
        _ => k.ToString().ToLowerInvariant(),
    };

    public DynObj FindProxy(string name)
    {
        var o = objs.Find(o => !o.dead && o.def.name == name);
        return o == null ? null : o.proxy;
    }
    public void Destroy(RuntimeObj o) { o.dead = true; }
    public void PurgeDead() { for (int i = objs.Count - 1; i >= 0; i--) if (objs[i].dead) objs.RemoveAt(i); }
    public void DestroyByName(string name)
    {
        var o = objs.Find(o => !o.dead && o.def.name == name);
        if (o != null) o.dead = true;
    }
    public DynObj SpawnSimple(string kind, string name, float x, float y)
    {
        var o = AddLive(new LicObject { name = name, kind = kind, x = x, y = y, w = 8, h = 8 });
        BindScript(o);
        return o.proxy;
    }
    public DynObj SpawnFromLua(string kind, DynValue opts)
    {
        var d = new LicObject { name = "spawn" + rnd.Next(100000), kind = kind };
        if (opts.Type == DataType.Table)
            foreach (var pair in opts.Table.Pairs)
            {
                string k = pair.Key.String?.ToLowerInvariant() ?? "";
                var v = pair.Value;
                try
                {
                    switch (k)
                    {
                        case "name": d.name = v.String; break;
                        case "x": d.x = (float)v.Number; break;
                        case "y": d.y = (float)v.Number; break;
                        case "w": d.w = (float)v.Number; break;
                        case "h": d.h = (float)v.Number; break;
                        case "vx": break; // скорость — ниже через прокси
                        case "vy": break;
                        case "color": d.color = v.String; break;
                        case "text": d.text = v.String; break;
                        case "sprite": d.sprite = v.String; break;
                        case "solid": d.solid = v.Boolean; break;
                        case "gravity": d.gravity = v.Boolean; break;
                        case "script": d.script = v.String; break;
                    }
                }
                catch { }
            }
        var o = AddLive(d);
        BindScript(o);
        if (opts.Type == DataType.Table)
        {
            var t = opts.Table;
            if (t.Get("vx").Type == DataType.Number) o.vx = (float)t.Get("vx").Number;
            if (t.Get("vy").Type == DataType.Number) o.vy = (float)t.Get("vy").Number;
            if (t.Get("num").Type == DataType.Number) o.num = (float)t.Get("num").Number;
        }
        return o.proxy;
    }

    public void Update(HashSet<Keys> keys, float dt) => Update(keys, dt, 0, 0, false);

    public void Update(HashSet<Keys> keys, float dt, float mwx, float mwy, bool lmb)
    {
        SetMouse(mwx, mwy, lmb);
        time += dt;
        _keys.Clear();
        foreach (var k in keys) _keys.Add(KeyName(k));
        var sc = Scene;
        for (int i = objs.Count - 1; i >= 0; i--)
        {
            var ob = objs[i];
            if (!ob.dead) continue;
            ob.deadT += dt; // труп лежит 1.2с и только потом исчезает
            if (ob.deadT > 1.2f) objs.RemoveAt(i);
        }
        foreach (var o in objs.ToArray()) // ToArray: скрипты могут спавнить объекты
        {
            if (o.dead) continue;
            BindScript(o);
            if (o.runner != null && o.error == "")
            {
                try { o.runner.Frame(dt, time); }
                catch (Exception ex) { o.error = ErrText(ex); Log(o.def.name + ": " + o.error); }
            }
        }
        // физика: земля и платформы держат только объекты с гравитацией
        // (статический UI без gravity никуда не телепортируется)
        foreach (var o in objs.ToArray())
        {
            if (o.dead) continue;
            if (o.def.gravity) o.vy += sc.gravity * dt;
            o.x += o.vx * dt;
            o.y += o.vy * dt;
            o.onGround = false;
            if (!o.def.gravity) continue;
            float bottom = o.y + o.h / 2;
            if (bottom >= sc.groundY && o.vy >= 0) { o.y = sc.groundY - o.h / 2; o.vy = 0; o.onGround = true; }
            else if (o.vy >= 0)
            {
                // платформы
                foreach (var p in objs)
                {
                    if (p == o || p.dead || !p.def.solid) continue;
                    float top = p.y - p.h / 2;
                    if (Math.Abs(o.x - p.x) < (o.w + p.w) / 2 * 0.9f
                        && bottom >= top && bottom - o.vy * dt <= top + 1)
                    { o.y = top - o.h / 2; o.vy = 0; o.onGround = true; bottom = o.y + o.h / 2; break; }
                }
            }
        }
        // камера за follow-объектом (если не зафиксирована сценой)
        if (!sc.lockCam)
        {
            var f = objs.Find(o => !o.dead && o.def.follow) ?? objs.Find(o => !o.dead);
            if (f != null) camX += (f.x - camX) * (1 - (float)Math.Pow(0.01, dt));
        }
        // марионетки: шаг и замах (трупы замирают, вспышка гаснет у всех)
        foreach (var o in objs)
        {
            o.hitT = Math.Max(0, o.hitT - dt);
            if (o.dead) continue;
            if (o.def.kind != "knight") continue;
            o.walkPh += Math.Abs(o.vx) * dt * 0.9f;
            o.attackT = Math.Max(0, o.attackT - dt);
            if (Math.Abs(o.vx) > 5) o.face = Math.Sign(o.vx);
        }
    }

    int SX(float wx) => (int)Math.Round((wx - camX) * ZOOM * zoom + W / 2);
    int SY(float wy) => (int)Math.Round((wy - camY) * ZOOM * zoom + H / 2);
    public Point ScreenToWorld(int sx, int sy) =>
        new Point((int)((sx - W / 2) / (float)(ZOOM * zoom) + camX), (int)((sy - H / 2) / (float)(ZOOM * zoom) + camY));

    static Color Dark(Color c, float k) => Color.FromArgb(
        Math.Max(0, Math.Min(255, (int)(c.R * k))),
        Math.Max(0, Math.Min(255, (int)(c.G * k))),
        Math.Max(0, Math.Min(255, (int)(c.B * k))));

    bool IsNight()
    {
        try
        {
            var c = ParseColor(Scene.bg1);
            return (c.R * 0.3f + c.G * 0.5f + c.B * 0.2f) < 95;
        }
        catch { return false; }
    }

    void KP(Graphics g, float wx1, float wy1, float wx2, float wy2, float w, Color dark, Color lite)
    {
        float z = ZOOM * zoom;
        using var p1 = new Pen(dark, Math.Max(2, w * z)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p1, SX(wx1), SY(wy1), SX(wx2), SY(wy2));
        using var p2 = new Pen(lite, Math.Max(1, w * z * 0.55f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p2, SX(wx1), SY(wy1) - 1, SX(wx2), SY(wy2) - 1);
    }

    void KBall(Graphics g, float wx, float wy, float r, Color c, Color hi)
    {
        float z = ZOOM * zoom;
        int s = Math.Max(2, (int)(r * 2 * z));
        int cx = SX(wx) - s / 2, cy = SY(wy) - s / 2;
        g.FillEllipse(B(c), cx, cy, s, s);
        g.FillEllipse(B(hi), cx + 1, cy + 1, Math.Max(1, s / 2), Math.Max(1, s / 2));
    }

    void JointBall(Graphics g, float wx, float wy, float r)
    {
        KBall(g, wx, wy, r, Color.FromArgb(90, 90, 100), Color.FromArgb(190, 190, 200));
    }

    // марионетка-рыцарь: у каждого вида своя внешность (sprite), дыхание, замах-дуга, вспышка
    void DrawKnight(Graphics g, RuntimeObj o, Color team)
    {
        string look0 = o.def.sprite ?? "";
        if (look0 == "dog" || look0 == "boar") { DrawBeast(g, o, team, look0 == "boar"); return; }
        if (look0 == "cata" || look0 == "ram" || look0 == "siege") { DrawSiege(g, o, team, look0 == "ram"); return; }
        string look = look0;
        float sc = look switch
        {
            "king" => 1.25f,
            "hero" => 1.15f,
            "pala" => 1.12f,
            "squire" => 0.85f,
            "spear" => 0.95f,
            "xbow" => 0.95f,
            "monk" => 0.95f,
            "sapper" => 0.95f,
            "bandit" => 1.0f,
            _ => 1f,
        };
        Color steel = Color.FromArgb(150, 155, 170), steelD = Color.FromArgb(88, 93, 108);
        Color steelL = Color.FromArgb(205, 210, 222);
        Color skin = Color.FromArgb(232, 185, 138);
        Color teamD = Dark(team, 0.6f);
        float face = o.face >= 0 ? 1 : -1;
        float ph = 1 - Math.Max(0, Math.Min(1, o.attackT / 0.3f)); // 0 удар..1 конец
        float swing = (float)Math.Sin(Math.PI * Math.Min(1, ph)); // дуга замаха
        float moveK = Math.Min(1, Math.Abs(o.vx) / 12f);
        float breathe = moveK < 0.1f ? (float)Math.Sin(time * 2.2 + o.x * 0.5) * 0.5f : 0;
        float feetY = o.y + o.h / 2;
        // тень (у трупа нет — он уже на земле в луже)
        if (!o.dead)
        {
            var st = g.Save();
            g.TranslateTransform(SX(o.x), SY(feetY));
            int sw = (int)(7 * sc * ZOOM * zoom);
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), -sw / 2, -2, sw, 4);
            g.Restore(st);
        }
        float hipX = o.x, hipY = feetY - 5 * sc + breathe * 0.4f;
        float lean = Math.Max(-2, Math.Min(2, o.vx * 0.02f));
        float shX = hipX + lean + face * 0.8f * sc, shY = hipY - 6 * sc + breathe * 0.5f;
        // ноги
        for (int leg = 0; leg < 2; leg++)
        {
            float lp = o.walkPh + (leg == 0 ? 0 : (float)Math.PI);
            float fx = hipX + (float)Math.Sin(lp) * 3 * sc * (0.2f + 0.8f * moveK) + face * sc;
            float fy = feetY - Math.Max(0, (float)Math.Sin(lp + Math.PI / 2)) * 1.6f * sc * moveK;
            float kx = (hipX + fx) / 2 + face * 1.4f * sc, ky = (hipY + fy) / 2 + 0.6f;
            KP(g, hipX, hipY, kx, ky, 2.1f * sc, steelD, steel);
            KP(g, kx, ky, fx, fy, 1.8f * sc, steelD, steel);
            KBall(g, kx, ky, 0.9f * sc, steelD, steel);
            int fsx = SX(fx), fsy = SY(fy);
            g.FillEllipse(B(Color.FromArgb(70, 50, 35)), fsx - 3, fsy - 1, 7, 3);
        }
        // плащ паладина, героя и короля
        if (look == "pala" || look == "king" || look == "hero")
        {
            float sway = (float)Math.Sin(time * 3 + o.x) * 1.2f;
            g.FillPolygon(B(look == "king" ? Color.FromArgb(90, 40, 130) : look == "hero" ? Color.FromArgb(170, 30, 30) : Color.FromArgb(150, 40, 40)),
                new[] { new PointF(SX(shX - face * 2), SY(shY)), new PointF(SX(hipX - face * 3 + sway), SY(hipY + 4)), new PointF(SX(hipX + face * 1 + sway), SY(hipY + 4)) });
        }
        // заплечный мешок сапёра (за спиной, под torso)
        if (look == "sapper")
        {
            float bx = hipX - face * 3.5f * sc, by = hipY - 3 * sc;
            RS2(g, bx, by, 4 * sc, 5 * sc, Color.FromArgb(110, 80, 50));
            RS2(g, bx, by - 2 * sc, 4 * sc, 1.2f, Color.FromArgb(78, 56, 34));
        }
        // торс в цветах команды + золотая отделка паладина/героя + роба монаха + наплечники
        KP(g, hipX, hipY, shX, shY, 5f * sc, teamD, team);
        if (look == "monk")
        {
            g.FillPolygon(B(Color.FromArgb(210, 190, 150)),
                new[] { new PointF(SX(hipX - 3.5f * sc), SY(hipY + 1)), new PointF(SX(hipX + 3.5f * sc), SY(hipY + 1)), new PointF(SX(shX), SY(shY - 1)) });
            RS2(g, shX, shY + 1.5f * sc, 1.4f, 3 * sc, Color.FromArgb(150, 40, 40)); // крест
        }
        else if (look == "pala" || look == "hero") KP(g, hipX + 0.4f, hipY - 1, shX + 0.4f, shY + 1, 3.4f * sc, Color.FromArgb(150, 110, 40), Color.FromArgb(230, 190, 90));
        else KP(g, hipX + 0.4f, hipY - 1, shX + 0.4f, shY + 1, 2f * sc, team, Dark(team, 1.15f));
        if (look == "sword" || look == "pala" || look == "king" || look == "spear")
        {
            KBall(g, shX + face * 1.5f * sc, shY + 0.5f, 1.5f * sc, steelD, steel); // наплечник
            KBall(g, shX - face * 2.5f * sc, shY + 1, 1.3f * sc, steelD, steel);
        }
        else JointBall(g, shX, shY, 1.6f * sc);
        // голова: каждому виду свой верх — капюшоны, шлемы, каска с лампой, корона
        float hx = shX + face * 1.1f * sc, hy = shY - 4.4f * sc;
        KBall(g, hx, hy, 2.8f * sc, skin, Color.FromArgb(245, 210, 170));
        if (look == "xbow" || look == "bandit" || look == "monk")
        {
            KBall(g, hx, hy - 0.5f * sc, 2.6f * sc, look == "monk" ? Color.FromArgb(190, 170, 135) : Color.FromArgb(96, 70, 48), look == "monk" ? Color.FromArgb(225, 210, 175) : Color.FromArgb(130, 100, 70)); // капюшон
            if (look == "bandit") RS2(g, hx - face * 1.5f * sc, hy + 0.5f * sc, 4 * sc, 2 * sc, Color.FromArgb(30, 25, 25)); // маска
            if (look == "xbow") RS2(g, hx + face * 0.5f * sc, hy - 3.4f * sc, 3 * sc, 1.2f, Color.FromArgb(60, 45, 30)); // ремешок колчана
        }
        else if (look == "squire")
        {
            KBall(g, hx, hy - 0.9f * sc, 2.5f * sc, steelD, steel);
            RS2(g, hx, hy - 1.2f * sc, 7 * sc, 1.2f, steelD); // широкие поля kettle-helm
        }
        else if (look == "spear")
        {
            // островерхий бацинет копейщика
            g.FillPolygon(B(steelD), new[] { new PointF(SX(hx - 2.5f * sc), SY(hy - 1 * sc)), new PointF(SX(hx + 2.5f * sc), SY(hy - 1 * sc)), new PointF(SX(hx + face * 0.5f * sc), SY(hy - 6 * sc)) });
            g.FillPolygon(B(steel), new[] { new PointF(SX(hx - 1.5f * sc), SY(hy - 1.5f * sc)), new PointF(SX(hx + 2.5f * sc), SY(hy - 1.5f * sc)), new PointF(SX(hx + face * 0.5f * sc), SY(hy - 5 * sc)) });
        }
        else if (look == "sword")
        {
            KBall(g, hx, hy - 0.9f * sc, 2.5f * sc, steelD, steelL); // круглый шлем
            RS2(g, hx + face * 1.6f * sc, hy - 0.5f * sc, 1.2f, 3.4f * sc, Color.FromArgb(15, 12, 12)); // носовая стрелка
        }
        else if (look == "sapper")
        {
            // кожаная каска сапёра + лобная лампа
            KBall(g, hx, hy - 0.9f * sc, 2.5f * sc, Color.FromArgb(96, 64, 40), Color.FromArgb(140, 100, 65));
            RS2(g, hx, hy - 1.4f * sc, 6 * sc, 1.2f, Color.FromArgb(70, 48, 30)); // поля каски
            float lampFl = 1 + (float)Math.Sin(time * 9 + o.x) * 0.15f;
            KBall(g, hx + face * 1.8f * sc, hy - 2.2f * sc, 1.1f * lampFl, Color.FromArgb(200, 150, 40), Color.FromArgb(255, 230, 150)); // лампа
        }
        else if (look == "pala")
        {
            KBall(g, hx, hy - 0.9f * sc, 2.6f * sc, steelD, steelL);
            RS2(g, hx + face * 1.2f * sc, hy - 1 * sc, 1.2f, 4 * sc, Color.FromArgb(15, 12, 12)); // смотровая щель
            // крылья на шлеме
            foreach (float side in new[] { -1f, 1f })
                g.FillPolygon(B(Color.FromArgb(235, 235, 240)),
                    new[] { new PointF(SX(hx + side * 2 * sc), SY(hy - 3 * sc)), new PointF(SX(hx + side * 5 * sc), SY(hy - 6 * sc)), new PointF(SX(hx + side * 4 * sc), SY(hy - 1 * sc)) });
        }
        else if (look == "hero")
        {
            // золочёный шлем героя + высокий красный гребень
            KBall(g, hx, hy - 0.9f * sc, 2.6f * sc, Color.FromArgb(150, 110, 40), Color.FromArgb(230, 190, 90));
            RS2(g, hx + face * 1.2f * sc, hy - 1 * sc, 1.2f, 4 * sc, Color.FromArgb(15, 12, 12));
            g.FillPolygon(B(Color.FromArgb(180, 35, 35)),
                new[] { new PointF(SX(hx - 1 * sc), SY(hy - 4.5f * sc)), new PointF(SX(hx + 1 * sc), SY(hy - 4.5f * sc)), new PointF(SX(hx), SY(hy - 9 * sc)) });
        }
        else KBall(g, hx, hy - 0.9f * sc, 2.5f * sc, steelD, steel); // шлем
        if (look == "king") // корона
            for (int s2 = -1; s2 <= 1; s2++)
                RS2(g, hx + s2 * 2 * sc - 1, hy - 6.5f * sc, 2, 3 * sc, Color.FromArgb(230, 190, 60));
        else if (look == "hero")
            g.FillRectangle(B(Color.FromArgb(180, 35, 35)), SX(hx) - 1, SY(hy) - (int)(9 * sc * ZOOM * zoom), 3, (int)(4.5f * sc * ZOOM * zoom)); // высокий гребень
        else if (look == "sword" || look == "spear" || look == "squire")
            g.FillRectangle(B(team), SX(hx) - 1, SY(hy) - (int)(5.5f * sc * ZOOM * zoom), 3, (int)(3.5f * sc * ZOOM * zoom)); // плюмаж
        // у капюшонов, каски и глухих шлемов плюмажа нет
        g.FillRectangle(B(Color.FromArgb(20, 15, 12)), SX(hx) + (int)(face * 4 * sc) - 1, SY(hy) - 1, 2, 2);
        // оружейная рука: дуга замаха + выпад в конце; копьё колет прямо
        {
            float lunge = ph > 0.5f ? (ph - 0.5f) * 7 * sc : 0;
            float elX = shX + face * 3f * sc, elY = shY + (1.5f - swing * 4f) * sc;
            float hx2 = shX + face * (6 * sc + lunge), hy2 = shY + (1 - swing * 5.5f) * sc;
            if (look == "spear") { hx2 = shX + face * (6 * sc + lunge * 1.6f); hy2 = shY + 0.5f * sc; elY = shY + 1 * sc; }
            KP(g, shX, shY, elX, elY, 1.9f * sc, teamD, team);
            KP(g, elX, elY, hx2, hy2, 1.7f * sc, skin, Color.FromArgb(245, 210, 170));
            KBall(g, hx2, hy2, 1.2f * sc, Color.FromArgb(96, 64, 40), Color.FromArgb(175, 125, 85));
            DrawBlade(g, look, hx2, hy2, face, swing, sc, steelD, steelL, team);
            if (swing > 0.25f && swing < 0.9f && look != "spear" && look != "xbow" && look != "monk")
            {
                // свист клинка: дуга из точек
                Color arc = Color.FromArgb(90, 255, 255, 255);
                for (int a2 = 0; a2 < 3; a2++)
                    KBall(g, hx2 + face * (2.5f + a2 * 2) * sc, hy2 - (2 + a2 * 1.6f - swing * 2) * sc, 0.8f * sc, arc, arc);
            }
        }
        // щит по виду: круглый — оруженосцу/копейщику/сапёру, башня — паладину, парадный — герою
        if (look != "xbow" && look != "monk")
        {
            var st = g.Save();
            g.TranslateTransform(SX(o.x - face * 4.5f * sc), SY(shY + 2.5f * sc));
            float z = ZOOM * zoom * sc;
            bool foe = look == "bandit" || look == "king" || look == "dog" || look == "boar";
            if (look == "squire" || look == "spear" || look == "sapper")
            {
                // круглый кулачный щит с умбоном
                g.FillEllipse(B(teamD), (int)(-3 * z), (int)(-3 * z), (int)(6 * z), (int)(6 * z));
                g.FillEllipse(B(foe ? steel : team), (int)(-2.3f * z), (int)(-2.3f * z), (int)(4.6f * z), (int)(4.6f * z));
                g.FillEllipse(B(steelL), (int)(-0.9f * z), (int)(-0.9f * z), (int)(1.8f * z), (int)(1.8f * z));
            }
            else if (look == "pala")
            {
                // высокий башенный щит с золотой каймой
                g.FillEllipse(B(teamD), (int)(-3 * z), (int)(-5 * z), (int)(6 * z), (int)(10 * z));
                g.FillEllipse(B(team), (int)(-2.3f * z), (int)(-4.3f * z), (int)(4.6f * z), (int)(8.6f * z));
                using var rp2 = new Pen(Color.FromArgb(230, 190, 60), 2);
                g.DrawEllipse(rp2, (int)(-3 * z), (int)(-5 * z), (int)(6 * z), (int)(10 * z));
                g.FillRectangle(B(Color.FromArgb(235, 235, 240)), (int)(-0.7f * z), (int)(-3.4f * z), (int)(1.4f * z), (int)(6.8f * z));
                g.FillRectangle(B(Color.FromArgb(235, 235, 240)), (int)(-1.7f * z), (int)(-1.6f * z), (int)(3.4f * z), (int)(1.4f * z));
            }
            else if (look == "hero")
            {
                // парадный щит героя: золото + красный крест
                g.FillEllipse(B(Color.FromArgb(120, 85, 30)), (int)(-3 * z), (int)(-3.5f * z), (int)(6 * z), (int)(7 * z));
                g.FillEllipse(B(Color.FromArgb(230, 190, 90)), (int)(-2.4f * z), (int)(-2.9f * z), (int)(4.8f * z), (int)(5.8f * z));
                g.FillRectangle(B(Color.FromArgb(180, 35, 35)), (int)(-0.7f * z), (int)(-2.2f * z), (int)(1.4f * z), (int)(4.4f * z));
                g.FillRectangle(B(Color.FromArgb(180, 35, 35)), (int)(-1.8f * z), (int)(-1.1f * z), (int)(3.6f * z), (int)(1.4f * z));
            }
            else
            {
                g.FillEllipse(B(teamD), (int)(-3 * z), (int)(-3.5f * z), (int)(6 * z), (int)(7 * z));
                g.FillEllipse(B(team), (int)(-2.4f * z), (int)(-2.9f * z), (int)(4.8f * z), (int)(5.8f * z));
                if (look == "king")
                {
                    using var rp = new Pen(Color.FromArgb(230, 190, 60), 2);
                    g.DrawEllipse(rp, (int)(-3 * z), (int)(-3.5f * z), (int)(6 * z), (int)(7 * z));
                }
                else if (!foe)
                {
                    g.FillRectangle(B(Color.FromArgb(235, 235, 240)), (int)(-0.7f * z), (int)(-2.2f * z), (int)(1.4f * z), (int)(4.4f * z));
                    g.FillRectangle(B(Color.FromArgb(235, 235, 240)), (int)(-1.8f * z), (int)(-1.1f * z), (int)(3.6f * z), (int)(1.4f * z));
                }
                else g.FillEllipse(B(steelL), (int)(-0.8f * z), (int)(-0.8f * z), (int)(1.6f * z), (int)(1.6f * z));
            }
            g.Restore(st);
        }
        // вспышка попадания
        if (o.hitT > 0)
        {
            Color wh = Color.FromArgb(170, 255, 255, 255);
            KP(g, hipX, hipY, shX, shY, 5.6f * sc, wh, wh);
            KBall(g, hx, hy, 3f * sc, wh, wh);
        }
        // ночью живые держат факел в левой руке + круг света
        if (!o.dead && IsNight() && look != "dog" && look != "boar" && look != "cata" && look != "ram" && look != "siege")
        {
            float tx = o.x - face * 4.5f * sc, ty = shY + 0.5f * sc;
            KP(g, o.x - face * 3 * sc, shY + 2 * sc, tx, ty - 4 * sc, 0.9f * sc, Color.FromArgb(74, 55, 40), Color.FromArgb(110, 80, 50));
            float fl = 1.2f + (float)Math.Sin(time * 11 + o.x * 2) * 0.3f;
            var halo = Color.FromArgb(50, 255, 200, 100);
            KBall(g, o.x, shY - 2 * sc, 9 * sc, halo, halo);
            KBall(g, tx, ty - 5 * sc, 1.5f * fl, Color.FromArgb(226, 88, 34), Color.FromArgb(255, 217, 122));
        }
    }

    // оружие в руке по виду: меч / кинжал / топор / арбалет / копьё / посох
    void DrawBlade(Graphics g, string look, float hx, float hy, float face, float swing, float sc, Color steelD, Color steelL, Color team)
    {
        if (look == "xbow")
        {
            KP(g, hx, hy, hx + face * 5 * sc, hy - swing * 2, 1.2f * sc, Color.FromArgb(91, 58, 30), Color.FromArgb(130, 90, 55));
            KP(g, hx + face * 3.5f * sc, hy - 2.5f * sc - swing, hx + face * 3.5f * sc, hy + 2.5f * sc - swing, 1f * sc, steelD, steelL);
            KP(g, hx + face * 1 * sc, hy - swing, hx + face * 7 * sc, hy - swing, 0.8f * sc, steelD, steelL);
            return;
        }
        if (look == "spear")
        {
            // длинное копьё колет прямо
            KP(g, hx - face * 2 * sc, hy + 0.5f * sc, hx + face * 9 * sc, hy - swing * 1.5f, 0.9f * sc, Color.FromArgb(91, 58, 30), Color.FromArgb(130, 90, 55));
            g.FillPolygon(B(steelL), new[] { new PointF(SX(hx + face * 9 * sc), SY(hy - swing * 1.5f - 1.2f)), new PointF(SX(hx + face * 9 * sc), SY(hy - swing * 1.5f + 1.2f)), new PointF(SX(hx + face * 12 * sc), SY(hy - swing * 1.5f)) });
            return;
        }
        if (look == "monk")
        {
            // посох с крестом
            KP(g, hx, hy + 3 * sc, hx + face * 1 * sc, hy - 8 * sc - swing * 2, 1.1f * sc, Color.FromArgb(110, 80, 50), Color.FromArgb(150, 115, 75));
            RS2(g, hx + face * 1 * sc, hy - 8 * sc - swing * 2, 3 * sc, 1.2f, Color.FromArgb(201, 162, 39));
            RS2(g, hx + face * 1 * sc, hy - 9.5f * sc - swing * 2, 1.2f, 3 * sc, Color.FromArgb(201, 162, 39));
            return;
        }
        if (look == "sapper")
        {
            // кирка: рукоять + двойное остриё
            KP(g, hx, hy, hx + face * 3 * sc, hy - 7 * sc - swing * 3, 1f * sc, Color.FromArgb(91, 58, 30), Color.FromArgb(130, 90, 55));
            float px = hx + face * 3 * sc, py = hy - 7 * sc - swing * 3;
            g.FillPolygon(B(steelL), new[] { new PointF(SX(px - 3.5f * sc), SY(py)), new PointF(SX(px + 3.5f * sc), SY(py)), new PointF(SX(px + 2 * sc), SY(py - 2 * sc)), new PointF(SX(px - 2 * sc), SY(py - 2 * sc)) });
            return;
        }
        float len = look == "squire" || look == "bandit" ? 4.5f : look == "king" ? 8f : look == "hero" ? 8.5f : 6.5f;
        float tx = hx + face * (len - swing * 1.5f) * sc, ty = hy - (1.5f + swing * 5f) * sc;
        if (look == "hero")
        {
            // длинный меч героя с золотой гардой
            KP(g, hx, hy, tx, ty, 1.3f * sc, Color.FromArgb(150, 110, 40), steelL);
            RS2(g, hx + face * 1.5f * sc, hy - 1 * sc - swing * 2, 1.4f, 4 * sc, Color.FromArgb(201, 162, 39));
        }
        else KP(g, hx, hy, tx, ty, 1.1f * sc, steelD, steelL);
        if (look == "king")
        {
            g.FillRectangle(B(steelD), SX(tx) - 4, SY(ty) - 5, 8, 10);
            g.FillRectangle(B(steelL), SX(tx) - 4, SY(ty) - 5, 8, 2);
        }
        else KBall(g, tx, ty, 0.8f * sc, steelL, Color.White);
    }

    void RS2(Graphics g, float wx, float wy, float w, float h, Color c)
    {
        float z = ZOOM * zoom;
        g.FillRectangle(B(c), SX(wx) - (int)(w * z / 2), SY(wy) - (int)(h * z / 2), Math.Max(1, (int)(w * z)), Math.Max(1, (int)(h * z)));
    }

    // осадная машина: катапульта (ложка качается при выстреле) или таран (бревно на цепях)
    void DrawSiege(Graphics g, RuntimeObj o, Color team, bool ram)
    {
        Color wood = Color.FromArgb(110, 80, 50), woodD = Color.FromArgb(78, 56, 34);
        Color steelD = Color.FromArgb(88, 93, 108), steelL = Color.FromArgb(205, 210, 222);
        float face = o.face >= 0 ? 1 : -1;
        float atk = Math.Max(0, Math.Min(1, o.attackT / 0.3f));
        float feetY = o.y + o.h / 2;
        float wdt = o.w;
        // тень (у подбитой машины нет)
        if (!o.dead)
        {
            var st = g.Save();
            g.TranslateTransform(SX(o.x), SY(feetY));
            int sw = (int)((wdt + 4) * ZOOM * zoom);
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), -sw / 2, -2, sw, 4);
            g.Restore(st);
        }
        // колёса со спицами (крутятся на ходу)
        foreach (float off in new[] { -wdt * 0.32f, wdt * 0.32f })
        {
            float wx = o.x + off, wy = feetY - 2.2f;
            KBall(g, wx, wy, 2.2f, woodD, wood);
            for (int s2 = 0; s2 < 3; s2++)
            {
                float aa = o.walkPh + s2 * (float)Math.PI / 3;
                KP(g, wx - (float)Math.Cos(aa) * 1.8f, wy - (float)Math.Sin(aa) * 1.8f,
                    wx + (float)Math.Cos(aa) * 1.8f, wy + (float)Math.Sin(aa) * 1.8f, 0.7f, woodD, woodD);
            }
            KBall(g, wx, wy, 0.7f, steelD, steelL);
        }
        if (!ram)
        {
            // рама + ложка катапульты (откидывается при выстреле)
            KP(g, o.x - wdt * 0.3f, feetY - 2, o.x, feetY - 9, 1.4f, woodD, wood);
            KP(g, o.x + wdt * 0.3f, feetY - 2, o.x, feetY - 9, 1.4f, woodD, wood);
            RS2(g, o.x, feetY - 9.5f, 2, 2, steelD);
            float armA = -0.9f + atk * 1.6f; // замах назад → бросок вперёд
            float ax = o.x + (float)Math.Sin(armA) * 7 * face, ay = feetY - 9 - (float)Math.Cos(armA) * 7;
            KP(g, o.x, feetY - 9, ax, ay, 1.2f, woodD, wood);
            KBall(g, ax, ay, 1.6f, woodD, wood);
            if (atk < 0.4f) KBall(g, ax, ay - 1, 1.1f, Color.FromArgb(120, 120, 125), Color.FromArgb(170, 170, 180)); // камень
            // расчет рядом
            KBall(g, o.x - face * (wdt * 0.3f + 1.5f), feetY - 4, 1.6f, Color.FromArgb(96, 70, 48), Color.FromArgb(130, 100, 70));
        }
        else
        {
            // навес + бревно тарана на цепях (качается)
            RS2(g, o.x, feetY - 9, wdt + 2, 1.6f, woodD);
            KP(g, o.x - wdt * 0.35f, feetY - 2, o.x - wdt * 0.3f, feetY - 9.5f, 1.3f, woodD, wood);
            KP(g, o.x + wdt * 0.35f, feetY - 2, o.x + wdt * 0.3f, feetY - 9.5f, 1.3f, woodD, wood);
            float sw2 = (float)Math.Sin(time * (atk > 0 ? 10 : 2) + o.x) * (atk > 0 ? 3f : 0.8f);
            float lx = o.x + face * 1 + sw2;
            KP(g, lx - 2, feetY - 9, lx - 2, feetY - 4.5f, 0.6f, steelD, steelD);
            KP(g, lx + 2, feetY - 9, lx + 2, feetY - 4.5f, 0.6f, steelD, steelD);
            KP(g, lx - 4, feetY - 4.5f, lx + 4, feetY - 4.5f, 2.6f, woodD, wood);
            KBall(g, lx + face * 4.5f, feetY - 4.5f, 1.8f, steelD, steelL); // стальная башка
            // черепа на навесе
            KBall(g, o.x - wdt * 0.2f, feetY - 11, 0.9f, Color.FromArgb(225, 215, 190), Color.White);
            KBall(g, o.x + wdt * 0.25f, feetY - 11, 0.9f, Color.FromArgb(225, 215, 190), Color.White);
        }
    }

    // зверь: пёс шустрый, кабан грузный с клыками. Рысь иноходи, атака — выпад головой.
    void DrawBeast(Graphics g, RuntimeObj o, Color c, bool bulky)
    {
        Color furD = Dark(c, 0.55f), furL = Dark(c, 1.15f);
        float face = o.face >= 0 ? 1 : -1;
        float atk = Math.Max(0, Math.Min(1, o.attackT / 0.3f));
        float swing = (float)Math.Sin(Math.PI * Math.Min(1, 1 - atk));
        float moveK = Math.Min(1, Math.Abs(o.vx) / 12f);
        float bodyY = o.y + (bulky ? 0 : -1);
        float len = bulky ? 5.5f : 4.5f, ht = bulky ? 4.5f : 3.5f;
        // тень (у трупа нет)
        if (!o.dead)
        {
            var st = g.Save();
            g.TranslateTransform(SX(o.x), SY(o.y + o.h / 2));
            int sw = (int)((len * 2 + 3) * ZOOM * zoom);
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), -sw / 2, -2, sw, 4);
            g.Restore(st);
        }
        // хвост
        {
            float wag = (float)Math.Sin(time * 8 + o.x) * 1.5f;
            KP(g, o.x - face * len, bodyY - 1, o.x - face * (len + 3), bodyY - 2 + wag, 0.9f, furD, furD);
        }
        // ноги рысью
        for (int leg = 0; leg < 4; leg++)
        {
            float ph = o.walkPh + (leg % 2 == 0 ? 0 : (float)Math.PI) + (leg < 2 ? 0 : 0.6f);
            float fx = o.x + (leg < 2 ? -len * 0.6f : len * 0.6f) + (float)Math.Sin(ph) * 2 * moveK;
            float fy = o.y + o.h / 2 - Math.Max(0, (float)Math.Sin(ph + Math.PI / 2)) * 1.4f * moveK;
            KP(g, fx, bodyY + 1, fx, fy, 1.2f, furD, c);
        }
        // туша + щетина кабана
        KP(g, o.x - face * len, bodyY, o.x + face * len, bodyY - 0.5f, ht, furD, c);
        if (bulky)
            for (int s2 = 0; s2 < 4; s2++)
                RS2(g, o.x - face * len * 0.4f + s2 * face * 1.5f, bodyY - ht / 2 - 1, 0.8f, 1.6f, furD);
        // голова с ушами, глазами; выпад при атаке
        float gx = o.x + face * (len + 1.5f + swing * 2.5f), gy = bodyY - 2.5f + swing * 1f;
        KBall(g, gx, gy, bulky ? 2.6f : 2.1f, c, furL);
        RS2(g, gx - 1.5f, gy - 4f, 1.6f, 2.2f, furD);
        RS2(g, gx + 0.5f, gy - 4f, 1.6f, 2.2f, furD);
        RS2(g, gx + face * 1.5f - 1, gy - 1, 2, 2, Color.FromArgb(180, 30, 30));
        RS2(g, gx + face * 2.5f - 1, gy + 1, 2, 1.6f, Dark(c, 0.7f));
        if (!bulky) RS2(g, gx - face * 2.5f, gy + 1.5f, 2.4f, 1.2f, Color.FromArgb(180, 40, 40)); // ошейник пса
        if (bulky) // клыки
        {
            g.FillPolygon(B(Color.White), new[] { new PointF(SX(gx + face * 3f), SY(gy + 1.5f)), new PointF(SX(gx + face * 4f), SY(gy + 1.5f)), new PointF(SX(gx + face * 3.4f), SY(gy - 1)) });
            g.FillPolygon(B(Color.White), new[] { new PointF(SX(gx + face * 1f), SY(gy + 1.8f)), new PointF(SX(gx + face * 2f), SY(gy + 1.8f)), new PointF(SX(gx + face * 1.4f), SY(gy - 0.5f)) });
        }
        if (o.hitT > 0)
        {
            Color wh = Color.FromArgb(170, 255, 255, 255);
            KP(g, o.x - face * len, bodyY, o.x + face * len, bodyY, ht + 1, wh, wh);
        }
    }

    // замок с зубцами, воротами, флагом; битый — трещины, дым, руины
    void DrawCastle(Graphics g, RuntimeObj o, Color team)
    {
        Color stone = Color.FromArgb(135, 130, 125), stoneD = Color.FromArgb(95, 90, 85);
        Color teamD = Dark(team, 0.7f);
        float frac = o.maxnum > 0 ? Math.Max(0, Math.Min(1, o.num / o.maxnum)) : 1;
        float cx = o.x, gy = o.y + o.h / 2;
        float wdt = o.w, hgt = o.h;
        // тень
        {
            var st = g.Save();
            g.TranslateTransform(SX(cx), SY(gy));
            int sw = (int)(wdt * ZOOM * zoom);
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), -sw / 2, -2, sw, 4);
            g.Restore(st);
        }
        float top = gy - hgt;
        if (frac < 0.33f) top = gy - hgt * 0.45f; // руины
        KP(g, cx - wdt / 2, gy, cx - wdt / 2, top, 3f, stoneD, stone);
        KP(g, cx + wdt / 2, gy, cx + wdt / 2, top, 3f, stoneD, stone);
        RS2(g, cx, (gy + top) / 2, wdt, gy - top, stone);
        RS2(g, cx - wdt / 2, top - 1, wdt, 2, stoneD);
        if (frac >= 0.33f)
        {
            // зубцы
            for (int m = 0; m < 5; m++)
            {
                if (frac < 0.66f && m == 2) continue; // выбитый зубец
                RS2(g, cx - wdt / 2 + 1 + m * (wdt - 2) / 4, top - 3, 2.5f, 3, stone);
            }
            // ворота
            RS2(g, cx, top + (gy - top) * 0.62f, 5, (gy - top) * 0.4f, Color.FromArgb(60, 50, 45));
            // окна
            RS2(g, cx - wdt / 4, top + 5, 2, 3, Color.FromArgb(255, 220, 130));
            RS2(g, cx + wdt / 4, top + 5, 2, 3, Color.FromArgb(255, 220, 130));
            float tfl = 1.5f + (float)Math.Sin(time * 13 + cx * 2) * 0.5f; // факелы у ворот
            RS2(g, cx - 5.5f, top + (gy - top) * 0.55f, 1, 4, Color.FromArgb(74, 55, 40));
            RS2(g, cx + 5.5f, top + (gy - top) * 0.55f, 1, 4, Color.FromArgb(74, 55, 40));
            KBall(g, cx - 5.5f, top + (gy - top) * 0.55f - 2.5f, 1.1f * tfl, Color.FromArgb(226, 88, 34), Color.FromArgb(255, 217, 122));
            KBall(g, cx + 5.5f, top + (gy - top) * 0.55f - 2.5f, 1.1f * (2 - tfl), Color.FromArgb(226, 88, 34), Color.FromArgb(255, 217, 122));
            // флаг команды
            float sway = (float)Math.Sin(time * 4 + cx) * 1.2f;
            RS2(g, cx, top - 5, 1, 7, Color.FromArgb(74, 55, 40));
            g.FillPolygon(B(team), new[] { new PointF(SX(cx), SY(top - 8)), new PointF(SX(cx + 7 + sway), SY(top - 6.5f)), new PointF(SX(cx), SY(top - 5)) });
            if (frac < 0.66f) // трещины
            {
                KP(g, cx - 3, top + 6, cx - 1, top + 14, 0.8f, stoneD, stoneD);
                KP(g, cx + 4, top + 10, cx + 2, top + 18, 0.8f, stoneD, stoneD);
            }
        }
        else
        {
            // щебень
            g.FillPolygon(B(stoneD), new[] { new PointF(SX(cx - wdt / 2), SY(gy)), new PointF(SX(cx), SY(top)), new PointF(SX(cx + wdt / 2), SY(gy)) });
            RS2(g, cx - 4, gy - 3, 8, 3, Color.FromArgb(70, 60, 55));
        }
        if (frac < 0.5f) // дым
        {
            float rise = (time * 9 + cx) % 14;
            KBall(g, cx + 2, gy - hgt - rise, 2.2f, Color.FromArgb(90, 120, 120, 120), Color.FromArgb(60, 150, 150, 150));
            KBall(g, cx - 3, gy - hgt - rise * 0.6f, 1.6f, Color.FromArgb(90, 130, 130, 130), Color.FromArgb(60, 150, 150, 150));
        }
        // полоска прочности
        RS2(g, cx, gy + 3, 22, 2.5f, Color.FromArgb(0, 0, 0, 160));
        RS2(g, cx - 11 + 11 * frac, gy + 3, 22 * frac, 2.5f, frac > 0.5f ? Color.FromArgb(61, 220, 95) : frac > 0.25f ? Color.FromArgb(230, 190, 60) : Color.FromArgb(220, 60, 60));
    }

    // лагерь: частокол, шатры, костёр с огнём
    void DrawCamp(Graphics g, RuntimeObj o, Color team)
    {
        Color wood = Color.FromArgb(110, 85, 55), woodD = Color.FromArgb(80, 60, 40);
        Color teamD = Dark(team, 0.7f);
        float cx = o.x, gy = o.y + o.h / 2;
        {
            var st = g.Save();
            g.TranslateTransform(SX(cx), SY(gy));
            int sw = (int)(o.w * ZOOM * zoom);
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), -sw / 2, -2, sw, 4);
            g.Restore(st);
        }
        // частокол
        for (int p2 = 0; p2 < 7; p2++)
        {
            float px = cx - o.w / 2 + 1 + p2 * (o.w - 2) / 6;
            RS2(g, px, gy - 5, 1.6f, 10, (p2 % 2 == 0) ? wood : woodD);
            g.FillPolygon(B(woodD), new[] { new PointF(SX(px) - 2, SY(gy - 10)), new PointF(SX(px) + 2, SY(gy - 10)), new PointF(SX(px), SY(gy - 12)) });
        }
        // шатры
        foreach (float off in new[] { -o.w * 0.22f, o.w * 0.22f })
        {
            float tx = cx + off;
            g.FillPolygon(B(teamD), new[] { new PointF(SX(tx - 5), SY(gy)), new PointF(SX(tx + 5), SY(gy)), new PointF(SX(tx), SY(gy - 11)) });
            g.FillPolygon(B(team), new[] { new PointF(SX(tx - 3), SY(gy)), new PointF(SX(tx + 3), SY(gy)), new PointF(SX(tx), SY(gy - 11)) });
            RS2(g, tx, gy - 2.5f, 2.4f, 5, Color.FromArgb(40, 30, 25));
        }
        // костёр
        {
            float fx = cx, fy = gy;
            RS2(g, fx - 2, fy - 1, 5, 1.6f, Color.FromArgb(74, 55, 40));
            RS2(g, fx - 2, fy - 1, 5, 1.6f, Color.FromArgb(74, 55, 40));
            float fl = 2.5f + (float)Math.Sin(time * 11 + cx) * 0.8f;
            g.FillPolygon(B(Color.FromArgb(226, 88, 34)), new[] { new PointF(SX(fx - 2), SY(fy - 1)), new PointF(SX(fx + 2), SY(fy - 1)), new PointF(SX(fx), SY(fy - 1 - fl - 2)) });
            g.FillPolygon(B(Color.FromArgb(255, 217, 122)), new[] { new PointF(SX(fx - 1), SY(fy - 1)), new PointF(SX(fx + 1), SY(fy - 1)), new PointF(SX(fx), SY(fy - 1 - fl)) });
        }
        // череп на шесте
        RS2(g, cx + o.w / 2 - 1, gy - 8, 1.4f, 12, woodD);
        KBall(g, cx + o.w / 2 - 1, gy - 10, 1.4f, Color.FromArgb(225, 215, 190), Color.White);
        // стойка с копьями
        RS2(g, cx - o.w / 2 + 1, gy - 5, 1.2f, 9, woodD);
        KP(g, cx - o.w / 2 - 1, gy - 9, cx - o.w / 2 + 1.5f, gy - 2, 0.9f, woodD, wood);
        KP(g, cx - o.w / 2 + 3, gy - 9, cx - o.w / 2 + 0.5f, gy - 2, 0.9f, woodD, wood);
        KBall(g, cx - o.w / 2 - 1, gy - 9.5f, 0.7f, Color.FromArgb(205, 210, 222), Color.White);
    }

    // башня обороны: дерево/камень по типу (num 1 лук, 2 пушка)
    void DrawTower(Graphics g, RuntimeObj o)
    {
        bool cannon = o.num > 1.5f;
        Color stone = Color.FromArgb(135, 130, 125), stoneD = Color.FromArgb(95, 90, 85);
        Color wood = Color.FromArgb(110, 80, 50), woodD = Color.FromArgb(80, 58, 36);
        float cx = o.x, gy = o.y + o.h / 2, hgt = o.h, wdt = o.w;
        {
            var st = g.Save();
            g.TranslateTransform(SX(cx), SY(gy));
            int sw = (int)(wdt * ZOOM * zoom);
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), -sw / 2, -2, sw, 4);
            g.Restore(st);
        }
        float top = gy - hgt;
        // каменный низ с кладкой
        RS2(g, cx, (gy + top) / 2 + 2, wdt, hgt - 4, stone);
        for (int r2 = 0; r2 < 3; r2++)
            RS2(g, cx, top + 5 + r2 * 4, wdt - 1, 0.8f, stoneD);
        RS2(g, cx, top + 1, wdt + 1, 2, stoneD);
        if (!cannon)
        {
            // деревянный верх лучников + бойницы
            RS2(g, cx, top - 2.5f, wdt - 1, 6, wood);
            RS2(g, cx, top - 5, wdt + 1, 1.6f, woodD);
            for (int s2 = -1; s2 <= 1; s2++)
                RS2(g, cx + s2 * (wdt / 4), top - 2.5f, 1.4f, 3, Color.FromArgb(30, 25, 25));
            g.FillPolygon(B(woodD), new[] { new PointF(SX(cx - wdt / 2 - 1), SY(top - 5)), new PointF(SX(cx + wdt / 2 + 1), SY(top - 5)), new PointF(SX(cx), SY(top - 10)) });
        }
        else
        {
            // каменный верх + жерло пушки
            RS2(g, cx, top - 2, wdt - 1, 5, stone);
            for (int m = 0; m < 4; m++)
                RS2(g, cx - wdt / 2 + 1 + m * (wdt - 2) / 3, top - 5.5f, 2, 2.5f, stone);
            RS2(g, cx + 1, top - 1, 7, 3, Color.FromArgb(50, 50, 55));
            RS2(g, cx + 7.5f, top - 1, 2, 3, Color.FromArgb(200, 120, 40));
            float sway = (float)Math.Sin(time * 4 + cx) * 1f;
            RS2(g, cx - wdt / 4, top - 11, 1.2f, 6, Color.FromArgb(74, 55, 40));
            g.FillPolygon(B(Color.FromArgb(150, 40, 40)), new[] { new PointF(SX(cx - wdt / 4), SY(top - 14)), new PointF(SX(cx - wdt / 4 + 6 + sway), SY(top - 12.5f)), new PointF(SX(cx - wdt / 4), SY(top - 11)) });
        }
    }

    // кнопка: скруглённый корпус с тенью, бликом сверху и тёмной каймой; наведение подсвечивает.
    // Имя задаёт иконку: btn_u1..8 — мини-боец, остальные — глиф действия.
    void DrawButton(Graphics g, RuntimeObj o, Color c)
    {
        float z = ZOOM * zoom;
        int w = Math.Max(6, (int)(o.w * z)), h = Math.Max(6, (int)(o.h * z));
        int x = SX(o.x) - w / 2, y = SY(o.y) - h / 2;
        bool hov = !o.dead && Math.Abs(mouseWX - o.x) < o.w / 2 && Math.Abs(mouseWY - o.y) < o.h / 2;
        bool press = hov && mouseClicked; // кадр клика — кнопка вжата
        if (hov) c = Color.FromArgb(
            Math.Min(255, c.R + 30), Math.Min(255, c.G + 30), Math.Min(255, c.B + 30));
        if (press) c = Dark(c, 0.8f);
        using var path = RoundedRect(x, y + (press ? 1 : 0), w, h, Math.Min(6, h / 2));
        g.FillRectangle(B(Color.FromArgb(90, 0, 0, 0)), x + 1, y + 2, w, h); // тень под кнопкой
        using (var body = new LinearGradientBrush(new Rectangle(x, y, w, h), Light(c, 1.25f), c, LinearGradientMode.Vertical))
            g.FillPath(body, path);
        int hh = Math.Max(2, h / 3); // верхний блик
        using var hi = RoundedRect(x + 2, y + 1, w - 4, hh, 3);
        g.FillPath(B(Color.FromArgb(hov ? 110 : 70, 255, 255, 255)), hi);
        using var pen = new Pen(Dark(c, 0.5f), 2);
        g.DrawPath(pen, path);
        DrawButtonIcon(g, o.def.name, x + w / 2, y + h / 2, w, h, z);
    }

    static string UnitLook(string btn) => btn switch
    {
        "btn_u1" => "squire", "btn_u2" => "sword", "btn_u3" => "pala",
        "btn_u4" => "xbow", "btn_u5" => "spear", "btn_u6" => "monk",
        "btn_u7" => "cata", "btn_u8" => "sapper", _ => "",
    };

    // иконка внутри кнопки: мини-бюст бойца по центру верха (текст цены — снизу),
    // глиф действия слева (текст кнопки сдвинут вправо в .lic)
    void DrawButtonIcon(Graphics g, string name, int cx, int cy, int w, int h, float z)
    {
        float u = z / 3f; // масштаб под зум
        string look = UnitLook(name);
        if (look != "")
        {
            int ix = cx, iy = cy - (int)(6 * u);
            var team = Color.FromArgb(90, 130, 200);
            g.FillEllipse(B(Dark(team, 0.6f)), ix - (int)(8 * u), iy + (int)(2 * u), (int)(16 * u), (int)(8 * u)); // плечи
            g.FillEllipse(B(team), ix - (int)(7 * u), iy + (int)(1 * u), (int)(14 * u), (int)(7 * u));
            g.FillEllipse(B(Color.FromArgb(232, 185, 138)), ix - (int)(4 * u), iy - (int)(5 * u), (int)(8 * u), (int)(8 * u)); // лицо
            var steelD = Color.FromArgb(88, 93, 108);
            var steelL = Color.FromArgb(205, 210, 222);
            switch (look)
            {
                case "squire": // котёл
                    g.FillEllipse(B(steelD), ix - (int)(4 * u), iy - (int)(7 * u), (int)(8 * u), (int)(6 * u));
                    g.FillRectangle(B(steelD), ix - (int)(7 * u), iy - (int)(3 * u), (int)(14 * u), (int)(2 * u));
                    break;
                case "sword": // шлем + меч
                    g.FillEllipse(B(steelD), ix - (int)(4 * u), iy - (int)(7 * u), (int)(8 * u), (int)(6 * u));
                    g.DrawLine(new Pen(steelL, 2), ix + (int)(5 * u), iy + (int)(3 * u), ix + (int)(8 * u), iy - (int)(8 * u));
                    break;
                case "pala": // крылатый шлем
                    g.FillEllipse(B(steelD), ix - (int)(4 * u), iy - (int)(7 * u), (int)(8 * u), (int)(6 * u));
                    g.FillPolygon(B(Color.White), new[] { new PointF(ix - 4 * u, iy - 5 * u), new PointF(ix - 9 * u, iy - 10 * u), new PointF(ix - 7 * u, iy - 3 * u) });
                    g.FillPolygon(B(Color.White), new[] { new PointF(ix + 4 * u, iy - 5 * u), new PointF(ix + 9 * u, iy - 10 * u), new PointF(ix + 7 * u, iy - 3 * u) });
                    break;
                case "xbow": // капюшон + арбалет
                    g.FillEllipse(B(Color.FromArgb(96, 70, 48)), ix - (int)(5 * u), iy - (int)(7 * u), (int)(10 * u), (int)(8 * u));
                    g.DrawLine(new Pen(Color.FromArgb(130, 90, 55), 2), ix - (int)(6 * u), iy + (int)(2 * u), ix + (int)(6 * u), iy - (int)(2 * u));
                    g.DrawLine(new Pen(steelL, 2), ix + (int)(3 * u), iy - (int)(5 * u), ix + (int)(3 * u), iy + (int)(1 * u));
                    break;
                case "spear": // остроконечный шлем + копьё
                    g.FillPolygon(B(steelD), new[] { new PointF(ix - 4 * u, iy - 2 * u), new PointF(ix + 4 * u, iy - 2 * u), new PointF(ix, iy - 9 * u) });
                    g.DrawLine(new Pen(Color.FromArgb(130, 90, 55), 2), ix + (int)(6 * u), iy + (int)(4 * u), ix + (int)(6 * u), iy - (int)(9 * u));
                    break;
                case "monk": // капюшон + крест
                    g.FillEllipse(B(Color.FromArgb(190, 170, 135)), ix - (int)(5 * u), iy - (int)(7 * u), (int)(10 * u), (int)(8 * u));
                    g.FillRectangle(B(Color.FromArgb(150, 40, 40)), ix - 1, iy - (int)(2 * u), 2, (int)(6 * u));
                    g.FillRectangle(B(Color.FromArgb(150, 40, 40)), ix - 3, iy, (int)(6 * u), 2);
                    break;
                case "cata": // колесо + рычаг
                    g.FillEllipse(B(Color.FromArgb(78, 56, 34)), ix - (int)(4 * u), iy, (int)(8 * u), (int)(8 * u));
                    g.DrawLine(new Pen(Color.FromArgb(110, 80, 50), 3), ix - (int)(6 * u), iy + (int)(4 * u), ix + (int)(6 * u), iy - (int)(7 * u));
                    break;
                case "sapper": // каска + лампа
                    g.FillEllipse(B(Color.FromArgb(96, 64, 40)), ix - (int)(4 * u), iy - (int)(7 * u), (int)(8 * u), (int)(6 * u));
                    g.FillEllipse(B(Color.FromArgb(255, 230, 150)), ix + (int)(1 * u), iy - (int)(5 * u), (int)(3 * u), (int)(3 * u));
                    break;
            }
            return;
        }
        // глифы действий (слева от текста)
        int gx = cx - w / 2 + (int)(9 * u), gy = cy;
        var wh = Color.FromArgb(240, 240, 245);
        switch (name)
        {
            case "btn_fire": // пламя
                g.FillPolygon(B(Color.FromArgb(226, 88, 34)), new[] { new PointF(gx - 4 * u, gy + 5 * u), new PointF(gx + 4 * u, gy + 5 * u), new PointF(gx, gy - 7 * u) });
                g.FillPolygon(B(Color.FromArgb(255, 217, 122)), new[] { new PointF(gx - 2 * u, gy + 5 * u), new PointF(gx + 2 * u, gy + 5 * u), new PointF(gx, gy - 2 * u) });
                break;
            case "btn_heal": // крест
                g.FillRectangle(B(Color.FromArgb(64, 200, 100)), gx - 2, gy - (int)(6 * u), 4, (int)(12 * u));
                g.FillRectangle(B(Color.FromArgb(64, 200, 100)), gx - (int)(6 * u), gy - 2, (int)(12 * u), 4);
                break;
            case "btn_inc": // монета $
                g.FillEllipse(B(Color.FromArgb(230, 190, 60)), gx - (int)(6 * u), gy - (int)(6 * u), (int)(12 * u), (int)(12 * u));
                g.DrawString("$", _f8, B(Color.FromArgb(90, 60, 10)), gx - 4, gy - 6);
                break;
            case "btn_wall": // кирпичи
                g.FillRectangle(B(Color.FromArgb(150, 150, 160)), gx - (int)(6 * u), gy - 1, (int)(12 * u), 5);
                g.FillRectangle(B(Color.FromArgb(120, 120, 130)), gx - (int)(6 * u), gy - (int)(6 * u), (int)(5 * u), 5);
                g.FillRectangle(B(Color.FromArgb(120, 120, 130)), gx + (int)(1 * u), gy - (int)(6 * u), (int)(5 * u), 5);
                break;
            case "btn_pause": // пауза
                g.FillRectangle(B(wh), gx - (int)(4 * u), gy - (int)(5 * u), 3, (int)(10 * u));
                g.FillRectangle(B(wh), gx + (int)(1 * u), gy - (int)(5 * u), 3, (int)(10 * u));
                break;
            case "btn_fast": // >>
                g.FillPolygon(B(wh), new[] { new PointF(gx - 5 * u, gy - 5 * u), new PointF(gx - 5 * u, gy + 5 * u), new PointF(gx, gy) });
                g.FillPolygon(B(wh), new[] { new PointF(gx, gy - 5 * u), new PointF(gx, gy + 5 * u), new PointF(gx + 5 * u, gy) });
                break;
            case "btn_menu": // флажок
                g.FillRectangle(B(Color.FromArgb(74, 55, 40)), gx - (int)(4 * u), gy - (int)(6 * u), 2, (int)(12 * u));
                g.FillPolygon(B(wh), new[] { new PointF(gx - 2 * u, gy - 6 * u), new PointF(gx + 6 * u, gy - 4 * u), new PointF(gx - 2 * u, gy - 2 * u) });
                break;
            case "btn_volley": // три стрелы вверх
                for (int k = -1; k <= 1; k++)
                {
                    g.DrawLine(new Pen(wh, 2), gx + k * 4 * u, gy + 5 * u, gx + k * 4 * u, gy - 4 * u);
                    g.FillPolygon(B(wh), new[] { new PointF(gx + k * 4 * u - 2 * u, gy - 4 * u), new PointF(gx + k * 4 * u + 2 * u, gy - 4 * u), new PointF(gx + k * 4 * u, gy - 7 * u) });
                }
                break;
            case "m_play": // треугольник
                g.FillPolygon(B(wh), new[] { new PointF(gx - 4 * u, gy - 6 * u), new PointF(gx - 4 * u, gy + 6 * u), new PointF(gx + 6 * u, gy) });
                break;
            case "m_levels": // сетка
                g.FillRectangle(B(wh), gx - 5, gy - 5, 4, 4);
                g.FillRectangle(B(wh), gx + 1, gy - 5, 4, 4);
                g.FillRectangle(B(wh), gx - 5, gy + 1, 4, 4);
                g.FillRectangle(B(wh), gx + 1, gy + 1, 4, 4);
                break;
            case "m_help": // ?
                g.DrawString("?", _f8, B(wh), gx - 4, gy - 6);
                break;
            case "m_random": // кубик
                g.FillRectangle(B(wh), gx - (int)(5 * u), gy - (int)(5 * u), (int)(10 * u), (int)(10 * u));
                g.FillRectangle(B(Color.FromArgb(62, 88, 158)), gx - (int)(3 * u), gy - (int)(3 * u), (int)(2 * u), (int)(2 * u));
                g.FillRectangle(B(Color.FromArgb(62, 88, 158)), gx + (int)(1 * u), gy - (int)(1 * u), (int)(2 * u), (int)(2 * u));
                g.FillRectangle(B(Color.FromArgb(62, 88, 158)), gx - (int)(1 * u), gy + (int)(1 * u), (int)(2 * u), (int)(2 * u));
                break;
            case "m_diff": // звезда
                g.FillPolygon(B(Color.FromArgb(255, 211, 77)), new[] { new PointF(gx, gy - 7 * u), new PointF(gx + 2 * u, gy - 2 * u), new PointF(gx + 7 * u, gy - 2 * u), new PointF(gx + 3 * u, gy + 1 * u), new PointF(gx + 4 * u, gy + 6 * u), new PointF(gx, gy + 3 * u), new PointF(gx - 4 * u, gy + 6 * u), new PointF(gx - 3 * u, gy + 1 * u), new PointF(gx - 7 * u, gy - 2 * u), new PointF(gx - 2 * u, gy - 2 * u) });
                break;
            case "m_wipe": // крест
                using (var xp = new Pen(Color.FromArgb(255, 120, 120), 3))
                {
                    g.DrawLine(xp, gx - 5, gy - 5, gx + 5, gy + 5);
                    g.DrawLine(xp, gx + 5, gy - 5, gx - 5, gy + 5);
                }
                break;
            case "lv_back": // стрелка назад
                g.FillPolygon(B(wh), new[] { new PointF(gx - 6 * u, gy), new PointF(gx + 2 * u, gy - 5 * u), new PointF(gx + 2 * u, gy - 2 * u), new PointF(gx + 6 * u, gy - 2 * u), new PointF(gx + 6 * u, gy + 2 * u), new PointF(gx + 2 * u, gy + 2 * u), new PointF(gx + 2 * u, gy + 5 * u) });
                break;
        }
    }

    static System.Drawing.Drawing2D.GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        r = Math.Max(1, Math.Min(r, Math.Min(w, h) / 2));
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Color Light(Color c, float k) => Color.FromArgb(
        Math.Max(0, Math.Min(255, (int)(c.R * k))),
        Math.Max(0, Math.Min(255, (int)(c.G * k))),
        Math.Max(0, Math.Min(255, (int)(c.B * k))));

    // полигон: кольца разделены NaN. Заливка цветом владельца + тёмный контур.
    void DrawPoly(Graphics g, RuntimeObj o, Color c)
    {
        var pts = o.def.pts;
        if (pts == null || pts.Count < 6) return;
        var ring = new List<PointF>();
        void Flush()
        {
            if (ring.Count >= 3)
            {
                g.FillPolygon(B(c), ring.ToArray());
                using var p = new Pen(Color.FromArgb(60, 0, 0, 0), 1);
                g.DrawPolygon(p, ring.ToArray());
            }
            ring.Clear();
        }
        for (int i = 0; i + 1 < pts.Count; i += 2)
        {
            if (float.IsNaN(pts[i])) { Flush(); continue; }
            ring.Add(new PointF(SX(pts[i]), SY(pts[i + 1])));
        }
        Flush();
    }

    static bool InRing(List<float> pts, int from, int to, float x, float y)
    {
        bool inside = false;
        int n = (to - from) / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = pts[from + i * 2], yi = pts[from + i * 2 + 1];
            float xj = pts[from + j * 2], yj = pts[from + j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    public static bool PolyHit(List<float> pts, float x, float y)
    {
        if (pts == null) return false;
        int from = 0;
        for (int i = 0; i <= pts.Count; i += 2)
        {
            if (i >= pts.Count || float.IsNaN(pts[i]))
            {
                if (InRing(pts, from, i, x, y)) return true;
                from = i + 2;
            }
        }
        return false;
    }

    // верхнее clickable-объект под точкой (для стратегий): poly/rect/circle/sprite
    public string PickAt(float wx, float wy)
    {
        for (int i = objs.Count - 1; i >= 0; i--)
        {
            var o = objs[i];
            if (o.dead) continue;
            switch (o.def.kind)
            {
                case "poly":
                    if (PolyHit(o.def.pts, wx, wy)) return o.def.name;
                    break;
                case "circle":
                    if ((wx - o.x) * (wx - o.x) / (o.w * o.w) + (wy - o.y) * (wy - o.y) / (o.h * o.h) < 0.25f)
                        return o.def.name;
                    break;
                case "text":
                    break;
                default:
                    if (Math.Abs(wx - o.x) < o.w / 2 && Math.Abs(wy - o.y) < o.h / 2)
                        return o.def.name;
                    break;
            }
        }
        return "";
    }

    public void Render(Graphics g, bool gizmos)
    {
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        if (_f8 == null) _f8 = new Font(FontFamily.GenericMonospace, 8);
        var sc = Scene;
        Color c1 = string.IsNullOrEmpty(sc.bg1) ? Color.FromArgb(24, 20, 36) : ParseColor(sc.bg1);
        Color c2 = string.IsNullOrEmpty(sc.bg2) ? Color.FromArgb(52, 44, 72) : ParseColor(sc.bg2);
        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, W, H), c1, c2, LinearGradientMode.Vertical))
            g.FillRectangle(sky, 0, 0, W, H);
        if (sc.deco)
        {
            // облака дрейфуют
            for (int ci = 0; ci < 3; ci++)
            {
                float cwx = camX * 0.3f + ((time * (3 + ci * 2) + ci * 190) % 320) - 160;
                int ccx = SX(cwx), ccy = SY(-62 + ci * 12);
                var cc = Color.FromArgb(38, 255, 255, 255);
                g.FillEllipse(B(cc), ccx - 24, ccy - 5, 48, 12);
                g.FillEllipse(B(cc), ccx - 10, ccy - 10, 28, 12);
            }
            // травинки и камешки вдоль земли (детерминировано по индексу)
            int gi0 = (int)Math.Floor((camX - 110) / 9);
            for (int gi = gi0; gi * 9 < camX + 110; gi++)
            {
                int hsh = (int)(((long)gi * 2654435761L & 0x7fffffffL) % 100);
                int gwx = SX(gi * 9 + hsh % 5);
                int ggy = SY(sc.groundY);
                if (ggy < 0 || ggy > H) continue;
                if (hsh < 55) RS2(g, gi * 9 + hsh % 5, sc.groundY - 1.2f, 1, 2.4f, Color.FromArgb(70, 120, 60));
                else if (hsh < 75) RS2(g, gi * 9 + hsh % 5, sc.groundY - 0.8f, 1.6f, 1.2f, Color.FromArgb(120, 115, 100));
                else RS2(g, gi * 9 + hsh % 5, sc.groundY - 1.8f, 2.2f, 1.2f, Color.FromArgb(230, 200, 120));
            }
        }
        // сетка
        for (int gx = (int)(camX / 20) * 20 - 200; gx < camX + 200; gx += 20)
            g.FillRectangle(B(Color.FromArgb(40, 255, 255, 255)), SX(gx), 0, 1, H);
        int gy = SY(sc.groundY);
        if (gy < H - 1) // земля ниже экрана (карты мира) — не рисуем
        {
            g.FillRectangle(B(Color.FromArgb(90, 122, 64)), 0, Math.Max(0, gy), W, H - Math.Max(0, gy));
            if (gy >= 0) g.FillRectangle(B(Color.FromArgb(150, 195, 115)), 0, gy, W, 3);
        }
        foreach (var o in objs)
        {
            if (o.dead)
            {
                // павший рыцарь лежит плашмя на земле в луже; остальные исчезают сразу
                if (o.def.kind != "knight") continue;
                var dc = ParseColor(o.def.color);
                float ground = o.y + o.h / 2; // низ тела = земля (у юнитов gravity=false, y=-h/2)
                float gyc = ground - 1.2f; // точка вращения почти у земли — туша не висит
                var dst = g.Save();
                g.TranslateTransform(SX(o.x), SY(gyc));
                g.RotateTransform(90 * o.face); // ровно 90° — плашмя
                g.TranslateTransform(-SX(o.x), -SY(gyc));
                DrawKnight(g, o, dc);
                g.Restore(dst);
                var pst = g.Save();
                g.TranslateTransform(SX(o.x), SY(o.y + o.h / 2));
                int pool = Math.Min(30, 10 + (int)(o.deadT * 20));
                g.FillEllipse(B(Color.FromArgb(127, 29, 29)), -pool / 2, -2, pool, 5);
                g.Restore(pst);
                continue;
            }
            int sx = SX(o.x), sy = SY(o.y);
            int sw = Math.Max(2, (int)(o.w * ZOOM)), sh = Math.Max(2, (int)(o.h * ZOOM));
            var c = ParseColor(o.def.color);
            switch (o.def.kind)
            {
                case "circle":
                    g.FillEllipse(B(c), sx - sw / 2, sy - sh / 2, sw, sh);
                    break;
                case "knight":
                    DrawKnight(g, o, c);
                    break;
                case "castle":
                    DrawCastle(g, o, c);
                    break;
                case "camp":
                    DrawCamp(g, o, c);
                    break;
                case "tower":
                    DrawTower(g, o);
                    break;
                case "button":
                    DrawButton(g, o, c);
                    break;
                case "poly":
                    DrawPoly(g, o, c);
                    break;
                case "text":
                    {
                        var tsize = g.MeasureString(o.def.text, _f8);
                        float txp = sx - tsize.Width / 2, typ = sy - 6;
                        g.DrawString(o.def.text, _f8, B(Color.FromArgb(160, 0, 0, 0)), txp + 1, typ + 1); // тень для читаемости
                        g.DrawString(o.def.text, _f8, B(c), txp, typ);
                    }
                    break;
                case "sprite":
                    if (_sprites.TryGetValue(o.def.sprite, out var bmp))
                        g.DrawImage(bmp, sx - sw / 2, sy - sh / 2, sw, sh);
                    else g.FillRectangle(B(c), sx - sw / 2, sy - sh / 2, sw, sh);
                    break;
                default:
                    g.FillRectangle(B(Color.FromArgb(0, 0, 0, 90)), sx - sw / 2, sy - sh / 2, sw, sh);
                    g.FillRectangle(B(c), sx - sw / 2, sy - sh / 2, sw - 2, sh - 2);
                    break;
            }
            if (o.error != "")
                g.DrawString("!", _f8, Brushes.Red, sx - 3, sy - sh / 2 - 12);
        }
        if (gizmos)
        {
            foreach (var o in objs)
            {
                if (o.dead) continue;
                int sx = SX(o.x), sy = SY(o.y);
                int sw = Math.Max(2, (int)(o.w * ZOOM)), sh = Math.Max(2, (int)(o.h * ZOOM));
                using var p = new Pen(Color.Cyan, 1) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(p, sx - sw / 2 - 2, sy - sh / 2 - 2, sw + 4, sh + 4);
            }
            g.DrawString(proj.name + " / " + sc.name, _f8, Brushes.Yellow, 6, 6);
        }
    }
}
