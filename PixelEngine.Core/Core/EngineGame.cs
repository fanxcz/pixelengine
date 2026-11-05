using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelEngine.Assets;
using PixelEngine.Graphics;
using PixelEngine.Input;
using PixelEngine.Rendering;
using PixelEngine.Scene;
using PixelEngine.Diagnostics;
using System.Diagnostics;
using GameScene = PixelEngine.Scene.Scene;

namespace PixelEngine.Core;

/// <summary>MonoGame host with a fixed simulation step and variable-rate rendering.</summary>
public abstract class EngineGame : Game
{
    private const float FixedStep = 1f / 60f;
    private const int MaxFixedStepsPerFrame = 5;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private LightingRenderer _lightingRenderer = null!;
    private RenderContext _renderContext = null!;
    private double _lastUpdateMilliseconds;
    private long _lastProfilerWriteTimestamp;
    private readonly EngineTime _time = new();
    private readonly InputState _input = new();
    private float _accumulator;
    private bool _sceneStarted;

    protected EngineGame(int width = 1280, int height = 720)
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = width,
            PreferredBackBufferHeight = height,
            SynchronizeWithVerticalRetrace = true
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    public GameScene ActiveScene { get; private set; } = new("Untitled");
    public Camera2D Camera { get; } = new();
    public AssetStore Assets { get; private set; } = null!;
    public InputState Input => _input;
    public EngineTime Time => _time;
    public EngineProfiler Profiler { get; } = new();

    protected override void LoadContent()
    {
        base.LoadContent();
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        Assets = new AssetStore(GraphicsDevice);
        _lightingRenderer = new LightingRenderer(GraphicsDevice);
        _renderContext = new RenderContext(_spriteBatch, GraphicsDevice, Camera, Assets.WhitePixel);
        OnEngineInitialize();
    }

    protected virtual void OnEngineInitialize() { }
    protected void SetScene(GameScene scene)
    {
        ActiveScene = scene ?? throw new ArgumentNullException(nameof(scene));
        ActiveScene.Start();
        _sceneStarted = true;
    }

    protected override void Update(GameTime gameTime)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            _input.Poll();
            _time.Input = _input;
            var elapsed = EngineTime.SanitizeDeltaTime(gameTime.ElapsedGameTime.TotalSeconds);
            _time.DeltaTime = elapsed;
            _time.TotalTime = EngineTime.SanitizeTotalTime(gameTime.TotalGameTime.TotalSeconds, _time.TotalTime);
            _time.Frame++;
            if (!_sceneStarted) { ActiveScene.Start(); _sceneStarted = true; }

            _accumulator += elapsed;
            var steps = 0;
            while (_accumulator >= FixedStep && steps < MaxFixedStepsPerFrame)
            {
                _time.FixedDeltaTime = FixedStep;
                ActiveScene.FixedUpdate(_time);
                _accumulator -= FixedStep;
                steps++;
            }
            if (steps == MaxFixedStepsPerFrame && _accumulator >= FixedStep)
                _accumulator = 0; // avoid a spiral of death after a long stall

            ActiveScene.Update(_time);
            Camera.ClampPosition(GraphicsDevice.Viewport);
            base.Update(gameTime);
        }
        finally { _lastUpdateMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    protected override void Draw(GameTime gameTime)
    {
        var started = Stopwatch.GetTimestamp();
        GraphicsDevice.Clear(ActiveScene.BackgroundColor);
        _renderContext.ResetFrame();
        _spriteBatch.Begin(SpriteSortMode.BackToFront, samplerState: SamplerState.PointClamp,
            transformMatrix: Camera.GetViewMatrix(GraphicsDevice.Viewport));
        ActiveScene.Draw(_renderContext);
        _spriteBatch.End();
        _lightingRenderer.Draw(_spriteBatch, Camera, GraphicsDevice.Viewport,
            _renderContext.Lights, _renderContext.ShadowSegments, ActiveScene.AmbientLight);
        base.Draw(gameTime);
        var drawMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var objects = ActiveScene.Objects;
        var componentCount = 0;
        for (var i = 0; i < objects.Count; i++)
            componentCount += objects[i].Components.Count;
        var elapsedSeconds = EngineTime.SanitizeDeltaTime(gameTime.ElapsedGameTime.TotalSeconds);
        Profiler.RecordSample(Math.Max(1e-6, elapsedSeconds), _lastUpdateMilliseconds,
            drawMs, objects.Count, componentCount);

        // The editor runs in a separate process. Publish at 1 Hz so the Profiler tab
        // can read live metrics without forcing a file write on every rendered frame.
        var now = Stopwatch.GetTimestamp();
        if (_lastProfilerWriteTimestamp == 0 || Stopwatch.GetElapsedTime(_lastProfilerWriteTimestamp, now).TotalSeconds >= 1d)
        {
            _lastProfilerWriteTimestamp = now;
            try { RuntimeProfilerStore.WriteSnapshot(Profiler.Snapshot); }
            catch (Exception ex) { Debug.WriteLine($"[PixelEngine Profiler] telemetry write failed: {ex.Message}"); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Assets?.Dispose();
            _spriteBatch?.Dispose();
            _lightingRenderer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
