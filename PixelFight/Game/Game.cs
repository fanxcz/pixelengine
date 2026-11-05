using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PixelFight;

// Вся игра: бойцы, ИИ, арена, кровь, HUD, меню. Своя мини-логика, без движка.
// Буфер 480x270, каждый кадр закрашивается ЦЕЛИКОМ (иначе шлейфы-артефакты).
// Боец — неуклюжая марионетка: руки/ноги с суставами, ими надо двигать чтобы ударить.
public enum Weapon { Sword, Axe, Mace, Spear, Dagger, Flail, Pike, Hammer, Bow, Halberd }
enum State { Menu, Fight, MatchEnd, Map, Help, Settings, Countries, Shop, Controls }
enum Lang { RU, EN }
enum Mode { Quick, Camp, Versus, Train, Survive }

struct Stage
{
    public string ru, en;
    public Weapon w;
    public float hp, aggr;
}

public class Fighter
{
    public bool isPlayer;
    public Weapon weapon;
    public float x, y, vx, vy, hp = 100, maxHp = 100, face = 1;
    public float cd, walkPh, hurtT, hitCd, dashCd;
    public bool alive = true, blocking, onGround, charged;
    public int jumps = 2;
    public float move, aimX, aimY, tx, ty; // aim = сглаженный прицел, t = цель прицела
    public float armAng, aimWind, prevBase;
    public bool spinMode; // рука крутится клавишами (2-й игрок), а не за целью
    public float spinVel; // скорость вращения руки
    public float tipX, tipY, tipSpeed; // кончик клинка (мир), его скорость
    public readonly float[] trailX = new float[8], trailY = new float[8];
    public int trailIdx;
    public bool wantJump, wantBlock;
    public bool disarmed; // без оружия — бьёт слабой рукой
    public bool isBoss; // босс этапа: особые приёмы
    public float forceSpin; // принудительное вращение руки (вихрь), рад/с
    public float jumpPow = -300; // сила прыжка (у босса выше для топота)
    public bool justLanded; // только что приземлился (этот тик)
    public float landV; // скорость падения в момент приземления
    public float stunT, kickCd, blockAge = 999f; // оглушение, откат пинка, возраст блока
    public float deadT;
    public Bitmap bTorso, bHead, bLegs, bArm; // детальные спрайты
    // Марионетка: анимация конечностей (руки/ноги как у людей, неуклюже)
    public float punchT, kickAnim, jabCd, jabAnim; // таймеры ударов руками/ногами
    public float lean, leanV; // наклон торса (пружина — неуклюжесть)
    public float armExt; // вытянутость оружейной руки (сглаженная)
    public float offBal; // разболтанность после ударов
}

struct Particle { public float x, y, vx, vy, life; public int size; }
struct Decal { public float x, y; }
struct DmgNum { public float x, y, life; public string text; public Color col; }
struct Drop { public float x, vx; public Weapon w; } // выбитое оружие на земле
struct Shot { public float x, y, vx, vy, life; public bool fromPlayer, arrow; } // топор босса / стрела лука
struct Ring { public float x, r, maxR; public bool hitDone; } // кольцо топота

public class Game
{
    public const int W = 480, H = 270;
    const int ZOOM = 3; // мир крупнее: бойцы в 1.5 раза больше
    const int ARENA = 420; // полуширина арены: камера ездит за бойцами
    const float GRAV = 900, SPEED = 100, JUMP = -300;

    static readonly Color BloodC = Color.FromArgb(193, 18, 31);
    static readonly Color BloodDark = Color.FromArgb(127, 29, 29);
    static readonly Color Skin = Color.FromArgb(232, 185, 138);

    Random _r = new(); // логика (сид пишется в повтор)
    readonly Font _f8, _f12, _fTitle, _fBanner;
    readonly Dictionary<Color, SolidBrush> _brushes = new();
    readonly Random _rv = new(); // только рендер (искры) — поток логики не трогает

    State _state = State.Menu;
    Fighter _p, _e;
    readonly List<Particle> _parts = new();
    readonly List<Decal> _decals = new();
    readonly List<DmgNum> _nums = new();
    readonly List<Drop> _drops = new();
    readonly List<Shot> _shots = new();
    readonly List<Ring> _rings = new();
    int _scoreP, _scoreE, _round = 1;
    Weapon _selected = Weapon.Sword;
    string _banner = "";
    float _bannerT, _roundT, _fightT, _time, _timeScale = 1, _slowT, _hitstop, _cheerT;
    float _camX, _shake, _shX, _shY, _flash, _whiteFlash;
    float _musT, _musBeat;
    float _aiThink, _aiMove, _aiAggr;
    int _aiMode; // 0 выбор, 1 замах-дуга, 2 отход, 3 блок, 4 стрейф, 5 погоня
    float _aiModeT, _aiSwT, _aiSwDur, _aiSwA, _aiSwB;
    int _aiPh;
    int _mX = 240, _mY = 135; // мышь в буфере (для прицела)
    bool _prevLmb, _paused, _clicked;
    int _coins, _banked;
    int _combo, _maxCombo, _hits, _dmgDealt;
    int _parries, _disarms; // за забег (на экран конца)
    float _dmgTakenRun; // урон по игроку за раунд (чистая победа)
    float _comboT;
    public Action ToggleFullscreen;
    Rectangle _btnPause;
    Lang _lang = Lang.RU;
    // кнопки меню (прямоугольники в координатах буфера)
    readonly Rectangle[] _btnWeapon = new Rectangle[10];
    readonly Rectangle[] _btnLang = new Rectangle[2];
    struct ShopItem { public string ru, en; public int price; }
    static readonly ShopItem[] SHOP = {
        new ShopItem { ru = "Шлем (-15% урона)", en = "Helmet (-15% dmg)", price = 60 },
        new ShopItem { ru = "Кольчуга (-15% урона)", en = "Mail (-15% dmg)", price = 100 },
        new ShopItem { ru = "Острый клинок (+20% урон)", en = "Sharp blade (+20% dmg)", price = 120 },
        new ShopItem { ru = "Сапоги (+25% скорость)", en = "Boots (+25% speed)", price = 80 },
        new ShopItem { ru = "Сердце (+25 HP)", en = "Heart (+25 HP)", price = 150 },
        new ShopItem { ru = "Крепкий щит (блок лучше)", en = "Sturdy shield (better block)", price = 90 },
        new ShopItem { ru = "Зелье (Q в бою, +35 HP)", en = "Potion (Q in fight, +35 HP)", price = 40 },
        new ShopItem { ru = "Тяжёлые наручи (пинок ×2)", en = "Heavy bracers (kick x2)", price = 110 },
        new ShopItem { ru = "Поножи (-10% урона)", en = "Greaves (-10% dmg)", price = 70 },
        new ShopItem { ru = "Плащ (12% увернуться)", en = "Cloak (12% dodge)", price = 130 },
        new ShopItem { ru = "Кастет (+4 рукой)", en = "Knuckles (+4 fist)", price = 70 },
        new ShopItem { ru = "Зеркальный доспех (возврат 15%)", en = "Mirror armor (return 15%)", price = 160 },
        new ShopItem { ru = "Амулет ярости (+30% при HP<35%)", en = "Rage amulet (+30% at low HP)", price = 140 },
        new ShopItem { ru = "Кольцо регенерации (+1 HP/с)", en = "Regen ring (+1 HP/s)", price = 150 },
    };
    static Rectangle ShopRow(int i) => new Rectangle(120, 46 + i * 14, 240, 13);
    static bool HasItem(int i) => (SaveData.Shop & (1 << i)) != 0;
    Mode _mode = Mode.Quick;
    int _stageIdx, _campCountry = -1, _diff;
    State _shopFrom = State.Menu;
    Color _bladeHi = Color.FromArgb(215, 227, 244);
    Color _bladeLo = Color.FromArgb(138, 147, 166);
    Color _trim = Color.FromArgb(201, 162, 39);
    Theme _theme = Theme.Rus;
    bool _campAdvance, _campWin, _survNext; // бесконечные режимы: флаг следующего врага
    Rectangle _btnQuick, _btnCamp, _btnRetry, _btnMenu, _btnVersus;
    Rectangle _btnHelp, _btnSettings, _btnBack, _btnBackS, _btnBackC;
    Rectangle _btnTrain, _btnSurv;
    Rectangle _btnResume, _btnRestartP, _btnQuit, _btnSndP;
    Rectangle _btnSound, _btnScreen, _btnDiff;
    Rectangle _btnSens, _btnPad, _btnCtl;
    Rectangle _btnShop, _btnShopM;
    HashSet<Keys> _prev = new();
    string _capture; // какое действие переназначаем (захват клавиши)

    // GameForm зовёт на каждое нажатие: захват клавиши для переназначения
    public void NoteKey(Keys key)
    {
        if (_capture == null) return;
        Binds.Set(_capture, key);
        _capture = null;
        Sfx.Jump();
    }

    static Rectangle ControlRow(int i) => new Rectangle(90, 62 + i * 20, 300, 18);
    static readonly (string id, string ru, string en)[] CTRLS = {
        ("L", "Влево", "Left"), ("R", "Вправо", "Right"), ("J", "Прыжок", "Jump"), ("K", "Пинок", "Kick"),
        ("Jb", "Тычок рукой", "Jab"), ("P", "Зелье", "Potion"), ("Pa", "Пауза", "Pause"), ("Re", "Заново", "Restart"),
    };
    static string CtrlKey(string id) => id switch
    {
        "L" => Binds.Left.ToString(), "R" => Binds.Right.ToString(), "J" => Binds.Jump.ToString(),
        "K" => Binds.Kick.ToString(), "Jb" => Binds.Jab.ToString(), "P" => Binds.Potion.ToString(),
        "Pa" => Binds.Pause.ToString(), _ => Binds.Restart.ToString(),
    };

    public Game()
    {
        _f8 = new Font(FontFamily.GenericMonospace, 8);
        _f12 = new Font(FontFamily.GenericMonospace, 12, FontStyle.Bold);
        _fTitle = new Font(FontFamily.GenericMonospace, 28, FontStyle.Bold);
        _fBanner = new Font(FontFamily.GenericMonospace, 20, FontStyle.Bold);
        SaveData.Load();
        Binds.Load();
        _lang = SaveData.Lang == 1 ? Lang.EN : Lang.RU;
        Sfx.Enabled = SaveData.Sound;
        _diff = SaveData.Diff;
        LoadArena();
        for (int i = 0; i < 10; i++) _btnWeapon[i] = new Rectangle(30 + (i / 5) * 225, 78 + (i % 5) * 17, 215, 16);
        _btnLang[0] = new Rectangle(180, 184, 55, 18);
        _btnLang[1] = new Rectangle(245, 184, 55, 18);
        _btnQuick = new Rectangle(30, 208, 130, 22);
        _btnVersus = new Rectangle(175, 208, 130, 22);
        _btnCamp = new Rectangle(320, 208, 130, 22);
        _btnHelp = new Rectangle(8, 234, 88, 20);
        _btnTrain = new Rectangle(102, 234, 88, 20);
        _btnSurv = new Rectangle(196, 234, 88, 20);
        _btnShop = new Rectangle(290, 234, 88, 20);
        _btnSettings = new Rectangle(384, 234, 88, 20);
        _btnShopM = new Rectangle(245, 232, 130, 24);
        _btnBack = new Rectangle(105, 232, 130, 24);
        _btnBackC = new Rectangle(8, 26, 100, 20);
        _btnBackS = new Rectangle(165, 250, 150, 17);
        _btnMenu = new Rectangle(165, 200, 150, 24);
        _btnRetry = new Rectangle(165, 228, 150, 22);
        _btnResume = new Rectangle(165, 140, 150, 24);
        _btnRestartP = new Rectangle(165, 168, 150, 24);
        _btnQuit = new Rectangle(165, 196, 150, 24);
        _btnSndP = new Rectangle(165, 222, 150, 20);
        _btnSound = new Rectangle(140, 92, 200, 22);
        _btnScreen = new Rectangle(140, 114, 200, 22);
        _btnDiff = new Rectangle(140, 136, 200, 22);
        _btnSens = new Rectangle(140, 158, 200, 22);
        _btnPad = new Rectangle(140, 180, 200, 22);
        _btnCtl = new Rectangle(140, 202, 200, 22);
        _btnPause = new Rectangle(436, 8, 36, 26);
        _diff = SaveData.Diff;
    }

    string T(string ru, string en) => _lang == Lang.RU ? ru : en;
    // достижение: тост + 50 монет в кубышку
    void Unlock(int id, string ru, string en)
    {
        if ((SaveData.Ach & (1 << id)) != 0) return;
        SaveData.Ach |= 1 << id;
        SaveData.Bank += 50;
        SaveData.Store();
        _banner = T("НАГРАДА: ", "AWARD: ") + T(ru, en) + T(" (+50)", " (+50)");
        _bannerT = 2.5f;
        Sfx.Horn();
    }
    int AchCount() { int n = 0; for (int i = 0; i < 6; i++) if ((SaveData.Ach & (1 << i)) != 0) n++; return n; }
    bool Hover(Rectangle r) => r.Contains(_mX, _mY);

    Brush B(Color c)
    {
        if (!_brushes.TryGetValue(c, out var b)) { b = new SolidBrush(c); _brushes[c] = b; }
        return b;
    }

    // ---------- поток матча ----------

    void StartMatch() { StartMatchWithSeed(Environment.TickCount); }

    void StartMatchWithSeed(int seed)
    {
        _recSeed = seed; _r = new Random(seed);
        // полный сброс volatile-состояния — иначе повтор рассинхронится
        _time = 0; _slowT = 0; _hitstop = 0; _cheerT = 0; _flash = 0; _whiteFlash = 0;
        _musT = 0; _musBeat = 0;
        _prev = new HashSet<Keys>(); _prevLmb = false; _liveEsc = false;
        _scoreP = 0; _scoreE = 0; _round = 1; _timeScale = 1; _coins = 0; _banked = 0; _maxCombo = 0; _hits = 0; _dmgDealt = 0; _combo = 0; _comboT = 0; _paused = false; _parries = 0; _disarms = 0; _stageIdx = 0; _campWin = false; _campAdvance = false; StartRound(); _state = State.Fight;
        // снимок для повтора: режим, магазин, зелья, сложность
        _snap = new Snap { mode = _mode, camp = _campCountry, sel = _selected, diff = _diff, shop = SaveData.Shop, pot = SaveData.Potions, lang = SaveData.Lang };
        _frames.Clear(); _recording = true; _replaying = false;
    }

    // --- повтор боя: запись инпутов + сид, воспроизведение 1-в-1 ---
    struct Frame { public HashSet<Keys> keys; public Point mouse; public bool lmb, rmb; }
    readonly List<Frame> _frames = new List<Frame>();
    bool _recording, _replaying, _liveEsc;
    int _repIdx, _recSeed;
    struct Snap { public Mode mode; public int camp; public Weapon sel; public int diff, shop, pot, lang; }
    Snap _snap;

    public bool HasReplay => _frames.Count > 0;
    public bool Replaying => _replaying;

    public void PlayReplay()
    {
        if (!HasReplay) return;
        var fr = new List<Frame>(_frames); // StartMatchWithSeed чистит — вернём
        _mode = _snap.mode; _campCountry = _snap.camp; _selected = _snap.sel;
        _diff = _snap.diff; SaveData.Shop = _snap.shop; SaveData.Potions = _snap.pot; SaveData.Lang = _snap.lang;
        StartMatchWithSeed(_recSeed);
        _frames.Clear(); _frames.AddRange(fr);
        _recording = false; _replaying = true; _repIdx = 0; // кадры подсунем в Update
        _banner = T("ПОВТОР БОЯ (Esc — выйти)", "REPLAY (Esc — exit)");
        _bannerT = 99;
    }

    public void StopReplay()
    {
        _replaying = false;
        SaveData.Shop = _snap.shop; SaveData.Potions = _snap.pot; // расходники как были
        SaveData.Store();
    }

    // Монеты забега — в кубышку (без дублей при повторах) + рекорды
    void BankRun()
    {
        SaveData.Bank += _coins - _banked;
        _banked = _coins;
        if (_maxCombo > SaveData.BestCombo) SaveData.BestCombo = _maxCombo;
        if (_coins > SaveData.BestCoins) SaveData.BestCoins = _coins;
        SaveData.Store();
    }

    void StartRound()
    {
        DropSkin(_p); DropSkin(_e);
        _parts.Clear();
        _decals.Clear(); // кровь не тащим в следующий раунд
        _nums.Clear();
        _drops.Clear();
        _shots.Clear();
        _rings.Clear();
        bool camp = _mode == Mode.Camp;
        if (camp && (_campCountry < 0 || _campCountry > 2)) _campCountry = 0;
        if (camp) _stageIdx = Math.Max(0, Math.Min(CurStages().Length - 1, _stageIdx));
        _p = new Fighter { isPlayer = true, weapon = _selected, x = -60, y = 0, face = 1 };
        _p.maxHp = 100 + (HasItem(4) ? 25 : 0); _p.hp = _p.maxHp;
        Weapon ew = camp ? CurStages()[_stageIdx].w
            : new[] { Weapon.Sword, Weapon.Axe, Weapon.Mace, Weapon.Spear, Weapon.Dagger, Weapon.Flail, Weapon.Pike, Weapon.Hammer, Weapon.Bow, Weapon.Halberd }[_r.Next(10)];
        _e = new Fighter { isPlayer = false, weapon = ew, x = 60, y = 0, face = -1 };
        _e.maxHp = camp ? CurStages()[_stageIdx].hp : 100; _e.hp = _e.maxHp;
        if (_mode == Mode.Survive && _round > 1) { _e.maxHp = _e.hp = 90 + (_round - 1) * 14; } // волны крепчают
        bool boss = camp && _stageIdx == CurStages().Length - 1;
        if (boss) { _e.isBoss = true; _e.maxHp += 40; _e.hp = _e.maxHp; } // босс этапа
        BuildSkin(_p); BuildSkin(_e);
        var ci = camp ? Countries.ALL[_campCountry] : null;
        _bladeHi = ci != null ? ci.bladeHi : Color.FromArgb(215, 227, 244);
        _bladeLo = ci != null ? ci.bladeLo : Color.FromArgb(138, 147, 166);
        _trim = ci != null ? ci.trim : Color.FromArgb(201, 162, 39);
        _theme = camp ? (Theme)_campCountry : HasCustom() && _r.Next(7) == 6 ? Theme.Custom : (Theme)_r.Next(6);
        var cst0 = CurStages();
        int csi0 = Math.Max(0, Math.Min(cst0.Length - 1, _stageIdx));
        _banner = camp
            ? ((_e.isBoss ? T("БОСС: ", "BOSS: ") : T("ЭТАП ", "STAGE ") + (_stageIdx + 1) + "/" + cst0.Length + ": ") + (_lang == Lang.RU ? cst0[csi0].ru : cst0[csi0].en))
            : T("РАУНД ", "ROUND ") + _round;
        _bannerT = 1.6f;
        _fightT = 1.2f; // анонс БОЙ! в начале раунда
        _roundT = 0;
        _dmgTakenRun = 0; // чистота раунда
        _combo = 0; _comboT = 0;
        _shake = 0; _shX = 0; _shY = 0; _flash = 0; _whiteFlash = 0; _timeScale = 1; _slowT = 0; _hitstop = 0;
        _camX = 0; _paused = false;
        _aiAggr = (0.45f + _round * 0.1f + (float)_r.NextDouble() * 0.2f + (camp ? CurStages()[_stageIdx].aggr : 0)
            + new[] { -0.2f, 0f, 0.2f }[_diff]) * AggrMul;
        _aiMode = 0; _aiThink = 0;
        InitArm(_p, _e); InitArm(_e, _p);
        _p.spinMode = false;
        _e.spinMode = _mode == Mode.Versus; _e.spinVel = 0;
        Sfx.Horn();
        _state = State.Fight; // важно для повтора этапа: выйти с экрана конца
    }

    bool StateIsMenuScreen() => _state == State.Map || _state == State.Help || _state == State.Settings || _state == State.Countries || _state == State.Shop || _state == State.Controls;
    static Rectangle NodeRect(int i) => new Rectangle(24 + i * 88, 110, 80, 80);
    static Rectangle CountryRect(int i) => new Rectangle(40 + (i % 3) * 140, 58 + (i / 3) * 100, 120, 92);

