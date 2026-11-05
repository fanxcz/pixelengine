using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PixelFight;

// Формат проекта движка (.lic — один JSON-файл: сцены, объекты, скрипты, ассеты).
// Ассеты (PNG) хранятся прямо внутри base64 — проект переносим одним файлом.
// Импорт: File -> Open .lic. Своя игра собирается из объектов + скриптов C#/Lua.

public class LicObject
{
    public string name { get; set; } = "Obj";
    public string kind { get; set; } = "rect"; // rect | circle | sprite | text
    public float x { get; set; } = 0;   // центр
    public float y { get; set; } = -40; // центр (вверх — минус, земля y=0)
    public float w { get; set; } = 12;
    public float h { get; set; } = 20;
    public string color { get; set; } = "#3E589E";
    public string text { get; set; } = "";
    public string sprite { get; set; } = ""; // имя ассета
    public bool solid { get; set; } = true;
    public bool gravity { get; set; } = true;
    public bool follow { get; set; } = false; // камера едет за ним
    public string script { get; set; } = "";  // имя скрипта
    public List<float> pts { get; set; } // полигон (kind=poly): плоские x,y мировых координат
    public LicObject Clone() => (LicObject)MemberwiseClone();
}

public class LicScene
{
    public string name { get; set; } = "Level1";
    public float gravity { get; set; } = 900;
    public float groundY { get; set; } = 0;
    public bool lockCam { get; set; } = false; // фиксированная камера (карты, стратегии)
    public bool freeCam { get; set; } = false; // зум колесом + панорама ПКМ (карты мира)
    public bool deco { get; set; } = false; // травинки, камешки и облака поверх фона
    public float camX0 { get; set; } = 0;
    public float camY0 { get; set; } = 0;
    public string bg1 { get; set; } = ""; // градиент фона (пусто = дефолт)
    public string bg2 { get; set; } = "";
    public List<LicObject> objects { get; set; } = new List<LicObject>();
}

public class LicScript
{
    public string name { get; set; } = "hero.lua";
    public string lang { get; set; } = "lua"; // lua | csharp
    public string code { get; set; } = "";
}

public class LicAsset
{
    public string name { get; set; } = "hero.png";
    public string base64 { get; set; } = ""; // байты файла
    public string type { get; set; } = "png"; // png | wav
}

public class LicProject
{
    public string app { get; set; } = "PixelEngine";
    public int version { get; set; } = 1;
    public string name { get; set; } = "MyGame";
    public int startScene { get; set; } = 0;
    public List<LicScene> scenes { get; set; } = new List<LicScene>();
    public List<LicScript> scripts { get; set; } = new List<LicScript>();
    public List<LicAsset> assets { get; set; } = new List<LicAsset>();

    static readonly JsonSerializerOptions _js = new JsonSerializerOptions
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, _js));

    public static LicProject Load(string path) =>
        JsonSerializer.Deserialize<LicProject>(File.ReadAllText(path), _js) ?? new LicProject();

    public LicScript FindScript(string name)
    {
        foreach (var s in scripts)
            if (s.name == name) return s;
        return null;
    }

    public static LicProject NewSample()
    {
        var p = new LicProject { name = "DemoGame" };
        var sc = new LicScene { name = "Level1" };
        sc.objects.Add(new LicObject { name = "hero", kind = "rect", x = -40, y = -60, w = 12, h = 22, color = "#3E589E", follow = true, script = "hero.lua" });
        sc.objects.Add(new LicObject { name = "enemy", kind = "rect", x = 60, y = -60, w = 12, h = 22, color = "#9E3A3A", script = "enemy.cs" });
        sc.objects.Add(new LicObject { name = "ground", kind = "rect", x = 0, y = 10, w = 400, h = 20, color = "#5A7A40", gravity = false });
        sc.objects.Add(new LicObject { name = "plat", kind = "rect", x = 40, y = -60, w = 60, h = 8, color = "#8A6A3A", gravity = false });
        sc.objects.Add(new LicObject { name = "title", kind = "text", x = 0, y = -140, w = 12, h = 12, color = "#FFFFFF", text = "A/D + W — иди и прыгай, F — выстрел", gravity = false, solid = false });
        p.scenes.Add(sc);
        p.scripts.Add(new LicScript
        {
            name = "hero.lua",
            lang = "lua",
            code = "-- hero.lua: стрелки или A/D — ход, W/вверх/пробел — прыжок, F — выстрел\n"
                 + "local cool = 0\n"
                 + "function update(dt)\n"
                 + "  local L = api.key('left') or api.key('a')\n"
                 + "  local R = api.key('right') or api.key('d')\n"
                 + "  if L then self.vx = -140 end\n"
                 + "  if R then self.vx = 140 end\n"
                 + "  if not L and not R then self.vx = 0 end\n"
                 + "  if (api.key('up') or api.key('w') or api.key('space')) and self.ground then self.vy = -380 end\n"
                 + "  cool = cool - dt\n"
                 + "  if api.key('f') and cool <= 0 then\n"
                 + "    cool = 0.35\n"
                 + "    local b = api.spawn('rect', { name='b', x=self.x, y=self.y - 10, w=5, h=5, color='#FFD34D', gravity=false, solid=false })\n"
                 + "    b.vx = 260\n"
                 + "    api.log('pew')\n"
                 + "  end\n"
                 + "end\n",
        });
        p.scripts.Add(new LicScript
        {
            name = "enemy.cs",
            lang = "csharp",
            code = "// enemy.cs: патруль, а если герой рядом — идёт к нему\n"
                 + "var p = api.find(\"hero\");\n"
                 + "float dir = self.vx >= 0 ? 1 : -1;\n"
                 + "if (p != null && System.Math.Abs(p.x - self.x) < 70)\n"
                 + "    dir = System.Math.Sign(p.x - self.x);\n"
                 + "if (self.x > 120) dir = -1;\n"
                 + "if (self.x < -120) dir = 1;\n"
                 + "self.vx = dir * 45;\n",
        });
        return p;
    }
}
