using PixelFight;

namespace PFMaui;

// Главная (и единственная) страница: канвас игры + сенсорные кнопки.
// Палец по арене = прицел, быстрый тап = рывок, кнопки = движение/блок/удары.
public class MainPage : ContentPage
{
    public static MainPage Current;
    public GraphicsView ArenaView => _view;
    readonly Game _game = new Game();
    readonly GraphicsView _view = new GraphicsView { BackgroundColor = Colors.Black };
    readonly HashSet<Keys> _held = new HashSet<Keys>();
    readonly HashSet<Keys> _taps = new HashSet<Keys>();
    Pt _touch = new Pt(240, 135); // прицел в координатах буфера
    bool _lmbPulse;               // быстрый тап = рывок/клик (один кадр)
    bool _rmbHeld;                // кнопка ЩИТ
    DateTime _pressT;
    Pt _pressAt;
    bool _pressMoved;
    DateTime _lastTap;
    float _viewW, _viewH;

    public MainPage()
    {
        Current = this;
        BackgroundColor = Colors.Black;
        Sfx.Init();
        var draw = new FightDrawable(_game);
        _view.Drawable = draw;

        var grid = new Grid();
        grid.Add(_view);

        // нижние кнопки: слева движение, справа бой
        var pad = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
            },
            InputTransparent = true,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(8, 0, 8, 10),
        };
        var left = new HorizontalStackLayout { Spacing = 8, InputTransparent = true };
        left.Add(MoveBtn("◀", Keys.A));
        left.Add(MoveBtn("▶", Keys.D));
        left.Add(MoveBtn("▲", Keys.W));
        var right = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, InputTransparent = true };
        right.Add(HoldBtn("🛡", v => _rmbHeld = v));
        right.Add(TapBtn("👟", () => _taps.Add(Keys.F)));
        right.Add(TapBtn("🗡", CycleWeapon));
        right.Add(TapBtn("🧪", () => _taps.Add(Keys.Q)));
        pad.Add(left, 0, 0);
        pad.Add(right, 1, 0);
        grid.Add(pad);

        Content = grid;

        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (s, e) => Tick();
        timer.Start();
    }

    Button MoveBtn(string text, Keys key)
    {
        var b = Btn(text);
        b.Pressed += (s, e) => _held.Add(key);
        b.Released += (s, e) => _held.Remove(key);
        return b;
    }

    Button HoldBtn(string text, Action<bool> set)
    {
        var b = Btn(text);
        b.Pressed += (s, e) => set(true);
        b.Released += (s, e) => set(false);
        return b;
    }

    Button TapBtn(string text, Action tap)
    {
        var b = Btn(text);
        b.Clicked += (s, e) => tap();
        return b;
    }

    static Button Btn(string text) => new Button
    {
        Text = text,
        FontSize = 22,
        WidthRequest = 64,
        HeightRequest = 64,
        CornerRadius = 32,
        BackgroundColor = new Color(0.22f, 0.22f, 0.27f, 0.5f),
        TextColor = Colors.White,
        Opacity = 0.85,
    };

    void CycleWeapon()
    {
        try { _game.SetPlayerWeapon(_game.GetPlayerWeapon() + 1); Sfx.Jump(); } catch { }
    }

    // Сырые касания арены из MainActivity (координаты GraphicsView, dip).
    // Палец = прицел, быстрый тап = рывок/клик, в конце матча тап = R, дабл-тап = T.
    public void ArenaDown(float x, float y)
    {
        _pressT = DateTime.Now;
        _pressAt = new Pt((int)x, (int)y);
        _pressMoved = false;
        ToBuf(x, y, ref _touch);
    }

    public void ArenaMove(float x, float y)
    {
        if (Math.Abs(x - _pressAt.X) + Math.Abs(y - _pressAt.Y) > 24) _pressMoved = true;
        ToBuf(x, y, ref _touch);
    }

    public void ArenaUp(float x, float y)
    {
        ToBuf(x, y, ref _touch);
        if (_pressMoved || (DateTime.Now - _pressT).TotalMilliseconds >= 300) return;
        string st = "";
        try { st = _game.GetState(); } catch { }
        bool end = st.StartsWith("2|");
        if (end && (DateTime.Now - _lastTap).TotalMilliseconds < 350)
        {
            _taps.Add(Keys.T);
            _lastTap = DateTime.MinValue;
        }
        else if (end)
        {
            _taps.Add(Keys.R);
            _lastTap = DateTime.Now;
        }
        else
        {
            _lmbPulse = true;
            _lastTap = DateTime.Now;
        }
    }

    void ToBuf(float x, float y, ref Pt buf)
    {
        float s = Math.Min(_viewW / Game.W, _viewH / Game.H);
        if (s <= 0) return;
        float ox = (_viewW - Game.W * s) / 2, oy = (_viewH - Game.H * s) / 2;
        buf = new Pt(
            Math.Max(0, Math.Min(Game.W - 1, (int)((x - ox) / s))),
            Math.Max(0, Math.Min(Game.H - 1, (int)((y - oy) / s))));
    }

    void Tick()
    {
        _viewW = (float)_view.Width;
        _viewH = (float)_view.Height;
        var keys = new HashSet<Keys>(_held);
        foreach (var k in _taps) keys.Add(k);
        _taps.Clear();
        bool lmb = _lmbPulse;
        _lmbPulse = false;
        try { _game.Update(keys, _touch, lmb, _rmbHeld, 1f / 60f); } catch { }
        _view.Invalidate();
    }
}

// Рисовальщик: буфер 480x270 по центру, целочисленный масштаб.
public class FightDrawable : IDrawable
{
    readonly Game _game;
    public FightDrawable(Game game) { _game = game; }
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float s = Math.Min(dirtyRect.Width / Game.W, dirtyRect.Height / Game.H);
        canvas.Translate((dirtyRect.Width - Game.W * s) / 2, (dirtyRect.Height - Game.H * s) / 2);
        canvas.Scale(s, s);
        try { _game.Render(new Gfx(canvas)); } catch { }
    }
}
