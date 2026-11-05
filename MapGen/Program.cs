using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PixelFight;

// MapGen: карта с GitHub (Natural Earth GeoJSON) -> игра-стратегия .lic на движке.
// Использование: dotnet run --project MapGen -- ne50.geojson world.lic
// Страна = провинция (как в Age of History 2 — только здесь весь мир сразу).
static class MapGen
{
    const double KX = 72.0 / 180.0; // lon -> x (-72..72)
    const double KY = 0.36;           // lat -> y (слегка ужато, место под UI)
    const double YOFF = 6.0;          // сдвиг карты вниз от кнопок
    static float PX(double lon) => (float)(lon * KX);
    static float PY(double lat) => (float)(-lat * KY + YOFF);

    class Country
    {
        public string name, iso;
        public List<List<float>> rings = new List<List<float>>(); // мировые x,y
        public float cx, cy;      // столица (точка внутри)
        public float minx, miny, maxx, maxy;
        public List<int> nb = new List<int>();
        public List<int> snb = new List<int>(); // морские соседи (десант только с портом)
        public string color;
        public bool cap;
        public int army;
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: MapGen <in.geojson> <out.lic>"); return 1; }
        var list = Load(args[0]);
        Console.WriteLine("стран: " + list.Count);
        SimplifyAll(list);
        Capitals(list);
        Neighbors(list);
        Paint(list);
        SaveLic(list, args[1]);
        return 0;
    }

    static string Prop(JsonElement p, params string[] keys)
    {
        foreach (var k in keys)
            if (p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return "";
    }

    static List<Country> Load(string path)
    {
        var list = new List<Country>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var pr = f.GetProperty("properties");
            string name = Prop(pr, "NAME", "ADMIN", "name");
            string iso = Prop(pr, "ISO_A3", "ADM0_A3", "ISO_A2");
            if (name == "" || name == "Antarctica" || iso == "ATA") continue; // Антарктиду не берём
            var g = f.GetProperty("geometry");
            string type = g.GetProperty("type").GetString();
            var rawRings = new List<List<double[]>>();
            if (type == "Polygon")
            {
                foreach (var ring in g.GetProperty("coordinates").EnumerateArray())
                    rawRings.Add(ReadRing(ring));
            }
            else if (type == "MultiPolygon")
            {
                foreach (var poly in g.GetProperty("coordinates").EnumerateArray())
                    foreach (var ring in poly.EnumerateArray())
                        rawRings.Add(ReadRing(ring));
            }
            else continue;
            var c = new Country { name = name, iso = iso };
            foreach (var ring in rawRings)
            {
                if (ring.Count < 4) continue;
                double mn = 999, mx = -999;
                foreach (var pt in ring) { mn = Math.Min(mn, pt[0]); mx = Math.Max(mx, pt[0]); }
                bool unwrap = mx - mn > 180; // через антимеридиан (Фиджи, НЗ)
                var w = new List<float>();
                foreach (var pt in ring)
                {
                    double lon = pt[0];
                    if (unwrap && lon < 0) lon += 360;
                    w.Add(PX(lon));
                    w.Add(PY(pt[1]));
                }
                c.rings.Add(w);
            }
            if (c.rings.Count > 0) list.Add(c);
        }
        return list;
    }

    static List<double[]> ReadRing(JsonElement ring)
    {
        var pts = new List<double[]>();
        foreach (var pt in ring.EnumerateArray())
            pts.Add(new double[] { pt[0].GetDouble(), pt[1].GetDouble() });
        return pts;
    }

    // Дуглас-Пекер по кольцу
    static List<float> Simp(List<float> r, double tol)
    {
        int n = r.Count / 2;
        if (n <= 8) return new List<float>(r);
        bool closed = Math.Abs(r[0] - r[n * 2 - 2]) < 1e-9 && Math.Abs(r[1] - r[n * 2 - 1]) < 1e-9;
        int m = closed ? n - 1 : n;
        var keep = new bool[m];
        keep[0] = keep[m - 1] = true;
        var st = new Stack<int[]>();
        st.Push(new[] { 0, m - 1 });
        while (st.Count > 0)
        {
            var ab = st.Pop();
            int a = ab[0], b = ab[1];
            double ax = r[a * 2], ay = r[a * 2 + 1], bx = r[b * 2], by = r[b * 2 + 1];
            double dx = bx - ax, dy = by - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double best = 0; int bi = -1;
            for (int i = a + 1; i < b; i++)
            {
                double px = r[i * 2] - ax, py = r[i * 2 + 1] - ay;
                double d = len < 1e-9 ? Math.Sqrt(px * px + py * py) : Math.Abs(dx * py - dy * px) / len;
                if (d > best) { best = d; bi = i; }
            }
            if (best > tol && bi > 0) { keep[bi] = true; st.Push(new[] { a, bi }); st.Push(new[] { bi, b }); }
        }
        var o = new List<float>();
        for (int i = 0; i < m; i++) if (keep[i]) { o.Add(r[i * 2]); o.Add(r[i * 2 + 1]); }
        if (closed) { o.Add(o[0]); o.Add(o[1]); }
        return o;
    }

