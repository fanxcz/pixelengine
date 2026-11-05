using System.Collections.Generic;

namespace PixelFight;

// Купец — игра про экономику на движке: 8 городов, 4 товара, цены плавают,
// караван возит, кнопки КУПИТЬ/ПРОДАТЬ/ЕХАТЬ, месяцы идут, цель — 3000 монет.
// Меню Project → New merchant. Весь геймплей — Lua ниже.
public static class MerchantGame
{
    public const float BXBuy = -45, BXSell = -10, BXGo = 25, BXTurn = 58, BtnY = 35;

    struct City { public string n; public int x, y; }

    static readonly City[] CITIES = {
        new City { n="Новгород", x=-64, y=-20 },
        new City { n="Рига", x=-44, y=8 },
        new City { n="Москва", x=-22, y=-12 },
        new City { n="Казань", x=0, y=12 },
        new City { n="Астрахань", x=22, y=-10 },
        new City { n="Стамбул", x=44, y=8 },
        new City { n="Багдад", x=64, y=-14 },
        new City { n="Самарканд", x=8, y=-28 },
    };

    static readonly string[] GOODS = { "Хлеб", "Железо", "Ткань", "Специи" };
    static readonly int[] BASE = { 10, 25, 40, 70 };

    public static LicProject Build()
    {
        var p = new LicProject { name = "Merchant" };
        var sc = new LicScene { name = "Рынок", gravity = 0, groundY = 1000, lockCam = true, camX0 = 0, camY0 = -4, bg1 = "#3A2E1E", bg2 = "#171208" };
        sc.objects.Add(new LicObject { name = "gm", kind = "text", x = 0, y = -60, text = "", gravity = false, solid = false, script = "market.lua" });
        sc.objects.Add(new LicObject { name = "ui_money", kind = "text", x = -52, y = -44, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_turn", kind = "text", x = 8, y = -44, color = "#FFFFFF", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_sel", kind = "text", x = 52, y = -44, color = "#FFFFFF", gravity = false, solid = false });
        for (int i = 0; i < CITIES.Length; i++)
        {
            sc.objects.Add(new LicObject { name = "c" + (i + 1), kind = "rect", x = CITIES[i].x, y = CITIES[i].y, w = 20, h = 12, color = "#4A5A7A", gravity = false });
            sc.objects.Add(new LicObject { name = "cn" + (i + 1), kind = "text", x = CITIES[i].x, y = CITIES[i].y, color = "#FFFFFF", gravity = false, solid = false, text = CITIES[i].n });
        }
        sc.objects.Add(new LicObject { name = "mark", kind = "circle", x = -64, y = -20, w = 24, h = 16, color = "#FFD34D", gravity = false, solid = false });
        for (int g = 0; g < 4; g++)
        {
            sc.objects.Add(new LicObject { name = "r" + (g + 1), kind = "rect", x = 0, y = 20 + g * 3, w = 70, h = 3, color = "#22222A", gravity = false });
            sc.objects.Add(new LicObject { name = "rt" + (g + 1), kind = "text", x = 0, y = 20 + g * 3, color = "#DDDDDD", gravity = false, solid = false });
        }
        sc.objects.Add(new LicObject { name = "btn_buy", kind = "rect", x = BXBuy, y = BtnY, w = 24, h = 8, color = "#2A5A3A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_buy_t", kind = "text", x = BXBuy, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "КУПИТЬ" });
        sc.objects.Add(new LicObject { name = "btn_sell", kind = "rect", x = BXSell, y = BtnY, w = 24, h = 8, color = "#5A3A2A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_sell_t", kind = "text", x = BXSell, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "ПРОДАТЬ" });
        sc.objects.Add(new LicObject { name = "btn_go", kind = "rect", x = BXGo, y = BtnY, w = 24, h = 8, color = "#3E589E", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_go_t", kind = "text", x = BXGo, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "ЕХАТЬ" });
        sc.objects.Add(new LicObject { name = "btn_turn", kind = "rect", x = BXTurn, y = BtnY, w = 24, h = 8, color = "#6A6A7A", gravity = false });
        sc.objects.Add(new LicObject { name = "btn_turn_t", kind = "text", x = BXTurn, y = BtnY, color = "#FFFFFF", gravity = false, solid = false, text = "МЕСЯЦ" });
        p.scenes.Add(sc);
        p.scripts.Add(new LicScript { name = "market.lua", lang = "lua", code = Lua() });
        return p;
    }

    static string Lua()
    {
        var cn = new List<string>();
        for (int i = 0; i < CITIES.Length; i++)
            cn.Add(" '" + CITIES[i].n + "',");
        var gd = new List<string>();
        for (int i = 0; i < GOODS.Length; i++)
            gd.Add(" '" + GOODS[i] + "',");
        return @"
-- Купец: вози товары, богатей. Цель — 3000 монет за 24 месяца.
-- Клик по городу — выбрать, ЕХАТЬ — переезд (платно), строки — товар, КУПИТЬ/ПРОДАТЬ по 5 шт.
local CITY={" + string.Join("", cn) + @"
}
local GOODS={" + string.Join("", gd) + @"
}
local BASE={10,25,40,70}
local money=300
local cargo={0,0,0,0}
local loc=1
local selc=1
local selg=1
local month=1
local over=nil
local mult={} -- mult[city][good]
local function abs(x) return x<0 and -x or x end
function start()
  for c=1,#CITY do
    mult[c]={}
    for g=1,#GOODS do
      local m=1.0
      if (c+g)%4==0 then m=0.6 end
      if (c+g)%4==2 then m=1.5 end
      mult[c][g]=m
    end
  end
  api.log('торгуй! цель — 3000 за 24 месяца')
end
function price(c,g) return math.max(1,math.floor(BASE[g]*mult[c][g])) end
function load()
  local n=0
  for g=1,#GOODS do n=n+cargo[g] end
  return n
end
function refresh()
  for c=1,#CITY do
    local o=api.find('c'..c)
    if c==loc then o.color='#2A7A3A'
    elseif c==selc then o.color='#8A8A9A'
    else o.color='#4A5A7A' end
    local m=api.find('mark')
    m.x=api.find('c'..loc).x m.y=api.find('c'..loc).y
  end
  for g=1,#GOODS do
    local t=(g==selg) and '> ' or '  '
    api.find('rt'..g).text=t..GOODS[g]..' '..price(selc,g)..' ('..cargo[g]..')'
  end
  api.find('ui_money').text='Монеты: '..math.floor(money)
  api.find('ui_turn').text='Месяц '..month..'/24'
  if over=='win' then api.find('ui_sel').text='БОГАЧ! Победа!'
  elseif over=='lose' then api.find('ui_sel').text='Банкрот...'
  else api.find('ui_sel').text=CITY[selc]..' (трюм '..load()..'/40)' end
end
function buy()
  if over then return end
  if load()+5>40 then api.log('трюм полон') return end
  local c=5*price(selc,selg)
  if money<c then api.log('нет денег') return end
  money=money-c
  cargo[selg]=cargo[selg]+5
  api.log('куплено 5 '..GOODS[selg]..' за '..c)
end
function sell()
  if over then return end
  if cargo[selg]<5 then api.log('нечего продать') return end
  local c=math.floor(5*price(selc,selg)*0.9)
  money=money+c
  cargo[selg]=cargo[selg]-5
  api.log('продано за '..c)
  if money>=3000 and not over then over='win' api.log('win!') end
end
function go()
  if over then return end
  if selc==loc then api.log('ты уже здесь') return end
  local o1=api.find('c'..loc)
  local o2=api.find('c'..selc)
  local cost=math.floor((abs(o1.x-o2.x)+abs(o1.y-o2.y))*0.5)
  if money<cost then api.log('дорога стоит '..cost) return end
  money=money-cost
  loc=selc
  api.log('переезд за '..cost)
end
function nextmonth()
  if over then return end
  month=month+1
  for c=1,#CITY do
    for g=1,#GOODS do
      mult[c][g]=mult[c][g]*(0.85+api.rand(0,0.3))
      if mult[c][g]<0.4 then mult[c][g]=0.4 end
      if mult[c][g]>2.2 then mult[c][g]=2.2 end
    end
  end
  local r=api.rand(0,1)
  if r<0.15 and money>500 then
    local l=math.floor(money*0.1)
    money=money-l
    api.log('разбойники! -'..l)
  elseif r<0.25 then
    money=money+50
    api.log('подарок +50')
  end
  api.log('месяц '..month)
  if month>24 then
    if money>=3000 then over='win' else over='lose' end
    api.log('итог: '..math.floor(money))
  end
end
function update(dt)
  refresh()
  if over then return end
  if api.click() then
    local nm=api.pick()
    local pre=nm:sub(1,1)
    if pre=='c' then
      local i=tonumber(nm:sub(2))
      if i and CITY[i] then selc=i end
    elseif nm=='mark' then selc=loc
    elseif pre=='r' then
      local i=tonumber(nm:sub(2))
      if i and GOODS[i] then selg=i end
    else
      local mx=api.mousex()
      local my=api.mousey()
      if abs(mx+45)<13 and abs(my-35)<4 then buy()
      elseif abs(mx+10)<13 and abs(my-35)<4 then sell()
      elseif abs(mx-25)<13 and abs(my-35)<4 then go()
      elseif abs(mx-58)<13 and abs(my-35)<4 then nextmonth() end
    end
  end
end
";
    }
}
