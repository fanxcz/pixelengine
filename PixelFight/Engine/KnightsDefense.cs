using System;
using System.Collections.Generic;
using System.IO;

namespace PixelFight;

// «Рыцари» v2: заклинания (огонь/лечение, мана), именные боссы каждые 10 уровней,
// герой Сэр Роланд с выбором прокачки (Z/X/C), катапульты и новые юниты
// (копейщик, монах, 7 кнопок), тактика (G — точка сбора, H — держать, R — отход),
// биомы по главам + погода, экономика событий, звуки (синтез WAV в .lic),
// частицы, анонсы и статистика. 100+ уровней, прогресс в хранилище.
// Меню Project → New knights.
public static class KnightsDefense
{
    public const float BX1 = -63, BX2 = -27, BX3 = 9, BX4 = 45, BtnY = 36;

    // --- синтез WAV (8-бит mono 22050) для ассетов .lic ---
    static byte[] Wav(Func<int, float> fn, float dur)
    {
        int rate = 22050, n = (int)(rate * dur);
        var d = new byte[44 + n];
        void Str(int o, string s) { for (int i = 0; i < s.Length; i++) d[o + i] = (byte)s[i]; }
        Str(0, "RIFF"); BitConverter.GetBytes(36 + n).CopyTo(d, 4);
        Str(8, "WAVE"); Str(12, "fmt "); BitConverter.GetBytes(16).CopyTo(d, 16);
        BitConverter.GetBytes((short)1).CopyTo(d, 20); BitConverter.GetBytes((short)1).CopyTo(d, 22);
        BitConverter.GetBytes(rate).CopyTo(d, 24); BitConverter.GetBytes(rate).CopyTo(d, 28);
        BitConverter.GetBytes((short)1).CopyTo(d, 32); BitConverter.GetBytes((short)8).CopyTo(d, 34);
        Str(36, "data"); BitConverter.GetBytes(n).CopyTo(d, 40);
        for (int i = 0; i < n; i++)
            d[44 + i] = (byte)Math.Max(0, Math.Min(255, 128 + 100 * fn(i)));
        return d;
    }

    static byte[] HitWav()
    {
        var r = new Random(11);
        return Wav(i =>
        {
            float t = i / 22050f;
            float env = (float)Math.Exp(-t * 28);
            return ((float)r.NextDouble() * 2 - 1) * env + (float)Math.Sin(2 * Math.PI * 160 * t) * env * 0.5f;
        }, 0.18f);
    }

    static byte[] FanfareWav()
    {
        float[] notes = { 523, 659, 784, 1046 };
        return Wav(i =>
        {
            float t = i / 22050f;
            int k = Math.Min(3, (int)(t / 0.2f));
            float f = notes[k];
            float env = Math.Max(0, 1 - (t - k * 0.2f) * 3);
            return (float)Math.Sin(2 * Math.PI * f * t) * env * 0.8f
                 + (float)Math.Sin(2 * Math.PI * f * 2 * t) * env * 0.2f;
        }, 0.9f);
    }

    static byte[] FireWav()
    {
        var r = new Random(23);
        float last = 0;
        return Wav(i =>
        {
            float t = i / 22050f;
            float env = (float)Math.Exp(-t * 5);
            float n = (float)r.NextDouble() * 2 - 1;
            last = last * 0.92f + n * 0.08f; // низкий гул огня
            return last * 3 * env;
        }, 0.5f);
    }

    static byte[] HealWav()
    {
        return Wav(i =>
        {
            float t = i / 22050f;
            float f = 400 + t * 800;
            float env = (float)Math.Sin(Math.PI * Math.Min(1, t / 0.5f));
            return (float)Math.Sin(2 * Math.PI * f * t) * env * 0.7f;
        }, 0.5f);
    }