    static double Area(List<float> r)
    {
        double s = 0;
        int n = r.Count / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
            s += r[j * 2] * r[i * 2 + 1] - r[i * 2] * r[j * 2 + 1];
        return Math.Abs(s) / 2;
    }

    static void SimplifyAll(List<Country> list)
    {
        foreach (var c in list)
        {
            var nr = new List<List<float>>();
            foreach (var r in c.rings)
            {
                var s = Simp(r, 0.22);
                if (Area(s) >= 0.08) nr.Add(s); // мелочь (<0.3°²) топим в океане
            }
            int pts = 0;
            foreach (var r in nr) pts += r.Count / 2;
            if (pts > 500) // жирные — упрощаем сильнее
            {
                nr.Clear();
                foreach (var r in c.rings)
                {
                    var s = Simp(r, 0.55);
                    if (Area(s) >= 0.15) nr.Add(s);
                }
            }
            c.rings = nr;
            c.minx = c.miny = 9999; c.maxx = c.maxy = -9999;
            foreach (var r in nr)
                for (int i = 0; i + 1 < r.Count; i += 2)
                {
                    c.minx = Math.Min(c.minx, r[i]); c.maxx = Math.Max(c.maxx, r[i]);
                    c.miny = Math.Min(c.miny, r[i + 1]); c.maxy = Math.Max(c.maxy, r[i + 1]);
                }
        }
        list.RemoveAll(c => c.rings.Count == 0);
    }

    // столица: точка внутри самого большого кольца, подальше от краёв
    static void Capitals(List<Country> list)
    {
        foreach (var c in list)
        {
            List<float> big = c.rings[0];
            foreach (var r in c.rings) if (Area(r) > Area(big)) big = r;
            float bw = 0;
            float stepx = (c.maxx - c.minx) / 12, stepy = (c.maxy - c.miny) / 12;
            if (stepx < 1e-6 || stepy < 1e-6) { c.cx = (c.minx + c.maxx) / 2; c.cy = (c.miny + c.maxy) / 2; continue; }
            float bx = c.cx = (c.minx + c.maxx) / 2, by = c.cy = (c.miny + c.maxy) / 2;
            for (float gx = c.minx; gx <= c.maxx; gx += stepx)
                for (float gy = c.miny; gy <= c.maxy; gy += stepy)
                {
                    var flat = new List<float>(big);
                    if (!Runtime.PolyHit(flat, gx, gy)) continue;
                    float d = Math.Min(Math.Min(gx - c.minx, c.maxx - gx), Math.Min(gy - c.miny, c.maxy - gy));
                    if (d > bw) { bw = d; bx = gx; by = gy; }
                }
            c.cx = bx; c.cy = by;
        }
    }

