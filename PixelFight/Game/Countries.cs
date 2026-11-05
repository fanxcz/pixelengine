using System.Drawing;

namespace PixelFight;

// Страны: контуры скачаны с GitHub (johan/world.geo.json), упрощены и запечены.
// У каждой — свой арсенал, арена, кампания из 3 этапов.
enum Theme { Japan, Germany, Rus, France, England, Horde, Custom }

struct StageC
{
    public string ru, en;
    public Weapon w;
    public float hp, aggr;
}

class CountryInfo
{
    public string ru, en;
    public string[] wru = new string[10]; // имена оружия по Weapon
    public string[] wen = new string[10];
    public Color bladeHi, bladeLo, trim;  // оттенки клинков
    public int[] map;                     // контур x,y,... (бокс 200xH)
    public int mapH;
    public StageC[] stgs;
}

static class Countries
{
    static readonly int[] MAP_JAPAN = { 185,68,179,81,182,89,173,100,153,107,125,108,102,127,91,120,91,108,63,112,44,120,25,120,41,132,31,159,20,166,13,159,17,145,6,140,0,129,15,124,24,114,40,106,51,95,83,90,100,94,117,65,128,73,151,57,160,51,170,31,168,13,174,3,191,0,200,22,200,35,185,51,185,68 };
    static readonly int[] MAP_GERMANY = { 87,0,88,9,110,14,110,22,132,17,145,11,170,20,180,27,185,38,179,44,187,52,193,64,191,72,200,86,190,88,184,86,179,90,163,94,155,100,138,104,142,111,145,120,156,126,169,135,161,145,153,148,156,163,154,166,147,162,136,161,120,165,101,164,98,170,87,164,80,165,56,158,52,163,33,163,36,147,47,132,15,128,4,122,6,113,1,108,4,93,0,69,13,69,19,61,24,41,20,33,25,29,43,27,47,32,62,21,57,13,56,0,73,3,87,0 };
    static readonly int[] MAP_RUSSIA = { 104,1,110,1,113,3,107,4,112,4,116,5,126,6,130,5,132,7,137,7,144,8,146,6,161,8,172,9,175,10,182,10,188,11,192,10,200,11,198,17,199,19,199,20,192,21,187,23,181,23,178,24,178,26,176,29,174,32,170,35,168,29,170,26,176,22,178,20,173,20,167,24,162,23,155,24,141,30,145,31,149,32,148,36,145,41,141,45,137,45,135,46,136,44,139,42,140,39,138,39,134,37,130,34,128,32,123,32,122,33,120,36,115,36,112,36,107,37,104,36,100,36,96,34,92,35,90,36,86,35,80,37,78,36,75,35,71,35,66,31,62,31,59,30,55,29,49,30,45,32,43,33,43,35,38,35,33,34,28,36,26,37,27,39,28,41,25,43,28,47,26,47,24,46,22,46,18,45,15,43,13,42,15,40,14,39,16,38,17,36,13,35,10,35,9,34,7,33,6,33,5,32,7,31,5,30,5,29,2,28,0,26,0,24,2,23,5,20,4,17,2,14,2,11,6,10,17,12,17,15,8,14,10,17,13,17,16,17,19,14,22,14,21,12,24,13,27,14,34,11,36,12,41,11,43,10,49,11,53,11,52,10,54,7,59,6,59,8,61,12,59,15,61,13,62,11,62,9,62,6,63,8,66,7,70,6,75,5,78,3,86,2,91,2,96,1,103,0 };
    static readonly int[] MAP_FRANCE = { 60,20,90,5,130,0,160,15,175,45,165,80,140,110,110,130,80,125,55,100,40,70,45,40 };
    static readonly int[] MAP_ENGLAND = { 120,10,150,0,170,15,175,45,160,80,140,110,120,105,105,80,100,50,110,25 };
    static readonly int[] MAP_HORDE = { 10,30,60,10,120,5,180,15,195,40,170,60,120,70,70,65,30,55,5,45 };