    public static LicProject Build()
    {
        var p = new LicProject { name = "Knights v7" };
        var sc = new LicScene { name = "Поле", gravity = 0, groundY = 0, lockCam = true, camX0 = 0, camY0 = -4, deco = true, bg1 = "#8FC3E8", bg2 = "#D8EBD8" };
        sc.objects.Add(new LicObject { name = "gm", kind = "text", x = 0, y = -60, text = "", gravity = false, solid = false, script = "knights.lua" });
        sc.objects.Add(new LicObject { name = "ui_money", kind = "text", x = -52, y = -44, color = "#5A3A00", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_time", kind = "text", x = 0, y = -44, color = "#333333", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ui_base", kind = "text", x = 52, y = -44, color = "#333333", gravity = false, solid = false });
        // --- живая карта: дорога, деревья, камни, цветы, кусты (под всем) ---
        for (int gx = -78; gx <= 78; gx += 12)
            sc.objects.Add(new LicObject { name = "road", kind = "rect", x = gx, y = -2, w = 12, h = 7, color = "#C8B078", gravity = false, solid = false });
        // --- живая карта: деревья стоят на земле (ствол врыт у горизонта), мелочь — на траве ---
        var rngd = new Random(42);
        for (int i = 0; i < 9; i++)
        {
            float tx = -72 + i * 18 + rngd.Next(-4, 5);
            float trunkH = 9 + rngd.Next(0, 5);
            float trunkY = -1 - trunkH / 2; // низ ствола ровно у земли (y=-1, за дорогой)
            float crownY = -1 - trunkH - 4 - rngd.Next(0, 3);
            float crownW = 12 + rngd.Next(0, 4);
            sc.objects.Add(new LicObject { name = "trunk", kind = "rect", x = tx, y = trunkY, w = 3, h = trunkH, color = "#6A4A30", gravity = false, solid = false });
            sc.objects.Add(new LicObject { name = "crown", kind = "circle", x = tx, y = crownY, w = crownW, h = crownW - 2, color = i % 3 == 0 ? "#3A7A3A" : "#4A8A44", gravity = false, solid = false });
        }
        for (int i = 0; i < 14; i++)
        {
            float rx = -76 + rngd.Next(0, 152);
            float ry = 3 + rngd.Next(0, 5); // на траве перед дорогой, выше кнопок
            if (i % 4 == 0) sc.objects.Add(new LicObject { name = "rock", kind = "circle", x = rx, y = ry, w = 3, h = 2, color = "#9A958A", gravity = false, solid = false });
            else if (i % 4 == 1) sc.objects.Add(new LicObject { name = "bush", kind = "circle", x = rx, y = ry, w = 5, h = 3, color = "#3E7A3E", gravity = false, solid = false });
            else if (i % 4 == 2) sc.objects.Add(new LicObject { name = "flower", kind = "rect", x = rx, y = ry, w = 1, h = 2, color = i % 2 == 0 ? "#E86A8A" : "#E8D84A", gravity = false, solid = false });
            else sc.objects.Add(new LicObject { name = "grass", kind = "rect", x = rx, y = ry, w = 2, h = 2, color = "#4E8A3E", gravity = false, solid = false });
        }
        sc.objects.Add(new LicObject { name = "base_p", kind = "castle", x = -70, y = -15, w = 16, h = 30, color = "#3E589E", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "base_e", kind = "camp", x = 70, y = -15, w = 22, h = 26, color = "#9E3A3A", gravity = false, solid = false });
        string[] un = { "$20", "$50", "$120", "$90", "$35", "$70", "$150", "$100" };
        float[] ux = { -63, -45, -27, -9, 9, 27, 45, 63 };
        for (int i = 0; i < 8; i++)
        {
            sc.objects.Add(new LicObject { name = "btn_u" + (i + 1), kind = "button", x = ux[i], y = BtnY, w = 16, h = 10, color = "#3E589E", gravity = false, solid = false });
            sc.objects.Add(new LicObject { name = "btn_u" + (i + 1) + "t", kind = "text", x = ux[i], y = BtnY + 3, color = "#FFFFFF", gravity = false, solid = false, text = un[i] });
        }
        sc.objects.Add(new LicObject { name = "btn_fire", kind = "button", x = -57, y = 27, w = 20, h = 6, color = "#8A3A1A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_fire_t", kind = "text", x = -54, y = 27, color = "#FFFFFF", gravity = false, solid = false, text = "ОГОНЬ" });
        sc.objects.Add(new LicObject { name = "btn_heal", kind = "button", x = -34, y = 27, w = 20, h = 6, color = "#2A6A4A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_heal_t", kind = "text", x = -31, y = 27, color = "#FFFFFF", gravity = false, solid = false, text = "ЛЕЧ" });
        sc.objects.Add(new LicObject { name = "btn_inc", kind = "button", x = -11, y = 27, w = 20, h = 6, color = "#2A5A3A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_inc_t", kind = "text", x = -8, y = 27, color = "#FFFFFF", gravity = false, solid = false, text = "ДОХОД" });
        sc.objects.Add(new LicObject { name = "btn_wall", kind = "button", x = 11, y = 27, w = 20, h = 6, color = "#6A6A7A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_wall_t", kind = "text", x = 14, y = 27, color = "#FFFFFF", gravity = false, solid = false, text = "СТЕНА" });
        sc.objects.Add(new LicObject { name = "btn_pause", kind = "button", x = 34, y = 27, w = 20, h = 6, color = "#444458", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_pause_t", kind = "text", x = 37, y = 27, color = "#FFFFFF", gravity = false, solid = false, text = "ПАУЗА" });
        sc.objects.Add(new LicObject { name = "btn_fast", kind = "button", x = 57, y = 27, w = 20, h = 6, color = "#444458", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_fast_t", kind = "text", x = 60, y = 27, color = "#FFFFFF", gravity = false, solid = false, text = ">>" });
        sc.objects.Add(new LicObject { name = "btn_menu", kind = "button", x = 16, y = 20, w = 16, h = 6, color = "#444450", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_menu_t", kind = "text", x = 16, y = 20, color = "#FFFFFF", gravity = false, solid = false, text = "МЕНЮ" });
        sc.objects.Add(new LicObject { name = "btn_volley", kind = "button", x = -12, y = 20, w = 30, h = 6, color = "#5A3A6A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "btn_volley_t", kind = "text", x = -9, y = 20, color = "#FFFFFF", gravity = false, solid = false, text = "ЗАЛП 50" });
        // --- меню: панель, заголовок, кнопки ---
        sc.objects.Add(new LicObject { name = "panel", kind = "rect", x = 0, y = -10, w = 170, h = 104, color = "#14141C", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "title", kind = "text", x = 0, y = -44, color = "#FFD34D", gravity = false, solid = false, text = "РЫЦАРИ" });
        sc.objects.Add(new LicObject { name = "subtitle", kind = "text", x = 0, y = -34, color = "#AAAAAA", gravity = false, solid = false, text = "оборона замка • 100 уровней • v7" });
        sc.objects.Add(new LicObject { name = "stats", kind = "text", x = 0, y = 16, color = "#FFD34D", gravity = false, solid = false, text = "" });
        string[] mn = { "ИГРАТЬ", "УРОВНИ", "ПОМОЩЬ", "ЛЕГКО" };
        string[] mk = { "m_play", "m_levels", "m_help", "m_diff" };
        float[] mx = { -33, 33, -33, 33 };
        float[] my = { -22, -22, -8, -8 };
        for (int i = 0; i < 4; i++)
        {
            sc.objects.Add(new LicObject { name = mk[i], kind = "button", x = mx[i], y = my[i], w = 62, h = 12, color = "#3E589E", gravity = false, solid = false });
            sc.objects.Add(new LicObject { name = mk[i] + "t", kind = "text", x = mx[i], y = my[i], color = "#FFFFFF", gravity = false, solid = false, text = mn[i] });
        }
        sc.objects.Add(new LicObject { name = "m_random", kind = "button", x = 0, y = 5, w = 62, h = 10, color = "#2A6A5A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "m_random_t", kind = "text", x = 0, y = 5, color = "#FFFFFF", gravity = false, solid = false, text = "СЛУЧАЙНЫЙ" });
        sc.objects.Add(new LicObject { name = "m_wipe", kind = "button", x = 0, y = 30, w = 44, h = 8, color = "#6A2A2A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "m_wipe_t", kind = "text", x = 0, y = 30, color = "#FFFFFF", gravity = false, solid = false, text = "СБРОС" });
        // --- сетка 50 уровней 10x5 + назад ---
        for (int li = 1; li <= 50; li++)
        {
            int col = (li - 1) % 10, row = (li - 1) / 10;
            sc.objects.Add(new LicObject { name = "lv" + li, kind = "button", x = -63 + col * 14, y = -30 + row * 12, w = 12, h = 9, color = "#33333C", gravity = false, solid = false });
            sc.objects.Add(new LicObject { name = "lv" + li + "t", kind = "text", x = -63 + col * 14, y = -30 + row * 12, color = "#888888", gravity = false, solid = false, text = "" + li });
        }
        sc.objects.Add(new LicObject { name = "lv_back", kind = "button", x = -14, y = 34, w = 44, h = 8, color = "#444450", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "lv_back_t", kind = "text", x = -14, y = 34, color = "#FFFFFF", gravity = false, solid = false, text = "НАЗАД" });
        sc.objects.Add(new LicObject { name = "lv_page", kind = "button", x = 32, y = 34, w = 32, h = 8, color = "#2A4A5A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "lv_page_t", kind = "text", x = 32, y = 34, color = "#FFFFFF", gravity = false, solid = false, text = "СТР 1" });
        // --- помощь ---
        string[] help = {
            "1-8/клик: выпуск. V-залп со стен ($50).",
            "ОГОНЬ/ЛЕЧ: мана. ДОХОД/СТЕНА: казна $.",
            "U-урон I-броня. Space-пауза F-скорость.",
            "G-точка, H-держать, R-отход, P-пауза.",
            "Герой: Z-сила X-броня C-скорость.",
            "Сапёр рвётся к лагерю (подкоп, x3).",
            "Ночь каждый 4-й: юниты с факелами.",
            "Боссы каждые 10. Всего 100 уровней.",
            "Победа: звёзды (без потерь, быстро).",
            "Замок пал — ЕЩЁ РАЗ. Удачи!",
        };
        for (int i = 0; i < help.Length; i++)
            sc.objects.Add(new LicObject { name = "h" + i, kind = "text", x = 0, y = -30 + i * 6.5f, color = "#DDDDDD", gravity = false, solid = false, text = help[i] });
        sc.objects.Add(new LicObject { name = "ui_next", kind = "text", x = -35, y = -24, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "flag", kind = "rect", x = 0, y = -200, w = 2, h = 8, color = "#FFD34D", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "ann", kind = "text", x = 0, y = 8, color = "#FFFFFF", gravity = false, solid = false });
        for (int i = 0; i < 50; i++)
            sc.objects.Add(new LicObject { name = "w" + i, kind = "rect", x = 0, y = -200, w = 1, h = 3, color = "#AACCFF", gravity = false, solid = false });
        // --- экран завершения: панель победы/поражения со звёздами и кнопкой дальше ---
        sc.objects.Add(new LicObject { name = "win_panel", kind = "rect", x = 0, y = -200, w = 100, h = 40, color = "#14141C", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "win_title", kind = "text", x = 0, y = -200, color = "#FFD34D", gravity = false, solid = false, text = "" });
        sc.objects.Add(new LicObject { name = "win_stars", kind = "text", x = 0, y = -200, color = "#FFE97A", gravity = false, solid = false, text = "" });
        sc.objects.Add(new LicObject { name = "win_info", kind = "text", x = 0, y = -200, color = "#FFFFFF", gravity = false, solid = false, text = "" });
        sc.objects.Add(new LicObject { name = "win_next", kind = "button", x = 0, y = -200, w = 44, h = 9, color = "#2A6A3A", gravity = false, solid = false });
        sc.objects.Add(new LicObject { name = "win_next_t", kind = "text", x = 0, y = -200, color = "#FFFFFF", gravity = false, solid = false, text = "" });
        p.scenes.Add(sc);
        p.scripts.Add(new LicScript { name = "knights.lua", lang = "lua", code = Lua() });
        p.assets.Add(new LicAsset { name = "hit.wav", type = "wav", base64 = Convert.ToBase64String(HitWav()) });
        p.assets.Add(new LicAsset { name = "fanfare.wav", type = "wav", base64 = Convert.ToBase64String(FanfareWav()) });
        p.assets.Add(new LicAsset { name = "fire.wav", type = "wav", base64 = Convert.ToBase64String(FireWav()) });
        p.assets.Add(new LicAsset { name = "heal.wav", type = "wav", base64 = Convert.ToBase64String(HealWav()) });
        return p;
    }

    static string Lua()
    {
        return @"
-- Рыцари v2: 1-8 юниты (8=сапёр-подкоп x3 по базе), ОГОНЬ/ЛЕЧ (мана), V-залп со стен,
-- ДОХОД/СТЕНА, U/I казармы. G-точка, H-держать, R-отход. 100 уровней, боссы каждые 10.
-- G-точка, H-держать, R-отход. Герой Сэр Роланд: Z/X/C. Боссы каждые 10. Ночь каждый 4-й ур. — факелы.
local CATS={
 {cost=20,hp=35,dmg=6,range=8,rate=0.7,spd=16,cd=1,col='#7A9AC8',w=8,h=13,look='squire'},
 {cost=50,hp=85,dmg=13,range=9,rate=0.9,spd=13,cd=2,col='#3E589E',w=8,h=13,look='sword'},
 {cost=120,hp=260,dmg=24,range=9,rate=1.4,spd=8,cd=4,col='#C8A028',w=9,h=14,look='pala'},
 {cost=90,hp=45,dmg=18,range=42,rate=1.6,spd=11,cd=3,col='#3A7A44',w=8,h=13,look='xbow'},
 {cost=35,hp=55,dmg=9,range=11,rate=0.8,spd=15,cd=1,col='#4A7A8A',w=8,h=13,look='spear'},
 {cost=70,hp=50,dmg=0,range=10,rate=1.2,spd=12,cd=2,col='#E8E0D0',w=8,h=13,look='monk',heal=true},
 {cost=150,hp=120,dmg=70,range=55,rate=3.0,spd=5,cd=5,col='#6A5A3A',w=11,h=12,look='cata',bonus=2.5},
 {cost=100,hp=70,dmg=10,range=8,rate=0.8,spd=19,cd=3,col='#8A6A3A',w=8,h=13,look='sapper',bonus=3},
}
local FOES={
 {hp=25,dmg=5,spd=15,prize=8,col='#8A6A4A',w=8,h=13,t=0,look='bandit'},
 {hp=60,dmg=9,spd=12,prize=14,col='#7A4A2A',w=9,h=10,t=20,look='dog'},
 {hp=160,dmg=16,spd=9,prize=30,col='#5A2A2A',w=10,h=12,t=60,look='boar'},
 {hp=500,dmg=30,spd=7,prize=150,col='#4A2A6A',w=11,h=16,t=999,look='king'},
 {hp=45,dmg=7,spd=18,prize=12,col='#6A5A3A',w=8,h=13,t=40,look='sapper'},
}
local BOSS={
 {hp=600,dmg=20,spd=14,prize=200,col='#8A2020',w=11,h=14,look='ram',skill='ram',nm='Таран'},
 {hp=400,dmg=8,spd=8,prize=200,col='#5A2A6A',w=10,h=14,look='king',skill='sum',nm='Призыватель'},
 {hp=900,dmg=40,spd=6,prize=300,col='#3A3A4A',w=14,h=18,look='king',skill='slam',splash=10,nm='Великан'},
}
local CHAP={
 'Зелёные поля','Тёмный лес','Речные броды','Пепельные холмы','Чёрная крепость',
}
-- биомы по главам: фон, погода (rain/snow/none), цвет земли-настроения
local BIO={
 {bg1='#8FC3E8',bg2='#D8EBD8',w='none'},
 {bg1='#5A7A5A',bg2='#2A4028',w='rain'},
 {bg1='#7AB8D8',bg2='#C8E8F0',w='none'},
 {bg1='#C8A878',bg2='#E8D8B0',w='none'},
 {bg1='#4A4448',bg2='#1A181C',w='none'},
}
local MAXLV=100
local page=0
local wins=0
local bank=0
local levelGold0=0
local volleyCd=0
local fireArmed=false
local DIFFN={'Рекрут','Ветеран','Легенда'}
local DIFFM={0.8,1.0,1.4}
local DIFFG={200,150,120}
local diffIdx=2
local level=1
local money=150
local startMoney=150
local rate=8
local mana=50
local inclvl=1
local walllvl=1
local php=500
local emax=900
local ehp=900
local time=0
local budget=0
local nextKing=90
local kills=0
local goldGot=0
local killsThis=0
local lostThis=0
local over=nil
local U={}
local E={}
local PT={}
local cds={0,0,0,0,0,0,0,0}
local keys17={false,false,false,false,false,false,false,false}
local rallyX=nil
local holdMode=false
local retreatT=0
local paused=false
local fast=false
local ulvl=1
local alvl=1
local killsThis=0
local lostThis=0
local night=false
local torchT=0
local warned=false
local hero=nil
local heroLvl=1
local heroXp=0
local heroWait=0
local heroChoice=false
local evT=15
local annT=0
local bossAlive=nil
local Wcache={}
local wmode='none'
local mode='menu' -- menu | levels | help | game
local maxopen=1
local function abs(x) return x<0 and -x or x end
local function mult() return (1+(level-1)*0.07)*DIFFM[diffIdx] end
local function chap() return CHAP[math.min(#CHAP,math.floor((level-1)/20)+1)] end
local function bio() return BIO[math.min(#BIO,math.floor((level-1)/20)+1)] end
function start()
  level=tonumber(api.load('knights_level') or '1') or 1
  if level<1 then level=1 end
  if level>MAXLV then level=MAXLV end
  maxopen=tonumber(api.load('knights_max') or '1') or 1
  if maxopen<1 then maxopen=1 end
  if maxopen>MAXLV then maxopen=MAXLV end
  diffIdx=tonumber(api.load('knights_diff') or '2') or 2
  if diffIdx<1 or diffIdx>3 then diffIdx=2 end
  wins=tonumber(api.load('knights_wins') or '0') or 0
  bank=tonumber(api.load('knights_bank') or '0') or 0
  for i=0,49 do Wcache[i]=api.find('w'..i) end
  applyBio()
  toMenu()
  api.log('knights: меню')
end
function hideAll()
  for _,n in ipairs({'panel','title','subtitle','stats','m_play','m_playt','m_levels','m_levelst','m_help','m_helpt','m_diff','m_difft','m_random','m_random_t','m_wipe','m_wipe_t'}) do
    local o=api.find(n)
    if o then o.y=-200 end
  end
  for i=1,50 do
    local a=api.find('lv'..i)
    local b=api.find('lv'..i..'t')
    if a then a.y=-200 end
    if b then b.y=-200 end
  end
  local lb=api.find('lv_back')
  local lbt=api.find('lv_back_t')
  if lb then lb.y=-200 end
  if lbt then lbt.y=-200 end
  local pg=api.find('lv_page')
  local pgt=api.find('lv_page_t')
  if pg then pg.y=-200 end
  if pgt then pgt.y=-200 end
  for i=0,9 do
    local h=api.find('h'..i)
    if h then h.y=-200 end
  end
end
function clearField()
  -- убрать всё боевое мгновенно: трупы/частицы не висят поверх меню
  for _,u in ipairs(U) do u.o.destroy() end
  for _,f in ipairs(E) do f.o.destroy() end
  for _,p in ipairs(PT) do p.o.destroy() end
  U={}
  E={}
  PT={}
  pcall(function() api.purge() end)
  for i=0,49 do
    local o=Wcache[i]
    if o then o.y=-200 end
  end
  local fl=api.find('flag')
  if fl then fl.y=-200 end
  api.find('ann').text=''
  annT=0
  api.find('ui_next').text=''
end
function showObj(n,x,y)
  local o=api.find(n)
  if o then o.x=x o.y=y end
end
function hideEnd()
  for _,n in ipairs({'win_panel','win_title','win_stars','win_info','win_next','win_next_t'}) do
    local o=api.find(n)
    if o then o.y=-200 end
  end
end
function toMenu()
  mode='menu'
  resetBoard()
  clearField()
  hideAll()
  hideEnd()
  showObj('panel',0,-10)
  showObj('title',0,-44)
  api.find('title').text='РЫЦАРИ'
  showObj('subtitle',0,-34)
  api.find('subtitle').text='оборона замка • 100 уровней • v7'
  showObj('stats',0,16)
  api.find('stats').text='Побед: '..wins..' • Казна: $'..bank..' • Открыто: '..maxopen..'/'..MAXLV
  showObj('m_play',-33,-22) showObj('m_playt',-33,-22)
  showObj('m_levels',33,-22) showObj('m_levelst',33,-22)
  showObj('m_help',-33,-8) showObj('m_helpt',-33,-8)
  showObj('m_diff',33,-8)
  api.find('m_difft').x=33 api.find('m_difft').y=-8
  api.find('m_difft').text=DIFFN[diffIdx]
  showObj('m_random',0,5) showObj('m_random_t',0,5)
  showObj('m_wipe',0,30) showObj('m_wipe_t',0,30)
end
function toLevels()
  mode='levels'
  clearField()
  hideAll()
  showObj('panel',0,-10)
  showObj('title',0,-44)
  api.find('title').text='УРОВНИ '..(page*50+1)..'-'..(page*50+50)
  for i=1,50 do
    local L=page*50+i
    local col=(i-1)%10
    local row=math.floor((i-1)/10)
    local a=api.find('lv'..i)
    local b=api.find('lv'..i..'t')
    if a then
      a.x=-63+col*14 a.y=-30+row*12
      if L<maxopen then a.color='#3E589E'
      elseif L==maxopen then a.color='#C8A028'
      else a.color='#33333C' end
    end
    if b then
      b.x=-63+col*14 b.y=-30+row*12
      b.text=''..L
      local st3=tonumber(api.load('knights_stars_'..L) or '0') or 0
      if L>maxopen then b.color='#666666'
      elseif st3==3 then b.color='#FFD34D'
      elseif L<=maxopen then b.color='#FFFFFF'
      else b.color='#666666' end
    end
  end
  showObj('lv_back',-14,34) showObj('lv_back_t',-14,34)
  showObj('lv_page',32,34) showObj('lv_page_t',32,34)
  api.find('lv_page_t').text='СТР '..(page+1)
end
function toHelp()
  mode='help'
  clearField()
  hideAll()
  showObj('panel',0,-10)
  showObj('title',0,-44)
  api.find('title').text='ПОМОЩЬ'
  for i=0,8 do showObj('h'..i,0,-28+i*7) end
  showObj('lv_back',0,34) showObj('lv_back_t',0,34)
end
function toGame(n)
  level=n
  startMoney=DIFFG[diffIdx]+(level-1)*20
  money=startMoney
  levelGold0=goldGot
  emax=900+(level-1)*60
  ehp=emax
  mode='game'
  hideAll()
  api.find('title').text='РЫЦАРИ'
  resetBoard()
  applyBio()
  ann('Уровень '..level..': '..chap(),4)
  api.log('игра! ур.'..level)
end
function applyBio()
  night=(level%4==0)
  if night then
    if api.bg then api.bg('#1A2030','#0A0C14') end
    wmode='none'
    for i=0,49 do
      local o=Wcache[i]
      if o then o.y=-200 end
    end
    api.log('ночь! юниты с факелами')
    return
  end
  local b=bio()
  if api.bg then api.bg(b.bg1,b.bg2) end
  wmode=b.w
  local col=wmode=='snow' and '#FFFFFF' or '#AACCFF'
  for i=0,49 do
    local o=Wcache[i]
    if o then
      o.color=col
      if wmode=='snow' then o.w=2 o.h=2 else o.w=1 o.h=4 end
    end
  end
  api.log('биом: '..chap())
end
function weather(dt)
  if wmode=='none' then
    for i=0,49 do
      local o=Wcache[i]
      if o then o.y=-200 end
    end
    return
  end
  local rain=wmode=='rain'
  for i=0,49 do
    local o=Wcache[i]
    if o then
      if o.y<-100 then
        o.x=api.rand(-80,80)
        o.y=api.rand(-70,-50)
      end
      o.y=o.y+dt*(rain and 55 or 12)
      o.x=o.x+dt*(rain and -14 or 4)
      if o.y>45 then o.y=-60 o.x=api.rand(-80,80) end
    end
  end
end
function ann(s,d)
  api.find('ann').text=s
  annT=d or 3
end
-- звёзды победы: 1 за победу, +1 без потерь, +1 за скорость (пар 60+2с/ур.)
function starRating()
  local s=1
  if lostThis==0 then s=s+1 end
  if time<(60+level*2) then s=s+1 end
  return s
end
-- экран завершения: панель, звёзды, статистика, кнопка ДАЛЕЕ/ЕЩЁ РАЗ
function showEnd(win)
  local t=math.floor(time)
  local gain=goldGot-levelGold0
  if win then
    local stars=starRating()
    local key='knights_stars_'..level
    local best=tonumber(api.load(key) or '0') or 0
    if stars>best then api.save(key,tostring(stars)) end
    if stars==3 then money=money+50 gain=gain+50 end
    api.find('win_title').text='ПОБЕДА!'
    api.find('win_stars').text=stars==3 and '***' or stars==2 and '**' or '*'
    api.find('win_info').text='Фраги '..killsThis..' Потери '..lostThis..' Время '..t..'с +$'..gain
    api.find('win_next_t').text='ДАЛЕЕ'
    api.log('win! '..stars..' звезды, фраги '..killsThis..', потери '..lostThis..', время '..t..'с (ДАЛЕЕ/пробел)')
  else
    api.find('win_title').text='ЗАМОК ПАЛ'
    api.find('win_stars').text=''
    api.find('win_info').text='Фраги '..killsThis..' Потери '..lostThis..' Время '..t..'с'
    api.find('win_next_t').text='ЕЩЁ РАЗ'
    api.log('lose... фраги '..killsThis..', потери '..lostThis..' (ЕЩЁ РАЗ/пробел)')
  end
  showObj('win_panel',0,-4)
  showObj('win_title',0,-20)
  showObj('win_stars',0,-11)
  showObj('win_info',0,-2)
  showObj('win_next',0,9) showObj('win_next_t',0,9)
end
-- шаг дальше с экрана завершения (пробел или кнопка)
function overAdvance()
  hideEnd()
  if over=='win' then
    if level>=MAXLV then
      api.log('Кампания пройдена! Ты легенда.')
      ann('КАМПАНИЯ ПРОЙДЕНА!',6)
      toMenu()
    else
      level=level+1
      if level>maxopen then maxopen=level api.save('knights_max',tostring(maxopen)) end
      api.save('knights_level',tostring(level))
      startMoney=DIFFG[diffIdx]+(level-1)*20
      emax=900+(level-1)*60
      inclvl=1 walllvl=1 rate=8
      resetBoard()
      applyBio()
      api.play('fanfare.wav')
      api.log('Уровень '..level..': '..chap())
    end
  else
    resetBoard()
    api.log('Ещё раз! Уровень '..level)
  end
end
function nextInfo()
  if bossAlive then return 'босс!' end
  if level%10==0 and time<15 then return 'босс '..math.max(0,math.floor(15-time))..'с' end
  local rate=(6+time/25)*(1+level*0.05)
  local names={'крысы','псы','кабаны'}
  local times={0,20,60}
  local prizes={FOES[1].prize,FOES[2].prize,FOES[3].prize}
  if level>=5 then names[4]='сапёры' times[4]=40 prizes[4]=FOES[5].prize end
  local bestT=1e9
  local bestN='тихо...'
  for k=1,#names do
    if time>=times[k] then
      local need=prizes[k]*3-budget
      local t=need<=0 and 0 or need/math.max(1,rate)
      if t<bestT then bestT=t bestN=names[k] end
    end
  end
  return bestN..' '..math.floor(bestT)..'с'
end
function menuClicks()
  local t=api.find('title')
  if t and mode=='menu' then t.y=-44+math.sin(api.time()*2)*1.5 end
  if not api.click() then return end
  local nm=api.pick()
  if nm=='m_play' then
    local lv=math.min(level,MAXLV)
    toGame(lv)
    api.log('игра! ур.'..lv)
  elseif nm=='m_levels' then toLevels()
  elseif nm=='m_help' then toHelp()
  elseif nm=='m_diff' then
    diffIdx=diffIdx%3+1
    api.save('knights_diff',tostring(diffIdx))
    api.find('m_difft').text=DIFFN[diffIdx]
    api.log('сложность: '..DIFFN[diffIdx])
  elseif nm=='m_random' then
    local lv=math.max(1,math.min(maxopen,math.floor(api.rand(1,maxopen+1))))
    toGame(lv)
    api.log('игра! ур.'..lv)
  elseif nm=='m_wipe' then
    level=1 maxopen=1 page=0 wins=0 bank=0 goldGot=0
    api.save('knights_level','1')
    api.save('knights_max','1')
    api.save('knights_wins','0')
    api.save('knights_bank','0')
    toMenu()
    api.log('прогресс сброшен')
  end
end
function levelClicks()
  if not api.click() then return end
  local nm=api.pick()
  if nm=='lv_back' then toMenu() return end
  if nm=='lv_page' then page=1-page toLevels() return end
  if nm:sub(1,2)=='lv' then
    local i=tonumber(nm:sub(3))
    local L=i and (page*50+i) or nil
    if L and L>=1 and L<=MAXLV and L<=maxopen then
      toGame(L)
      api.log('игра! ур.'..L)
    elseif L and L>maxopen then
      api.log('закрыто: пройди уровень '..maxopen)
    end
  end
end
function puff(x,y,col,n,spd,life,sz)
  if #PT>120 then return end
  for i=1,n do
    local a=api.rand(0,6.28)
    local o=api.spawn('rect',{name='pt',x=x,y=y,w=sz,h=sz,color=col,gravity=false,solid=false})
    PT[#PT+1]={o=o,vx=api.rand(-1,1)*spd,vy=api.rand(-1,0.3)*spd,life=life*api.rand(0.6,1.2)}
  end
end
function updPT(dt)
  for i=#PT,1,-1 do
    local p=PT[i]
    p.life=p.life-dt
    if p.life<=0 then p.o.destroy() table.remove(PT,i)
    else p.o.x=p.o.x+p.vx*dt p.o.y=p.o.y+p.vy*dt end
  end
end
function deploy(i)
  local c=CATS[i]
  if over or not c then return end
  if cds[i]>0 then return end
  local n=0
  for _,u in ipairs(U) do n=n+1 end
  if n>=12 then api.log('лимит воинов!') return end
  if money<c.cost then api.log('нужно $'..c.cost) return end
  money=money-c.cost
  cds[i]=c.cd
  local o=api.spawn('knight',{name='kat',x=-62,y=-c.h/2,w=c.w,h=c.h,color=c.col,gravity=false,solid=false,sprite=c.look})
  U[#U+1]={o=o,hp=c.hp,maxhp=c.hp,dmg=c.dmg,range=c.range,rate=c.rate,cd=0,spd=c.spd,heal=c.heal,bonus=c.bonus or 1}
  if (c.bonus or 1)>2 then api.log('Сапёр — подкоп под стены!')
  else
    local cries={'За замок!','Вперёд!','Держать строй!','Сталь звенит!','За Роланда!'}
    api.log(cries[math.floor(api.rand(1,6))])
  end
end
function spawnHero()
  local o=api.spawn('knight',{name='hero',x=-62,y=-6.5,w=9,h=13,color='#D4A017',gravity=false,solid=false,sprite='hero'})
  hero={o=o,hp=300,maxhp=300,dmg=20,range=10,rate=0.8,cd=0,spd=14}
  U[#U+1]=hero
  heroLvl=1 heroXp=0
  api.log('Сэр Роланд в строю!')
end
function foeSpawn(kind)
  local f=FOES[kind]
  local m=mult()
  local o=api.spawn('knight',{name='foe',x=62,y=-f.h/2,w=f.w,h=f.h,color=f.col,gravity=false,solid=false,sprite=f.look})
  E[#E+1]={o=o,hp=f.hp*m,dmg=f.dmg*m,spd=f.spd*(night and 0.9 or 1),prize=f.prize,rate=1.0,cd=0,range=9} -- фишка: ночью враги медленнее
end
function bossSpawn()
  local b=BOSS[((math.floor(level/10)-1)%3)+1]
  local m=mult()
  local o=api.spawn('knight',{name='boss',x=62,y=-f0(b),w=b.w,h=b.h,color=b.col,gravity=false,solid=false,sprite='king'})
  bossAlive={o=o,hp=b.hp*m,dmg=b.dmg*m,spd=b.spd,prize=b.prize,rate=1.2,cd=0,range=11,skill=b.skill,splash=b.splash,t=0,nm=b.nm}
  E[#E+1]=bossAlive
  ann('БОСС: '..b.nm..'!',4)
  api.log('boss '..b.nm)
  api.play('fanfare.wav')
end
function f0(b) return -b.h/2 end
function director(dt)
  budget=budget+dt*(6+time/25)*(1+level*0.05)
  if level%10==0 and bossAlive==nil and time>15 then
    bossSpawn()
    return
  end
  if time>=nextKing and level%10~=0 then
    nextKing=nextKing+120
    budget=0
    foeSpawn(4)
    api.log('КОРОЛЬ БАНДИТОВ идёт!')
    return
  end
  local changed=true
  while changed and budget>0 do
    changed=false
    for k=5,1,-1 do
      if k==4 then
        -- король только по таймеру nextKing
      else
        local f=FOES[k]
        if time>=f.t and budget>=f.prize*3 then
          budget=budget-f.prize*3
          foeSpawn(k)
          changed=true
          break
        end
      end
    end
  end
end
function hurtFx(x,y)
  puff(x,y,'#C02020',3,40,0.4,2)
end
function fightSide(units, foes, dir, baseX, dt, isMine)
  for _,u in ipairs(units) do
    local tgt=nil
    local bd=u.range+1
    for _,f in ipairs(foes) do
      local d=abs(u.o.x-f.o.x)
      if d<u.range and d<bd then bd=d tgt=f end
    end
    local bdist=abs(u.o.x-baseX)
    local goBase=bdist<u.range+6 and (tgt==nil or bdist<bd)
    if holdMode and isMine and tgt==nil then u.o.vx=0
    elseif retreatT>0 and isMine then u.o.vx=-6*dir
    elseif goBase then
      u.o.vx=0
      u.cd=u.cd-dt
      if u.cd<=0 then
        u.cd=u.rate
        u.o.attack=0.3
        return {base=true,dmg=u.dmg*(u.bonus or 1)*(units==U and (1+0.15*ulvl) or 1)}
      end
    elseif tgt then
      u.o.vx=0
      u.cd=u.cd-dt
      if u.cd<=0 then
        u.cd=u.rate
        u.o.attack=0.3
        if u.heal then
          local low=nil
          for _,a in ipairs(units) do
            if a.hp<a.maxhp and (low==nil or a.hp<low.hp) then low=a end
          end
          if low then low.hp=math.min(low.maxhp,low.hp+25) puff(low.o.x,low.o.y-8,'#40C060',4,20,0.5,2) end
        else
          local mult=(units==U) and (1+0.15*ulvl) or 1
          if foes==U then mult=mult*(1-0.08*alvl) end
          if units==U and hero then mult=mult*1.05 end -- фишка: аура Роланда +5%
          local crit=api.rand(0,1)<0.10 -- фишка: крит 10% x2
          tgt.hp=tgt.hp-u.dmg*mult*(crit and 2 or 1)
          tgt.o.num=tgt.hp
          if tgt.o.hit then tgt.o.hit() end
          if crit then puff(tgt.o.x,tgt.o.y-10,'#FFE97A',4,60,0.4,2) end
          hurtFx(tgt.o.x,tgt.o.y-6)
          if u.splash then
            for _,f2 in ipairs(foes) do
              if f2~=tgt and abs(f2.o.x-tgt.o.x)<u.splash then
                f2.hp=f2.hp-u.dmg*0.5
                f2.o.num=f2.hp
                if f2.o.hit then f2.o.hit() end
              end
            end
          end
        end
      end
    else
      local goal=(rallyX and isMine) and rallyX or nil
      if goal and ((dir>0 and u.o.x<goal) or (dir<0 and u.o.x>goal)) then
        u.o.vx=dir*u.spd
      elseif goal==nil then
        u.o.vx=dir*u.spd
      else u.o.vx=0 end
    end
  end
  return nil
end
function sweep(units, prizeMe)
  for i=#units,1,-1 do
    local u=units[i]
    if u.hp<=0 then
      if prizeMe then
        money=money+u.prize
        goldGot=goldGot+u.prize
        kills=kills+1
        killsThis=killsThis+1
        mana=math.min(100,mana+2) -- фишка: мана за фраг
        api.play('hit.wav')
        puff(u.o.x,u.o.y,'#A02020',8,60,0.6,2)
        if kills%10==0 then api.log('фраги: '..kills) end
        if killsThis%10==0 then money=money+30 goldGot=goldGot+30 api.log('серия! +$30') end -- фишка: серия
        if hero then heroXp=heroXp+1 checkHero() end
      else
        lostThis=lostThis+1
        puff(u.o.x,u.o.y,'#804020',6,50,0.5,2)
      end
      if u==hero then
        hero=nil heroWait=10
        api.log('Роланд пал... вернётся через 10с')
      end
      if u==bossAlive then bossAlive=nil end
      u.o.destroy()
      table.remove(units,i)
    else
      u.o.num=u.hp
    end
  end
end
function checkHero()
  if not hero then return end
  if heroXp>=heroLvl*5 then
    heroXp=0
    heroChoice=true
    ann('Роланд ур.'..(heroLvl+1)..': Z-сила X-броня C-скорость',6)
    api.play('fanfare.wav')
  end
end
function heroLvlUp(kind)
  heroLvl=heroLvl+1
  heroChoice=false
  if kind=='z' then hero.dmg=hero.dmg+8 api.log('сила! урон '..hero.dmg)
  elseif kind=='x' then hero.maxhp=hero.maxhp+80 hero.hp=hero.maxhp api.log('броня! хп '..hero.maxhp)
  else hero.spd=hero.spd+4 hero.rate=math.max(0.4,hero.rate-0.1) api.log('скорость!') end
end
function bossSkill(dt)
  for _,f in ipairs(E) do
    if f.skill=='ram' then
      f.t=(f.t or 0)+dt
      if f.t>6 then f.t=0 f.ramT=1 api.log('Таран разгоняется!') end
      if f.ramT and f.ramT>0 then f.ramT=f.ramT-dt f.o.vx=(f.o.x<0 and 1 or -1)*f.spd*3 end
    elseif f.skill=='sum' then
      f.t=(f.t or 0)+dt
      if f.t>8 and #E<20 then
        f.t=0
        foeSpawn(1) foeSpawn(1)
        api.log('Призыватель зовёт крыс!')
      end
    elseif f.skill=='slam' then
      -- удар по площади — в момент обычной атаки бьёт всех рядом
    end
  end
end
function economy(dt)
  evT=evT-dt
  if evT>0 then return end
  evT=api.rand(20,30)
  local r=api.rand(0,1)
  if r<0.35 then
    local g=math.floor(60+level*8)
    money=money+g goldGot=goldGot+g
    ann('Налоги! +$'..g,3) api.log('налоги +$'..g)
  elseif r<0.6 then
    for _,u in ipairs(U) do u.hp=math.max(1,u.hp-20) end
    ann('Чума! -20 всем',3) api.log('чума!')
  elseif r<0.85 then
    money=money+50
    local o=api.spawn('knight',{name='kat',x=-62,y=-6.5,w=8,h=13,color='#3E589E',gravity=false,solid=false,sprite='sword'})
    U[#U+1]={o=o,hp=85,maxhp=85,dmg=13,range=9,rate=0.9,cd=0,spd=13}
    ann('Наёмник вступил!',3) api.log('наёмник!')
  else
    money=math.max(0,money-60)
    ann('Крысы в амбаре! -$60',3) api.log('крысы!')
  end
end
function castVolley()
  if volleyCd>0 then api.log('заливается... '..math.ceil(volleyCd)..'с') return end
  if mana<50 then api.log('мало маны') return end
  mana=mana-50
  volleyCd=25
  local n=0
  for _,f in ipairs(E) do
    if n>=6 then break end
    f.hp=f.hp-50
    f.o.num=f.hp
    if f.o.hit then f.o.hit() end
    hurtFx(f.o.x,f.o.y-6)
    puff(f.o.x,f.o.y-10,'#D8D8D8',3,60,0.4,1)
    n=n+1
  end
  puff(-62,-14,'#D8D8D8',10,120,0.6,2)
  api.play('hit.wav')
  api.log('Залп со стен! целей: '..n)
end
function buyAtk()
  local c=120*ulvl
  if money>=c then money=money-c ulvl=ulvl+1 api.log('урон +'..ulvl) else api.log('нужно $'..c) end
end
function buyDef()
  local c=120*alvl
  if money>=c then money=money-c alvl=alvl+1 api.log('броня '..alvl) else api.log('нужно $'..c) end
end
function castFire(mx,my)
  if mana<30 then api.log('мало маны') return end
  mana=mana-30
  puff(mx,my,'#E85820',14,120,0.7,3)
  puff(mx,my,'#FFD97A',8,80,0.5,2)
  api.play('fire.wav')
  for _,f in ipairs(E) do
    local d=abs(f.o.x-mx)+abs(f.o.y-my)*0.5
    if d<14 then f.hp=f.hp-60*(wmode=='rain' and 0.8 or 1) f.o.num=f.hp f.o.hit() end -- фишка: дождь гасит огонь
  end
  api.log('огонь!')
end
function castHeal()
  if mana<40 then api.log('мало маны') return end
  mana=mana-40
  for _,u in ipairs(U) do
    u.hp=math.min(u.maxhp or 9999,u.hp+80)
    puff(u.o.x,u.o.y-8,'#40C060',4,25,0.6,2)
  end
  api.play('heal.wav')
  api.log('лечение!')
end
function refresh()
  api.find('ui_money').text='$'..math.floor(money)..' +$'..rate..'/с У'..ulvl..'Б'..alvl..' м'..math.floor(mana)
  api.find('ui_time').text='Ур.'..level..' • '..math.floor(time)
  api.find('ui_base').text='Замок '..math.floor(math.max(0,php))..' • Лагерь '..math.floor(math.max(0,ehp))
  local b=api.find('base_p')
  b.num=php
  pcall(function() b.maxnum=900 end)
  local be=api.find('base_e')
  be.num=ehp
  pcall(function() be.maxnum=emax end)
  api.find('gm').num=kills
  api.find('btn_pause_t').text=paused and '▶' or 'ПАУЗА'
  api.find('btn_fast_t').text=fast and '×2' or '>>'
  local fl=api.find('flag')
  if retreatT>0 then fl.color='#C03A3A' fl.x=-60 fl.y=-10
  elseif holdMode then fl.color='#3E8ABE' fl.x=-60 fl.y=-10
  elseif rallyX then fl.color='#FFD34D' fl.x=rallyX fl.y=-10
  else fl.y=-200 end
  if paused then api.find('ui_next').text='II ПАУЗА (P)'
  else api.find('ui_next').text='След: '..nextInfo() end
  -- подсветка: тёмная кнопка = нельзя использовать
  for i=1,8 do
    local b=api.find('btn_u'..i)
    if b then
      local c=CATS[i]
      b.color=(money>=c.cost and cds[i]<=0) and '#3E589E' or '#3A3A44'
    end
  end
  local BC={btn_fire='#8A3A1A',btn_heal='#2A6A4A',btn_inc='#2A5A3A',btn_wall='#6A6A7A',btn_volley='#5A3A6A'}
  local ok={btn_fire=mana>=30,btn_heal=mana>=40,btn_volley=(mana>=50 and volleyCd<=0),btn_inc=(money>=100*inclvl),btn_wall=(money>=80*walllvl)}
  for n,col in pairs(BC) do
    local b=api.find(n)
    if b then b.color=ok[n] and col or '#3A3A44' end
  end
  api.find('btn_volley_t').text=volleyCd>0 and (math.ceil(volleyCd)..'с') or 'ЗАЛП 50'
  api.find('btn_inc_t').text='ДОХОД '..inclvl
  api.find('btn_wall_t').text='СТЕНА '..walllvl
  if night then api.find('ui_time').text='Ур.'..level..' • НОЧЬ '..math.floor(time) end
  if annT>0 then annT=annT-0.016 else api.find('ann').text='' end
end
function resetBoard()
  for _,u in ipairs(U) do u.o.destroy() end
  for _,f in ipairs(E) do f.o.destroy() end
  for _,p in ipairs(PT) do p.o.destroy() end
  U={}
  E={}
  PT={}
  pcall(function() api.purge() end)
  local fl0=api.find('flag')
  if fl0 then fl0.y=-200 end
  money=startMoney
  php=500
  ehp=emax
  time=0
  killsThis=0
  lostThis=0
  budget=0
  nextKing=90
  cds={0,0,0,0,0,0,0,0}
  bossAlive=nil
  hero=nil heroWait=0 heroLvl=1 heroXp=0 heroChoice=false
  rallyX=nil holdMode=false retreatT=0
  volleyCd=0
  fireArmed=false
  warned=false
  hideEnd()
  paused=false
  ulvl=1 alvl=1
  mana=50
  over=nil
end
function dostep(dt)
  time=time+dt
  money=money+rate*dt
  mana=math.min(100,mana+5*dt)
  weather(dt)
  if night then
    torchT=torchT-dt
    if torchT<=0 then
      torchT=0.15
      for _,u in ipairs(U) do
        if api.rand(0,1)<0.5 then puff(u.o.x+api.rand(-2,2),u.o.y-10,'#FFD97A',1,8,0.3,1) end
      end
      for _,f in ipairs(E) do
        if api.rand(0,1)<0.3 then puff(f.o.x+api.rand(-2,2),f.o.y-10,'#FF9A4A',1,8,0.3,1) end
      end
    end
  end
  for i=1,8 do if cds[i]>0 then cds[i]=cds[i]-dt end end
  if volleyCd>0 then volleyCd=volleyCd-dt end
  if retreatT>0 then retreatT=retreatT-dt end
  if heroWait>0 then
    heroWait=heroWait-dt
    if heroWait<=0 then spawnHero() end
  end
  if hero==nil and heroWait<=0 and time>2 then spawnHero() end
  director(dt)
  bossSkill(dt)
  economy(dt)
  local hit=fightSide(U,E,1,70,dt)
  if hit then
    ehp=ehp-hit.dmg
    if ehp<=0 and over==nil then
      over='win'
      money=money+100+level*10
      goldGot=goldGot+100+level*10
      wins=wins+1
      bank=bank+(goldGot-levelGold0)
      api.save('knights_wins',tostring(wins))
      api.save('knights_bank',tostring(bank))
      api.play('fanfare.wav')
      showEnd(true)
    end
  end
  local hit2=fightSide(E,U,-1,-70,dt)
  if hit2 then
    -- фишка: каждый уровень СТЕНЫ режет урон по замку на 5% (до -50%)
    php=php-hit2.dmg*(1-math.min(0.5,0.05*walllvl))
    if php<=0 and over==nil then
      over='lose'
      showEnd(false)
    end
  end
  -- фишка: замок чинится +1/с до 500, предупреждение о пожаре один раз
  if php<500 then php=math.min(500,php+1*dt) end
  if php<125 and not warned then warned=true ann('Замок горит! Чини СТЕНУ!',4) api.log('замок горит!') end
  sweep(E,true)
  sweep(U,false)
  updPT(dt)
end
function update(dt)
  if mode=='menu' then menuClicks() return end
  if mode=='levels' then levelClicks() return end
  if mode=='help' then
    if api.click() and api.pick()=='lv_back' then toMenu() end
    return
  end
  refresh()
  if api.click() then
    local nm0=api.pick()
    if nm0=='btn_pause' then paused=not paused api.log(paused and 'пауза' or 'вперёд!') end
    if nm0=='btn_fast' then fast=not fast api.log(fast and 'скорость ×2' or 'скорость ×1') end
  end
  do
    local pk=api.key('p')
    if pk and not keys17[10] then paused=not paused api.log(paused and 'пауза' or 'вперёд!') end
    keys17[10]=pk
  end
  if over then
    if api.click() then
      local nmover=api.pick()
      if nmover=='btn_menu' then
        api.save('knights_level',tostring(level))
        resetBoard()
        toMenu()
        pspace=api.key('space')
        return
      elseif nmover=='win_next' then
        overAdvance()
        pspace=api.key('space')
        return
      end
    end
    local s=api.key('space')
    if s and not pspace then overAdvance() end
    pspace=s
    return
  end
  -- ввод (кнопки и клавиши) работает и в паузе — стоит только симуляция
  if not paused then
    for rep=1,(fast and 2 or 1) do
      if over then break end
      dostep(dt)
    end
  end
  if heroChoice then
    if api.key('z') then heroLvlUp('z') end
    if api.key('x') then heroLvlUp('x') end
    if api.key('c') then heroLvlUp('c') end
  end
  if api.click() then
    local nm=api.pick()
    local mx0,my0=api.mousex(),api.mousey()
    if fireArmed and nm~='btn_fire' and nm:sub(1,4)~='btn_' and nm:sub(1,4)~='win_' then
      fireArmed=false
      castFire(mx0,my0)
    elseif nm=='btn_u1' then fireArmed=false deploy(1)
    elseif nm=='btn_u2' then fireArmed=false deploy(2)
    elseif nm=='btn_u3' then fireArmed=false deploy(3)
    elseif nm=='btn_u4' then fireArmed=false deploy(4)
    elseif nm=='btn_u5' then fireArmed=false deploy(5)
    elseif nm=='btn_u6' then fireArmed=false deploy(6)
    elseif nm=='btn_u7' then fireArmed=false deploy(7)
    elseif nm=='btn_u8' then fireArmed=false deploy(8)
    elseif nm=='btn_fire' then
      if mana<30 then api.log('мало маны') fireArmed=false
      else fireArmed=not fireArmed ann(fireArmed and 'Огонь: кликни цель!' or 'Огонь отменён',3) end
    elseif nm=='btn_heal' then castHeal()
    elseif nm=='btn_volley' then castVolley()
    elseif nm=='btn_inc' then
      local c=100*inclvl
      if money>=c then money=money-c inclvl=inclvl+1 rate=rate+4 api.log('доход +$'..rate..'/с') else api.log('нужно $'..c) end
    elseif nm=='btn_wall' then
      local c=80*walllvl
      if money>=c then money=money-c walllvl=walllvl+1 php=math.min(900,php+150) api.log('стена! замок '..math.floor(php)) else api.log('нужно $'..c) end
    elseif nm=='btn_menu' then
      api.save('knights_level',tostring(level))
      resetBoard()
      toMenu()
    end
  end
  for i=1,8 do
    local k=api.key(tostring(i))
    if k and not keys17[i] then deploy(i) end
    keys17[i]=k
  end
  do
    local gk=api.key('g')
    if gk and not keys17[0] and api.mousex()<30 then
      rallyX=api.mousex()
      api.log('точка сбора!')
    end
    keys17[0]=gk
    local hk=api.key('h')
    if hk and not keys17[8] then holdMode=not holdMode api.log(holdMode and 'держать!' or 'вперёд!') end
    keys17[8]=hk
    local rk=api.key('r')
    if rk and not keys17[9] then retreatT=4 api.log('отход!') end
    keys17[9]=rk
    local vk=api.key('v')
    if vk and not keys17[13] then castVolley() end
    keys17[13]=vk
    local sk=api.key('space')
    if sk and not keys17[14] then paused=not paused api.log(paused and 'пауза' or 'вперёд!') end
    keys17[14]=sk
    local fk=api.key('f')
    if fk and not keys17[15] then fast=not fast api.log(fast and 'скорость ×2' or 'скорость ×1') end
    keys17[15]=fk
    local uk=api.key('u')
    if uk and not keys17[11] then buyAtk() end
    keys17[11]=uk
    local ik=api.key('i')
    if ik and not keys17[12] then buyDef() end
    keys17[12]=ik
  end
end
";
    }
}
