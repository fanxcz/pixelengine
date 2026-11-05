using System;
using System.Collections.Generic;

namespace PixelFight;

// Tower Defense «Оборона» на движке: враги идут по дороге волнами,
// клик по траве ставит башню (ЛУК/ПУШКА), пули, деньги за фраги, 15 волн.
// Меню Project → New tower defense. Весь геймплей — Lua ниже.
public static class TowerDefense
{
    public const float BXBow = -40, BXCannon = -5, BXWave = 32, BtnY = 35;

    // дорога (мировые координаты)
    static readonly float[] WAY = { -78, -30, -30, -30, -30, -5, 20, -5, 20, 20, 70, 20 };

    public static LicProject Build()
    {
        var p = new LicProject { name = "Defense" };
        var sc = new LicScene { name = "Поле", gravity = 0, groundY = 1000, lockCam = true, camX0 = 0, camY0 = -4, bg1 = "#1E3A24", bg2 = "#0E1E14" };
        sc.objects.Add(new LicObject { name = "gm", kind = "text", x = 0, y = -60, text = "", gravity = false, solid = false, script = "defense.lua" });
        sc.objects.Add(new LicObject { name = "ui_money", kind = "text", x = -58, y = -44, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_wave", kind = "text", x = -6, y = -44, color = "#FFFFFF", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_lives", kind = "text", x = 48, y = -44, color = "#FFFFFF", gravity = false, solid = false });
        // дорога из клеток
        foreach (var pt in PathCells())
            sc.objects.Add(new LicObject { name = "road" + pt[2], kind = "rect", x = pt[0], y = pt[1], w = 10, h = 10, color = "#8A7A4A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "t_start", kind = "text", x = -70, y = -30, color = "#FFD34D", gravity = false, solid = false, text = "СТАРТ" });
        sc.objects.Add(new LicObject { name = "t_end", kind = "text", x = 62, y = 20, color = "#FF6A6A", gravity = false, solid = false, text = "БАЗА" });
        sc.objects.Add(new LicObject { name = "btn_bow", kind = "rect", x = BXBow, y = BtnY, w = 28, h = 8, color = "#2A5A3A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_bow_t", kind = "text", x = BXBow, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "ЛУК 50" });
        sc.objects.Add(new LicObject { name = "btn_can", kind = "rect", x = BXCannon, y = BtnY, w = 28, h = 8, color = "#8A5A2A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_can_t", kind = "text", x = BXCannon, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "ПУШКА 130" });
        sc.objects.Add(new LicObject { name = "btn_wave", kind = "rect", x = BXWave, y = BtnY, w = 28, h = 8, color = "#3E589E", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_wave_t", kind = "text", x = BXWave, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "ВОЛНА" });
        p.scenes.Add(sc);
        p.scripts.Add(new LicScript { name = "defense.lua", lang = "lua", code = Lua() });
        return p;
    }

    static List<float[]> PathCells()
    {
        var cells = new List<float[]>();
        int id = 0;
        for (int s = 0; s + 3 < WAY.Length; s += 2)
        {
            float x1 = WAY[s], y1 = WAY[s + 1], x2 = WAY[s + 2], y2 = WAY[s + 3];
            float len = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            int n = Math.Max(1, (int)(len / 6));
            for (int i = 0; i <= n; i++)
            {
                cells.Add(new float[] { x1 + (x2 - x1) * i / n, y1 + (y2 - y1) * i / n, id });
                id++;
            }
        }
        return cells;
    }

    static string Lua()
    {
        var wps = new List<string>();
        for (int i = 0; i + 1 < WAY.Length; i += 2)
            wps.Add("{" + WAY[i].ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "," + WAY[i + 1].ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "},");
        return @"
-- Оборона: клик по траве — башня (тип кнопкой), ВОЛНА — досрочно, 15 волн — победа.
local WAY={" + string.Join("", wps) + @"
}
local money=220
local lives=20
local wave=0
local seltype='bow'
local over=nil
local E={}
local T={}
local B={}
local spawnLeft=0
local spawnT=0
local waveBreak=0
local kills=0
local started=false
local COST={bow=50,can=130}
local DMG={bow=7,can=22}
local RATE={bow=0.5,can=1.4}
local RANGE={bow=24,can=20}
local function abs(x) return x<0 and -x or x end
local function dist(ax,ay,bx,by) return abs(ax-bx)+abs(ay-by) end
function nearPath(x,y)
  for s=1,#WAY-1 do
    local x1=WAY[s][1] local y1=WAY[s][2] local x2=WAY[s+1][1] local y2=WAY[s+1][2]
    local dx=x2-x1 local dy=y2-y1
    local L=dx*dx+dy*dy
    local t=0
    if L>0 then t=((x-x1)*dx+(y-y1)*dy)/L end
    if t<0 then t=0 end
    if t>1 then t=1 end
    if dist(x,y,x1+dx*t,y1+dy*t)<9 then return true end
  end
  return false
end
function startwave()
  wave=wave+1
  spawnLeft=5+wave*2
  spawnT=0
  api.log('волна '..wave)
end
function spawnenemy()
  local s=1+wave*0.12
  local kind='norm'
  local r=api.rand(0,1)
  if wave>=5 and r<0.2 then kind='tank' end
  if wave>=3 and r>=0.2 and r<0.5 then kind='fast' end
  local hp=20*s
  local sp=11
  local prize=8
  if kind=='fast' then hp=12*s sp=17 prize=10 end
  if kind=='tank' then hp=60*s sp=7 prize=18 end
  local col='#C44A4A'
  if kind=='fast' then col='#D8B93A' end
  if kind=='tank' then col='#7A3A8A' end
  local look='bandit'
  if kind=='fast' then look='dog' end
  if kind=='tank' then look='boar' end
  local o=api.spawn('knight',{name='e',x=WAY[1][1],y=WAY[1][2],w=7,h=11,color=col,gravity=false,solid=false,sprite=look})
  E[#E+1]={o=o,hp=hp,wp=2,sp=sp,prize=prize}
  spawnLeft=spawnLeft-1
end
function update(dt)
  if over then
    api.find('ui_lives').text=over
    return
  end
  if not started then
    started=true
    startwave()
  end
  if spawnLeft>0 then
    spawnT=spawnT-dt
    if spawnT<=0 then spawnT=0.8 spawnenemy() end
  elseif #E==0 then
    waveBreak=waveBreak+dt
    if waveBreak>3 then
      waveBreak=0
      if wave>=15 then over='ПОБЕДА!' api.log('win') else startwave() end
    end
  end
  for i=#E,1,-1 do
    local e=E[i]
    local tx=WAY[e.wp][1]
    local ty=WAY[e.wp][2]
    local dx=tx-e.o.x
    local dy=ty-e.o.y
    local d=abs(dx)+abs(dy)
    local step=e.sp*dt
    if d<=step then
      e.o.x=tx e.o.y=ty e.wp=e.wp+1
      if e.wp>#WAY then
        e.o.destroy()
        table.remove(E,i)
        lives=lives-1
        api.find('ui_lives').text='Жизни: '..lives
        api.log('прорыв! жизней '..lives)
        if lives<=0 then over='ПОРАЖЕНИЕ' api.log('lose') end
      end
    else
      e.o.vx=dx/d*e.sp e.o.vy=dy/d*e.sp -- скорость для физики и анимации ног
    end
  end
  for _,t in ipairs(T) do
    t.cd=t.cd-dt
    if t.cd<=0 then
      local best=nil
      local bd=RANGE[t.type]+1
      for _,e in ipairs(E) do
        local d=dist(t.x,t.y,e.o.x,e.o.y)
        if d<RANGE[t.type] and d<bd then bd=d best=e end
      end
      if best then
        t.cd=RATE[t.type]
        local dx=best.o.x-t.x
        local dy=best.o.y-t.y
        local d=abs(dx)+abs(dy)
        if d<0.1 then d=0.1 end
        local sp=90
        local b=api.spawn('rect',{name='b',x=t.x,y=t.y,w=2,h=2,color='#FFE26A',gravity=false,solid=false})
        B[#B+1]={o=b,vx=dx/d*sp,vy=dy/d*sp,dmg=DMG[t.type],life=1.2,splash=(t.type=='can') and 7 or 0}
      end
    end
  end
  for i=#B,1,-1 do
    local b=B[i]
    b.o.x=b.o.x+b.vx*dt
    b.o.y=b.o.y+b.vy*dt
    b.life=b.life-dt
    local hit=nil
    for _,e in ipairs(E) do
      if dist(b.o.x,b.o.y,e.o.x,e.o.y)<3 then hit=e break end
    end
    if hit then
      hit.hp=hit.hp-b.dmg
      if b.splash>0 then
        for _,e in ipairs(E) do
          if e~=hit and dist(b.o.x,b.o.y,e.o.x,e.o.y)<b.splash then e.hp=e.hp-b.dmg*0.5 end
        end
      end
      if hit.hp<=0 then
        money=money+hit.prize
        kills=kills+1
        api.log('убит '..kills)
        hit.o.destroy()
        for k,e in ipairs(E) do if e==hit then table.remove(E,k) break end end
      end
      b.o.destroy()
      table.remove(B,i)
    elseif b.life<=0 then
      b.o.destroy()
      table.remove(B,i)
    end
  end
  api.find('ui_money').text='Монеты: '..math.floor(money)..' (лук)'
  api.find('ui_wave').text='Волна '..wave..'/15'
  api.find('ui_lives').text='Жизни: '..lives
  if seltype=='can' then api.find('ui_money').text='Монеты: '..math.floor(money)..' (пушка)' end
  if api.click() then
    local nm=api.pick()
    if nm=='btn_bow' or nm=='btn_bow_t' then seltype='bow'
    elseif nm=='btn_can' or nm=='btn_can_t' then seltype='can'
    elseif nm=='btn_wave' or nm=='btn_wave_t' then
      if spawnLeft<=0 and #E==0 then waveBreak=99 else api.log('волна идёт!') end
    elseif nm=='' then
      local mx=api.mousex()
      local my=api.mousey()
      if my<28 and money>=COST[seltype] and not nearPath(mx,my) then
        money=money-COST[seltype]
        local col='#3AA84A'
        if seltype=='can' then col='#E8862A' end
        local o=api.spawn('tower',{name='t',x=mx,y=my,w=9,h=14,color=col,gravity=false,solid=false,num=(seltype=='can' and 2 or 1)})
        T[#T+1]={x=mx,y=my,type=seltype,cd=0}
        api.log('башня за '..COST[seltype])
      elseif my<28 and money<COST[seltype] then api.log('нет денег')
      elseif my<28 then api.log('сюда нельзя (дорога)') end
    end
  end
end
";
    }
}
