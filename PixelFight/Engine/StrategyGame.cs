using System.Collections.Generic;

namespace PixelFight;

// Пошаговая стратегия в духе Age of History 2 — готовая игра на движке.
// 30 провинций, 5 стран, ходы, набор армии, войны и миры, ИИ, победа/поражение.
// Меню Project → New strategy. Координаты кнопок публичны (для автотеста).
public static class StrategyGame
{
    public const float BTurnX = -58, BRecX = -16, BPeaX = 26, BtnY = -36;

    struct Prov { public string n; public int x, y; public string o; public int a; public bool cap; }

    static readonly Prov[] MAP = {
        new Prov { n="Стокгольм", x=-63, y=-18, o="Швеция", a=200, cap=true },
        new Prov { n="Уппсала", x=-45, y=-18, o="Швеция", a=90 },
        new Prov { n="Варшава", x=-27, y=-18, o="Польша", a=200, cap=true },
        new Prov { n="Мост", x=-9, y=-18, o="", a=110 },
        new Prov { n="Тверь", x=9, y=-18, o="Русь", a=90 },
        new Prov { n="Казань", x=27, y=-18, o="Орда", a=200, cap=true },
        new Prov { n="Самара", x=45, y=-18, o="Орда", a=90 },
        new Prov { n="Уфа", x=63, y=-18, o="Орда", a=90 },
        new Prov { n="Гётеборг", x=-63, y=-4, o="Швеция", a=90 },
        new Prov { n="Краков", x=-45, y=-4, o="Польша", a=90 },
        new Prov { n="Минск", x=-27, y=-4, o="Польша", a=90 },
        new Prov { n="Рязань", x=9, y=-4, o="", a=50 },
        new Prov { n="Воронеж", x=27, y=-4, o="Русь", a=90 },
        new Prov { n="Царицын", x=45, y=-4, o="Османы", a=90 },
        new Prov { n="Стамбул", x=63, y=-4, o="Османы", a=200, cap=true },
        new Prov { n="Рига", x=-63, y=10, o="", a=60 },
        new Prov { n="Новгород", x=-45, y=10, o="Русь", a=90 },
        new Prov { n="Псков", x=-27, y=10, o="Русь", a=90 },
        new Prov { n="Тула", x=9, y=10, o="", a=50 },
        new Prov { n="Курск", x=27, y=10, o="Русь", a=90 },
        new Prov { n="Саратов", x=45, y=10, o="Орда", a=90 },
        new Prov { n="Баку", x=63, y=10, o="Османы", a=90 },
        new Prov { n="Готланд", x=-63, y=22, o="", a=50 },
        new Prov { n="Вологда", x=-45, y=22, o="", a=50 },
        new Prov { n="Ладога", x=-27, y=22, o="", a=50 },
        new Prov { n="Белоозеро", x=-9, y=22, o="", a=50 },
        new Prov { n="Муром", x=9, y=22, o="", a=50 },
        new Prov { n="Суздаль", x=27, y=22, o="Русь", a=90 },
        new Prov { n="Яик", x=45, y=22, o="", a=50 },
        new Prov { n="Эмба", x=63, y=22, o="", a=50 },
    };