    public static readonly CountryInfo[] ALL = {
        new CountryInfo {
            ru = "ЯПОНИЯ", en = "JAPAN",
            wru = new[] { "Катана", "Нодати", "Нагината", "Канабо", "Танто", "Кусаригама", "Такэяри", "Оцути", "Юми", "Хоко" },
            wen = new[] { "Katana", "Nodachi", "Naginata", "Kanabo", "Tanto", "Kusarigama", "Takeyari", "Otsuchi", "Yumi", "Hoko" },
            bladeHi = Color.FromArgb(240, 244, 248), bladeLo = Color.FromArgb(150, 160, 175),
            trim = Color.FromArgb(201, 162, 39),
            map = MAP_JAPAN, mapH = 166,
            stgs = new[] {
                new StageC { ru = "РОНИН", en = "RONIN", w = Weapon.Sword, hp = 90, aggr = 0 },
                new StageC { ru = "СОХЭЙ", en = "SOHEI", w = Weapon.Spear, hp = 100, aggr = 0.1f },
                new StageC { ru = "СИНБИ", en = "SHINOBI", w = Weapon.Dagger, hp = 110, aggr = 0.2f },
                new StageC { ru = "САМУРАЙ", en = "SAMURAI", w = Weapon.Axe, hp = 122, aggr = 0.25f },
                new StageC { ru = "СУМОТОРИ", en = "SUMOTORI", w = Weapon.Mace, hp = 135, aggr = 0.3f },
            },
        },
        new CountryInfo {
            ru = "ГЕРМАНИЯ", en = "GERMANY",
            wru = new[] { "Мессер", "Цвайхендер", "Алебарда", "Моргенштерн", "Кинжал", "Кистень", "Пика", "Молот", "Боген", "Глефа" },
            wen = new[] { "Messer", "Zweihander", "Halberd", "Morgenstern", "Dagger", "Flail", "Pike", "Hammer", "Bogen", "Gleve" },
            bladeHi = Color.FromArgb(200, 205, 215), bladeLo = Color.FromArgb(120, 125, 140),
            trim = Color.FromArgb(90, 95, 110),
            map = MAP_GERMANY, mapH = 170,
            stgs = new[] {
                new StageC { ru = "ФЕХТОВАЛЬЩИК", en = "FENCER", w = Weapon.Sword, hp = 90, aggr = 0 },
                new StageC { ru = "АЛЕБАРДЩИК", en = "HALBERDIER", w = Weapon.Spear, hp = 100, aggr = 0.1f },
                new StageC { ru = "ЛАНДСКНЕХТ", en = "LANDSKNECHT", w = Weapon.Pike, hp = 110, aggr = 0.2f },
                new StageC { ru = "ГОТШЕР", en = "GOTSHER", w = Weapon.Flail, hp = 122, aggr = 0.25f },
                new StageC { ru = "РИТТЕР", en = "RITTER", w = Weapon.Mace, hp = 135, aggr = 0.3f },
            },
        },
        new CountryInfo {
            ru = "РУСЬ", en = "RUS",
            wru = new[] { "Меч", "Секира", "Копьё", "Палица", "Засапожник", "Кистень", "Пика", "Молот", "Лук", "Рогатина" },
            wen = new[] { "Sword", "Axe", "Spear", "Mace", "Dagger", "Flail", "Pike", "Hammer", "Bow", "Rogatina" },
            bladeHi = Color.FromArgb(215, 220, 225), bladeLo = Color.FromArgb(140, 145, 155),
            trim = Color.FromArgb(180, 140, 70),
            map = MAP_RUSSIA, mapH = 47,
            stgs = new[] {
                new StageC { ru = "ДРУЖИННИК", en = "DRUZHINNIK", w = Weapon.Sword, hp = 90, aggr = 0 },
                new StageC { ru = "РАТНИК", en = "RATNIK", w = Weapon.Spear, hp = 100, aggr = 0.1f },
                new StageC { ru = "ОХОТНИК", en = "HUNTER", w = Weapon.Dagger, hp = 110, aggr = 0.2f },
                new StageC { ru = "ГРИДЕНЬ", en = "GRIDEN", w = Weapon.Axe, hp = 122, aggr = 0.25f },
                new StageC { ru = "БОГАТЫРЬ", en = "BOGATYR", w = Weapon.Hammer, hp = 140, aggr = 0.3f },
            },
        },
        new CountryInfo {
            ru = "ФРАНЦИЯ", en = "FRANCE",
            wru = new[] { "Бастард", "Вуж", "Алебарда", "Булава", "Стилет", "Цеп", "Пика", "Молот", "Арбалет", "Гвизарма" },
            wen = new[] { "Bastard", "Voulge", "Halberd", "Mace", "Stiletto", "Flail", "Pike", "Hammer", "Arbalest", "Guisarme" },
            bladeHi = Color.FromArgb(225, 230, 240), bladeLo = Color.FromArgb(145, 150, 165),
            trim = Color.FromArgb(60, 90, 180),
            map = MAP_FRANCE, mapH = 130,
            stgs = new[] {
                new StageC { ru = "ДУЭЛЯНТ", en = "DUELIST", w = Weapon.Sword, hp = 90, aggr = 0 },
                new StageC { ru = "КОПЕЙЩИК", en = "PIKEMAN", w = Weapon.Pike, hp = 100, aggr = 0.1f },
                new StageC { ru = "БРЕТЁР", en = "BRETTER", w = Weapon.Dagger, hp = 110, aggr = 0.2f },
                new StageC { ru = "ЖАНДАРМ", en = "GENDARME", w = Weapon.Flail, hp = 122, aggr = 0.25f },
                new StageC { ru = "ПАЛАДИН", en = "PALADIN", w = Weapon.Hammer, hp = 140, aggr = 0.3f },
            },
        },
        new CountryInfo {
            ru = "АНГЛИЯ", en = "ENGLAND",
            wru = new[] { "Меч", "Билль", "Алебарда", "Молот", "Кортик", "Цеп", "Пика", "Палица", "Лук", "Глефа" },
            wen = new[] { "Sword", "Bill", "Halberd", "Maul", "Dirk", "Flail", "Pike", "Mace", "Bow", "Glaive" },
            bladeHi = Color.FromArgb(210, 215, 225), bladeLo = Color.FromArgb(135, 140, 150),
            trim = Color.FromArgb(170, 60, 60),
            map = MAP_ENGLAND, mapH = 110,
            stgs = new[] {
                new StageC { ru = "ОПОЛЧЕНЕЦ", en = "MILITIA", w = Weapon.Sword, hp = 90, aggr = 0 },
                new StageC { ru = "ЙОМЕН", en = "YEOMAN", w = Weapon.Spear, hp = 100, aggr = 0.1f },
                new StageC { ru = "БИЛЛЬМЕН", en = "BILLMAN", w = Weapon.Axe, hp = 110, aggr = 0.2f },
                new StageC { ru = "МЕН-ЕТ-АРМС", en = "MAN-AT-ARMS", w = Weapon.Flail, hp = 122, aggr = 0.25f },
                new StageC { ru = "ЛОРД", en = "LORD", w = Weapon.Hammer, hp = 140, aggr = 0.3f },
            },
        },
        new CountryInfo {
            ru = "ОРДА", en = "HORDE",
            wru = new[] { "Сабля", "Тесак", "Пика", "Дубина", "Кинжал", "Кистень", "Дротик", "Молот", "Лук", "Рогатина" },
            wen = new[] { "Sabre", "Tesak", "Pike", "Club", "Dagger", "Flail", "Dart", "Hammer", "Bow", "Rogatina" },
            bladeHi = Color.FromArgb(220, 215, 200), bladeLo = Color.FromArgb(140, 135, 120),
            trim = Color.FromArgb(150, 100, 40),
            map = MAP_HORDE, mapH = 70,
            stgs = new[] {
                new StageC { ru = "НУКЕР", en = "NUKER", w = Weapon.Sword, hp = 90, aggr = 0 },
                new StageC { ru = "ДЖИГИТ", en = "JIGIT", w = Weapon.Spear, hp = 100, aggr = 0.1f },
                new StageC { ru = "МЕРГЕН", en = "MERGEN", w = Weapon.Dagger, hp = 110, aggr = 0.2f },
                new StageC { ru = "БАТЫР", en = "BATYR", w = Weapon.Axe, hp = 122, aggr = 0.25f },
                new StageC { ru = "ХАН", en = "KHAN", w = Weapon.Flail, hp = 140, aggr = 0.3f },
            },
        },
    };
}
