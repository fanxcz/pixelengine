using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using MoonSharp.Interpreter;
using LuaScript = MoonSharp.Interpreter.Script;

namespace PixelFight;

// Живой объект сцены, видимый из скриптов (и Lua, и C#).
// self.x, self.vx, self.ground ... + ячейки памяти Set/Get.
[MoonSharpUserData]
public class DynObj
{
    readonly Runtime _rt;
    readonly RuntimeObj _o;
    readonly Dictionary<string, float> _mem = new Dictionary<string, float>();
    public DynObj(Runtime rt, RuntimeObj o) { _rt = rt; _o = o; }
    public string name { get => _o.def.name; set => _o.def.name = value; }
    public float x { get => _o.x; set => _o.x = value; }
    public float y { get => _o.y; set => _o.y = value; }
    public float vx { get => _o.vx; set => _o.vx = value; }
    public float vy { get => _o.vy; set => _o.vy = value; }
    public float w { get => _o.w; set => _o.w = Math.Max(1, value); }
    public float h { get => _o.h; set => _o.h = Math.Max(1, value); }
    public bool ground => _o.onGround;
    public bool dead => _o.dead;
    public float num { get => _o.num; set => _o.num = value; }
    public float maxnum { get => _o.maxnum; set => _o.maxnum = value; }
    public float attack { get => _o.attackT; set => _o.attackT = value; } // замах марионетки
    public void hit() { _o.hitT = 0.18f; } // вспышка получения урона
    public float face => _o.face;
    public string color { get => _o.def.color; set => _o.def.color = value; }
    public string text { get => _o.def.text; set => _o.def.text = value; }
    public float Get(string k) => _mem.TryGetValue(k, out var v) ? v : 0;
    public void Set(string k, float v) => _mem[k] = v;
    public void destroy() => _rt.Destroy(_o);
}

// API движка для скриптов: api.log, api.key, api.spawn, api.find ...
[MoonSharpUserData]
public class EngineApi
{
    readonly Runtime _rt;
    public EngineApi(Runtime rt) { _rt = rt; }
    public void log(string s) => _rt.Log(s);
    public bool key(string name) => _rt.IsKey(name);
    public float mousex() => _rt.mouseWX;
    public float mousey() => _rt.mouseWY;
    public bool click() => _rt.mouseClicked;
    public string pick() => _rt.PickAt(_rt.mouseWX, _rt.mouseWY);
    public void cam(float x, float y, float z)
    {
        _rt.camX = x; _rt.camY = y;
        _rt.zoom = Math.Max(0.6f, Math.Min(4f, z));
    }
    public float time() => _rt.time;
    public float rand(float a, float b) => (float)(_rt.rnd.NextDouble() * (b - a) + a);
    public DynObj find(string name) => _rt.FindProxy(name);
    // Lua: api.spawn('rect', {name='b', x=.., y=.., w=.., h=.., color='#..', gravity=false, solid=false})
    public DynObj spawn(string kind, DynValue opts) => _rt.SpawnFromLua(kind, opts);
    // C#: api.Spawn("rect", "b", x, y)
    public DynObj Spawn(string kind, string name, float x, float y) => _rt.SpawnSimple(kind, name, x, y);
    public void destroy(string name) => _rt.DestroyByName(name);
    public void purge() => _rt.PurgeDead(); // мгновенно убрать трупы/частицы (выход в меню)
    // звук из ассетов .lic (wav): api.play('hit.wav')
    public void play(string name) => _rt.PlayAsset(name);
    // хранилище: api.save('level','12'), api.load('level')
    public void save(string k, string v) => Store.Set(k, v);
    public string load(string k) => Store.Get(k);
    // фон: api.bg('#111', '#222')
    public void bg(string c1, string c2) => _rt.SetBG(c1, c2);
}

// Глобалы C#-скрипта: код выполняется КАЖДЫЙ кадр (self, api, dt, time доступны сразу).
public class ScriptGlobals
{
    public DynObj self;
    public EngineApi api;
    public float dt;
    public float time;
}

public interface IScriptRunner
{
    string Check(); // "" = ок, иначе текст ошибок (кнопка Check, без запуска)
    void Start(DynObj self, EngineApi api); // сброс + init
    void Frame(float dt, float time);       // один кадр
}

public static class ScriptFactory
{
    public static IScriptRunner Create(LicScript s) =>
        s.lang == "csharp" ? (IScriptRunner)new CSharpRunner(s.code) : new LuaRunner(s.code);
    // проверка синтаксиса без запуска (кнопка Check в редакторе)
    public static string CheckCode(string lang, string code)
    {
        try { return Create(new LicScript { lang = lang, code = code }).Check(); }
        catch (Exception ex) { return ex.Message; }
    }
}

// ---- Lua (MoonSharp, настоящий Lua 5.2): start() один раз, update(dt) каждый кадр ----
class LuaRunner : IScriptRunner
{
    readonly string _code;
    LuaScript _s;
    Closure _update;
    static bool _reg;
    public LuaRunner(string code) { _code = code; }
    static void EnsureReg()
    {
        if (_reg) return;
        UserData.RegisterType<DynObj>();
        UserData.RegisterType<EngineApi>();
        _reg = true;
    }
    public string Check()
    {
        try { EnsureReg(); new LuaScript().DoString(_code); return ""; }
        catch (Exception ex) { return ex.Message; }
    }
    public void Start(DynObj self, EngineApi api)
    {
        EnsureReg();
        _s = new LuaScript();
        _s.Globals["self"] = self;
        _s.Globals["api"] = api;
        _s.DoString(_code);
        var st = _s.Globals.Get("start");
        if (st.Type == DataType.Function) st.Function.Call();
        var up = _s.Globals.Get("update");
        _update = up.Type == DataType.Function ? up.Function : null;
    }
    public void Frame(float dt, float time)
    {
        if (_update == null) return;
        _s.Globals["dt"] = dt;
        _s.Globals["time"] = time;
        _update.Call(dt);
    }
}

// ---- C# (Roslyn Scripting): тело скрипта = кадр (dt, time, self, api) ----
class CSharpRunner : IScriptRunner
{
    readonly string _code;
    Microsoft.CodeAnalysis.Scripting.Script<object> _script;
    string _err = "";
    DynObj _self;
    EngineApi _api;
    public CSharpRunner(string code) { _code = code; }
    public string Check() { Compile(); return _err; }
    void Compile()
    {
        if (_script != null) return;
        try
        {
            var opts = Microsoft.CodeAnalysis.Scripting.ScriptOptions.Default
                .WithReferences(typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(ScriptGlobals).Assembly)
                .WithImports("System", "PixelFight");
            _script = CSharpScript.Create<object>(_code, opts, typeof(ScriptGlobals));
            var diags = _script.Compile();
            var errs = new List<string>();
            foreach (var d in diags)
                if (d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    errs.Add(d.ToString());
            _err = string.Join("\n", errs);
        }
        catch (Exception ex) { _err = ex.Message; }
    }
    public void Start(DynObj self, EngineApi api) { Compile(); _self = self; _api = api; }
    public void Frame(float dt, float time)
    {
        if (_err != "" || _script == null) return;
        var g = new ScriptGlobals { self = _self, api = _api, dt = dt, time = time };
        // Task.Run: скрипт без await идёт синхронно; await внутри не повесит UI-поток
        Task.Run(() => _script.RunAsync(g)).GetAwaiter().GetResult();
    }
}
