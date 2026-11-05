namespace PixelEngine.Diagnostics;

public readonly record struct EngineProfilerSnapshot(
    double FramesPerSecond,
    double FrameMilliseconds,
    double UpdateMilliseconds,
    double DrawMilliseconds,
    long ManagedMemoryBytes,
    int ObjectCount,
    int ComponentCount,
    long FrameCount);

/// <summary>Rolling, low-overhead frame profiler used by EngineGame and test harnesses.</summary>
public sealed class EngineProfiler
{
    private const int Capacity = 120;
    private readonly double[] _frame = new double[Capacity];
    private readonly double[] _update = new double[Capacity];
    private readonly double[] _draw = new double[Capacity];
    private int _cursor;
    private int _count;
    private int _objects;
    private int _components;
    private long _frames;

    public void RecordSample(double frameSeconds, double updateMilliseconds, double drawMilliseconds,
        int objectCount, int componentCount, long? managedMemoryBytes = null)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds <= 0) frameSeconds = 1d / 60d;
        _frame[_cursor] = frameSeconds;
        _update[_cursor] = Math.Max(0, double.IsFinite(updateMilliseconds) ? updateMilliseconds : 0);
        _draw[_cursor] = Math.Max(0, double.IsFinite(drawMilliseconds) ? drawMilliseconds : 0);
        _cursor = (_cursor + 1) % Capacity;
        _count = Math.Min(Capacity, _count + 1);
        _objects = Math.Max(0, objectCount);
        _components = Math.Max(0, componentCount);
        // Managed heap sampling is relatively expensive. Sample at 2 Hz for a 60 FPS
        // game while still accepting an explicit caller-provided measurement every frame.
        if (managedMemoryBytes.HasValue || _frames == 0 || _frames % 30 == 0)
            _managedMemoryBytes = Math.Max(0L, managedMemoryBytes ?? GC.GetTotalMemory(false));
        _frames++;
    }

    private long _managedMemoryBytes;

    public EngineProfilerSnapshot Snapshot
    {
        get
        {
            if (_count == 0) return new EngineProfilerSnapshot(0, 0, 0, 0, GC.GetTotalMemory(false), _objects, _components, _frames);
            var frame = Average(_frame);
            var update = Average(_update);
            var draw = Average(_draw);
            return new EngineProfilerSnapshot(frame > 0 ? 1d / frame : 0d, frame * 1000d,
                update, draw, _managedMemoryBytes, _objects, _components, _frames);
        }
    }

    private double Average(double[] values)
    {
        var sum = 0d;
        for (var i = 0; i < _count; i++) sum += values[i];
        return sum / _count;
    }
}