    static void Neighbors(List<Country> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            for (int j = i + 1; j < list.Count; j++)
            {
                var b = list[j];
                if (b.minx > a.maxx + 2 || b.maxx < a.minx - 2 || b.miny > a.maxy + 2 || b.maxy < a.miny - 2)
                    continue;
                if (Touch(a, b, 1.4f))
                {
                    a.nb.Add(j + 1); b.nb.Add(i + 1); // индексы p-объектов (1-based)
                }
                else if (Touch(a, b, 9.0f))
                {
                    a.snb.Add(j + 1); b.snb.Add(i + 1); // через море — нужен порт
                }
            }
        }
        int lone = 0;
        foreach (var c in list) if (c.nb.Count == 0) lone++;
        Console.WriteLine("без соседей: " + lone);
    }

    static bool Touch(Country a, Country b, float d)
    {
        float d2 = d * d;
        foreach (var ra in a.rings)
            foreach (var rb in b.rings)
            {
                for (int i = 0; i + 1 < ra.Count; i += 4)
                    for (int j = 0; j + 1 < rb.Count; j += 4)
                    {
                        float dx = ra[i] - rb[j], dy = ra[i + 1] - rb[j + 1];
                        if (dx * dx + dy * dy < d2) return true;
                    }
            }
        return false;
    }

    static readonly string[] PALETTE = {
        "#3E589E", "#9E3A3A", "#3A7A44", "#D8B93A", "#7A4A9E", "#D86A2A", "#4AA8B8", "#B84A7A",
    };
    static readonly HashSet<string> MAJORS = new HashSet<string> {
        "Russia", "Germany", "France", "United Kingdom", "United States of America",
        "China", "Japan", "Turkey", "Spain", "Italy", "Poland", "Ukraine",
        "Brazil", "India", "Egypt", "Iran", "Canada", "Australia", "Mexico", "Indonesia",
    };

    static void Paint(List<Country> list)
    {
        // жадная раскраска карты (соседи — разные цвета)
        int[] col = new int[list.Count];
        for (int i = 0; i < col.Length; i++) col[i] = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var used = new bool[PALETTE.Length];
            foreach (int j in list[i].nb) if (col[j - 1] >= 0) used[col[j - 1]] = true;
            int k = 0;
            while (k < PALETTE.Length && used[k]) k++;
            col[i] = k % PALETTE.Length;
            list[i].color = PALETTE[col[i]];
        }
        foreach (var c in list)
        {
            c.cap = MAJORS.Contains(c.name);
            float area = (c.maxx - c.minx) * (c.maxy - c.miny);
            c.army = c.cap ? 150 : area > 60 ? 80 : area > 8 ? 50 : 30;
        }
    }

    static string Esc(string s) => s.Replace("'", "’");

    static void SaveLic(List<Country> list, string path)
    {
        var p = new LicProject { name = "WorldAges" };
        var sc = new LicScene { name = "Мир", gravity = 0, groundY = 1000, lockCam = true, camX0 = 0, camY0 = -4, freeCam = true, bg1 = "#274B73", bg2 = "#101E33" };
        sc.objects.Add(new LicObject { name = "gm", kind = "text", x = 0, y = -60, text = "", gravity = false, solid = false, script = "world.lua" });
        sc.objects.Add(new LicObject { name = "ui_money", kind = "text", x = -58, y = -45, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_turn", kind = "text", x = -6, y = -45, color = "#FFFFFF", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_sel", kind = "text", x = 48, y = -45, color = "#FFFFFF", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_turn", kind = "rect", x = -59, y = -37, w = 26, h = 10, color = "#2A5A3A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_turn_t", kind = "text", x = -59, y = -37, color = "#FFFFFF", gravity = false, solid = false, text = "ХОД" });
        sc.objects.Add(new LicObject { name = "btn_rec", kind = "rect", x = -31, y = -37, w = 26, h = 10, color = "#3E589E", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_rec_t", kind = "text", x = -31, y = -37, color = "#FFFFFF", gravity = false, solid = false, text = "НАБОР" });
        sc.objects.Add(new LicObject { name = "btn_bld", kind = "rect", x = -3, y = -37, w = 26, h = 10, color = "#8A6A3A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_bld_t", kind = "text", x = -3, y = -37, color = "#FFFFFF", gravity = false, solid = false, text = "СТРОЙ" });
        sc.objects.Add(new LicObject { name = "btn_pea", kind = "rect", x = 25, y = -37, w = 26, h = 10, color = "#6A6A7A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_pea_t", kind = "text", x = 25, y = -37, color = "#FFFFFF", gravity = false, solid = false, text = "МИР" });
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            var flat = new List<float>();
            foreach (var r in c.rings)
            {
                if (flat.Count > 0) { flat.Add(float.NaN); flat.Add(float.NaN); }
                flat.AddRange(r);
            }
            sc.objects.Add(new LicObject
            {
                name = "p" + (i + 1), kind = "poly", x = c.cx, y = c.cy,
                w = Math.Max(2, c.maxx - c.minx), h = Math.Max(2, c.maxy - c.miny),
                color = c.color, gravity = false, pts = flat,
            });
            sc.objects.Add(new LicObject
            {
                name = "t" + (i + 1), kind = "text", x = c.cx, y = c.cy,
                color = "#FFFFFF", gravity = false, solid = false, text = c.cap ? c.name : "",
            });
        }
        sc.objects.Add(new LicObject { name = "ev1", kind = "text", x = 0, y = 28, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ev2", kind = "text", x = 0, y = 32, color = "#CCCCCC", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ev3", kind = "text", x = 0, y = 36, color = "#999999", gravity = false, solid = false });
        p.scenes.Add(sc);
        p.scripts.Add(new LicScript { name = "world.lua", lang = "lua", code = Lua(list) });
        p.Save(path);
        Console.WriteLine("saved " + path + " (" + new FileInfo(path).Length / 1024 + " КБ)");
    }

    static string Lua(List<Country> list)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(@"
-- WorldAges: весь мир с GitHub (Natural Earth). Кликни страну — играй за неё.
-- Своя: клик — выбрать. Чужая рядом — атака (с моря нужен порт). Дальняя — цель дипломатии.
-- ХОД, НАБОР (50), СТРОЙ (здания/порт), МИР/СОЮЗ по выбранной цели.
local P={
");
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            sb.Append(" {n='" + Esc(c.name) + "',x=" + F(c.cx) + ",y=" + F(c.cy) + ",o='" + Esc(c.name) + "',a=" + c.army + ",col='" + c.color + "'" + (c.cap ? ",cap=true" : "") + ",k=0,f=0,r=0,port=false,nb={" + string.Join(",", c.nb) + "},snb={" + string.Join(",", c.snb) + "}},");
            sb.Append("\n");
        }
        sb.Append(@" }
local C={}
for _,p in ipairs(P) do C[p.o]={color=p.col,money=80,ai=true} end
local ME=nil
local started=false
local turn=1
local sel=nil
local wars={}
local over=nil
local ev={}
local pspace=false
local O={}
local T={}
local function abs(x) return x<0 and -x or x end
local function wkey(a,b) return a..'|'..b end
local function atwar(a,b)
  if a==b then return false end
  return wars[wkey(a,b)] or wars[wkey(b,a)] or false
end
local function setwar(a,b)
  if a==b then return end
  wars[wkey(a,b)]=true
end
local function unwar(a)
  local drop={}
  for k,_ in pairs(wars) do
    local s=k:find('|',1,true)
    if k:sub(1,s-1)==a or k:sub(s+1)==a then drop[#drop+1]=k end
  end
  for _,k in ipairs(drop) do wars[k]=nil end
end
local function haswar(a)
  for k,_ in pairs(wars) do
    local s=k:find('|',1,true)
    if k:sub(1,s-1)==a or k:sub(s+1)==a then return true end
  end
  return false
end
local rel={} -- отношения пар: -100..100
local tgt=nil -- страна-цель дипломатии (клик по дальней чужой)
local function relv(a,b)
  if a==b then return 0 end
  return rel[wkey(a,b)] or rel[wkey(b,a)] or 0
end
local function reladd(a,b,d)
  if a==b then return end
  local k=wkey(a,b)
  local v=(rel[k] or 0)+d
  if v>100 then v=100 end
  if v<-100 then v=-100 end
  rel[k]=v
end
local function isally(a,b)
  if a==b then return false end
  local q=(rel[wkey(a,b)] or rel[wkey(b,a)]) or 0
  return q>=60
end
local function provs(c)
  local n=0
  for _,p in ipairs(P) do if p.o==c then n=n+1 end end
  return n
end
local function total(c)
  local n=0
  for _,p in ipairs(P) do if p.o==c then n=n+p.a end end
  return n
end
local function isnb(a,b)
  for _,x in ipairs(P[a].nb) do if x==b then return true end end
  return false
end
local function addev(s)
  table.insert(ev,1,s)
  while #ev>3 do table.remove(ev) end
end
function start()
  for i=1,#P do O[i]=api.find('p'..i) T[i]=api.find('t'..i) end
  addev('Кликни свою страну!')
  api.log('world: '..#P..' стран')
end
function refresh()
  for i,p in ipairs(P) do
    O[i].num=p.a
    if started and p.o==ME and i==sel then O[i].color='#FFFFFF'
    else O[i].color=C[p.o].color end
  end
  if started then
    local allies=0
    for c,_ in pairs(C) do if c~=ME and isally(ME,c) and provs(c)>0 then allies=allies+1 end end
    api.find('ui_money').text='Казна: '..math.floor(C[ME].money)
    api.find('ui_turn').text='Ход '..turn
    if over=='win' then api.find('ui_sel').text='ПОБЕДА! Мир твой!'
    elseif over=='lose' then api.find('ui_sel').text='ПОРАЖЕНИЕ'
    elseif sel then
      local b=' К'..P[sel].k..' Ф'..P[sel].f..' Р'..P[sel].r
      if P[sel].port then b=b..' порт' end
      api.find('ui_sel').text=P[sel].n..' ('..math.floor(P[sel].a)..')'..b
    elseif tgt and P[tgt] then api.find('ui_sel').text=P[tgt].o..' (отн. '..relv(ME,P[tgt].o)..')'
    else api.find('ui_sel').text='выбери провинцию' end
    if tgt and P[tgt] and P[tgt].o~=ME and not over then
      if atwar(ME,P[tgt].o) then api.find('btn_pea_t').text='МИР 40'
      elseif isally(ME,P[tgt].o) then api.find('btn_pea_t').text='СОЮЗ ✓'
      else api.find('btn_pea_t').text='СОЮЗ 60' end
    else api.find('btn_pea_t').text='МИР 40' end
  else
    api.find('ui_money').text=''
    api.find('ui_turn').text='Ход '..turn
    api.find('ui_sel').text='кликни свою страну'
  end
  for i=1,3 do api.find('ev'..i).text=ev[i] or '' end
end
function combat(ai,di,byme)
  local A=P[ai].a
  if A<50 then if byme then api.log('мало войска (нужно 50)') end return end
  local D=P[di].a
  local a=A*api.rand(0.75,1.0)
  local d=D*api.rand(0.75,1.0)+(P[di].cap and 40 or 0)+P[di].f*60
  local atk=P[ai].o
  local dfn=P[di].o
  reladd(atk,dfn,-30)
  if a>d then
    P[di].o=atk
    P[di].a=math.max(15,(A-D)*0.55)
    P[ai].a=A*0.35
    setwar(atk,dfn)
    addev(P[di].n..' взят!')
    api.log('take '..P[di].n)
  else
    P[di].a=math.max(8,D-A*0.55)
    P[ai].a=A*0.4
    setwar(atk,dfn)
    addev('Атака отбита')
    api.log('defend '..P[di].n)
  end
  checkend()
end
function checkend()
  if not started then return end
  local my=provs(ME)
  if my==0 and not over then over='lose' addev('ПОРАЖЕНИЕ') api.log('lose') end
  if my==#P and not over then over='win' addev('ПОБЕДА!') api.log('win') end
end
function dorecruit()
  if not sel or P[sel].o~=ME then api.log('выбери свою страну') return end
  if C[ME].money<50 then api.log('нет денег') return end
  C[ME].money=C[ME].money-50
  P[sel].a=math.min(400,P[sel].a+100)
  api.log('набор +100')
end
function dopeace()
  local t=(tgt and P[tgt]) and P[tgt].o or nil
  if not t or t==ME then api.log('кликни чужую страну') return end
  if atwar(ME,t) then
    if C[ME].money<40 then api.log('нет денег на мир') return end
    C[ME].money=C[ME].money-40
    unwar(ME)
    reladd(ME,t,20)
    addev('Мир с '..t..'!')
    api.log('peace')
  elseif isally(ME,t) then api.log('уже союзники')
  else
    if C[ME].money<60 then api.log('союз стоит 60') return end
    C[ME].money=C[ME].money-60
    unwar(ME)
    rel[wkey(ME,t)]=70
    addev('Союз с '..t..'!')
    api.log('ally '..t)
  end
end
function dobuild()
  if not sel or P[sel].o~=ME then api.log('выбери свою провинцию') return end
  local p=P[sel]
  local function pay(c) if C[ME].money<c then api.log('нужно '..c) return false end C[ME].money=C[ME].money-c return true end
  if p.k<1 then if pay(60) then p.k=1 api.log('казарма построена') end
  elseif p.f<1 then if pay(60) then p.f=1 api.log('форт построен') end
  elseif p.r<1 then if pay(80) then p.r=1 api.log('рынок построен') end
  elseif not p.port then if pay(80) then p.port=true api.log('порт построен: десант!') end
  else api.log('всё построено') end
end
function ai_country(c)
  local cc=C[c]
  local n=provs(c)
  if n==0 then return end
  cc.money=cc.money+2*n
  if c==ME then
    local al=0
    for cc2,_ in pairs(C) do if cc2~=ME and isally(ME,cc2) and provs(cc2)>0 then al=al+1 end end
    cc.money=cc.money+al*2
  end
  for _,p in ipairs(P) do
    if p.o==c then
      cc.money=cc.money+p.r*4
      if p.cap then cc.money=cc.money+8 end
      p.a=math.min(300,p.a*(1.06+0.25*p.k)+3)
    end
  end
  if c~=ME and isally(c,ME) then
    if total(c)>total(ME)*2 and api.rand(0,1)<0.05 then
      rel[wkey(c,ME)]=0 addev(c..' разрывает союз!') return
    end
  end
  if c~=ME and atwar(c,ME) and total(c)<total(ME)*0.45 then
    unwar(c) addev(c..' просит мира!') return
  end
  if cc.money>=60 then
    local bi=0
    for i,p in ipairs(P) do
      if p.o==c and p.a<350 and (bi==0 or p.a>P[bi].a) then bi=i end
    end
    if bi>0 then cc.money=cc.money-60 P[bi].a=P[bi].a+120 end
  end
  if cc.money>=120 then
    local bi2=0
    for i,p in ipairs(P) do
      if p.o==c and (bi2==0 or p.a>P[bi2].a) then bi2=i end
    end
    if bi2>0 then
      local p=P[bi2]
      cc.money=cc.money-60
      if p.k<1 then p.k=1 elseif p.f<1 then p.f=1 elseif p.r<1 then p.r=1
      elseif #p.snb>0 and not p.port then p.port=true cc.money=cc.money-20 end
    end
  end
  for i,p in ipairs(P) do
    if p.o==c and p.a>=80 then
    local bj=0
    local function consider(j,sea)
      local q=P[j]
      if q.o==c or isally(c,q.o) then return end
      if sea and not p.port then return end
      if p.a<=q.a*1.25 then return end
      local hostile=atwar(c,q.o)
      if not hostile and q.o==ME then
        hostile=api.rand(0,1)<0.10-relv(c,ME)*0.002 and total(c)>total(ME)*1.3
      end
      if not hostile and q.o~=ME then hostile=api.rand(0,1)<0.35 end
      if hostile and (bj==0 or q.a<P[bj].a) then bj=j end
    end
    for _,j in ipairs(p.nb) do consider(j,false) end
    for _,j in ipairs(p.snb) do consider(j,true) end
    if bj>0 then combat(i,bj,false) end
    end
  end
end
function endturn()
  if over or not started then return end
  local my=provs(ME)
  C[ME].money=C[ME].money+2*my
  local al=0
  for cc,_ in pairs(C) do if cc~=ME and isally(ME,cc) and provs(cc)>0 then al=al+1 end end
  C[ME].money=C[ME].money+al*2
  for _,p in ipairs(P) do
    if p.o==ME then
      C[ME].money=C[ME].money+p.r*4
      if p.cap then C[ME].money=C[ME].money+8 end
      p.a=math.min(300,p.a*(1.06+0.25*p.k)+3)
    end
  end
  for c,cc in pairs(C) do if c~=ME then ai_country(c) end end
  turn=turn+1
  api.log('ход '..turn)
  checkend()
end
function inBtn(mx,my,bx) return abs(mx-bx)<14 and abs(my+37)<6 end
function issea(a,b)
  for _,x in ipairs(P[a].snb) do if x==b then return true end end
  return false
end
function update(dt)
  refresh()
  if over then return end
  local s=api.key('space')
  if started and s and not pspace then endturn() end
  pspace=s
  if api.click() then
    if not started then
      local nm=api.pick()
      if nm:sub(1,1)=='p' then
        local i=tonumber(nm:sub(2))
        if i and P[i] then
          ME=P[i].o
          started=true
          sel=i
          C[ME].ai=false
          addev('Ты — '..ME)
          api.log('me='..ME)
        end
      end
      return
    end
    local mx=api.mousex()
    local my=api.mousey()
    if inBtn(mx,my,-59) then endturn()
    elseif inBtn(mx,my,-31) then dorecruit()
    elseif inBtn(mx,my,-3) then dobuild()
    elseif inBtn(mx,my,25) then dopeace()
    else
      local nm=api.pick()
      local i=(nm:sub(1,1)=='p') and tonumber(nm:sub(2)) or nil
      if i and P[i] then
        if sel and i~=sel and P[i].o~=ME then
          if isally(ME,P[i].o) then api.log('это союзник!') tgt=i
          elseif isnb(sel,i) then combat(sel,i,true)
          elseif issea(sel,i) then
            if P[sel].port then combat(sel,i,true)
            else api.log('нужен порт для десанта (СТРОЙ)') tgt=i end
          else api.log('далеко: только соседи') tgt=i end
        elseif P[i].o==ME then sel=i
        else tgt=i sel=nil end
      else sel=nil end
    end
  end
end
");
        return sb.ToString();
    }

    static string F(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}