    StageC[] CurStages() => Countries.ALL[Math.Max(0, Math.Min(Countries.ALL.Length - 1, _campCountry))].stgs;
    int CampUnl() => _campCountry switch { 0 => SaveData.Unlj, 1 => SaveData.Unld, 2 => SaveData.Unlr, 3 => SaveData.Unlf, 4 => SaveData.Unle, _ => SaveData.Unlh };
    void CampUnlSet(int v)
    {
        switch (_campCountry)
        {
            case 0: SaveData.Unlj = v; break;
            case 1: SaveData.Unld = v; break;
            case 2: SaveData.Unlr = v; break;
            case 3: SaveData.Unlf = v; break;
            case 4: SaveData.Unle = v; break;
            default: SaveData.Unlh = v; break;
        }
        SaveData.Store();
    }
    string EnemyName()
    {
        if (_mode == Mode.Versus) return T("ИГРОК 2", "PLAYER 2");
        if (_mode != Mode.Camp) return T("РАЗБОЙНИК", "BRIGAND");
        var st = CurStages();
        int si = Math.Max(0, Math.Min(st.Length - 1, _stageIdx));
        return _lang == Lang.RU ? st[si].ru : st[si].en;
    }

    static float Dmg(Weapon w) => w switch { Weapon.Axe => 34, Weapon.Mace => 26, Weapon.Spear => 18, Weapon.Dagger => 14, Weapon.Flail => 24, Weapon.Pike => 16, Weapon.Hammer => 40, Weapon.Bow => 6, Weapon.Halberd => 20, _ => 22 };
    static float Range(Weapon w) => w switch { Weapon.Spear => 40, Weapon.Axe => 28, Weapon.Flail => 32, Weapon.Dagger => 22, Weapon.Pike => 48, Weapon.Hammer => 26, Weapon.Bow => 24, Weapon.Halberd => 34, _ => 30 };
    static float SwingTime(Weapon w) => w switch { Weapon.Axe => 0.26f, Weapon.Mace => 0.24f, Weapon.Spear => 0.20f, Weapon.Dagger => 0.14f, Weapon.Flail => 0.22f, Weapon.Pike => 0.30f, Weapon.Hammer => 0.30f, Weapon.Halberd => 0.24f, _ => 0.18f };
    static float Cooldown(Weapon w) => w switch { Weapon.Axe => 0.65f, Weapon.Mace => 0.55f, Weapon.Spear => 0.42f, Weapon.Dagger => 0.34f, Weapon.Flail => 0.60f, Weapon.Pike => 0.60f, Weapon.Hammer => 0.75f, Weapon.Halberd => 0.55f, _ => 0.45f };
    static float Knock(Weapon w) => w switch { Weapon.Mace => 220, Weapon.Axe => 170, Weapon.Spear => 120, Weapon.Dagger => 90, Weapon.Flail => 190, Weapon.Pike => 100, Weapon.Hammer => 260, Weapon.Bow => 100, Weapon.Halberd => 150, _ => 140 };
    static string WName(Weapon w) => w switch { Weapon.Axe => "Axe", Weapon.Mace => "Mace", Weapon.Spear => "Spear", Weapon.Dagger => "Dagger", Weapon.Flail => "Flail", Weapon.Pike => "Pike", Weapon.Hammer => "Hammer", Weapon.Bow => "Bow", Weapon.Halberd => "Halberd", _ => "Sword" };
    // имя оружия: в кампании страны — национальное, иначе обычное
    string WLoca(Weapon w)
    {
        if (_mode == Mode.Camp && _campCountry >= 0)
        {
            var c = Countries.ALL[_campCountry];
            return _lang == Lang.RU ? c.wru[(int)w] : c.wen[(int)w];
        }
        return _lang == Lang.RU ? w switch
        {
            Weapon.Axe => "Топор", Weapon.Mace => "Булава", Weapon.Spear => "Копьё",
            Weapon.Dagger => "Кинжал", Weapon.Flail => "Кистень",
            Weapon.Pike => "Пика", Weapon.Hammer => "Молот",
            Weapon.Bow => "Лук", Weapon.Halberd => "Алебарда", _ => "Меч",
        } : WName(w);
    }
    string WDesc(Weapon w, int n)
    {
        string nm = WLoca(w);
        return _lang == Lang.RU ? w switch
        {
            Weapon.Axe => n + " " + nm + " (мощный)", Weapon.Mace => n + " " + nm + " (отброс)",
            Weapon.Spear => n + " " + nm + " (длинное)", Weapon.Dagger => n + " " + nm + " (кинжал)",
            Weapon.Flail => n + " " + nm + " (цепь!)", Weapon.Pike => n + " " + nm + " (пика)",
            Weapon.Hammer => n + " " + nm + " (сокрушает)", Weapon.Bow => n + " " + nm + " (стреляет)",
            Weapon.Halberd => n + " " + nm + " (притяжка)", _ => n + " " + nm + " (быстрый)",
        } : w switch
        {
            Weapon.Axe => n + " " + nm + " (heavy)", Weapon.Mace => n + " " + nm + " (knockback)",
            Weapon.Spear => n + " " + nm + " (long)", Weapon.Dagger => n + " " + nm + " (dagger)",
            Weapon.Flail => n + " " + nm + " (chain)", Weapon.Pike => n + " " + nm + " (pike)",
            Weapon.Hammer => n + " " + nm + " (smash)", Weapon.Bow => n + " " + nm + " (shoots)",
            Weapon.Halberd => n + " " + nm + " (pull)", _ => n + " " + nm + " (fast)",
        };
    }

    // ---------- update ----------

    bool Pressed(HashSet<Keys> k, Keys key) => k.Contains(key) && !_prev.Contains(key);