    public static LicProject Build()
    {
        var p = new LicProject { name = "Ages" };
        var sc = new LicScene { name = "Мир", gravity = 0, groundY = 12 };
        // gm первым: камера стоит на месте (следит за ним)
        sc.objects.Add(new LicObject { name = "gm", kind = "text", x = 0, y = -60, text = "", gravity = false, solid = false, script = "ages.lua" });
        sc.objects.Add(new LicObject { name = "ui_money", kind = "text", x = -58, y = -50, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_turn", kind = "text", x = -6, y = -50, color = "#FFFFFF", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_sel", kind = "text", x = 48, y = -50, color = "#FFFFFF", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_turn", kind = "rect", x = BTurnX, y = BtnY, w = 32, h = 10, color = "#2A5A3A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_turn_t", kind = "text", x = BTurnX, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "ХОД (пробел)" });
        sc.objects.Add(new LicObject { name = "btn_rec", kind = "rect", x = BRecX, y = BtnY, w = 32, h = 10, color = "#3E589E", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_rec_t", kind = "text", x = BRecX, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "НАБОР 50" });
        sc.objects.Add(new LicObject { name = "btn_pea", kind = "rect", x = BPeaX, y = BtnY, w = 32, h = 10, color = "#6A6A7A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_pea_t", kind = "text", x = BPeaX, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "МИР 40" });
        sc.objects.Add(new LicObject { name = "ring", kind = "circle", x = 0, y = -200, w = 21, h = 17, color = "#FFFFFF", gravity = false, solid = false });
        for (int i = 0; i < MAP.Length; i++)
        {
            var m = MAP[i];
            sc.objects.Add(new LicObject { name = "p" + (i + 1), kind = "rect", x = m.x, y = m.y, w = 15, h = 11, color = "#777777", gravity = false });
            sc.objects.Add(new LicObject { name = "t" + (i + 1), kind = "text", x = m.x, y = m.y, color = "#FFFFFF", gravity = false, solid = false });
        }
        sc.objects.Add(new LicObject { name = "ev1", kind = "text", x = 0, y = 30, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ev2", kind = "text", x = 0, y = 36, color = "#CCCCCC", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ev3", kind = "text", x = 0, y = 42, color = "#999999", gravity = false, solid = false });
        p.scenes.Add(sc);
        p.scripts.Add(new LicScript { name = "ages.lua", lang = "lua", code = Lua() });
        return p;
    }

    static string Lua()
    {
        var provs = new List<string>();
        for (int i = 0; i < MAP.Length; i++)
        {
            var m = MAP[i];
            provs.Add(" {n='" + m.n + "',x=" + m.x + ",y=" + m.y + ",o='" + m.o + "',a=" + m.a + (m.cap ? ",cap=true" : "") + "},");
        }
        return @"
-- Ages: пошаговая стратегия. Клик по своей — выбрать, по соседней чужой — атака.
-- ХОД/пробел — ход, НАБОР — +120 войска за 50, МИР — мир со всеми за 40.
local NEI=26
local P={
" + string.Join("\n", provs) + @"
}
local C={
 ['Русь']={color='#3E589E',money=120,ai=false},
 ['Швеция']={color='#D8B93A',money=100,ai=true},
 ['Польша']={color='#D8D8D8',money=100,ai=true},
 ['Орда']={color='#9E3A3A',money=100,ai=true},
 ['Османы']={color='#3A7A44',money=100,ai=true},
}
local ME='Русь'
local NEUC='#777777'
local turn=1
local sel=nil
local wars={}
local over=nil
local ev={}
local pspace=false
local O={}
local T={}
local function abs(x) return x<0 and -x or x end
local function dist(a,b) return abs(P[a].x-P[b].x)+abs(P[a].y-P[b].y) end
local function wkey(a,b) return a..'|'..b end
local function atwar(a,b)
  if a=='' or b=='' or a==b then return false end
  return wars[wkey(a,b)] or wars[wkey(b,a)] or false
end
local function setwar(a,b)
  if a=='' or b=='' or a==b then return end
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
local function addev(s)
  table.insert(ev,1,s)
  while #ev>3 do table.remove(ev) end
end
function start()
  for i=1,#P do O[i]=api.find('p'..i) T[i]=api.find('t'..i) end
  addev('Захватывай провинции!')
  addev('Клик — выбрать и атаковать')
  api.log('ages started: '..#P..' провинций')
end
function refresh()
  for i,p in ipairs(P) do
    O[i].num=p.a
    O[i].color=p.o=='' and NEUC or C[p.o].color
    T[i].text=p.n
  end
  api.find('ui_money').text='Казна: '..math.floor(C[ME].money)
  api.find('ui_turn').text='Ход '..turn
  if over=='win' then api.find('ui_sel').text='ПОБЕДА! Мир твой!'
  elseif over=='lose' then api.find('ui_sel').text='ПОРАЖЕНИЕ'
  elseif sel then api.find('ui_sel').text=P[sel].n..' ('..math.floor(P[sel].a)..')'
  else api.find('ui_sel').text='выбери провинцию' end
  local r=api.find('ring')
  if sel and not over then r.x=P[sel].x r.y=P[sel].y else r.x=0 r.y=-200 end
  for i=1,3 do api.find('ev'..i).text=ev[i] or '' end
end
function combat(ai,di,byme)
  local A=P[ai].a
  if A<60 then if byme then api.log('мало войска (нужно 60)') end return end
  local D=P[di].a
  local a=A*api.rand(0.75,1.0)
  local d=D*api.rand(0.75,1.0)+(P[di].cap and 40 or 0)
  local atk=P[ai].o
  local dfn=P[di].o
  if a>d then
    P[di].o=atk
    P[di].a=math.max(20,(A-D)*0.55)
    P[ai].a=A*0.35
    setwar(atk,dfn)
    addev(P[di].n..' взят ('..atk..')')
    api.log('take '..P[di].n)
  else
    P[di].a=math.max(10,D-A*0.55)
    P[ai].a=A*0.4
    setwar(atk,dfn)
    addev('Атака на '..P[di].n..' отбита')
    api.log('defend '..P[di].n)
  end
  checkend()
end
function checkend()
  local my=provs(ME)
  if my==0 and not over then over='lose' addev('ПОРАЖЕНИЕ') api.log('lose') end
  if my==#P and not over then over='win' addev('ПОБЕДА!') api.log('win') end
end
function dorecruit()
  if not sel or P[sel].o~=ME then api.log('выбери свою провинцию') return end
  if C[ME].money<50 then api.log('нет денег') return end
  C[ME].money=C[ME].money-50
  P[sel].a=math.min(500,P[sel].a+120)
  api.log('набор +120 в '..P[sel].n)
end
function dopeace()
  if not haswar(ME) then api.log('войн нет') return end
  if C[ME].money<40 then api.log('нет денег на мир') return end
  C[ME].money=C[ME].money-40
  unwar(ME)
  addev('Мир со всеми!')
  api.log('peace')
end
function ai_country(c)
  local cc=C[c]
  local n=provs(c)
  if n==0 then return end
  cc.money=cc.money+5*n
  for _,p in ipairs(P) do if p.o==c then p.a=math.min(350,p.a*1.07+4) end end
  if atwar(c,ME) and total(c)<total(ME)*0.45 then
    unwar(c) addev(c..' просит мира!') return
  end
  if cc.money>=80 then
    local bi=0
    for i,p in ipairs(P) do
      if p.o==c and p.a<400 and (bi==0 or p.a>P[bi].a) then bi=i end
    end
    if bi>0 then cc.money=cc.money-80 P[bi].a=P[bi].a+150 end
  end
  for i,p in ipairs(P) do
    if p.o==c and p.a>=90 then
    local bj=0
    for j,q in ipairs(P) do
      if q.o~=c and dist(i,j)<NEI and p.a>q.a*1.25 then
        local hostile=q.o=='' or atwar(c,q.o)
        if not hostile and q.o==ME then hostile=api.rand(0,1)<0.12 and total(c)>total(ME)*1.4 end
        if not hostile and q.o~='' and C[q.o].ai then hostile=api.rand(0,1)<0.4 end
        if hostile and (bj==0 or q.a<P[bj].a) then bj=j end
      end
    end
    if bj>0 then combat(i,bj,false) end
    end
  end
end
function endturn()
  if over then return end
  C[ME].money=C[ME].money+5*provs(ME)
  for _,p in ipairs(P) do
    if p.o==ME then p.a=math.min(350,p.a*1.07+4)
    elseif p.o=='' then p.a=math.min(150,p.a*1.05+2) end
  end
  for c,cc in pairs(C) do if cc.ai then ai_country(c) end end
  turn=turn+1
  api.log('ход '..turn)
  checkend()
end
function provAt(mx,my)
  for i,p in ipairs(P) do
    if abs(mx-p.x)<9 and abs(my-p.y)<7 then return i end
  end
  return nil
end
function inBtn(mx,my,bx) return abs(mx-bx)<16 and abs(my+36)<6 end
function update(dt)
  refresh()
  if over then return end
  local s=api.key('space')
  if s and not pspace then endturn() end
  pspace=s
  if api.click() then
    local mx=api.mousex()
    local my=api.mousey()
    if inBtn(mx,my,-58) then endturn()
    elseif inBtn(mx,my,-16) then dorecruit()
    elseif inBtn(mx,my,26) then dopeace()
    else
      local i=provAt(mx,my)
      if i then
        if sel and i~=sel and P[i].o~=ME then
          if dist(sel,i)<NEI then combat(sel,i,true)
          else api.log('далеко: иди через соседей') end
        elseif P[i].o==ME then sel=i
        else sel=nil end
      else sel=nil end
    end
  end
end
";
    }
}
