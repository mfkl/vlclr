// Same measurement and dump as timing_shim.h in the VLC benchmark branch
// (see README.md), so C and C# results are directly comparable: per-frame
// time of the pixel work only (first frame reported separately), the same
// log line, the same dump layout.

using System.Diagnostics;
using VLCLR;
using VLCLR.Native;
using VLCLR.Plugin;

namespace DotnetGaussianBlur;

internal sealed class BenchStats
{
    private readonly string _implementation;
    private readonly List<double> _milliseconds = new(4096);
    private double _firstMilliseconds = -1;
    private long _start;
    private readonly string? _dumpPath = Environment.GetEnvironmentVariable("VLCLR_BLUR_DUMP");
    private readonly int _dumpLimit = int.TryParse(Environment.GetEnvironmentVariable("VLCLR_BLUR_DUMP_FRAMES"), out int n) ? n : 30;
    private int _dumped;

    public BenchStats(string implementation) => _implementation = implementation;

    public void Begin() => _start = Stopwatch.GetTimestamp();

    public void End()
    {
        double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
        if (_firstMilliseconds < 0)
            _firstMilliseconds = ms;
        else
            _milliseconds.Add(ms);
    }

    /// <summary>Appends the visible planes of <paramref name="picture"/>, like the C shim.</summary>
    public unsafe void Dump(VLCFrame picture)
    {
        if (string.IsNullOrEmpty(_dumpPath) || _dumped >= _dumpLimit)
            return;
        using var file = new FileStream(_dumpPath, FileMode.Append, FileAccess.Write);
        for (int i = 0; i < picture.PlaneCount; i++)
        {
            VLCPlane plane = picture.GetPlane(i);
            for (int y = 0; y < plane.VisibleLines; y++)
            {
                file.Write(new ReadOnlySpan<byte>((byte*)plane.Pixels + (nint)y * plane.Pitch, plane.VisiblePitch));
            }
        }
        _dumped++;
    }

    public void Report(VLCLogger logger, float sigma)
    {
        if (_milliseconds.Count == 0)
            return;
        double sum = 0;
        foreach (double ms in _milliseconds)
            sum += ms;
        _milliseconds.Sort();
        int count = _milliseconds.Count;
        logger.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[bench] impl={_implementation} sigma={sigma:0.00} frames={count} first_ms={_firstMilliseconds:0.000} mean_ms={sum / count:0.000} median_ms={_milliseconds[count / 2]:0.000} p95_ms={_milliseconds[(int)(count * 0.95)]:0.000} min_ms={_milliseconds[0]:0.000}"));
    }
}