    public void Update(HashSet<Keys> keys, Point mouse, bool lmb, bool rmb, float dt)
    {
        // живой Esc во время повтора — выход (проверяем ДО подмены ввода записью)
        bool liveEsc = keys.Contains(Keys.Escape);
        if (_replaying && liveEsc && !_liveEsc)
        {
            StopReplay();
            _state = State.MatchEnd;
            _prev = new HashSet<Keys>(); _prevLmb = false; _liveEsc = true;
            return;
        }
        _liveEsc = liveEsc;
        // повтор: подсовываем записанные кадры вместо живого ввода
        if (_replaying && _state == State.Fight && _repIdx < _frames.Count)
        {
            var f = _frames[_repIdx++];
            keys = f.keys; mouse = f.mouse; lmb = f.lmb; rmb = f.rmb;
        }
        else if (_replaying && _state == State.Fight)
        {
            StopReplay();
            _state = State.MatchEnd;
            _prev = new HashSet<Keys>(keys); _prevLmb = lmb;
            return;
        }
        else if (_state == State.Fight && _recording)
            _frames.Add(new Frame { keys = new HashSet<Keys>(keys), mouse = mouse, lmb = lmb, rmb = rmb });
        if (_state != State.Fight) _recording = false;
        _mX = mouse.X; _mY = mouse.Y;
        _clicked = lmb && !_prevLmb;
        _time += dt;
        float rdt = dt;
        if (_slowT > 0) { _slowT -= rdt; if (_slowT <= 0) _timeScale = 1; }
        // хитстоп: короткая заморозка при ударе — сочно
        if (_hitstop > 0) { _hitstop -= rdt; _prev = new HashSet<Keys>(keys); _prevLmb = lmb; return; }
        float sdt = dt * _timeScale * TimeMul;

        _bannerT = Math.Max(0, _bannerT - rdt);
        _fightT = Math.Max(0, _fightT - rdt);
        _comboT = Math.Max(0, _comboT - rdt);
        if (_comboT <= 0) _combo = 0;
        _cheerT = Math.Max(0, _cheerT - rdt);
        _flash = Math.Max(0, _flash - rdt * 1.5f);
        _shake = Math.Max(0, _shake - rdt * 18);
        _shX = (float)(_r.NextDouble() * 2 - 1) * _shake;
        _shY = (float)(_r.NextDouble() * 2 - 1) * _shake;
        _whiteFlash = Math.Max(0, _whiteFlash - rdt * 2f);
        // процедурные барабаны: бой — быстро и зло, меню — редко, босс — ещё злее
        _musT -= rdt;
        if (_musT <= 0)
        {
            bool bossF = _state == State.Fight && _e != null && _e.alive && _e.isBoss;
            _musT = _state == State.Fight ? (bossF ? 0.26f : 0.32f) : 0.55f;
            Sfx.Drum(_musBeat % 4 == 0);
            if (_musBeat % 2 == 1) Sfx.Tick();
            if (_state == State.Fight && _musBeat % 2 == 0) Sfx.Pluck((int)(_musBeat / 2)); // мелодия боя
            _musBeat++;
        }

        if (_state == State.Menu)
        {
            bool clicked = lmb && !_prevLmb;
            if (Pressed(keys, Keys.D1)) _selected = Weapon.Sword;
            if (Pressed(keys, Keys.D2)) _selected = Weapon.Axe;
            if (Pressed(keys, Keys.D3)) _selected = Weapon.Mace;
            if (Pressed(keys, Keys.D4)) _selected = Weapon.Spear;
            if (Pressed(keys, Keys.D5)) _selected = Weapon.Dagger;
            if (Pressed(keys, Keys.D6)) _selected = Weapon.Flail;
            if (Pressed(keys, Keys.D7)) _selected = Weapon.Pike;
            if (Pressed(keys, Keys.D8)) _selected = Weapon.Hammer;
            if (Pressed(keys, Keys.D9)) _selected = Weapon.Bow;
            if (Pressed(keys, Keys.D0)) _selected = Weapon.Halberd;
            for (int i = 0; i < 10; i++)
                if (clicked && Hover(_btnWeapon[i])) { _selected = (Weapon)i; Sfx.Jump(); }
            if (clicked && Hover(_btnLang[0])) { _lang = Lang.RU; SaveData.Lang = 0; SaveData.Store(); Sfx.Jump(); }
            if (clicked && Hover(_btnLang[1])) { _lang = Lang.EN; SaveData.Lang = 1; SaveData.Store(); Sfx.Jump(); }
            if (Pressed(keys, Keys.Enter) || Pressed(keys, Keys.Space) || (clicked && Hover(_btnQuick))) { _mode = Mode.Quick; StartMatch(); }
            if (Pressed(keys, Keys.V) || (clicked && Hover(_btnVersus))) { _mode = Mode.Versus; StartMatch(); }
            if (Pressed(keys, Keys.C) || (clicked && Hover(_btnCamp))) { _mode = Mode.Camp; _state = State.Countries; }
            if (Pressed(keys, Keys.H) || (clicked && Hover(_btnHelp))) _state = State.Help;
            if (Pressed(keys, Keys.T) || (clicked && Hover(_btnTrain))) { _mode = Mode.Train; StartMatch(); }
            if (Pressed(keys, Keys.Y) || (clicked && Hover(_btnSurv))) { _mode = Mode.Survive; StartMatch(); }
            if (Pressed(keys, Keys.O) || (clicked && Hover(_btnSettings))) _state = State.Settings;
            if (clicked && Hover(_btnShop)) { _shopFrom = State.Menu; _state = State.Shop; }
            _prev = new HashSet<Keys>(keys);
            _prevLmb = lmb;
            return;
        }
        if (StateIsMenuScreen())
        {
            // Map / Countries / Help / Settings / Shop
            bool clicked = lmb && !_prevLmb;
            bool backHit = clicked && (_state == State.Shop ? Hover(_btnBackS) : _state == State.Countries ? Hover(_btnBackC) : Hover(_btnBack));
            if (_capture == null && (Pressed(keys, Keys.Escape) || backHit))
                _state = _state == State.Map ? State.Countries : _state == State.Shop ? _shopFrom : State.Menu;
            if (_state == State.Map)
            {
                if (clicked && Hover(_btnShopM)) { _shopFrom = State.Map; _state = State.Shop; }
                if (clicked) for (int i = 0; i < CurStages().Length; i++)
                    if (Hover(NodeRect(i)) && i <= CampUnl())
                    { _mode = Mode.Camp; _stageIdx = i; StartRound(); }
            }
            if (_state == State.Countries && clicked)
            {
                for (int i = 0; i < Countries.ALL.Length; i++)
                    if (Hover(CountryRect(i))) { _mode = Mode.Camp; _campCountry = i; _state = State.Map; Sfx.Jump(); }
            }
            if (_state == State.Settings && clicked)
            {
                if (Hover(_btnSound)) { Sfx.Enabled = !Sfx.Enabled; SaveData.Sound = Sfx.Enabled; SaveData.Store(); Sfx.Jump(); }
                if (Hover(_btnScreen)) ToggleFullscreen?.Invoke();
                if (Hover(_btnDiff)) { _diff = (_diff + 1) % 3; SaveData.Diff = _diff; SaveData.Store(); Sfx.Jump(); }
                if (Hover(_btnSens)) { int[] sv = { 8, 11, 14, 17, 20 }; SaveData.Sens = sv[(System.Array.IndexOf(sv, SaveData.Sens) + 1) % sv.Length]; SaveData.Store(); Sfx.Jump(); }
                if (Hover(_btnPad)) { SaveData.Pad = !SaveData.Pad; SaveData.Store(); Sfx.Jump(); }
                if (Hover(_btnCtl)) { _state = State.Controls; Sfx.Jump(); }
            }
            if (_state == State.Controls)
            {
                if (_capture != null)
                {
                    if (Pressed(keys, Keys.Escape)) _capture = null; // отмена захвата
                }
                else
                {
                    if (clicked) for (int i = 0; i < CTRLS.Length; i++)
                        if (Hover(ControlRow(i))) { _capture = CTRLS[i].id; Sfx.Tick(); }
                }
            }
            if (_state == State.Shop && clicked)
            {
                for (int i = 0; i < SHOP.Length; i++)
                {
                    if (!Hover(ShopRow(i))) continue;
                    if (i == 6) // зелье — стакается до 5
                    {
                        if (SaveData.Potions < 5 && SaveData.Bank >= SHOP[i].price)
                        { SaveData.Bank -= SHOP[i].price; SaveData.Potions++; SaveData.Store(); Sfx.Heal(); }
                    }
                    else if (!HasItem(i) && SaveData.Bank >= SHOP[i].price)
                    { SaveData.Bank -= SHOP[i].price; SaveData.Shop |= 1 << i; SaveData.Store(); Sfx.Horn(); }
                }
            }
            _prev = new HashSet<Keys>(keys);
            _prevLmb = lmb;
            return;
        }
        if (_state == State.MatchEnd)
        {
            bool clicked = lmb && !_prevLmb;
            if (Pressed(keys, Keys.Enter) || (clicked && Hover(_btnMenu))) _state = State.Menu;
            if (Pressed(keys, Binds.Restart)) { if (_mode == Mode.Camp && !_campWin) StartRound(); else StartMatch(); }
            if (Pressed(keys, Keys.T) && HasReplay && !_replaying) PlayReplay();
            if (_mode == Mode.Camp && !_campWin && clicked && Hover(_btnRetry)) StartRound();
            _prev = new HashSet<Keys>(keys);
            _prevLmb = lmb;
            UpdateFx(sdt);
            return;
        }

        if (Pressed(keys, Keys.Escape))
        {
            if (_replaying) { StopReplay(); _state = State.MatchEnd; }
            else
            {
                if (_mode == Mode.Train || _mode == Mode.Survive) BankRun(); // монеты в кубышку
                _state = State.Menu; _timeScale = 1;
            }
        }
        if (lmb && !_prevLmb && _btnPause.Contains(_mX, _mY)) { _paused = !_paused; Sfx.Jump(); }
        if (Pressed(keys, Binds.Pause)) _paused = !_paused;
        if (_paused)
        {
            bool pc = lmb && !_prevLmb;
            if (pc && _btnResume.Contains(_mX, _mY)) _paused = false;
            else if (pc && _btnRestartP.Contains(_mX, _mY)) { _paused = false; StartRound(); }
            else if (pc && _btnQuit.Contains(_mX, _mY)) { _paused = false; _timeScale = 1; _state = State.Menu; }
            else if (pc && _btnSndP.Contains(_mX, _mY)) { Sfx.Enabled = !Sfx.Enabled; SaveData.Sound = Sfx.Enabled; SaveData.Store(); }
            _prev = new HashSet<Keys>(keys); _prevLmb = lmb; return;
        }
        if (Pressed(keys, Binds.Restart)) { if (_mode == Mode.Camp) StartRound(); else StartMatch(); _prev = new HashSet<Keys>(keys); _prevLmb = lmb; return; }
        if (Pressed(keys, Keys.D1)) _p.weapon = Weapon.Sword;
        if (Pressed(keys, Keys.D2)) _p.weapon = Weapon.Axe;
        if (Pressed(keys, Keys.D3)) _p.weapon = Weapon.Mace;
        if (Pressed(keys, Keys.D4)) _p.weapon = Weapon.Spear;
        if (Pressed(keys, Keys.D5)) _p.weapon = Weapon.Dagger;
        if (Pressed(keys, Keys.D6)) _p.weapon = Weapon.Flail;
        if (Pressed(keys, Keys.D7)) _p.weapon = Weapon.Pike;
        if (Pressed(keys, Keys.D8)) _p.weapon = Weapon.Hammer;
        if (Pressed(keys, Keys.D9)) _p.weapon = Weapon.Bow;
        if (Pressed(keys, Keys.D0)) _p.weapon = Weapon.Halberd;

        // ввод игрока: прицел следует за курсором
        _p.move = (keys.Contains(Binds.Right) || keys.Contains(Keys.Right) ? 1 : 0)
                - (keys.Contains(Binds.Left) || keys.Contains(Keys.Left) ? 1 : 0);
        if (Pressed(keys, Binds.Jump) || Pressed(keys, Keys.Space) || Pressed(keys, Keys.Up)) _p.wantJump = true;
        _p.tx = (mouse.X - 240) / (float)ZOOM + _camX;
        _p.ty = (mouse.Y - 135) / (float)ZOOM - 40;
        _p.wantBlock = rmb;

        // ЛКМ — рывок в сторону курсора. Урон ТОЛЬКО вращением меча (кончик клинка).
        if (lmb && !_prevLmb && !_btnPause.Contains(_mX, _mY) && _p.alive && _p.dashCd <= 0)
            DoDash(_p, _p.tx >= _p.x ? 1 : -1);
        // F — пинок в клинче (нога вылетает, видно суставы)
        if (Pressed(keys, Binds.Kick) && _p.alive) DoKick(_p, _e);
        // E — тычок свободной рукой / щитом (левая рука, ближний бой)
        if (Pressed(keys, Binds.Jab) && _p.alive) DoJab(_p, _e);
        // лук стреляет сам в сторону прицела
        if (_p.weapon == Weapon.Bow && _p.alive && _e != null && _e.alive) FireBow(_p);
        // Q — зелье из магазина
        if (Pressed(keys, Binds.Potion) && _p.alive && SaveData.Potions > 0 && _p.hp < _p.maxHp)
        {
            SaveData.Potions--; SaveData.Store();
            _p.hp = Math.Min(_p.maxHp, _p.hp + 35);
            Sfx.Heal();
            Burst(_p.x, _p.y - 24, 12, 0, Color.FromArgb(80, 220, 100));
            _nums.Add(new DmgNum { x = _p.x, y = _p.y - 52, life = 0.8f, text = "+35", col = Color.LightGreen });
        }

        if (_mode == Mode.Versus) VersusInput(keys, rdt);
        else AI(sdt);
        Phys(_p, sdt);
        Phys(_e, sdt);
        AimArm(_p, sdt); AimArm(_e, sdt);
        SweepCheck(_p, _e, sdt); SweepCheck(_e, _p, sdt);

        if (_p.alive && _e.alive)
        {
            float dx = _e.x - _p.x;
            if (Math.Abs(dx) < 10 && Math.Abs(_e.y - _p.y) < 30)
            {
                float push = (10 - Math.Abs(dx)) * 0.5f * Math.Sign(dx == 0 ? 1 : dx);
                _p.x -= push; _e.x += push;
            }
        }

        // подбор выбитого оружия: пройти сверху
        foreach (var f in new[] { _p, _e })
        {
            if (f == null || !f.alive || !f.disarmed) continue;
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(_drops[i].x - f.x) < 13 && f.onGround)
                {
                    f.weapon = _drops[i].w; f.disarmed = false;
                    _drops.RemoveAt(i);
                    Sfx.Jump();
                    _nums.Add(new DmgNum { x = f.x, y = f.y - 50, life = 0.8f, text = T("С МЕЧОМ!", "ARMED!"), col = Color.LightGreen });
                    break;
                }
            }
        }

        float mid = Math.Max(-(ARENA - 80), Math.Min(ARENA - 80, (_p.x + _e.x) / 2));
        _camX += (mid - _camX) * (1 - (float)Math.Pow(0.01, rdt));

        UpdateFx(sdt);

        if (_roundT > 0)
        {
            _roundT -= rdt;
            if (_roundT <= 0)
            {
                _timeScale = 1;
                if (_mode == Mode.Camp)
                {
                    if (_campAdvance)
                    {
                        _campAdvance = false;
                        _stageIdx++;
                        if (_stageIdx >= CurStages().Length)
                        {
                            _campWin = true;
                            SaveData.CampWins++; SaveData.Store();
                            Sfx.Cheer();
                            if (SaveData.CampWins >= 6) Unlock(5, "Завоеватель", "Conqueror");
                            _banner = T("КАМПАНИЯ ПРОЙДЕНА!", "CAMPAIGN COMPLETE!");
                            _bannerT = 99;
                            BankRun();
                            _state = State.MatchEnd;
                        }
                        else StartRound();
                    }
                    else { BankRun(); _state = State.MatchEnd; } // поражение — экран повтора этапа
                }
                else if ((_mode == Mode.Train || _mode == Mode.Survive) && _survNext)
                {
                    _survNext = false;
                    if (_mode == Mode.Survive) { _round++; _p.hp = Math.Min(_p.maxHp, _p.hp + _p.maxHp * 0.25f); }
                    StartRound();
                }
                else if (_scoreP >= 3 || _scoreE >= 3)
                {
                    _banner = _scoreP >= 3 ? T("МАТЧ ВЫИГРАН!", "MATCH WON!") : T("МАТЧ ПРОИГРАН", "MATCH LOST");
                    _bannerT = 99;
                    if (_scoreP >= 3) Sfx.Cheer();
                    BankRun();
                    _state = State.MatchEnd;
                }
                else { _round++; StartRound(); }
            }
        }
        _prev = new HashSet<Keys>(keys);
        _prevLmb = lmb;
    }

    void UpdateFx(float dt)
    {
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var q = _parts[i];
            q.vy += 400 * dt; q.x += q.vx * dt; q.y += q.vy * dt; q.life -= dt;
            if (q.y > 0) { q.y = 0; q.vx *= 0.5f; q.vy = 0; }
            if (q.life <= 0) _parts.RemoveAt(i); else _parts[i] = q;
        }
        for (int i = _nums.Count - 1; i >= 0; i--)
        {
            var n = _nums[i];
            n.y -= 22 * dt; n.life -= dt;
            if (n.life <= 0) _nums.RemoveAt(i); else _nums[i] = n;
        }
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.vx = MoveToward(d.vx, 0, 300 * dt);
            d.x += d.vx * dt;
            d.x = Math.Max(-ARENA, Math.Min(ARENA, d.x));
            _drops[i] = d;
        }
        // снаряды: топоры босса летят в игрока, стрелы лука — во врага
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var sh = _shots[i];
            if (!sh.arrow) sh.vy += 500 * dt;
            sh.x += sh.vx * dt; sh.y += sh.vy * dt; sh.life -= dt;
            bool kill = sh.life <= 0 || sh.y >= 0;
            Fighter vic = sh.fromPlayer ? _e : _p;
            if (!kill && vic != null && vic.alive)
            {
                float dx = sh.x - vic.x, dy = sh.y - (vic.y - 24);
                if (dx * dx + dy * dy < 14 * 14)
                {
                    kill = true;
                    HitByShot(sh, vic);
                }
            }
            if (kill)
            {
                if (sh.y >= 0) Burst(sh.x, 0, 4, 0, Color.FromArgb(200, 200, 200));
                _shots.RemoveAt(i);
            }
            else _shots[i] = sh;
        }
        // попадание снаряда: топор босса — в игрока, стрела — во врага
    void HitByShot(Shot sh, Fighter vic)
    {
        float dir = sh.vx >= 0 ? 1 : -1;
        bool facing = Math.Sign(dir) == Math.Sign(vic.face);
        if (vic.blocking && facing)
        {
            Sfx.Clang();
            Burst(sh.x, sh.y, 5, -dir, Color.FromArgb(255, 220, 120));
            vic.vx += dir * 80;
            return;
        }
        float dmg = sh.arrow ? 12 : 15;
        if (sh.arrow)
        {
            bool mine = sh.fromPlayer;
            if (mine && HasItem(2)) dmg *= 1.2f;
            if (mine) dmg *= new[] { 1.15f, 1f, 0.9f }[_diff];
        }
        if (!sh.fromPlayer)
        {
            if (HasItem(0)) dmg *= 0.85f;
            if (HasItem(1)) dmg *= 0.85f;
            if (HasItem(8)) dmg *= 0.9f;
            dmg *= new[] { 0.7f, 1f, 1.3f }[_diff];
        }
        vic.hp -= dmg; vic.hurtT = 0.3f;
        if (vic.isPlayer) _dmgTakenRun += dmg;
        vic.vx += dir * 160; vic.vy = Math.Min(vic.vy, -120);
        Sfx.Hit(); Burst(sh.x, sh.y, 10, dir, BloodC);
        if (!sh.fromPlayer) { _flash = 0.3f; }
        _shake = Math.Min(_shake + 2, 8);
        _nums.Add(new DmgNum { x = vic.x, y = vic.y - 44, life = 0.8f, text = ((int)dmg).ToString(), col = Color.Orange });
        if (sh.fromPlayer) _coins += 2;
        if (vic.hp <= 0 && vic.alive) KillFighter(vic, dir);
    }
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var rg = _rings[i];
            rg.r += 150 * dt;
            if (!rg.hitDone && _p != null && _p.alive && _p.onGround)
            {
                float d = Math.Abs(_p.x - rg.x);
                if (d < rg.r + 8 && d > rg.r - 14)
                {
                    rg.hitDone = true;
                    float dir = _p.x >= rg.x ? 1 : -1;
                    _p.hp -= 12; _p.hurtT = 0.3f; _dmgTakenRun += 12;
                    _p.vx += dir * 260; _p.vy = -320;
                    Sfx.Hit(); Burst(_p.x, 0, 12, dir, Color.FromArgb(200, 200, 200));
                    _shake = Math.Min(_shake + 3, 8);
                    _nums.Add(new DmgNum { x = _p.x, y = _p.y - 44, life = 0.8f, text = "12", col = Color.White });
                    if (_p.hp <= 0 && _p.alive) KillFighter(_p, dir);
                }
            }
            if (rg.r >= rg.maxR) _rings.RemoveAt(i); else _rings[i] = rg;
        }
        if (_parts.Count > 400) _parts.RemoveRange(0, _parts.Count - 400);
    }

    // Лук: стрела летит куда смотрит рука (авто-огонь в Update, у ИИ — в AI)
    void FireBow(Fighter atk)
    {
        if (!atk.alive || atk.cd > 0) return;
        HandPos(atk, out float hx, out float hy);
        float a = atk.armAng;
        _shots.Add(new Shot { x = hx, y = hy, vx = (float)Math.Cos(a) * 320, vy = (float)Math.Sin(a) * 320, life = 1.4f, fromPlayer = atk.isPlayer, arrow = true });
        atk.cd = Math.Max(atk.cd, 0.8f);
        Sfx.Swing();
        Burst(hx, hy, 2, 0, Color.FromArgb(200, 200, 200));
    }

    void DoDash(Fighter f, float dir)
    {
        if (!f.alive || f.dashCd > 0) return;
        f.vx = dir * 280; f.vy = Math.Min(f.vy, -60);
        f.dashCd = 0.9f; Sfx.Swing();
        Burst(f.x, 0, 6, -dir, Color.FromArgb(200, 200, 200));
    }

    // Второй игрок на одной клавиатуре: стрелки — движение, N/M — вращение меча
    void VersusInput(HashSet<Keys> keys, float dt)
    {
        var s = _e;
        if (!s.alive) return;
        s.move = (keys.Contains(Keys.Right) ? 1 : 0) - (keys.Contains(Keys.Left) ? 1 : 0);
        if (Pressed(keys, Keys.Up)) s.wantJump = true;
        float sv = 0;
        if (keys.Contains(Keys.N)) sv -= 1;
        if (keys.Contains(Keys.M)) sv += 1;
        s.spinVel = MoveToward(s.spinVel, sv * 14, 30 * dt);
        s.wantBlock = keys.Contains(Keys.Oemcomma);
        if (Pressed(keys, Keys.OemQuestion)) DoDash(s, s.move != 0 ? Math.Sign(s.move) : s.face);
        if (Pressed(keys, Keys.OemPeriod)) DoKick(s, _p);
        if (Pressed(keys, Keys.L)) DoJab(s, _p);
        // J — смена оружия по кругу, K — зелье (как Q у первого)
        if (Pressed(keys, Keys.J) && s.alive) { s.weapon = (Weapon)(((int)s.weapon + 1) % 10); Sfx.Jump(); }
        if (Pressed(keys, Keys.K) && s.alive && SaveData.Potions > 0 && s.hp < s.maxHp)
        {
            SaveData.Potions--; SaveData.Store();
            s.hp = Math.Min(s.maxHp, s.hp + 35);
            Sfx.Heal();
            Burst(s.x, s.y - 24, 12, 0, Color.FromArgb(80, 220, 100));
            _nums.Add(new DmgNum { x = s.x, y = s.y - 52, life = 0.8f, text = "+35", col = Color.LightGreen });
        }
    }

    void AI(float dt)
    {
        var s = _e; var t = _p;
        if (!s.alive) return;
        if (_mode == Mode.Train) { s.move = 0; s.wantBlock = false; return; } // манекен стоит
        if (!t.alive) { s.move = 0; s.wantBlock = false; return; }
        _aiThink -= dt; _aiModeT -= dt;
        float dx = t.x - s.x, dist = Math.Abs(dx);
        float range = ReachF(s);
        // опасность: кончик меча игрока рядом и быстрый, либо идёт замах/заряд
        float tdx = t.tipX - s.x, tdy = t.tipY - (s.y - 22);
        bool danger = t.tipSpeed > 160 && tdx * tdx + tdy * tdy < 45 * 45;
        bool pSpinning = t.tipSpeed > 300 && dist < 85; // игрок яростно крутит — отойти или закрыть

        if (_aiThink <= 0 && _aiMode != 1)
        {
            _aiThink = 0.12f;
            float skill = Math.Min(0.15f + _aiAggr * 0.5f, 0.7f);
            if (s.hp < 30 && dist < 75) { _aiMode = 2; _aiModeT = 0.5f; }
            else if ((danger && _r.NextDouble() < skill) || (pSpinning && _r.NextDouble() < 0.6))
            { _aiMode = 3; _aiModeT = 0.25f + (float)_r.NextDouble() * 0.25f; }
            else if (dist > range + 10) { _aiMode = 5; _aiModeT = 0.4f; }
            else if (dist < range - 12 && _r.NextDouble() < 0.45) { _aiMode = 2; _aiModeT = 0.3f; }
            else if (s.isBoss && s.cd <= 0 && dist < 130 && _r.NextDouble() < 0.3) BossMove(s, t, dist);
            else if (s.cd <= 0 && dist < range + 6 && _r.NextDouble() < 0.3 + _aiAggr * 0.3) StartSweep(s, t);
            else { _aiMode = 4; _aiModeT = 0.35f + (float)_r.NextDouble() * 0.4f; _aiMove = _r.NextDouble() < 0.5 ? -0.8f : 0.8f; }
            if (_aiMode == 5 && dist > 40 && dist < 95 && s.onGround && _r.NextDouble() < 0.35)
                s.wantJump = true; // прыжковая атака
            if (dist < 20 && Math.Abs(t.y - s.y) < 22 && s.kickCd <= 0 && _r.NextDouble() < 0.35)
                DoKick(s, t); // пинок в клинче
            else if (dist < 24 && Math.Abs(t.y - s.y) < 22 && s.jabCd <= 0 && _r.NextDouble() < 0.3)
                DoJab(s, t); // тычок рукой
        }

        s.wantBlock = false;
        switch (_aiMode)
        {
            case 1: // замах: прицел бежит по дуге через врага
                _aiSwT += dt;
                float k = Math.Min(1, _aiSwT / _aiSwDur);
                float a = _aiSwA + (_aiSwB - _aiSwA) * (1 - (1 - k) * (1 - k));
                float rr = Math.Max(20, dist);
                s.tx = s.x + (float)Math.Cos(a) * rr;
                s.ty = (s.y - 32) + (float)Math.Sin(a) * rr;
                s.move = 0;
                if (k >= 1)
                {
                    _aiMode = 0;
                    s.charged = false;
                    s.cd = Cooldown(s.weapon) * 0.6f;
                    if (_r.NextDouble() < _aiAggr * 0.5 && dist < range + 6 && s.cd <= 0.2f)
                        StartSweep(s, t); // комбо: обратный замах
                }
                break;
            case 2: s.move = -Math.Sign(dx == 0 ? 1 : dx); s.wantBlock = true; AimPoint(s, t); if (_aiModeT <= 0) _aiMode = 0; break;
            case 3: s.move = -Math.Sign(dx == 0 ? 1 : dx) * 0.3f; s.wantBlock = true; AimPoint(s, t); if (_aiModeT <= 0) _aiMode = 0; break;
            case 4: s.move = _aiMove; AimPoint(s, t); if (_aiModeT <= 0) _aiMode = 0; break;
            case 6: // натиск: пауза-угроза, потом рывок с мельницей
                if (_aiPh == 0)
                {
                    s.move = 0; AimPoint(s, t);
                    if (_aiModeT <= 0) { _aiPh = 1; _aiModeT = 0.5f; s.vx = Math.Sign(dx == 0 ? 1 : dx) * 380; s.forceSpin = 14 * (dx >= 0 ? 1 : -1); Sfx.Swing(); }
                }
                else
                {
                    AimPoint(s, t);
                    if (_aiModeT <= 0) { _aiMode = 0; s.forceSpin = 0; s.cd = Cooldown(s.weapon); }
                }
                break;
            case 7: // вихрь: крутится на месте, подходит ближе
                s.move = Math.Sign(dx) * 0.3f;
                s.tx = t.x; s.ty = t.y - 20;
                if (_aiModeT <= 0) { _aiMode = 0; s.forceSpin = 0; s.cd = Cooldown(s.weapon); }
                break;
            case 8: // топот: ждём приземления после прыжка
                if (s.justLanded)
                {
                    _rings.Add(new Ring { x = s.x, r = 6, maxR = 95 });
                    Sfx.Stomp();
                    Burst(s.x, 0, 20, 0, Color.FromArgb(200, 200, 200));
                    _shake = 8;
                    s.forceSpin = 0; s.jumpPow = -300;
                    _aiMode = 0; s.cd = Cooldown(s.weapon);
                }
                break;
            case 9: // метнуть топор в игрока
                s.move = 0; AimPoint(s, t);
                if (_aiModeT <= 0)
                {
                    _aiMode = 0; s.cd = Cooldown(s.weapon);
                    float ang = (float)Math.Atan2((t.y - 24) - (s.y - 30), t.x - s.x);
                    _shots.Add(new Shot { x = s.x, y = s.y - 30, vx = (float)Math.Cos(ang) * 220, vy = (float)Math.Sin(ang) * 220 - 120, life = 1.6f });
                    Sfx.Swing();
                }
                break;
            default: s.move = Math.Sign(dx); AimPoint(s, t); if (s.weapon == Weapon.Bow) { if (s.cd <= 0 && dist < 170 && dist > 30) FireBow(s); else if (dist < range + 6 && s.cd <= 0) StartSweep(s, t); } else if (dist < range + 6 && s.cd <= 0) StartSweep(s, t); break;
        }
        if (s.onGround && dist < 50 && dist > 5 && _r.NextDouble() < dt * 0.5) s.wantJump = true;
    }

    // Особые приёмы боссов: натиск (все), топот (Япония), бросок (Германия), вихрь (Русь)
    void BossMove(Fighter s, Fighter t, float dist)
    {
        if (_campCountry == 1 && dist > 50) { _aiMode = 9; _aiModeT = 0.45f; s.move = 0; Sfx.Swing(); }
        else if (_campCountry == 0 && s.onGround) { _aiMode = 8; s.move = 0; s.jumpPow = -460; s.wantJump = true; Sfx.Jump(); }
        else if (_campCountry == 2 && _r.NextDouble() < 0.5) { _aiMode = 7; _aiModeT = 1.1f; s.forceSpin = 13 * (t.x >= s.x ? 1 : -1); Sfx.Swing(); }
        else { _aiMode = 6; _aiModeT = 0.45f; _aiPh = 0; s.move = 0; }
    }

    void AimPoint(Fighter s, Fighter t)
    {
        // прицел: грудь, иногда голова (попытка крита) + лёгкое виляние
        bool head = _r.Next(100) < 30;
        s.tx = t.x + (float)Math.Sin(_time * 3) * 6;
        s.ty = t.y - (head ? 36 : 24);
    }

    void StartSweep(Fighter s, Fighter t)
    {
        _aiMode = 1; _aiSwT = 0;
        float dir = _r.NextDouble() < 0.5 ? -1 : 1;
        float a0 = (float)Math.Atan2((t.y - 26) - (s.y - 32), t.x - s.x);
        _aiSwA = a0 - 1.2f * dir; _aiSwB = a0 + 1.2f * dir;
        s.charged = _r.NextDouble() < 0.12;
        _aiSwDur = s.charged ? 0.24f : 0.18f;
        float rq = Math.Max(20, Math.Abs(t.x - s.x));
        s.tx = s.x + (float)Math.Cos(_aiSwA) * rq;
        s.ty = (s.y - 32) + (float)Math.Sin(_aiSwA) * rq;
    }

    // Рука: сглаженный прицел + замах; угол НЕ нормализуем — полные обороты копятся (спин = удар)
    void AimArm(Fighter f, float dt)
    {
        float k = 1 - (float)Math.Exp(-SaveData.Sens * dt);
        f.aimX += (f.tx - f.aimX) * k;
        f.aimY += (f.ty - f.aimY) * k;
        float b = (float)Math.Atan2(f.aimY - (f.y - 32), f.aimX - f.x);
        float d = b - f.prevBase;
        while (d > Math.PI) d -= 2 * (float)Math.PI;
        while (d < -Math.PI) d += 2 * (float)Math.PI;
        if (f.spinMode) { f.aimWind += f.spinVel * dt; f.prevBase = b; } // 2-й игрок крутит клавишами
        else { f.aimWind += d; f.prevBase = b; }
        float dd = f.aimWind - f.armAng;
        while (dd > Math.PI) dd -= 2 * (float)Math.PI;
        while (dd < -Math.PI) dd += 2 * (float)Math.PI;
        float k2 = 1 - (float)Math.Exp(-16 * dt);
        if (f.forceSpin != 0)
        {
            // вихрь: рука крутится сама с заданной скоростью
            f.armAng += f.forceSpin * dt;
            f.aimWind = f.armAng;
            f.prevBase = b;
        }
        else if (!f.blocking) f.armAng += dd * k2; // в блоке рука замирает
    }

    static float TipLen(Weapon w) => w switch { Weapon.Spear => 36, Weapon.Sword => 30, Weapon.Mace => 25, _ => 24 };
    // длина клинка ОТ КИСТИ (мировые единицы) — кисть держит рукоять, остриё дальше
    static float BladeLen(Weapon w) => w switch { Weapon.Spear => 15, Weapon.Sword => 11, Weapon.Dagger => 7, Weapon.Flail => 12, Weapon.Pike => 18, Weapon.Hammer => 10, Weapon.Bow => 6, Weapon.Halberd => 16, _ => 9 };
    // кисть оружейной руки: плечо (x, y-31) + направление руки * вылет
    static void HandPos(Fighter f, out float hx, out float hy)
    {
        float ext = Math.Max(0, Math.Min(1.4f, f.armExt + f.punchT * 1.2f));
        float hd = 6.5f + ext * 2.5f;
        hx = f.x + (float)Math.Cos(f.armAng) * hd;
        hy = (f.y - 31) + (float)Math.Sin(f.armAng) * hd;
    }
    // реальный радиус поражения: длина клинка + радиус тела
    static float Reach(Weapon w) => 8 + BladeLen(w) + 13;

    static void TipWorld(Fighter f, out float tx, out float ty)
    {
        // кончик = кисть + клинок вдоль руки (совпадает с картинкой!)
        HandPos(f, out float hx, out float hy);
        float bl = f.disarmed ? 3 : BladeLen(f.weapon);
        tx = hx + (float)Math.Cos(f.armAng) * bl;
        ty = hy + (float)Math.Sin(f.armAng) * bl;
    }
    static float ReachF(Fighter f) => 8 + (f.disarmed ? 3 : BladeLen(f.weapon)) + 13;

    void InitArm(Fighter f, Fighter enemy)
    {
        f.tx = f.aimX = enemy.x; f.ty = f.aimY = -20;
        f.armAng = f.aimWind = f.prevBase = (float)Math.Atan2(-20 - (f.y - 32), enemy.x - f.x);
        TipWorld(f, out float tx, out float ty);
        f.tipX = tx; f.tipY = ty; f.tipSpeed = 0; f.hitCd = 0;
        for (int i = 0; i < 8; i++) { f.trailX[i] = tx; f.trailY[i] = ty; }
    }

    // Удар кончиком: отрезок пути клинка пересекает тело + скорость выше порога.
    // Крутишь курсором — клинок носится и рубит. Урон растёт со скоростью.
    void SweepCheck(Fighter atk, Fighter vic, float dt)
    {
        TipWorld(atk, out float tx, out float ty);
        if (dt > 0 && atk.alive && vic.alive && atk.hitCd <= 0)
        {
            float ex = tx - atk.tipX, ey = ty - atk.tipY;
            atk.tipSpeed = (float)Math.Sqrt(ex * ex + ey * ey) / dt;
            if (atk.tipSpeed > 120)
            {
                float cx = vic.x, cy = vic.y - 26, r = 16;
                float len2 = ex * ex + ey * ey;
                float tt = len2 > 0 ? ((cx - atk.tipX) * ex + (cy - atk.tipY) * ey) / len2 : 0;
                tt = Math.Max(0, Math.Min(1, tt));
                float px = atk.tipX + ex * tt - cx, py = atk.tipY + ey * tt - cy;
                if (px * px + py * py <= r * r)
                {
                    // укол: кончик летит вдоль клинка — быстрее и пробивает блок
                    float len = Math.Max(1, (float)Math.Sqrt(len2));
                    float dot = (ex * (float)Math.Cos(atk.armAng) + ey * (float)Math.Sin(atk.armAng)) / len;
                    bool thrust = dot > 0.9f;
                    // руб сверху: клинок смотрит вниз — мощнее и швыряет
                    bool over = Math.Sin(atk.armAng) > 0.7f;
                    float mult = Math.Max(0.65f, Math.Min(1.6f, 0.65f + atk.tipSpeed / 700f));
                    if (over) mult = Math.Min(1.8f, mult * 1.3f);
                    DealHit(atk, vic, mult, ty, thrust, over);
                }
            }
        }
        atk.tipX = tx; atk.tipY = ty;
        atk.trailX[atk.trailIdx] = tx; atk.trailY[atk.trailIdx] = ty;
        atk.trailIdx = (atk.trailIdx + 1) % 8;
    }

    void Phys(Fighter f, float dt)
    {
        if (!f.alive)
        {
            f.deadT += dt;
            f.vx = MoveToward(f.vx, 0, 300 * dt);
            f.vy += GRAV * dt; f.x += f.vx * dt; f.y += f.vy * dt;
            if (f.y > 0) { f.y = 0; f.vy = 0; }
            f.x = Math.Max(-ARENA, Math.Min(ARENA, f.x));
            return;
        }
        Fighter enemy = f.isPlayer ? _e : _p;
        if (f.stunT > 0) // оглушён: ни двигаться, ни блокировать
        {
            f.stunT -= dt;
            f.move = 0; f.wantBlock = false; f.wantJump = false;
        }
        f.onGround = f.y >= 0;
        if (f.onGround) f.jumps = 2; // двойной прыжок
        if (f.wantJump && f.jumps > 0)
        {
            f.vy = f.jumpPow; f.jumps--; Sfx.Jump();
            Burst(f.x, f.y, 4, 0, Color.FromArgb(200, 200, 200));
        }
        f.wantJump = false;
        f.justLanded = false;
        f.vy += GRAV * dt;
        float target = f.move * SPEED * (f.blocking ? 0.45f : 1f) * (f.isPlayer && HasItem(3) ? 1.25f : 1f);
        f.vx = MoveToward(f.vx, target, 800 * dt);
        f.x += f.vx * dt; f.y += f.vy * dt;
        if (f.y >= 0)
        {
            if (!f.onGround) // только что приземлился
            {
                f.justLanded = true;
                f.landV = f.vy;
                if (f.vy > 350) // жёсткая посадка — пыль
                {
                    Burst(f.x, 0, 8, 0, Color.FromArgb(200, 200, 200));
                    Sfx.Jump();
                }
            }
            f.y = 0; f.vy = 0; f.onGround = true;
        }
        f.x = Math.Max(-ARENA, Math.Min(ARENA, f.x));

        if (Math.Abs(f.move) > 0.1f) f.face = Math.Sign(f.move);
        else if (enemy != null && enemy.alive && enemy.x != f.x) f.face = Math.Sign(enemy.x - f.x);

        bool wasBlock = f.blocking;
        f.blocking = f.wantBlock && f.onGround;
        f.blockAge = f.blocking ? (wasBlock ? f.blockAge + dt : 0) : 999;
        f.cd -= dt;
        f.dashCd = Math.Max(0, f.dashCd - dt);
        f.kickCd = Math.Max(0, f.kickCd - dt);
        f.hurtT = Math.Max(0, f.hurtT - dt);
        f.hitCd = Math.Max(0, f.hitCd - dt);
        f.punchT = Math.Max(0, f.punchT - dt);
        f.kickAnim = Math.Max(0, f.kickAnim - dt);
        f.jabCd = Math.Max(0, f.jabCd - dt);
        f.jabAnim = Math.Max(0, f.jabAnim - dt);
        if (f.isPlayer && f.alive && HasItem(13)) f.hp = Math.Min(f.maxHp, f.hp + 1.2f * dt); // кольцо регенерации
        f.offBal = Math.Max(0, f.offBal - dt * 1.6f);
        // неуклюжая пружина наклона: наклоняется от скорости и ударов, болтается
        float leanTarget = Math.Max(-0.35f, Math.Min(0.35f, f.vx * 0.0016f))
            + (f.hurtT > 0 ? -f.face * 0.25f : 0) + (f.kickAnim > 0 ? f.face * 0.18f : 0);
        f.leanV += ((leanTarget - f.lean) * 60 - f.leanV * 8) * dt;
        f.lean += f.leanV * dt;
        // рука вытягивается когда клинок быстрый (крутишь — тянется), иначе согнута
        float wantExt = Math.Max(0, Math.Min(1, (f.tipSpeed - 120) / 500f));
        f.armExt += (wantExt - f.armExt) * (1 - (float)Math.Exp(-8 * dt));
        if (Math.Abs(f.vx) > 10 && f.onGround) f.walkPh += dt * (11 + Math.Abs(f.vx) * 0.02f);
    }

    static float MoveToward(float a, float b, float d) => a < b ? Math.Min(a + d, b) : Math.Max(a - d, b);

    void DealHit(Fighter atk, Fighter vic, float mult, float tipY, bool thrust, bool over)
    {
        if (vic.isPlayer && GodMode) return; // редактор: бессмертие
        atk.hitCd = thrust ? 0.25f : 0.38f;
        atk.punchT = 0.28f; atk.offBal = Math.Min(1, atk.offBal + 0.3f);
        if (vic.isPlayer && HasItem(9) && _r.NextDouble() < 0.12)
        {
            // ПЛАЩ: увернулся — мимо!
            atk.hitCd = Math.Max(atk.hitCd, 0.3f);
            Burst(vic.x, vic.y - 26, 4, 0, Color.FromArgb(150, 150, 170));
            _nums.Add(new DmgNum { x = vic.x, y = vic.y - 48, life = 0.7f, text = T("МИМО!", "MISS!"), col = Color.LightGray });
            Sfx.Swing();
            return;
        }
        bool head = tipY < vic.y - 34;
        float dmg = (atk.disarmed ? (atk.isPlayer && HasItem(10) ? 9 : 5) : Dmg(atk.weapon)) * mult * (head ? 1.5f : 1f) * (0.9f + (float)_r.NextDouble() * 0.2f);
        if (!atk.disarmed && atk.weapon == Weapon.Dagger && thrust) dmg *= 1.3f; // кинжал создан для уколов
        if (atk.charged) dmg *= 1.6f;
        if (atk.isPlayer && HasItem(2)) dmg *= 1.2f; // острый клинок из магазина
        if (atk.isPlayer && HasItem(12) && atk.hp < atk.maxHp * 0.35f) dmg *= 1.3f; // амулет ярости
        if (atk.isPlayer) dmg *= new[] { 1.15f, 1f, 0.9f }[_diff]; // сложность: игроку легче/тяжелее
        float dir = vic.x == atk.x ? atk.face : Math.Sign(vic.x - atk.x);
        bool facing = Math.Sign(dir) == Math.Sign(vic.face);
        if (vic.blocking && facing)
        {
            if (vic.blockAge < 0.18f)
            {
                // ПАРИРОВАНИЕ: подставил щит в последний момент — враг оглушён
                atk.stunT = 0.8f;
                atk.vx -= dir * 120;
                Sfx.Clang();
                Pad.Rumble(30000, 30000);
                Burst(vic.x - dir * 8, vic.y - 24, 10, -dir, Color.FromArgb(255, 240, 180));
                _hitstop = 0.08f;
                _nums.Add(new DmgNum { x = vic.x, y = vic.y - 48, life = 0.8f, text = T("ПАРИ!", "PARRY!"), col = Color.Cyan });
                _parries++;
                if (vic.isPlayer) { SaveData.Parries++; if (SaveData.Parries >= 5) Unlock(2, "Парировщик", "Parry master"); else SaveData.Store(); }
                return;
            }
            float bm = (vic.isPlayer && HasItem(5) ? 0.08f : 0.2f);
            bool chain = !atk.disarmed && atk.weapon == Weapon.Flail;
            if (thrust || chain) bm = Math.Min(0.6f, bm * 2); // укол и цепь частично пробивают блок
            dmg *= bm; Sfx.Clang();
            Burst(vic.x, vic.y - 20, 5, -dir, Color.FromArgb(255, 220, 120));
            vic.vx += dir * 60;
            _hitstop = 0.02f;
        }
        else
        {
            Sfx.Hit();
            Burst(vic.x, vic.y - (head ? 30 : 20), atk.charged ? 30 : head ? 26 : 14, dir, BloodC);
            _hitstop = 0.045f;
            if (vic.isPlayer) Pad.Rumble(50000, 50000);
            else if (atk.isPlayer) Pad.Rumble(0, 25000, 80);
        }
        if (vic.isPlayer)
        {
            if (HasItem(0)) dmg *= 0.85f; // шлем
            if (HasItem(1)) dmg *= 0.85f; // кольчуга
            if (HasItem(8)) dmg *= 0.9f; // поножи
            dmg *= new[] { 0.7f, 1f, 1.3f }[_diff]; // сложность: по игроку слабее/сильнее
            if (HasItem(11) && dmg > 0) // зеркальный доспех: 15% обратно
            {
                float back = dmg * 0.15f;
                atk.hp -= back;
                _nums.Add(new DmgNum { x = atk.x, y = atk.y - 52, life = 0.7f, text = "-" + ((int)back), col = Color.Cyan });
                atk.hurtT = Math.Max(atk.hurtT, 0.2f);
                if (atk.hp <= 0 && atk.alive) { KillFighter(atk, -dir); return; }
            }
        }
        vic.hp -= dmg; vic.hurtT = 0.25f;
        if (vic.isPlayer) _dmgTakenRun += dmg;
        if (atk.isPlayer) _coins += 2; // монеты за попадания
        if (!vic.disarmed && (atk.charged || atk.tipSpeed > 300) && _r.NextDouble() < (atk.charged ? 0.5 : 0.35))
        {
            Disarm(vic, dir); // выбил оружие из рук!
            _disarms++;
            SaveData.Disarms++;
            if (SaveData.Disarms >= 10) Unlock(3, "Обезоруживатель", "Disarmer");
            else SaveData.Store();
        }
        float kb = (atk.disarmed ? 60 : Knock(atk.weapon)) * (atk.charged ? 1.5f : 1f) * (over ? 1.4f : 1f);
        vic.vx += dir * kb * 0.6f; vic.vy = Math.Min(vic.vy, -60);
        if (!atk.disarmed && atk.weapon == Weapon.Halberd && vic.alive && vic.hp > 0)
        {
            // алебарда цепляет и тянет к себе
            vic.x = Math.Max(-ARENA, Math.Min(ARENA, vic.x - dir * 18));
            Burst(vic.x, vic.y - 10, 4, -dir, Color.FromArgb(200, 200, 200));
        }
        _shake = Math.Min(_shake + dmg * 0.06f + (atk.charged ? 2 : 0), 8);
        if (vic.isPlayer) _flash = 0.25f;
        if (atk.isPlayer)
        {
            _hits++;
            _dmgDealt += (int)dmg;
            if (_comboT > 0) _combo++; else _combo = 1;
            _comboT = 1.2f;
            if (_combo > _maxCombo) _maxCombo = _combo;
            if (_combo >= 10) Unlock(1, "Комбо x10", "Combo x10");
        }
        _nums.Add(new DmgNum
        {
            x = vic.x + ((float)_r.NextDouble() - 0.5f) * 10, y = vic.y - 44,
            life = 0.8f, text = head ? ((int)dmg) + T(" КРИТ!", " CRIT!") : ((int)dmg).ToString(),
            col = atk.charged ? Color.Yellow : head ? Color.Orange : Color.White,
        });
        if (vic.hp <= 0 && vic.alive) KillFighter(vic, dir);
    }

    void KillFighter(Fighter vic, float dir)
    {
        {
            vic.alive = false; vic.hp = 0;
            Sfx.Death();
            Pad.Rumble(65535, 65535, 250);
            Burst(vic.x, vic.y - 18, 50, dir, BloodC);
            _shake = 10;
            _timeScale = 0.25f; _slowT = 0.9f; // жёсткое слоу-мо добивания
            _whiteFlash = 0.5f;
            _cheerT = 1.6f; // толпа ликует
            bool pw = vic != _p;
            if (pw && _mode != Mode.Train)
            {
                SaveData.Kills++;
                if (SaveData.Kills >= 100) Unlock(4, "Сотня трупов", "Slayer");
                else SaveData.Store();
                if (_dmgTakenRun <= 0) Unlock(0, "Чистая победа", "Flawless");
            }
            if (_mode == Mode.Train || (_mode == Mode.Survive && pw))
            {
                // бесконечные режимы: враг повержен — следующий!
                _coins += _mode == Mode.Survive ? 20 + _round * 5 : 5;
                _banner = _mode == Mode.Survive ? T("ВОЛНА ", "WAVE ") + (_round + 1) + T("!", "!") : T("ЕЩЁ РАЗ!", "AGAIN!");
                _bannerT = 2;
                _survNext = true;
                _roundT = 2.2f;
            }
            else if (_mode == Mode.Camp)
            {
                if (pw)
                {
                    _coins += 20 + _stageIdx * 10;
                    CampUnlSet(Math.Max(CampUnl(), Math.Min(5, _stageIdx + 1)));
                    _banner = T("ЭТАП ПРОЙДЕН!", "STAGE CLEAR!"); _campAdvance = true;
                }
                else { _banner = T("ТЫ ПАЛ", "YOU FELL"); _campAdvance = false; }
                _bannerT = 2;
            }
            else
            {
                if (pw) _scoreP++; else _scoreE++;
                if (pw) _coins += 20; // награда за раунд
                _banner = _mode == Mode.Versus
                    ? (pw ? T("СИНИЙ ПОБЕДИЛ!", "BLUE WINS!") : T("КРАСНЫЙ ПОБЕДИЛ!", "RED WINS!"))
                    : (pw ? T("ПОБЕДА!", "VICTORY!") : T("ПОРАЖЕНИЕ", "DEFEAT"));
                _bannerT = 2;
            }
            _roundT = _mode == Mode.Camp ? 2.5f : 2.2f;
        }
    }

    // Пинок в клинче: отброс + микрооглушение, сквозь блок слабее.
    // Нога анимируется через kickAnim (суставы разгибаются — видно).
    void DoKick(Fighter atk, Fighter vic)
    {
        if (!atk.alive || !vic.alive || atk.kickCd > 0 || atk.stunT > 0) return;
        float dx = vic.x - atk.x;
        if (Math.Abs(dx) > 30 || Math.Abs(vic.y - atk.y) > 22) { atk.kickAnim = 0.22f; atk.kickCd = 0.25f; return; }
        float dir = dx == 0 ? atk.face : Math.Sign(dx);
        atk.kickCd = 0.8f;
        atk.kickAnim = 0.35f; atk.offBal = Math.Min(1, atk.offBal + 0.35f);
        float dmg = 6 * (atk.isPlayer && HasItem(7) ? 2 : 1); // тяжёлые наручи
        if (atk.isPlayer && HasItem(10)) dmg += 3; // кастет
        if (vic.blocking && Math.Sign(dir) == Math.Sign(vic.face)) dmg *= 0.5f;
        if (vic.isPlayer)
        {
            if (HasItem(0)) dmg *= 0.85f;
            if (HasItem(1)) dmg *= 0.85f;
            dmg *= new[] { 0.7f, 1f, 1.3f }[_diff];
        }
        vic.hp -= dmg;
        if (vic.isPlayer) _dmgTakenRun += dmg;
        vic.vx += dir * 260; vic.vy = Math.Min(vic.vy, -80);
        vic.stunT = Math.Max(vic.stunT, atk.isPlayer && HasItem(7) ? 0.5f : 0.25f);
        vic.hurtT = 0.25f;
        Burst(vic.x, vic.y - 10, 6, dir, Color.FromArgb(200, 200, 200));
        _nums.Add(new DmgNum { x = vic.x, y = vic.y - 44, life = 0.7f, text = ((int)dmg) + T("!", "!"), col = Color.LightGray });
        Sfx.Hit();
        _shake = Math.Min(_shake + 1, 8);
        if (atk.isPlayer) _coins += 1;
        if (vic.hp <= 0 && vic.alive) KillFighter(vic, dir);
    }

    // Тычок свободной рукой (E): левая рука вылетает вперёд, малый урон и толчок.
    // Руками/ногами надо двигать — неуклюже, но работает даже без оружия.
    public void DoJab(Fighter atk, Fighter vic)
    {
        if (atk == null || vic == null) return;
        if (!atk.alive || !vic.alive || atk.jabCd > 0 || atk.stunT > 0) return;
        float dx = vic.x - atk.x;
        if (Math.Abs(dx) > 24 || Math.Abs(vic.y - atk.y) > 22) { atk.jabAnim = 0.2f; atk.jabCd = 0.22f; return; }
        float dir = dx == 0 ? atk.face : Math.Sign(dx);
        atk.jabCd = 0.55f; atk.jabAnim = 0.3f;
        atk.punchT = 0.3f; atk.offBal = Math.Min(1, atk.offBal + 0.2f);
        float dmg = 4;
        if (vic.blocking && Math.Sign(dir) == Math.Sign(vic.face)) dmg *= 0.4f;
        if (vic.isPlayer)
        {
            if (HasItem(0)) dmg *= 0.85f;
            if (HasItem(1)) dmg *= 0.85f;
            dmg *= new[] { 0.7f, 1f, 1.3f }[_diff];
        }
        vic.hp -= dmg;
        if (vic.isPlayer) _dmgTakenRun += dmg;
        vic.vx += dir * 150;
        vic.hurtT = 0.2f;
        Burst(vic.x, vic.y - 26, 4, dir, Color.FromArgb(230, 220, 200));
        _nums.Add(new DmgNum { x = vic.x, y = vic.y - 44, life = 0.6f, text = ((int)dmg).ToString(), col = Color.LightGray });
        Sfx.Hit();
        if (atk.isPlayer) _coins += 1;
        if (vic.hp <= 0 && vic.alive) KillFighter(vic, dir);
    }

    // Выбить оружие: клинок летит на землю, подобрать — пройти сверху
    void Disarm(Fighter vic, float dir)
    {
        if (vic.disarmed) return;
        vic.disarmed = true;
        _drops.Add(new Drop { x = vic.x, vx = dir * 120, w = vic.weapon });
        _nums.Add(new DmgNum { x = vic.x, y = vic.y - 52, life = 1f, text = T("ОБЕЗОРУЖЕН!", "DISARMED!"), col = Color.Yellow });
        Sfx.Clang();
    }

    void Burst(float x, float y, int n, float dir, Color col)
    {
        for (int i = 0; i < n; i++)
            _parts.Add(new Particle
            {
                x = x, y = y,
                vx = dir * (40 + (float)_r.NextDouble() * 120) + ((float)_r.NextDouble() - 0.5f) * 80,
                vy = -(20 + (float)_r.NextDouble() * 140),
                life = 0.4f + (float)_r.NextDouble() * 0.5f,
                size = _r.Next(1, 3),
            });
        if (col == BloodC)
            for (int i = 0; i < n / 4; i++) // меньше пятен, только на земле
            {
                _decals.Add(new Decal { x = x + ((float)_r.NextDouble() - 0.5f) * 20, y = (float)_r.NextDouble() * 3 });
                if (_decals.Count > 150) _decals.RemoveAt(0);
            }
    }

    // ---------- render ----------

    int SX(float wx) => (int)Math.Round((wx - _camX) * ZOOM + 240 + _shX * ZOOM);
    int SY(float wy) => (int)Math.Round((wy + 40) * ZOOM + 135 + _shY * ZOOM);

    void R(Graphics g, float wx, float wy, float w, float h, Color c) =>
        g.FillRectangle(B(c), SX(wx), SY(wy), Math.Max(1, (int)Math.Round(w * ZOOM)), Math.Max(1, (int)Math.Round(h * ZOOM)));

    void RS(Graphics g, int sx, int sy, int w, int h, Color c) => g.FillRectangle(B(c), sx, sy, w, h);

    public void Render(Graphics g)
    {
        // ретро-текст БЕЗ сглаживания (иначе цветные ореолы при апскейле)
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        Ui.Begin(_mX, _mY, _clicked);
        if (_state == State.Menu || StateIsMenuScreen()) { DrawArena(g); DrawMenu(g); return; }
        DrawArena(g);
        foreach (var d in _decals) R(g, d.x, d.y, 2, 1, BloodDark);
        // валяющееся оружие
        foreach (var d in _drops)
        {
            var st = g.Save();
            g.TranslateTransform(SX(d.x) - 26, SY(-4));
            g.ScaleTransform(ZOOM / 2f, ZOOM / 2f);
            DrawWeapon(g, d.w, false);
            g.Restore(st);
        }
        if (_p != null && _e != null)
        {
            if (_p.y > _e.y) { DrawFighter(g, _e); DrawFighter(g, _p); }
            else { DrawFighter(g, _p); DrawFighter(g, _e); }
        }
        foreach (var q in _parts) R(g, q.x, q.y, q.size, q.size, BloodC);
        // метательные топоры босса
        foreach (var sh in _shots)
        {
            var sst = g.Save();
            g.TranslateTransform(SX(sh.x), SY(sh.y));
            if (sh.arrow)
            {
                g.RotateTransform((float)(Math.Atan2(sh.vy, sh.vx) * 180 / Math.PI));
                g.ScaleTransform(ZOOM / 2f, ZOOM / 2f);
                g.FillRectangle(B(Color.FromArgb(91, 58, 30)), -7, -1, 12, 2);
                g.FillPolygon(B(_bladeHi), new[] { new PointF(5, -2), new PointF(5, 2), new PointF(9, 0) });
            }
            else
            {
                g.RotateTransform(sh.life * 540);
                g.ScaleTransform(ZOOM / 2f, ZOOM / 2f);
                g.FillRectangle(B(Color.FromArgb(120, 125, 140)), -5, -1, 10, 2);
                g.FillRectangle(B(Color.FromArgb(200, 205, 215)), 3, -4, 3, 8);
            }
            g.Restore(sst);
        }
        // кольца топота
        foreach (var rg in _rings)
        {
            int rr = (int)(rg.r * ZOOM);
            int alpha = Math.Max(0, (int)(150 * (1 - rg.r / rg.maxR)));
            using var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 2);
            g.DrawEllipse(pen, SX(rg.x) - rr, SY(0) - 8, rr * 2, 10);
        }
        // цифры урона
        foreach (var n in _nums)
        {
            var pt = new PointF(SX(n.x) - 20, SY(n.y));
            g.DrawString(n.text, _f12, Brushes.Black, pt.X + 1, pt.Y + 1);
            g.DrawString(n.text, _f12, B(n.col), pt);
        }
        // счётчик комбо над игроком
        if (_combo >= 2 && _p != null && _p.alive)
        {
            var cp = new PointF(SX(_p.x) - 12, SY(_p.y - 58));
            g.DrawString("x" + _combo, _fBanner, Brushes.Black, cp.X + 2, cp.Y + 2);
            g.DrawString("x" + _combo, _fBanner, B(Color.Yellow), cp);
        }
        DrawHUD(g);
        DrawCrosshair(g);
        if (_fightT > 0.6f && _state == State.Fight)
        {
            var fb = Center(T("БОЙ!", "FIGHT!"), _fBanner, g, 120);
            g.DrawString(T("БОЙ!", "FIGHT!"), _fBanner, B(Color.Yellow), fb);
        }
        if (_replaying)
        {
            var rp = Center(T("▶ ПОВТОР (Esc — выйти)", "▶ REPLAY (Esc — exit)"), _f8, g, 34);
            g.DrawString(T("▶ ПОВТОР (Esc — выйти)", "▶ REPLAY (Esc — exit)"), _f8, B(Color.Yellow), rp);
        }
        // виньетка: затемнение по краям — киношно и красиво
        RS(g, 0, 0, W, 10, Color.FromArgb(36, 0, 0, 0));
        RS(g, 0, H - 12, W, 12, Color.FromArgb(48, 0, 0, 0));
        RS(g, 0, 0, 10, H, Color.FromArgb(36, 0, 0, 0));
        RS(g, W - 10, 0, 10, H, Color.FromArgb(36, 0, 0, 0));
        if (_paused && _state == State.Fight)
        {
            RS(g, 0, 0, W, H, Color.FromArgb(120, 0, 0, 0));
            Ui.Panel(g, new Rectangle(140, 100, 200, 148));
            var pb = Center(T("ПАУЗА", "PAUSED"), _fBanner, g, 104);
            g.DrawString(T("ПАУЗА", "PAUSED"), _fBanner, Brushes.White, pb);
            Btn(g, _btnResume, T("ДАЛЬШЕ", "RESUME"), _f12, false);
            Btn(g, _btnRestartP, T("ЗАНОВО (R)", "RESTART (R)"), _f12, false);
            Btn(g, _btnQuit, T("В МЕНЮ", "QUIT"), _f12, false);
            Btn(g, _btnSndP, T("ЗВУК: ", "SOUND: ") + (Sfx.Enabled ? T("ВКЛ", "ON") : T("ВЫКЛ", "OFF")), _f8, Sfx.Enabled);
        }
        if (_state == State.MatchEnd) DrawMenu(g);
    }

    void DrawArena(Graphics g)
    {
        // красивое небо: вертикальный градиент + тёплое солнце + дальние холмы
        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, W, 172),
            Color.FromArgb(126, 168, 214), Color.FromArgb(226, 232, 222), LinearGradientMode.Vertical))
            g.FillRectangle(sky, 0, 0, W, 172);
        using (var glow = new SolidBrush(Color.FromArgb(38, 255, 244, 200)))
        { g.FillEllipse(glow, 40, 20, 64, 64); g.FillEllipse(glow, 48, 28, 48, 48); }
        // дальние холмы (параллакс)
        for (int i = 0; i < 6; i++)
        {
            int hx = Mod((int)(-80 + i * 110 - _camX * 12), 620) - 70;
            g.FillEllipse(B(Color.FromArgb(150, 178, 150)), hx, 128, 130, 44);
        }
        for (int i = 0; i < 6; i++)
        {
            int hx = Mod((int)(-40 + i * 130 - _camX * 22), 620) - 70;
            g.FillEllipse(B(Color.FromArgb(132, 162, 130)), hx, 142, 120, 36);
        }
        // вытоптанная земля за ареной
        using (var soil = new LinearGradientBrush(new Rectangle(0, 168, W, 90),
            Color.FromArgb(168, 172, 140), Color.FromArgb(128, 136, 112), LinearGradientMode.Vertical))
            g.FillRectangle(soil, 0, 168, W, 90);

        // облака (параллакс от камеры)
        for (int i = 0; i < 4; i++)
        {
            int cx = Mod((int)(-160 + i * 130 + _time * (2 + i) + _camX * (4 + i * 2)), 560) - 40;
            int cy = 24 + i * 10;
            var cc = Color.FromArgb(235, 235, 228);
            var csh = Color.FromArgb(200, 205, 196);
            RS(g, cx, cy + 8, 68, 4, csh);
            RS(g, cx, cy, 68, 10, cc); RS(g, cx + 12, cy - 8, 44, 8, cc);
        }
        // фон страны: Япония / Германия / Русь (декор прибит к миру — видно, как камера едет)
        Theme bg = _theme == Theme.Custom ? (Theme)_decorBase : _theme;
        if (bg == Theme.Japan)
        {
            // красное солнце висит в небе
            var st = g.Save(); g.TranslateTransform(70, 50);
            g.FillEllipse(B(Color.FromArgb(200, 50, 55)), -20, -20, 40, 40);
            g.FillEllipse(B(Color.FromArgb(220, 90, 90)), -13, -13, 26, 26);
            g.Restore(st);
            // пагода (мир: -120)
            int pgx = SX(-120);
            for (int tier = 0; tier < 3; tier++)
            {
                int wdt = 52 - tier * 12, y = 140 - tier * 26;
                RS(g, pgx - wdt / 2, y, wdt, 22, Color.FromArgb(150, 60, 50));
                RS(g, pgx - wdt / 2 - 6, y - 6, wdt + 12, 6, Color.FromArgb(60, 50, 55));
            }
            RS(g, pgx - 2, 56, 4, 12, Color.FromArgb(201, 162, 39));
            // сакуры тянутся через всю арену
            int si0 = (int)Math.Floor((_camX - 160) / 170);
            for (int si = si0; si * 170 < _camX + 160; si++)
            {
                int x = SX(si * 170 + ((((si * 37) % 60) + 60) % 60) - 20);
                RS(g, x + 8, 120, 6, 34, Color.FromArgb(90, 65, 45));
                RS(g, x - 6, 92, 34, 30, Color.FromArgb(235, 170, 190));
                RS(g, x + 2, 82, 22, 16, Color.FromArgb(245, 200, 210));
            }
            // тории (мир: +100)
            int tor = SX(100);
            RS(g, tor - 8, 110, 8, 52, Color.FromArgb(180, 50, 50));
            RS(g, tor + 18, 110, 8, 52, Color.FromArgb(180, 50, 50));
            RS(g, tor - 16, 102, 52, 8, Color.FromArgb(180, 50, 50));
            RS(g, tor - 10, 116, 40, 5, Color.FromArgb(180, 50, 50));
        }
        else if (bg == Theme.Germany)
        {
            // ели через всю арену
            int ei0 = (int)Math.Floor((_camX - 160) / 170);
            for (int ei = ei0; ei * 170 < _camX + 160; ei++)
            {
                int x = SX(ei * 170 + ((((ei * 53) % 60) + 60) % 60) - 22);
                RS(g, x + 18, 130, 8, 26, Color.FromArgb(90, 65, 45));
                for (int tr = 0; tr < 3; tr++)
                {
                    int wdt = 46 - tr * 12, y = 122 - tr * 22;
                    g.FillPolygon(B(Color.FromArgb(45, 80, 55)),
                        new[] { new PointF(x + 22 - wdt / 2, y), new PointF(x + 22 + wdt / 2, y), new PointF(x + 22, y - 24) });
                }
            }
            // каменный замок (мир: +120)
            int cax = SX(120);
            RS(g, cax - 55, 110, 110, 55, Color.FromArgb(140, 135, 130));
            foreach (var off in new[] { -49, 27 })
            {
                RS(g, cax + off, 88, 22, 77, Color.FromArgb(130, 125, 120));
                g.FillPolygon(B(Color.FromArgb(150, 60, 60)),
                    new[] { new PointF(cax + off - 3, 88), new PointF(cax + off + 25, 88), new PointF(cax + off + 11, 62) });
            }
            RS(g, cax - 9, 132, 18, 33, Color.FromArgb(70, 60, 55));
            RS(g, cax - 41, 120, 8, 10, Color.FromArgb(255, 220, 130));
            RS(g, cax + 33, 120, 8, 10, Color.FromArgb(255, 220, 130));
            // фахверк (мир: -200)
            int fhx = SX(-200);
            RS(g, fhx - 40, 118, 80, 44, Color.FromArgb(225, 210, 180));
            RS(g, fhx - 46, 100, 92, 20, Color.FromArgb(80, 55, 40));
            for (int i = 0; i < 4; i++) RS(g, fhx - 36 + i * 20, 118, 5, 44, Color.FromArgb(100, 70, 50));
            RS(g, fhx - 40, 136, 80, 5, Color.FromArgb(100, 70, 50));
        }
        else if (bg == Theme.France)
        {
            // собор (мир: -140) + кипарисы через всю арену
            int cax = SX(-140);
            RS(g, cax - 30, 100, 60, 65, Color.FromArgb(210, 200, 180));
            g.FillPolygon(B(Color.FromArgb(120, 110, 130)),
                new[] { new PointF(cax - 34, 100), new PointF(cax + 34, 100), new PointF(cax, 58) });
            g.FillEllipse(B(Color.FromArgb(90, 110, 160)), cax - 11, 118, 22, 22);
            g.FillEllipse(B(Color.FromArgb(150, 180, 220)), cax - 7, 122, 14, 14);
            RS(g, cax - 8, 140, 16, 25, Color.FromArgb(80, 65, 50));
            int ki0 = (int)Math.Floor((_camX - 160) / 150);
            for (int ki = ki0; ki * 150 < _camX + 160; ki++)
            {
                int x = SX(ki * 150 + ((((ki * 31) % 50) + 50) % 50) - 20);
                g.FillEllipse(B(Color.FromArgb(45, 90, 55)), x - 6, 96, 16, 44);
                g.FillEllipse(B(Color.FromArgb(65, 115, 75)), x - 3, 100, 10, 30);
                RS(g, x - 1, 140, 3, 12, Color.FromArgb(90, 65, 45));
            }
        }
        else if (bg == Theme.England)
        {
            // донжон (мир: +140) + дубы через всю арену
            int cax = SX(140);
            RS(g, cax - 40, 105, 80, 60, Color.FromArgb(135, 130, 125));
            foreach (var off in new[] { -40, 22 })
            {
                RS(g, cax + off, 90, 18, 75, Color.FromArgb(125, 120, 115));
                RS(g, cax + off - 2, 84, 22, 6, Color.FromArgb(125, 120, 115));
            }
            RS(g, cax - 7, 140, 14, 25, Color.FromArgb(70, 60, 55));
            RS(g, cax - 25, 112, 10, 12, Color.FromArgb(255, 220, 130));
            RS(g, cax + 15, 112, 10, 12, Color.FromArgb(255, 220, 130));
            int oi0 = (int)Math.Floor((_camX - 160) / 160);
            for (int oi = oi0; oi * 160 < _camX + 160; oi++)
            {
                int x = SX(oi * 160 + ((((oi * 47) % 55) + 55) % 55) - 25);
                RS(g, x + 8, 118, 7, 30, Color.FromArgb(95, 70, 50));
                g.FillEllipse(B(Color.FromArgb(70, 125, 70)), x - 12, 82, 44, 40);
                g.FillEllipse(B(Color.FromArgb(90, 150, 90)), x - 4, 74, 28, 24);
            }
        }
        else if (bg == Theme.Horde)
        {
            // юрты (мир: ±) + бунчуки через всю арену, сухая степь
            for (int yi = -1; yi <= 1; yi++)
            {
                int yx = SX(yi * 150 - 60);
                RS(g, yx - 26, 128, 52, 30, Color.FromArgb(220, 205, 170));
                g.FillEllipse(B(Color.FromArgb(150, 110, 70)), yx - 26, 112, 52, 32);
                RS(g, yx - 3, 138, 6, 20, Color.FromArgb(90, 65, 45));
                RS(g, yx - 1, 96, 2, 18, Color.FromArgb(74, 55, 40));
                int sway = (int)(Math.Sin(_time * 4 + yi * 9) * 3);
                RS(g, yx + 1, 96, 14 + sway, 8, BloodC);
            }
            int ti0 = (int)Math.Floor((_camX - 160) / 120);
            for (int ti = ti0; ti * 120 < _camX + 160; ti++)
            {
                int x = SX(ti * 120 + ((((ti * 23) % 45) + 45) % 45) - 18);
                for (int b2 = 0; b2 < 3; b2++)
                    RS(g, x + b2 * 5, 150 + ((((ti * 17 + b2 * 11) % 20) + 20) % 20), 2, 8 + b2 * 3, Color.FromArgb(150, 130, 80));
            }
        }
        else
        {
            // берёзы через всю арену
            int bi0 = (int)Math.Floor((_camX - 160) / 170);
            for (int bi = bi0; bi * 170 < _camX + 160; bi++)
            {
                int x = SX(bi * 170 + ((((bi * 41) % 60) + 60) % 60) - 24);
                RS(g, x + 16, 108, 8, 52, Color.FromArgb(230, 225, 210));
                RS(g, x + 16, 120, 8, 2, Color.Black); RS(g, x + 16, 134, 8, 2, Color.Black); RS(g, x + 16, 148, 8, 2, Color.Black);
                RS(g, x - 4, 76, 48, 36, Color.FromArgb(120, 165, 95));
                RS(g, x + 4, 68, 32, 16, Color.FromArgb(140, 185, 110));
            }
            // кремлёвская стена (мир: -260..-60)
            RS(g, SX(-260), 130, SX(-60) - SX(-260), 32, Color.FromArgb(155, 60, 55));
            for (int wx = -260; wx < -60; wx += 12) RS(g, SX(wx), 124, 7, 6, Color.FromArgb(155, 60, 55));
            int twx = SX(-272);
            RS(g, twx, 100, 26, 62, Color.FromArgb(140, 55, 50));
            g.FillPolygon(B(Color.FromArgb(70, 130, 80)),
                new[] { new PointF(twx - 4, 100), new PointF(twx + 30, 100), new PointF(twx + 13, 72) });
            // церковь с луковицей (мир: +150)
            int chx = SX(150);
            RS(g, chx - 30, 115, 60, 47, Color.FromArgb(235, 230, 215));
            RS(g, chx - 12, 130, 24, 32, Color.FromArgb(90, 70, 55));
            var cst = g.Save(); g.TranslateTransform(chx, 100);
            g.FillEllipse(B(Color.FromArgb(70, 130, 200)), -14, -14, 28, 28);
            g.FillEllipse(B(Color.FromArgb(110, 170, 230)), -9, -9, 14, 14);
            g.Restore(cst);
            RS(g, chx - 1, 68, 2, 12, Color.FromArgb(201, 162, 39));
            RS(g, chx - 4, 71, 8, 2, Color.FromArgb(201, 162, 39));
        }
        // свои декорации поверх биома
        foreach (var d in _decor) DrawDecor(g, d.kind, d.x);
        // зеваки-деревенщины за забором (ликуют после смерти)
        float amp = _cheerT > 0 ? 4 : 1.5f;
        Color[] tunics = { Color.FromArgb(190, 175, 150), Color.FromArgb(175, 160, 135), Color.FromArgb(200, 185, 160) };
        Color[] hairs = { Color.FromArgb(80, 55, 35), Color.FromArgb(40, 30, 25), Color.FromArgb(150, 120, 70), Color.FromArgb(120, 120, 120) };
        // зеваки стоят вдоль всей арены (тоже привязаны к миру)
        int vi0 = (int)Math.Floor((_camX - 130) / 21);
        for (int i = vi0; i * 21 < _camX + 130; i++)
        {
            int t3 = ((i % 3) + 3) % 3, h4 = ((i % 4) + 4) % 4;
            int x = SX(i * 21 + (((i % 2) + 2) % 2) * 5) - 4;
            int y = (int)(Math.Sin(_time * 6 + i * 1.7) * amp);
            RS(g, x, 164 + y, 8, 14, tunics[t3]); // рубаха
            RS(g, x + 1, 158 + y, 6, 6, Skin); // голова
            RS(g, x + 1, 158 + y, 6, 2, hairs[h4]); // волосы
            if (t3 == 0) RS(g, x + 2, 161 + y, 4, 2, hairs[(h4 + 1) % 4]); // борода
        }
        // деревянный забор через всю арену (прибит к миру: столбы идут за камерой)
        R(g, _camX - 110, -35.7f, 220, 1.7f, Color.FromArgb(140, 110, 75));
        R(g, _camX - 110, -29.7f, 220, 1.7f, Color.FromArgb(140, 110, 75));
        int pi0 = (int)Math.Floor((_camX - 110) / 64);
        for (int i = pi0; i * 64 < _camX + 110; i++)
            RS(g, SX(i * 64) - 4, 140, 8, 42, Color.FromArgb(120, 92, 60));
        // трава арены: градиент + блик + тёмный низ
        int gy = SY(0);
        int gh = Math.Max(1, H - gy);
        using (var grass = new LinearGradientBrush(new Rectangle(0, gy, W, gh),
            Color.FromArgb(126, 168, 96), Color.FromArgb(82, 116, 64), LinearGradientMode.Vertical))
            g.FillRectangle(grass, 0, gy, W, gh);
        RS(g, 0, gy, W, 3, Color.FromArgb(150, 195, 115));
        RS(g, 0, H - 14, W, 14, Color.FromArgb(46, 56, 46));
        for (int i = (int)Math.Floor((_camX - 110) / 17); i * 17 < _camX + 110; i++)
            RS(g, SX(i * 17), gy + 8 + ((((i * 29) % 34) + 34) % 34), 4, 2, Color.FromArgb(95, 130, 70));
        for (int i = (int)Math.Floor((_camX - 110) / 9); i * 9 < _camX + 110; i++)
            if ((((i * 13) % 5) + 5) % 5 < 2) RS(g, SX(i * 9), gy - 2, 1, 3, Color.FromArgb(130, 175, 100));
    }

    static int Mod(int a, int n) { int m = a % n; return m < 0 ? m + n : m; }

    // Своя декорация по мировой координате: 0 ель, 1 берёза, 2 сакура, 3 камень, 4 знамя, 5 факел
    void DrawDecor(Graphics g, int kind, int wx)
    {
        int x = SX(wx);
        switch (kind)
        {
            case 0:
                RS(g, x - 4, 130, 8, 26, Color.FromArgb(90, 65, 45));
                for (int tr = 0; tr < 3; tr++)
                {
                    int wdt = 46 - tr * 12, y = 122 - tr * 22;
                    g.FillPolygon(B(Color.FromArgb(45, 80, 55)),
                        new[] { new PointF(x - wdt / 2, y), new PointF(x + wdt / 2, y), new PointF(x, y - 24) });
                }
                break;
            case 1:
                RS(g, x - 4, 108, 8, 52, Color.FromArgb(230, 225, 210));
                RS(g, x - 4, 120, 8, 2, Color.Black); RS(g, x - 4, 134, 8, 2, Color.Black);
                RS(g, x - 24, 76, 48, 36, Color.FromArgb(120, 165, 95));
                break;
            case 2:
                RS(g, x - 12, 120, 6, 34, Color.FromArgb(90, 65, 45));
                RS(g, x - 26, 92, 34, 30, Color.FromArgb(235, 170, 190));
                RS(g, x - 18, 82, 22, 16, Color.FromArgb(245, 200, 210));
                break;
            case 3:
                RS(g, x - 10, 196, 20, 10, Color.FromArgb(130, 130, 135));
                RS(g, x - 6, 190, 12, 8, Color.FromArgb(150, 150, 155));
                break;
            case 4:
                RS(g, x - 1, 130, 3, 52, Color.FromArgb(74, 55, 40));
                RS(g, x + 2, 130, 22 + (int)(Math.Sin(_time * 4 + wx) * 2), 12, BloodC);
                RS(g, x + 2, 130, 22, 3, Color.FromArgb(220, 80, 80));
                break;
            default:
                RS(g, x - 1, 150, 2, 22, Color.FromArgb(74, 55, 40));
                float f = (float)Math.Sin(_time * 12 + wx);
                RS(g, x - 3, 142, 6, 8, Color.FromArgb(226, 88, 34));
                RS(g, x - 2, 144 + (int)(f * 0.7), 4, 5, Color.FromArgb(255, 217, 122));
                break;
        }
    }

    // Детальные пиксель-спрайты бойца: S кожа, D тень, H/B волосы/борода,
    // E глаз, W порты, G пояс, K колено, F кулак/ступня. '.' = прозрачный.
    static readonly string[] HEAD_ROWS = {
        "HHHHHHHHHHHH",
        "HHHHHHHHHHHH",
        "HSSSSSSSSSSH",
        "HSEESSSEESSH",
        "HSSSSSSSSSSH",
        "HSSSDDSSSSSH",
        "HBBBBBBBBBBH",
        "HBDBBBBBBDBH",
        ".BBBBBBBBBB.",
        "..BBBBBBBB..",
    };
    static readonly string[] TORSO_ROWS = {
        "....SSSSSSSS....",
        "..SSSSSSSSSSSS..",
        ".SSSSSSSSSSSSSS.",
        ".SDDSSSSSSSDDSS.",
        ".SDDSSSDDSSSDDS.",
        ".SSSSSSDDSSSSSS.",
        ".SSSDDSSDDSSSSS.",
        ".SSSSSSDDSSSSSS.",
        ".SSSDDSSDDSSSSS.",
        ".SSSSSSSSSSSSDD.",
        "..SSSSSSSSSSSS..",
        "..DDDDDDDDDDDD..",
        "WWWWWWWWWWWWWWWW",
        "WWWWWWWWWWWWWWWW",
        "WWWWWGWWWWGWWWWW",
        "WWWWWWWWWWWWWWWW",
    };
    static readonly string[] LEGS_ROWS = {
        "SSSSSSSSSS",
        "SSSSSSSSSS",
        "SSSDDSSSDD",
        "SSSDDSSSDD",
        "SSSKKSSKKS",
        "SSSSSSSSSS",
        "SSSSSSSSSS",
        "SSSDDSSSDD",
        "SSSDDSSSDD",
        "SSSSSSSSSS",
        "SSSSSSSSSS",
        "SSSDDSSSDD",
        "SSSDDSSSDD",
        "SSSSSSSSSS",
        "SSSSSSSSSS",
        "SSSSSSSSSS",
        "FFFFFFFFFF",
        "FFFFFFFFFF",
    };
    static readonly string[] ARM_ROWS = {
        "DDDDDDDDDD",
        "DSSSSSSFFF",
        "SSSSSSSFFF",
        "DSSSSSSFFF",
        "DDDDDDDDDD",
    };

    static Bitmap Sprite(string[] rows, Dictionary<char, Color> pal)
    {
        int w = 0;
        foreach (var r in rows) w = Math.Max(w, r.Length);
        var bmp = new Bitmap(w, rows.Length, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                bmp.SetPixel(x, y, (c == '.' || c == ' ' || !pal.ContainsKey(c)) ? Color.Transparent : pal[c]);
            }
        return bmp;
    }

    void BuildSkin(Fighter f)
    {
        bool p = f.isPlayer;
        Color skin = p ? Color.FromArgb(232, 185, 138) : Color.FromArgb(214, 164, 124);
        Color skinD = p ? Color.FromArgb(198, 152, 110) : Color.FromArgb(182, 136, 100);
        Color knee = p ? Color.FromArgb(165, 125, 90) : Color.FromArgb(150, 110, 82);
        Color beard = p ? Color.FromArgb(70, 45, 28) : Color.FromArgb(30, 24, 20);
        Color trunk = p ? Color.FromArgb(70, 90, 140) : Color.FromArgb(130, 55, 55);
        Color trunkD = p ? Color.FromArgb(50, 65, 105) : Color.FromArgb(95, 40, 40);
        Color hand = p ? Color.FromArgb(210, 162, 118) : Color.FromArgb(192, 146, 106);
        Color foot = p ? Color.FromArgb(120, 85, 58) : Color.FromArgb(105, 75, 52);
        f.bHead = Sprite(HEAD_ROWS, new Dictionary<char, Color> { { 'H', beard }, { 'S', skin }, { 'E', Color.FromArgb(20, 15, 12) }, { 'D', skinD }, { 'B', beard } });
        f.bTorso = Sprite(TORSO_ROWS, new Dictionary<char, Color> { { 'S', skin }, { 'D', skinD }, { 'W', trunk }, { 'G', trunkD } });
        f.bLegs = Sprite(LEGS_ROWS, new Dictionary<char, Color> { { 'S', skin }, { 'D', skinD }, { 'K', knee }, { 'F', foot } });
        f.bArm = Sprite(ARM_ROWS, new Dictionary<char, Color> { { 'D', skinD }, { 'S', skin }, { 'F', hand } });
    }

    static void DropSkin(Fighter f)
    {
        if (f == null) return;
        if (f.bTorso != null) f.bTorso.Dispose();
        if (f.bHead != null) f.bHead.Dispose();
        if (f.bLegs != null) f.bLegs.Dispose();
        if (f.bArm != null) f.bArm.Dispose();
        f.bTorso = f.bHead = f.bLegs = f.bArm = null;
    }

    void Img(Graphics g, Bitmap bmp, float wx, float wy)
    {
        if (bmp == null) return;
        // арт 1px = 1 мировой единице: экранный размер = мировой × ZOOM
        g.DrawImage(bmp, new Rectangle(SX(wx), SY(wy), bmp.Width * ZOOM, bmp.Height * ZOOM));
    }

    void DrawFighter(Graphics g, Fighter f)
    {

        if (!f.alive)
        {
            // труп: всё тело заваливается набок одним куском
            var st = g.Save();
            g.TranslateTransform(SX(f.x), SY(0));
            g.RotateTransform(f.face > 0 ? 90 : -90);
            g.ScaleTransform(ZOOM / 2f, ZOOM / 2f);
            if (f.bLegs != null) g.DrawImageUnscaled(f.bLegs, -5, -36);
            if (f.bTorso != null) g.DrawImageUnscaled(f.bTorso, -8, -68);
            if (f.bHead != null) g.DrawImageUnscaled(f.bHead, -6, -88);
            g.Restore(st);
            int pool = Math.Min(44, 18 + (int)(f.deadT * 18));
            var pst = g.Save();
            g.TranslateTransform(SX(f.x), SY(0));
            g.FillEllipse(B(BloodDark), -pool / 2, -2, pool, 5);
            g.Restore(pst);
            return;
        }

        // мягкая тень-марионетка
        {
            float sw = 11 - Math.Min(6, -f.y * 0.05f);
            var sh = g.Save();
            g.TranslateTransform(SX(f.x), SY(0));
            g.FillEllipse(B(Color.FromArgb(60, 0, 0, 0)), (int)-sw, -3, (int)(sw * 2), 5);
            g.FillEllipse(B(Color.FromArgb(40, 0, 0, 0)), (int)-sw - 3, -2, (int)(sw * 2) + 6, 3);
            g.Restore(sh);
        }
        bool isP = f.isPlayer;
        Color skin = isP ? Color.FromArgb(240, 196, 150) : Color.FromArgb(224, 174, 132);
        Color skinD = isP ? Color.FromArgb(190, 140, 100) : Color.FromArgb(172, 126, 92);
        Color cloth = isP ? Color.FromArgb(62, 88, 158) : Color.FromArgb(158, 58, 58);
        Color clothD = isP ? Color.FromArgb(40, 58, 110) : Color.FromArgb(112, 38, 38);
        Color clothL = isP ? Color.FromArgb(110, 140, 220) : Color.FromArgb(220, 120, 120);
        Color pants = isP ? Color.FromArgb(52, 48, 70) : Color.FromArgb(70, 48, 44);
        Color pantsL = isP ? Color.FromArgb(88, 84, 120) : Color.FromArgb(120, 88, 80);
        Color glove = isP ? Color.FromArgb(96, 64, 40) : Color.FromArgb(84, 56, 36);
        Color boot = isP ? Color.FromArgb(66, 46, 30) : Color.FromArgb(60, 42, 30);
        if (isP && HasItem(1)) { cloth = Color.FromArgb(110, 115, 130); clothL = Color.FromArgb(170, 175, 190); clothD = Color.FromArgb(70, 75, 90); } // кольчуга видна
        if (isP && HasItem(3)) boot = Color.FromArgb(95, 100, 115); // сапоги стальные
        float wob = (float)Math.Sin(_time * 27 + (isP ? 0 : 2)) * f.offBal * 1.2f;
        float bob = f.onGround ? Math.Abs((float)Math.Sin(f.walkPh)) * -1.1f : 0;
        float hipX = f.x + wob * 0.4f, hipY = f.y - 18 + bob * 0.5f;
        float shXf = hipX + f.lean * 14 + f.face * 1.2f, shYf = hipY - 13 + bob * 0.5f;
        float face = f.face;
        // --- НОГИ: бедро + голень + ступня ---
        for (int leg = 0; leg < 2; leg++)
        {
            bool front = (leg == 0);
            float ph = f.walkPh + (front ? 0 : (float)Math.PI);
            float moveK = Math.Max(0, Math.Min(1, Math.Abs(f.vx) / 60f));
            float fx, fy;
            bool kicking = front && f.kickAnim > 0;
            if (!f.onGround) { fx = hipX + (front ? face * 6 : -face * 4) + wob * 0.5f; fy = f.y - (front ? 7 : 4); }
            else if (kicking) { float kk = f.kickAnim / 0.35f; fx = hipX + face * (7 + 13 * kk); fy = hipY + 9 - 7 * kk; }
            else { fx = hipX + (float)Math.Sin(ph) * 6.5f * (0.3f + 0.7f * moveK) + face * 1.5f; float lift = Math.Max(0, (float)Math.Sin(ph + Math.PI / 2)) * 3.2f * moveK; fy = f.y - lift; }
            float kneeX = (hipX + fx) / 2 + face * 2.6f + (kicking ? face * 2 : 0);
            float kneeY = (hipY + fy) / 2 + (kicking ? -1 : 1.2f);
            Color dd = front ? pants : Color.FromArgb(Math.Max(0, pants.R - 30), Math.Max(0, pants.G - 30), Math.Max(0, pants.B - 25));
            Color ll = front ? pantsL : pants;
            Bone(g, hipX, hipY, kneeX, kneeY, 3.4f, dd, ll);
            Bone(g, kneeX, kneeY, fx, fy, 2.8f, skinD, skin);
            JointBall(g, kneeX, kneeY, 1.5f, dd, ll);
            int fsx = SX(fx), fsy = SY(fy);
            g.FillEllipse(B(boot), fsx - 5, fsy - 2, 10, 4);
            g.FillRectangle(B(Color.FromArgb(120, 90, 60)), fsx - 5, fsy - 3, 10, 1);
        }
        // --- ТОРС ---
        Bone(g, hipX, hipY, shXf, shYf, 8.2f, clothD, cloth);
        Bone(g, hipX + 0.6f, hipY - 1, shXf + 0.6f, shYf + 1, 3.4f, cloth, clothL);
        JointBall(g, hipX + f.lean * 4, hipY - 1, 2.2f, Color.FromArgb(40, 30, 22), Color.FromArgb(201, 162, 39));
        if (isP && HasItem(12)) // амулет ярости на груди
            JointBall(g, (hipX + shXf) / 2, (hipY + shYf) / 2 + 1, 1.3f, Color.FromArgb(160, 30, 30), Color.FromArgb(255, 90, 90));
        JointBall(g, shXf, shYf, 2.6f, Color.FromArgb(90, 90, 100), Color.FromArgb(190, 190, 200));
        // --- ГОЛОВА ---
        float hx = shXf + face * 1.6f + wob * 0.3f, hy = shYf - 7.2f;
        JointBall(g, hx, hy, 4.6f, skinD, skin);
        {
            int hsx = SX(hx), hsy = SY(hy);
            Color dome = isP ? Color.FromArgb(150, 160, 182) : Color.FromArgb(96, 78, 60);
            Color domeHi = isP ? Color.FromArgb(208, 215, 230) : Color.FromArgb(142, 116, 90);
            g.FillEllipse(B(dome), hsx - 12, hsy - 14, 24, 17); // купол на всю голову
            g.FillEllipse(B(domeHi), hsx - 8, hsy - 12, 10, 7); // блик
            g.FillRectangle(B(dome), hsx - 13, hsy - 2, 26, 2); // поля
            if (f.isBoss) g.FillRectangle(B(Color.FromArgb(201, 162, 39)), hsx - 13, hsy - 2, 26, 1);
            if (isP && HasItem(0)) // плюмаж купленного шлема
            {
                g.FillRectangle(B(Color.FromArgb(200, 50, 50)), hsx - 2, hsy - 22, 5, 9);
                g.FillRectangle(B(Color.FromArgb(235, 100, 100)), hsx - 1, hsy - 22, 2, 9);
            }
            int nx = hsx + (int)face * 5;
            g.FillRectangle(B(dome), nx - 1, hsy - 3, 2, 8); // носовая стрелка
            g.FillRectangle(B(Color.White), hsx + (int)face * 1 - 2, hsy + 1, 4, 3);
            Color pup = f.isPlayer ? Color.FromArgb(20, 15, 12) : Color.FromArgb(180, 30, 30);
            g.FillRectangle(B(pup), hsx + (int)face * 3 - 1, hsy + 2, 2, 2);
            if (f.hurtT > 0) g.FillEllipse(B(Color.FromArgb(150, 220, 60, 60)), hsx - 7, hsy - 7, 14, 12);
        }
        // --- ЛЕВАЯ РУКА (сзади, у пояса; вперёд — только блок/тычок) ---
        {
            float guardX = hipX - face * 3.5f, guardY = shYf + 8f;
            if (!f.onGround) { guardX -= face * 1.5f; guardY -= 2; }
            guardX += (float)Math.Sin(f.walkPh + Math.PI) * 1f;
            if (f.blocking) { guardX = hipX + face * 8f; guardY = shYf + 2f; }
            if (f.jabAnim > 0) { float jk = f.jabAnim / 0.3f; guardX = hipX + face * (5 + 8 * jk); guardY = shYf + 4 - 3 * jk; }
            float elX = (shXf + guardX) / 2 - face * 2f, elY = (shYf + guardY) / 2 + 2.6f;
            Bone(g, shXf - face * 1, shYf + 1, elX, elY, 2.1f, skinD, skin);
            Bone(g, elX, elY, guardX, guardY, 1.9f, skinD, skin);
            JointBall(g, elX, elY, 1.1f, skinD, skin);
            JointBall(g, guardX, guardY, 1.4f, glove, Color.FromArgb(170, 120, 80));
            if (f.blocking)
            {
                var bs = g.Save();
                g.TranslateTransform(SX(guardX + face * 1.5f), SY(guardY));
                g.FillEllipse(B(Color.FromArgb(96, 72, 46)), -8, -8, 16, 16);
                g.FillEllipse(B(Color.FromArgb(150, 110, 70)), -7, -7, 14, 14);
                g.FillEllipse(B(Color.FromArgb(190, 190, 200)), -3, -3, 6, 6);
                g.FillEllipse(B(Color.White), -1, -1, 2, 2);
                if (f.isPlayer && HasItem(5))
                {
                    using var rp = new Pen(Color.FromArgb(201, 162, 39), 2);
                    g.DrawEllipse(rp, -8, -8, 16, 16); // крепкий щит с золотой оковкой
                }
                g.Restore(bs);
            }
        }

        // звёздочки оглушения
        if (f.stunT > 0)
            for (int i = 0; i < 3; i++)
            {
                float a = _time * 6 + i * 2.1f;
                RS(g, SX(f.x) + (int)(Math.Cos(a) * 10) - 1, SY(f.y - 50) + (int)(Math.Sin(a) * 3), 2, 2, Color.Yellow);
            }

        // --- ПРАВАЯ (оружейная) РУКА: короткая, кисть держит рукоять ---
        {
            float ang = f.armAng;
            float dx = (float)Math.Cos(ang), dy = (float)Math.Sin(ang);
            float px = -dy, py = dx;
            float ext = Math.Max(0, Math.Min(1.4f, f.armExt + f.punchT * 1.2f));
            float bend = (1.4f - ext * 0.9f) * (dx * face >= 0 ? 1 : -1);
            float hd = 6.5f + ext * 2.5f;
            float elX = shXf + dx * 3.5f + px * bend + wob * 0.3f;
            float elY = shYf + dy * 3.5f + py * bend;
            float handX = shXf + dx * hd + wob * 0.3f;
            float handY = shYf + dy * hd;
            Bone(g, shXf, shYf, elX, elY, 2.2f, skinD, skin);
            Bone(g, elX, elY, handX, handY, 2f, skinD, skin);
            JointBall(g, elX, elY, 1.1f, skinD, skin);
            if (!f.disarmed)
            {
                // оружие: рукоять В КИСТИ (сдвиг -5 чтобы не висело в воздухе)
                var st2 = g.Save();
                g.TranslateTransform(SX(handX), SY(handY));
                g.RotateTransform(ang * 180 / (float)Math.PI);
                g.ScaleTransform(ZOOM / 2f, ZOOM / 2f);
                g.TranslateTransform(-5, 0);
                DrawWeapon(g, f.weapon, f.charged);
                g.Restore(st2);
                // кисть поверх рукояти — видно что держит
                JointBall(g, handX, handY, 1.5f, glove, Color.FromArgb(175, 125, 85));
            }
            else JointBall(g, handX, handY, 1.6f, glove, Color.FromArgb(175, 125, 85)); // кулак
            // искры заряженного замаха ИИ
            if (f.charged)
            {
                int flick = _rv.Next(3);
                int csx = SX(shXf), csy = SY(shYf);
                for (int i = 0; i <= flick; i++)
                    RS(g, csx + _rv.Next(-8, 8), csy + _rv.Next(-8, 8), 1, 1, Color.Yellow);
            }
        }

        // след клинка: последние положения кончика
        for (int i = 0; i < 8; i++)
        {
            int j = (f.trailIdx + i) % 8;
            int a = 14 + i * 14;
            RS(g, SX(f.trailX[j]), SY(f.trailY[j]), 2, 2,
                f.charged ? Color.FromArgb(a, 255, 220, 100) : Color.FromArgb(a, 255, 255, 255));
        }
    }

    // Капсула-кость марионетки: толстая линия со скруглением + светлая сердцевина.
    void Bone(Graphics g, float x1, float y1, float x2, float y2, float w, Color dark, Color lite)
    {
        int sx1 = SX(x1), sy1 = SY(y1), sx2 = SX(x2), sy2 = SY(y2);
        float pw = Math.Max(2, w * ZOOM);
        using var p1 = new Pen(dark, pw) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p1, sx1, sy1, sx2, sy2);
        using var p2 = new Pen(lite, Math.Max(1, pw * 0.55f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p2, sx1, sy1 - 1, sx2, sy2 - 1);
    }
    void JointBall(Graphics g, float wx, float wy, float r, Color c, Color hi)
    {
        int s = (int)Math.Max(2, r * 2 * ZOOM);
        int cx = SX(wx) - s / 2, cy = SY(wy) - s / 2;
        g.FillEllipse(B(c), cx, cy, s, s);
        g.FillEllipse(B(hi), cx + 1, cy + 1, Math.Max(1, s / 2), Math.Max(1, s / 2));
    }

    void DrawWeapon(Graphics g, Weapon w, bool charged)
    {
        var steel = B(charged ? Color.FromArgb(255, 240, 180) : _bladeHi);
        var steelD = B(_bladeLo);
        var wood = B(Color.FromArgb(91, 58, 30));
        var gold = B(charged ? Color.FromArgb(255, 240, 180) : _trim);
        switch (w)
        {
            case Weapon.Sword:
                g.FillRectangle(wood, 5, -1, 5, 2);
                g.FillRectangle(gold, 10, -3, 2, 6);
                g.FillRectangle(steel, 12, -1.5f, 15, 3);
                g.FillRectangle(steelD, 27, -1.5f, 3, 3);
                break;
            case Weapon.Axe:
                g.FillRectangle(wood, 5, -1, 13, 2);
                g.FillRectangle(steelD, 15, -6, 3, 12);
                g.FillRectangle(steel, 18, -6, 4, 12);
                break;
            case Weapon.Mace:
                g.FillRectangle(wood, 5, -1, 11, 2);
                g.FillEllipse(steelD, 15, -5, 10, 10);
                g.FillRectangle(steel, 18, -7, 3, 3); g.FillRectangle(steel, 18, 4, 3, 3);
                g.FillRectangle(steel, 13, -2, 3, 3);
                break;
            case Weapon.Spear:
                g.FillRectangle(wood, 5, -1, 24, 2);
                g.FillPolygon(steel, new[] { new PointF(29, -3), new PointF(29, 3), new PointF(35, 0) });
                break;
            case Weapon.Dagger:
                g.FillRectangle(wood, 5, -1, 4, 2);
                g.FillRectangle(gold, 9, -2, 1, 4);
                g.FillRectangle(steel, 10, -1, 7, 2);
                g.FillPolygon(steel, new[] { new PointF(17, -1.5f), new PointF(17, 1.5f), new PointF(20, 0) });
                break;
            case Weapon.Flail:
                g.FillRectangle(wood, 5, -1, 9, 2);
                g.FillRectangle(steelD, 14, -2, 2, 2); g.FillRectangle(steel, 16, 1, 2, 2); g.FillRectangle(steelD, 18, -2, 2, 2);
                g.FillEllipse(steelD, 20, -4, 8, 8);
                g.FillEllipse(steel, 22, -2, 3, 3);
                break;
            case Weapon.Pike:
                g.FillRectangle(wood, 5, -1, 30, 2);
                g.FillPolygon(steel, new[] { new PointF(35, -2.5f), new PointF(35, 2.5f), new PointF(40, 0) });
                g.FillRectangle(gold, 5, -1, 2, 2);
                break;
            case Weapon.Hammer:
                g.FillRectangle(wood, 5, -1, 12, 2);
                g.FillRectangle(steelD, 15, -6, 7, 12);
                g.FillRectangle(steel, 15, -6, 7, 3);
                g.FillRectangle(gold, 17, -8, 3, 2);
                break;
            case Weapon.Bow:
                g.FillRectangle(wood, 8, -6, 2, 12);
                g.FillPolygon(wood, new[] { new PointF(8, -6), new PointF(8, 6), new PointF(13, 0) });
                g.FillRectangle(steelD, 4, -1, 9, 1);
                g.FillPolygon(steel, new[] { new PointF(13, -2), new PointF(13, 2), new PointF(16, 0) });
                break;
            case Weapon.Halberd:
                g.FillRectangle(wood, 5, -1, 26, 2);
                g.FillPolygon(steel, new[] { new PointF(31, -4), new PointF(31, 4), new PointF(37, 0) });
                g.FillRectangle(steelD, 26, -5, 4, 3);
                g.FillRectangle(gold, 5, -1, 2, 2);
                break;
        }
    }

    void Bar(Graphics g, int x, int hp, Color fill)
    {
        RS(g, x, 10, 120, 12, Color.Black);
        RS(g, x + 2, 12, 116, 8, Color.FromArgb(120, 30, 30));
        int w = (int)(116 * Math.Max(0, hp) / 100f);
        RS(g, x + 2, 12, w, 8, fill);
        RS(g, x + 2, 12, w, 2, Color.FromArgb(255, 255, 255, 90));
        for (int i = 1; i < 5; i++) RS(g, x + 2 + i * 22, 12, 1, 8, Color.FromArgb(0, 0, 0, 120));
    }

    void CoinBox(Graphics g)
    {
        RS(g, 8, 8, 64, 26, Color.FromArgb(90, 60, 25));
        RS(g, 9, 9, 62, 24, Color.FromArgb(210, 160, 70));
        RS(g, 9, 9, 62, 2, Color.FromArgb(240, 200, 120));
        var st = g.Save();
        g.TranslateTransform(22, 21);
        g.FillEllipse(B(Color.FromArgb(240, 200, 90)), -7, -7, 14, 14);
        g.FillEllipse(B(Color.FromArgb(190, 140, 60)), -7, -7, 14, 3);
        g.Restore(st);
        g.DrawString(_coins.ToString(), _f12, Brushes.Black, 33, 12);
    }

    void PauseBtn(Graphics g)
    {
        var r = _btnPause;
        bool hot = r.Contains(_mX, _mY);
        RS(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(90, 60, 25));
        RS(g, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2,
            hot ? Color.FromArgb(230, 180, 90) : Color.FromArgb(210, 160, 70));
        if (_paused) // в паузе — крестик
        {
            RS(g, r.X + 14, r.Y + 7, 3, 12, Color.Black);
            RS(g, r.X + 20, r.Y + 7, 3, 12, Color.Black);
            RS(g, r.X + 14, r.Y + 7, 9, 3, Color.Black);
            RS(g, r.X + 14, r.Y + 16, 9, 3, Color.Black);
        }
        else
        {
            RS(g, r.X + 14, r.Y + 7, 4, 12, Color.Black);
            RS(g, r.X + 21, r.Y + 7, 4, 12, Color.Black);
        }
    }

    void DrawHUD(Graphics g)
    {
        var ink = B(Color.FromArgb(60, 50, 40));
        CoinBox(g);
        // фляга зелий под монетами
        RS(g, 8, 38, 10, 12, Color.FromArgb(90, 60, 25));
        RS(g, 10, 34, 6, 5, Color.FromArgb(150, 150, 160));
        RS(g, 10, 41, 6, 6, Color.FromArgb(220, 60, 60));
        g.DrawString("×" + SaveData.Potions + T(" (Q)", " (Q)"), _f8, ink, 22, 38);
        Bar(g, 80, (int)(_p.hp / _p.maxHp * 100), Color.FromArgb(61, 220, 95));
        Bar(g, 260, (int)(_e.hp / _e.maxHp * 100), _e.isBoss ? Color.FromArgb(150, 80, 200) : Color.FromArgb(230, 57, 70));
        g.DrawString(Fit(g, _mode == Mode.Versus ? T("ИГРОК 1", "PLAYER 1") : T("ВИТЯЗЬ", "KNIGHT"), _f8, 120), _f8, ink, 82, 25);
        string en = Fit(g, EnemyName(), _f8, 130);
        g.DrawString(en, _f8, ink, 400 - g.MeasureString(en, _f8).Width, 25);
        string mid = _mode == Mode.Camp ? T("ЭТАП ", "STAGE ") + (_stageIdx + 1) + "/" + CurStages().Length
            : _mode == Mode.Train ? T("ТРЕНИРОВКА", "TRAINING")
            : _mode == Mode.Survive ? T("ВОЛНА ", "WAVE ") + _round
            : _mode == Mode.Versus ? T("ВДВОЁМ", "2P") : T("РАУНД ", "ROUND ") + _round;
        mid = Fit(g, mid, _f8, 76);
        var rn = Center(mid, _f8, g, 28);
        g.DrawString(mid, _f8, ink, rn);
        if (_mode == Mode.Quick)
        {
            var sc = Center(_scoreP + ":" + _scoreE, _f12, g, 6);
            g.DrawString(_scoreP + ":" + _scoreE, _f12, ink, sc);
        }
        PauseBtn(g);
        // низ экрана чистый, без текста
        if (_bannerT > 0 && _state == State.Fight)
        {
            string bn = Fit(g, _banner, _fBanner, 440);
            var b = Center(bn, _fBanner, g, 100);
            g.DrawString(bn, _fBanner, Brushes.Black, b.X + 2, b.Y + 2);
            g.DrawString(bn, _fBanner, Brushes.White, b);
        }
        if (_flash > 0)
            RS(g, 0, 0, W, H, Color.FromArgb((int)(_flash * 100), 200, 0, 0));
        if (_whiteFlash > 0)
            RS(g, 0, 0, W, H, Color.FromArgb((int)(_whiteFlash * 120), 255, 255, 255));
    }

    void DrawCrosshair(Graphics g)
    {
        // пиксельный прицел + пунктирная линия наводки от плеча
        if (_p != null && _p.alive && _state == State.Fight)
        {
            int shx = SX(_p.x), shy = SY(-32);
            float dx = _mX - shx, dy = _mY - shy;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 4)
            {
                int steps = (int)(len / 14);
                for (int i = 1; i < steps; i++)
                {
                    int px = (int)(shx + dx * i / steps), py = (int)(shy + dy * i / steps);
                    if ((i % 2) == 0) RS(g, px, py, 2, 2, Color.FromArgb(120, 255, 255, 255));
                }
            }
        }
        RS(g, _mX - 7, _mY, 15, 2, BloodC);
        RS(g, _mX, _mY - 7, 2, 15, BloodC);
        RS(g, _mX - 1, _mY - 1, 3, 3, Color.White);
    }

    static PointF Center(string t, Font f, Graphics g, int y)
    {
        var s = g.MeasureString(t, f);
        return new PointF((W - s.Width) / 2, y);
    }

    // текст в рамку: режем с многоточием, чтобы не вылезал за края
    static string Fit(Graphics g, string t, Font f, int maxW)
    {
        if (maxW < 10 || g.MeasureString(t, f).Width <= maxW) return t;
        while (t.Length > 1 && g.MeasureString(t + "…", f).Width > maxW) t = t.Substring(0, t.Length - 1);
        return t + "…";
    }

    // мини-иконка товара магазина (12px): шлем, кольчуга, клинок, сапоги, сердце,
    // щит, зелье, наручи, поножи, плащ, кастет
    void DrawItemIcon(Graphics g, int idx, int sx, int sy)
    {
        var st = g.Save();
        g.TranslateTransform(sx, sy);
        switch (idx)
        {
            case 0: // шлем
                g.FillRectangle(B(Color.FromArgb(150, 150, 160)), 1, 3, 10, 7);
                g.FillRectangle(B(Color.FromArgb(200, 200, 210)), 1, 3, 10, 2);
                g.FillRectangle(B(Color.FromArgb(90, 90, 100)), 5, 6, 2, 4);
                break;
            case 1: // кольчуга
                g.FillRectangle(B(Color.FromArgb(110, 110, 125)), 2, 1, 8, 11);
                g.FillRectangle(B(Color.FromArgb(150, 150, 170)), 2, 1, 8, 2);
                g.FillRectangle(B(Color.FromArgb(80, 80, 95)), 4, 4, 1, 7); g.FillRectangle(B(Color.FromArgb(80, 80, 95)), 7, 4, 1, 7);
                break;
            case 2: // клинок
                g.FillRectangle(B(Color.FromArgb(91, 58, 30)), 1, 5, 3, 2);
                g.FillRectangle(B(Color.FromArgb(201, 162, 39)), 4, 4, 1, 4);
                g.FillRectangle(B(_bladeHi), 5, 5, 7, 2);
                break;
            case 3: // сапоги
                g.FillRectangle(B(Color.FromArgb(110, 75, 45)), 2, 2, 3, 8);
                g.FillRectangle(B(Color.FromArgb(110, 75, 45)), 7, 2, 3, 8);
                g.FillRectangle(B(Color.FromArgb(70, 45, 28)), 1, 9, 5, 2); g.FillRectangle(B(Color.FromArgb(70, 45, 28)), 6, 9, 5, 2);
                break;
            case 4: // сердце
                g.FillEllipse(B(Color.FromArgb(220, 60, 60)), 1, 3, 5, 5);
                g.FillEllipse(B(Color.FromArgb(220, 60, 60)), 6, 3, 5, 5);
                g.FillPolygon(B(Color.FromArgb(220, 60, 60)), new[] { new PointF(1, 6), new PointF(11, 6), new PointF(6, 12) });
                break;
            case 5: // щит
                g.FillEllipse(B(Color.FromArgb(110, 85, 55)), 1, 1, 10, 10);
                g.FillEllipse(B(Color.FromArgb(150, 110, 70)), 2, 2, 8, 8);
                g.FillEllipse(B(Color.FromArgb(190, 190, 200)), 4, 4, 4, 4);
                break;
            case 6: // зелье
                g.FillRectangle(B(Color.FromArgb(150, 150, 160)), 4, 1, 4, 3);
                g.FillRectangle(B(Color.FromArgb(220, 60, 60)), 3, 4, 6, 7);
                g.FillRectangle(B(Color.FromArgb(255, 150, 150)), 4, 5, 2, 2);
                break;
            case 7: // наручи
                g.FillRectangle(B(Color.FromArgb(96, 64, 40)), 1, 4, 10, 5);
                g.FillRectangle(B(Color.FromArgb(201, 162, 39)), 1, 4, 10, 1); g.FillRectangle(B(Color.FromArgb(201, 162, 39)), 1, 8, 10, 1);
                g.FillRectangle(B(Color.FromArgb(201, 162, 39)), 5, 4, 2, 5);
                break;
            case 8: // поножи
                g.FillRectangle(B(Color.FromArgb(130, 130, 145)), 2, 1, 3, 11);
                g.FillRectangle(B(Color.FromArgb(130, 130, 145)), 7, 1, 3, 11);
                g.FillRectangle(B(Color.FromArgb(90, 90, 105)), 2, 5, 3, 1); g.FillRectangle(B(Color.FromArgb(90, 90, 105)), 7, 5, 3, 1);
                break;
            case 9: // плащ
                g.FillPolygon(B(Color.FromArgb(70, 50, 110)), new[] { new PointF(6, 0), new PointF(11, 12), new PointF(1, 12) });
                g.FillPolygon(B(Color.FromArgb(100, 75, 150)), new[] { new PointF(6, 0), new PointF(8, 12), new PointF(4, 12) });
                break;
            default: // кастет
                g.FillRectangle(B(Color.FromArgb(120, 120, 130)), 1, 6, 10, 3);
                for (int k = 0; k < 4; k++) g.FillRectangle(B(Color.FromArgb(160, 160, 175)), 1 + k * 3, 3, 2, 4);
                break;
            case 11: // зеркальный доспех
                g.FillRectangle(B(Color.FromArgb(140, 145, 160)), 2, 1, 8, 11);
                g.FillRectangle(B(Color.FromArgb(220, 230, 245)), 3, 2, 3, 4);
                g.FillRectangle(B(Color.FromArgb(90, 95, 110)), 2, 1, 8, 1); g.FillRectangle(B(Color.FromArgb(90, 95, 110)), 2, 11, 8, 1);
                break;
            case 12: // амулет ярости
                g.FillRectangle(B(Color.FromArgb(201, 162, 39)), 5, 1, 2, 3);
                g.FillEllipse(B(Color.FromArgb(200, 40, 40)), 3, 4, 6, 6);
                g.FillEllipse(B(Color.FromArgb(255, 120, 120)), 4, 5, 2, 2);
                break;
            case 13: // кольцо регенерации
                g.FillEllipse(B(Color.FromArgb(60, 180, 90)), 2, 2, 8, 8);
                g.FillEllipse(B(Color.FromArgb(20, 16, 30)), 4, 4, 4, 4);
                g.FillRectangle(B(Color.FromArgb(120, 255, 150)), 5, 1, 2, 3);
                break;
        }
        g.Restore(st);
    }

    // --- публичное API движка для редактора/консоли ---
    public bool GodMode = false;
    public float TimeMul = 1f, AggrMul = 1f;
    public bool Paused { get => _paused; set => _paused = value; }
    public void SetArena(int theme) { _theme = (Theme)(((theme % 7) + 7) % 7); }
    public int GetArena() => (int)_theme;
    public int GetCustomBase() => _decorBase;
    public void SetPlayerWeapon(int w) { _selected = (Weapon)(((w % 10) + 10) % 10); if (_p != null) _p.weapon = _selected; }
    public void SetEnemyWeapon(int w) { if (_e != null) _e.weapon = (Weapon)(((w % 10) + 10) % 10); }
    public int GetPlayerWeapon() => _p != null ? (int)_p.weapon : (int)_selected;
    public int GetEnemyWeapon() => _e != null ? (int)_e.weapon : 0;
    public void GiveCoins(int n) { _coins += n; }
    public void Heal() { if (_p != null) _p.hp = _p.maxHp; if (_e != null) _e.hp = _e.maxHp; }
    public void HealPlayer() { if (_p != null) _p.hp = _p.maxHp; }
    public void Boom() { Burst(_camX, -30, 40, 1, BloodC); Burst(_camX, -30, 20, -1, BloodC); _shake = 6; Sfx.Hit(); }
    public void Restart() { StartMatch(); }
    public void KickPlayer() { if (_p != null && _e != null) DoKick(_p, _e); }
    public void JabPlayer() { if (_p != null && _e != null) DoJab(_p, _e); }
    public void KickEnemy() { if (_p != null && _e != null) DoKick(_e, _p); }
    public float WorldX(int sx) => (sx - 240) / (float)ZOOM + _camX;
    // инспектор Unity-стиля: чтение/запись марионеток и камеры
    public float GetPlayerX() => _p != null ? _p.x : 0;
    public float GetPlayerY() => _p != null ? _p.y : 0;
    public float GetEnemyX() => _e != null ? _e.x : 0;
    public float GetEnemyY() => _e != null ? _e.y : 0;
    public float GetPlayerHP() => _p != null ? _p.hp : 0;
    public float GetPlayerMaxHP() => _p != null ? _p.maxHp : 100;
    public float GetEnemyHP() => _e != null ? _e.hp : 0;
    public float GetEnemyMaxHP() => _e != null ? _e.maxHp : 100;
    public float GetCamX() => _camX;
    public void SetCamX(float v) => _camX = Math.Max(-420, Math.Min(420, v));
    public void SetPlayerX(float v) { if (_p != null) _p.x = Math.Max(-420, Math.Min(420, v)); }
    public void SetEnemyX(float v) { if (_e != null) _e.x = Math.Max(-420, Math.Min(420, v)); }
    public void SetPlayerHP(float v) { if (_p != null) _p.hp = Math.Max(1, Math.Min(_p.maxHp, v)); }
    public void SetEnemyHP(float v) { if (_e != null) _e.hp = Math.Max(1, Math.Min(_e.maxHp, v)); }
    public string GetStateName() => _state.ToString();
    public string GetStats() => _coins + " монет, счёт " + _scoreP + ":" + _scoreE + ", раунд " + _round;
    public int GetCoins() => _coins;
    public Decor GetDecorAt(int i) => (i >= 0 && i < _decor.Count) ? _decor[i] : new Decor();
    public void SetDecorAt(int i, int kind, int x) { if (i >= 0 && i < _decor.Count) _decor[i] = new Decor { kind = kind, x = x }; }
    public void RemoveDecorAt(int i) { if (i >= 0 && i < _decor.Count) _decor.RemoveAt(i); }
    public string[] DecorNames() { var a = new string[_decor.Count]; for (int i = 0; i < a.Length; i++) a[i] = "#" + i + " kind=" + _decor[i].kind + " x=" + _decor[i].x; return a; }

    // --- своя арена: декор поверх выбранного биома ---
    public struct Decor { public int kind, x; }
    readonly System.Collections.Generic.List<Decor> _decor = new System.Collections.Generic.List<Decor>();
    int _decorBase;
    public void SetCustomBase(int b) { _decorBase = ((b % 3) + 3) % 3; }
    public void AddDecor(int kind, float worldX)
    {
        _decor.Add(new Decor { kind = ((kind % 6) + 6) % 6, x = (int)Math.Max(-ARENA, Math.Min(ARENA, worldX)) });
    }
    public void RemoveDecorNear(float worldX, float r)
    {
        for (int i = _decor.Count - 1; i >= 0; i--)
            if (Math.Abs(_decor[i].x - worldX) < r) { _decor.RemoveAt(i); break; }
    }
    public void ClearArena() { _decor.Clear(); }
    public bool HasCustom() => _decor.Count > 0;
    public int DecorCount() => _decor.Count;
    public void SaveArena()
    {
        var lines = new System.Collections.Generic.List<string> { "base=" + _decorBase };
        foreach (var d in _decor) lines.Add(d.kind + "," + d.x);
        SaveData.StoreArena(lines);
    }
    public void LoadArena()
    {
        _decor.Clear();
        try
        {
            foreach (var line in SaveData.LoadArena())
            {
                var p = line.Split(',');
                if (p.Length == 1 && p[0].StartsWith("base=") && int.TryParse(p[0].Substring(5), out int b))
                    _decorBase = Math.Max(0, Math.Min(2, b));
                else if (p.Length == 2 && int.TryParse(p[0], out int k) && int.TryParse(p[1], out int x))
                    AddDecor(k, x);
            }
        }
        catch { }
    }

    // Состояние для автотеста симуляции (целые числа, инвариантная культура не нужна)
    public string GetState()
    {
        string f(Fighter x) => x == null ? "-,-,-" : ((int)x.hp) + "," + ((int)x.x) + "," + ((int)x.y);
        string w(Fighter x) => x == null ? "-" : ((int)x.weapon).ToString();
        return ((int)_state) + "|" + _scoreP + "|" + _scoreE + "|" + _round + "|"
             + f(_p) + "|" + f(_e) + "|" + ((int)_camX) + "|" + _parts.Count + "|" + _decals.Count
             + "|" + ((int)_mode) + "|" + _stageIdx + "|" + _drops.Count + "|" + w(_p) + "|" + w(_e);
    }

    void Btn(Graphics g, Rectangle r, string text, Font f, bool active) =>
        Ui.Button(g, r, text, f, active);

    void DrawMenu(Graphics g)
    {
        RS(g, 0, 0, W, H, Color.FromArgb(180, 0, 0, 0));
        if (_state == State.Menu)
        {
            var t = Center("BLOODY PIXELS", _fTitle, g, 24);
            g.DrawString("BLOODY PIXELS", _fTitle, B(Color.FromArgb(230, 57, 70)), t);
        }
        if (_state == State.Menu)
        {
            var s = Center(T("рыцарский мордобой на курсоре", "cursor swordfighting"), _f12, g, 58);
            g.DrawString(T("рыцарский мордобой на курсоре", "cursor swordfighting"), _f12, Brushes.White, s);
            string rec = Fit(g, T("Рекорды — комбо: x", "Best — combo: x") + SaveData.BestCombo + T(" • кубышка: ", " • bank: ") + SaveData.Bank + T(" • кампании: ", " • campaigns: ") + SaveData.CampWins + T(" • ачивки: ", " • awards: ") + AchCount() + "/6", _f8, 460);
            var rc = Center(rec, _f8, g, 72);
            g.DrawString(rec, _f8, B(Color.FromArgb(200, 200, 210)), rc);
            for (int i = 0; i < 10; i++)
            {
                Btn(g, _btnWeapon[i], WDesc((Weapon)i, (i + 1) % 10), _f12, (Weapon)i == _selected);
                var wst = g.Save(); // мини-рисунок оружия слева от строки
                g.TranslateTransform(_btnWeapon[i].X - 22, _btnWeapon[i].Y + _btnWeapon[i].Height / 2);
                g.ScaleTransform(0.45f, 0.45f);
                DrawWeapon(g, (Weapon)i, false);
                g.Restore(wst);
            }
            var ll = Center(T("Язык / Language:", "Language / Язык:"), _f8, g, 170);
            g.DrawString(T("Язык / Language:", "Language / Язык:"), _f8, Brushes.White, ll);
            Btn(g, _btnLang[0], "RU", _f12, _lang == Lang.RU);
            Btn(g, _btnLang[1], "EN", _f12, _lang == Lang.EN);
            Btn(g, _btnQuick, T("БОЙ", "FIGHT"), _f12, _mode == Mode.Quick);
            Btn(g, _btnVersus, T("ВДВОЁМ (V)", "2P (V)"), _f12, _mode == Mode.Versus);
            Btn(g, _btnCamp, T("КАМПАНИЯ", "CAMPAIGN"), _f12, _mode == Mode.Camp);
            Btn(g, _btnHelp, T("ПОМОЩЬ (H)", "HELP (H)"), _f8, false);
            Btn(g, _btnTrain, T("ТРЕНЯ (T)", "GYM (T)"), _f8, _mode == Mode.Train);
            Btn(g, _btnSurv, T("ВЫЖИВ (Y)", "SURV (Y)"), _f8, _mode == Mode.Survive);
            Btn(g, _btnShop, T("МАГАЗИН", "SHOP"), _f8, false);
            Btn(g, _btnSettings, T("НАСТР (O)", "SET (O)"), _f8, false);
        }
        else if (_state == State.Map)
        {
            string cn = _lang == Lang.RU ? Countries.ALL[_campCountry].ru : Countries.ALL[_campCountry].en;
            var mt = Center(cn + T(" — выбери этап", " — pick a stage"), _fBanner, g, 30);
            g.DrawString(cn + T(" — выбери этап", " — pick a stage"), _fBanner, Brushes.White, mt);
            var bk = Center(T("Кубышка: ", "Bank: ") + SaveData.Bank, _f12, g, 66);
            g.DrawString(T("Кубышка: ", "Bank: ") + SaveData.Bank, _f12, B(Color.Yellow), bk);
            RS(g, 28, 148, 424, 3, Color.FromArgb(140, 110, 75));
            for (int i = 0; i < CurStages().Length; i++)
            {
                var r = NodeRect(i);
                bool done = i < CampUnl(), cur = i == CampUnl();
                string nm = _lang == Lang.RU ? CurStages()[i].ru : CurStages()[i].en;
                nm = Fit(g, nm, _f8, r.Width - 6);
                RS(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(20, 16, 30));
                var bc = done ? Color.FromArgb(61, 220, 95) : cur ? Color.Yellow : Color.FromArgb(90, 90, 100);
                RS(g, r.X, r.Y, r.Width, 2, bc); RS(g, r.X, r.Y + r.Height - 2, r.Width, 2, bc);
                RS(g, r.X, r.Y, 2, r.Height, bc); RS(g, r.X + r.Width - 2, r.Y, 2, r.Height, bc);
                g.DrawString((i + 1).ToString(), _fBanner, B(bc), r.X + (r.Width - 20) / 2, r.Y + 4);
                g.DrawString(nm, _f8, Brushes.White, r.X + (r.Width - g.MeasureString(nm, _f8).Width) / 2, r.Y + 44);
                string hp = "HP " + ((int)CurStages()[i].hp);
                g.DrawString(hp, _f8, B(Color.FromArgb(180, 255, 255, 255)), r.X + (r.Width - g.MeasureString(hp, _f8).Width) / 2, r.Y + 58);
                if (!done && !cur)
                    g.DrawString("X", _fBanner, B(Color.FromArgb(120, 120, 130)), r.X + (r.Width - 20) / 2, r.Y + 4);
            }
            Btn(g, _btnBack, T("НАЗАД (Esc)", "BACK (Esc)"), _f12, false);
            Btn(g, _btnShopM, T("МАГАЗИН", "SHOP"), _f12, false);
        }
        else if (_state == State.Help)
        {
            Ui.Panel(g, new Rectangle(60, 56, 360, 168));
            var ht = Center(T("КАК ИГРАТЬ", "HOW TO PLAY"), _f12, g, 64);
            g.DrawString(T("КАК ИГРАТЬ", "HOW TO PLAY"), _f12, B(Color.Yellow), ht);
            string[] lines = _lang == Lang.RU ? new[] {
                "A/D — движение, W — прыжок (×2 — двойной)",
                "КРУТИ МЫШЬ — оружейная рука тянется и рубит",
                "Руби СВЕРХУ вниз — мощно (×1.3 и отброс)",
                "Коли ВДОЛЬ клинка — укол пробивает блок (кинжал ×1.3, кистень бьёт сквозь блок)",
                "ПКМ в последний момент — ПАРИРОВАНИЕ (стан!)",
                "F — пинок ногой, E — тычок рукой в клинче",
                "Ноги/руки с суставами — двигай ими чтобы ударить",
                "1-6 — смена оружия, P — пауза, R — заново",
                "ВДВОЁМ: стрелки — ход, N/M — меч, , — блок, . — пинок, L — рука, J — оружие, K — зелье",
                "Кампании 3 стран + магазин за монеты",
            } : new[] {
                "A/D — move, W — jump (x2 — double)",
                "SPIN THE MOUSE — weapon arm reaches and slashes",
                "Slash DOWN from above — power hit (x1.3)",
                "Stab ALONG the blade — thrust pierces block (dagger x1.3, flail hits through)",
                "RMB at the last moment — PARRY (stun!)",
                "F — leg kick, E — off-hand jab in clinch",
                "Jointed arms/legs — move them to hit",
                "1-6 — swap weapon, P — pause, R — restart",
                "2P: arrows move, N/M spin, , block, . kick, L jab, J weapon, K potion",
                "3 country campaigns + coin shop",
            };
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = Fit(g, lines[i], _f8, 340); // не шире панели помощи
                var lp = Center(ln, _f8, g, 90 + i * 14);
                g.DrawString(ln, _f8, Brushes.White, lp);
            }
            Btn(g, _btnBack, T("НАЗАД (Esc)", "BACK (Esc)"), _f12, false);
        }
        else if (_state == State.Settings)
        {
            var st = Center(T("НАСТРОЙКИ", "SETTINGS"), _fBanner, g, 40);
            g.DrawString(T("НАСТРОЙКИ", "SETTINGS"), _fBanner, Brushes.White, st);
            Btn(g, _btnSound, T("ЗВУК: ", "SOUND: ") + (Sfx.Enabled ? T("ВКЛ", "ON") : T("ВЫКЛ", "OFF")), _f12, Sfx.Enabled);
            Btn(g, _btnScreen, T("ЭКРАН (F11)", "SCREEN (F11)"), _f12, false);
            string dn = _lang == Lang.RU ? new[] { "ЛЕГКО", "НОРМА", "ХАРД" }[_diff] : new[] { "EASY", "NORMAL", "HARD" }[_diff];
            Btn(g, _btnDiff, T("СЛОЖНОСТЬ: ", "DIFFICULTY: ") + dn, _f12, _diff == 0);
            Btn(g, _btnSens, T("ЧУВСТВИТЕЛЬНОСТЬ: ", "SENSITIVITY: ") + SaveData.Sens, _f12, false);
            Btn(g, _btnPad, T("ГЕЙМПАД: ", "GAMEPAD: ") + (SaveData.Pad ? T("ВКЛ", "ON") : T("ВЫКЛ", "OFF")), _f12, SaveData.Pad);
            Btn(g, _btnCtl, T("УПРАВЛЕНИЕ...", "CONTROLS..."), _f12, false);
            Btn(g, _btnBack, T("НАЗАД (Esc)", "BACK (Esc)"), _f12, false);
        }
        else if (_state == State.Controls)
        {
            var ct2 = Center(T("УПРАВЛЕНИЕ — клик = новая клавиша", "CONTROLS — click = new key"), _f12, g, 36);
            g.DrawString(T("УПРАВЛЕНИЕ — клик = новая клавиша", "CONTROLS — click = new key"), _f12, B(Color.Yellow), ct2);
            for (int i = 0; i < CTRLS.Length; i++)
            {
                var r = ControlRow(i);
                string nm = _lang == Lang.RU ? CTRLS[i].ru : CTRLS[i].en;
                bool cap = _capture == CTRLS[i].id;
                RS(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(20, 16, 30));
                var bc = cap ? Color.Yellow : Ui.Hover(r) ? Color.White : Color.FromArgb(140, 140, 160);
                RS(g, r.X, r.Y, r.Width, 1, bc); RS(g, r.X, r.Y + r.Height - 1, r.Width, 1, bc);
                RS(g, r.X, r.Y, 1, r.Height, bc); RS(g, r.X + r.Width - 1, r.Y, 1, r.Height, bc);
                g.DrawString(Fit(g, nm, _f8, 180), _f8, Brushes.White, r.X + 8, r.Y + 4);
                string kn = cap ? "..." : CtrlKey(CTRLS[i].id);
                g.DrawString(Fit(g, kn, _f8, 100), _f8, B(cap ? Color.Yellow : Color.FromArgb(150, 220, 255)), r.X + r.Width - 8 - g.MeasureString(kn, _f8).Width, r.Y + 4);
            }
            Btn(g, _btnBack, T("НАЗАД (Esc)", "BACK (Esc)"), _f12, false);
        }
        else if (_state == State.Countries)
        {
            var ct = Center(T("ВЫБЕРИ СТРАНУ", "PICK A COUNTRY"), _fBanner, g, 28);
            g.DrawString(T("ВЫБЕРИ СТРАНУ", "PICK A COUNTRY"), _fBanner, Brushes.White, ct);
            for (int i = 0; i < Countries.ALL.Length; i++)
            {
                var r = CountryRect(i);
                var c = Countries.ALL[i];
                RS(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(20, 16, 30));
                var bc = Ui.Hover(r) ? Color.White : Color.FromArgb(210, 160, 70);
                RS(g, r.X, r.Y, r.Width, 2, bc); RS(g, r.X, r.Y + r.Height - 2, r.Width, 2, bc);
                RS(g, r.X, r.Y, 2, r.Height, bc); RS(g, r.X + r.Width - 2, r.Y, 2, r.Height, bc);
                // контур страны с GitHub
                float sc = Math.Min(96f / 200, 40f / c.mapH);
                float ox = r.X + 12 + (96 - 200 * sc) / 2, oy = r.Y + 4 + (40 - c.mapH * sc) / 2;
                var pts = new PointF[c.map.Length / 2];
                for (int j = 0; j < pts.Length; j++)
                    pts[j] = new PointF(ox + c.map[j * 2] * sc, oy + c.map[j * 2 + 1] * sc);
                g.FillPolygon(B(Color.FromArgb(120, 160, 100)), pts);
                g.DrawPolygon(new Pen(Color.FromArgb(60, 90, 60)), pts);
                string nm = _lang == Lang.RU ? c.ru : c.en;
                nm = Fit(g, nm, _f12, r.Width - 6);
                g.DrawString(nm, _f12, Brushes.White, r.X + (r.Width - g.MeasureString(nm, _f12).Width) / 2, r.Y + 48);
                int unl = i == 0 ? SaveData.Unlj : i == 1 ? SaveData.Unld : i == 2 ? SaveData.Unlr : i == 3 ? SaveData.Unlf : i == 4 ? SaveData.Unle : SaveData.Unlh;
                string pr = unl >= 5 ? T("ПРОЙДЕНА!", "DONE!") : unl + "/5";
                var prc = unl >= 5 ? Color.FromArgb(61, 220, 95) : Color.FromArgb(180, 255, 255, 255);
                g.DrawString(pr, _f8, B(prc), r.X + (r.Width - g.MeasureString(pr, _f8).Width) / 2, r.Y + 64);
                string wline = Fit(g, c.wru[0] + " • " + c.wru[2], _f8, r.Width - 6);
                g.DrawString(wline, _f8, B(Color.FromArgb(150, 150, 160)),
                    r.X + (r.Width - g.MeasureString(wline, _f8).Width) / 2, r.Y + 76);
            }
            Btn(g, _btnBackC, T("НАЗАД (Esc)", "BACK (Esc)"), _f8, false);
        }
        else if (_state == State.Shop)
        {
            var st2 = Center(T("МАГАЗИН", "SHOP"), _fBanner, g, 20);
            g.DrawString(T("МАГАЗИН", "SHOP"), _fBanner, Brushes.White, st2);
            var bk2 = Center(T("Кубышка: ", "Bank: ") + SaveData.Bank, _f12, g, 44);
            g.DrawString(T("Кубышка: ", "Bank: ") + SaveData.Bank, _f12, B(Color.Yellow), bk2);
            for (int i = 0; i < SHOP.Length; i++)
            {
                var r = ShopRow(i);
                bool owned = i == 6 ? false : HasItem(i);
                bool afford = SaveData.Bank >= SHOP[i].price;
                RS(g, r.X, r.Y, r.Width, r.Height, Color.FromArgb(20, 16, 30));
                var bc = owned ? Color.FromArgb(61, 220, 95) : Ui.Hover(r) && afford ? Color.White : Color.FromArgb(140, 140, 160);
                RS(g, r.X, r.Y, r.Width, 1, bc); RS(g, r.X, r.Y + r.Height - 1, r.Width, 1, bc);
                RS(g, r.X, r.Y, 1, r.Height, bc); RS(g, r.X + r.Width - 1, r.Y, 1, r.Height, bc);
                string nm = _lang == Lang.RU ? SHOP[i].ru : SHOP[i].en;
                nm = Fit(g, nm, _f8, r.Width - 64);
                DrawItemIcon(g, i, r.X + 3, r.Y + 1);
                g.DrawString(nm, _f8, B(owned ? Color.FromArgb(150, 255, 150) : Color.White), r.X + 18, r.Y + 2);
                string pr = i == 6 ? "×" + SaveData.Potions : owned ? T("КУПЛЕНО", "OWNED") : SHOP[i].price.ToString();
                pr = Fit(g, pr, _f8, 60);
                var prc = i == 6 ? Color.Yellow : owned ? Color.FromArgb(61, 220, 95) : afford ? Color.Yellow : Color.FromArgb(130, 130, 130);
                g.DrawString(pr, _f8, B(prc), r.X + r.Width - 6 - g.MeasureString(pr, _f8).Width, r.Y + 2);
            }
            Btn(g, _btnBackS, T("НАЗАД (Esc)", "BACK (Esc)"), _f12, false);
        }
        else
        {
            string bn = Fit(g, _banner, _fBanner, 440);
            var b = Center(bn, _fBanner, g, 80);
            g.DrawString(bn, _fBanner, Brushes.Black, b.X + 2, b.Y + 2);
            g.DrawString(bn, _fBanner, Brushes.White, b);
            var cb = Center(T("Монет: ", "Coins: ") + _coins + T(" • Кубышка: ", " • Bank: ") + SaveData.Bank, _f12, g, 126);
            g.DrawString(T("Монет: ", "Coins: ") + _coins + T(" • Кубышка: ", " • Bank: ") + SaveData.Bank, _f12, B(Color.Yellow), cb);
            string stx = Fit(g, T("Удары: ", "Hits: ") + _hits + T(" • Урон: ", " • Dmg: ") + _dmgDealt + T(" • Комбо: x", " • Combo: x") + _maxCombo + T(" • Пари: ", " • Parry: ") + _parries + T(" • Обезор: ", " • Disarm: ") + _disarms, _f8, 460);
            var stt = Center(stx, _f8, g, 152);
            g.DrawString(stx, _f8, Brushes.White, stt);
            Btn(g, _btnMenu, T("В МЕНЮ (Enter)", "MENU (Enter)"), _f12, false);
            if (HasReplay && !_replaying)
            {
                var rp = Center(T("T — смотреть повтор боя", "T — watch replay"), _f8, g, 190);
                g.DrawString(T("T — смотреть повтор боя", "T — watch replay"), _f8, B(Color.FromArgb(150, 220, 255)), rp);
            }
            if (_mode == Mode.Camp && !_campWin)
            {
                Btn(g, _btnRetry, T("ЕЩЁ РАЗ (R)", "RETRY (R)"), _f12, false);
            }
            else
            {
                var rh = Center(T("R — новый матч", "R — rematch"), _f8, g, 232);
                g.DrawString(T("R — новый матч", "R — rematch"), _f8, Brushes.White, rh);
            }
        }
    }
}
