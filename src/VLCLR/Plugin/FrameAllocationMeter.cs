// Per-frame allocation meter
// Counts the managed allocations of each frame callback after a warm-up
// VLC Version: 4.0.6

namespace VLCLR.Plugin;

/// <summary>
/// Counts the bytes a plugin allocates on the managed heap while it processes
/// frames, so that a plugin that allocates per frame shows up in VLC's log.
/// </summary>
/// <remarks>
/// <para>
/// Each plugin is its own Native AOT library with its own garbage collector,
/// and every collector keeps its own allocation budget: a plugin that
/// allocates on each frame holds several MB more and makes the GC run while
/// video plays. A plugin that allocates nothing per frame costs about 0.5 MB.
/// </para>
/// <para>
/// The frames right after open and after each flush are a warm-up and are not
/// counted: buffers sized from the first picture are allocated there. Each
/// frame is measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/>
/// (about 2 ns, exact). Allocations on other threads (a thread pool used by
/// the plugin) are only seen by the whole-runtime count, read when each
/// warm-up ends and at each flush and at close, as
/// <see cref="GC.GetTotalAllocatedBytes"/> with precise counts takes tens of
/// microseconds. It counts every plugin instance of the library: two filters
/// of one plugin in a chain share a runtime.
/// </para>
/// </remarks>
internal sealed class FrameAllocationMeter
{
    /// <summary>Frames not counted after open and after each flush.</summary>
    public const int DefaultWarmUpFrames = 3;

    private readonly int _warmUpFrames;
    private int _warmUpLeft;
    private bool _measuring;
    private long _frameStart;
    private long _frames;
    private long _runtimeStart = -1;
    private long _runtimeBytes = -1;

    public FrameAllocationMeter(int warmUpFrames = DefaultWarmUpFrames)
    {
        _warmUpFrames = warmUpFrames;
        _warmUpLeft = warmUpFrames;
    }

    /// <summary>Frames seen, warm-up included.</summary>
    public long Frames => _frames;

    /// <summary>Frames counted (after warm-up).</summary>
    public long MeasuredFrames { get; private set; }

    /// <summary>Counted frames that allocated anything.</summary>
    public long AllocatingFrames { get; private set; }

    /// <summary>Bytes allocated by the counted frames, on the frame's thread.</summary>
    public long Bytes { get; private set; }

    /// <summary>Most bytes allocated by one counted frame.</summary>
    public long MaxFrameBytes { get; private set; }

    /// <summary>Number (from 1, warm-up included) of the first counted frame that allocated, or 0.</summary>
    public long FirstAllocatingFrame { get; private set; }

    /// <summary>
    /// Bytes allocated by the whole runtime, on every thread, while frames were
    /// counted (from the end of each warm-up to the next <see cref="Restart"/>
    /// or <see cref="Stop"/>), or -1 when not measured.
    /// </summary>
    public long RuntimeBytes => _runtimeBytes;

    /// <summary>Whether the counted frames allocated nothing on their thread.</summary>
    public bool IsAllocationFree => Bytes == 0;

    /// <summary>Starts a new warm-up, for a flush or a format change.</summary>
    public void Restart()
    {
        EndRuntimeCount();
        _warmUpLeft = _warmUpFrames;
    }

    /// <summary>Call on the frame's thread before the plugin processes it.</summary>
    public void BeginFrame()
    {
        _frames++;
        _measuring = _warmUpLeft == 0;
        if (_measuring)
        {
            if (_runtimeStart < 0)
            {
                _runtimeStart = GC.GetTotalAllocatedBytes(precise: true);
            }
            _frameStart = GC.GetAllocatedBytesForCurrentThread();
        }
    }

    /// <summary>Call on the same thread once the plugin has processed the frame.</summary>
    public void EndFrame()
    {
        if (!_measuring)
        {
            if (_warmUpLeft > 0)
            {
                _warmUpLeft--;
            }
            return;
        }

        _measuring = false;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - _frameStart;
        MeasuredFrames++;
        if (bytes > 0)
        {
            AllocatingFrames++;
            Bytes += bytes;
            MaxFrameBytes = Math.Max(MaxFrameBytes, bytes);
            if (FirstAllocatingFrame == 0)
            {
                FirstAllocatingFrame = _frames;
            }
        }
    }

    /// <summary>Ends the whole-runtime count; call at close, before anything else allocates.</summary>
    public void Stop() => EndRuntimeCount();

    private void EndRuntimeCount()
    {
        if (_runtimeStart >= 0)
        {
            _runtimeBytes = Math.Max(_runtimeBytes, 0) + GC.GetTotalAllocatedBytes(precise: true) - _runtimeStart;
            _runtimeStart = -1;
        }
    }

    /// <summary>
    /// One line for VLC's log. Tests match its start: "Frame allocations: none in N frames".
    /// </summary>
    public string Summary(string unit = "frame")
    {
        if (MeasuredFrames == 0)
        {
            return $"Frame allocations: not measured ({_frames} {unit}s, fewer than the {_warmUpFrames} of the warm-up)";
        }

        string runtime = RuntimeBytes >= 0 ? $"; whole runtime meanwhile: {RuntimeBytes} bytes" : "";
        if (IsAllocationFree)
        {
            return $"Frame allocations: none in {MeasuredFrames} {unit}s after a warm-up of {_warmUpFrames}{runtime}";
        }

        return $"Frame allocations: {Bytes} bytes in {AllocatingFrames} of {MeasuredFrames} {unit}s after a warm-up of {_warmUpFrames} "
            + $"(at most {MaxFrameBytes} in one, first in {unit} {FirstAllocatingFrame}){runtime}";
    }
}
