// Times yadif on real 1080i frames, VLC's C and assembly against the C#
// implementations, all driven by the same per-plane loop as VLC's
// RenderYadif, and checks every variant against VLC's C sample for sample.
// Before timing, a sweep over random pictures of odd sizes, every field and
// parity, checks the same.
//
// Harness [rounds] [framesPerRound] [cpu]
//   expects frames-8bit.yuv and frames-10bit.yuv (three 1920x1080 frames,
//   yuv420p and yuv420p10le) one directory up from the executable (build.ps1).

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using VLCLR.Filters.Video;
using VLCLR.Native;

int rounds = args.Length > 0 ? int.Parse(args[0]) : 7;
int framesPerRound = args.Length > 1 ? int.Parse(args[1]) : 5;
int cpu = args.Length > 2 ? int.Parse(args[2]) : 2;

var process = Process.GetCurrentProcess();
process.PriorityClass = ProcessPriorityClass.High;
process.ProcessorAffinity = (nint)(1L << cpu);
Thread.CurrentThread.Priority = ThreadPriority.Highest;

string dir = AppContext.BaseDirectory;
var vlc = new NativeYadif(Path.Combine(dir, "cyadif.dll"));
var vlcIsa = new NativeYadif(Path.Combine(dir, "cyadif-isa.dll"));
string isa = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));

var csharp = new List<YadifImplementation>
{
    YadifImplementation.Reference,
    YadifImplementation.Scalar,
    YadifImplementation.Vector128,
};
if (Avx2.IsSupported)
{
    csharp.Add(YadifImplementation.Vector256);
}
if (Avx512BW.IsSupported)
{
    csharp.Add(YadifImplementation.Vector512);
}

// Variants per bit depth: name, render(dst, prev, cur, next, field, parity).
var variants8 = new List<Variant>
{
    new("VLC C (x86-64)", (d, p, c, n, f, par) => vlc.Render(NativeYadif.C, d, p, c, n, f, par)),
    new($"VLC C ({isa})", (d, p, c, n, f, par) => vlcIsa.Render(NativeYadif.C, d, p, c, n, f, par)),
    new("VLC SSE2 asm", (d, p, c, n, f, par) => vlc.Render(NativeYadif.Sse2, d, p, c, n, f, par)),
    new("VLC SSSE3 asm", (d, p, c, n, f, par) => vlc.Render(NativeYadif.Ssse3, d, p, c, n, f, par)),
};
var variants10 = new List<Variant>
{
    new("VLC C 16-bit (x86-64)", (d, p, c, n, f, par) => vlc.Render(NativeYadif.C16, d, p, c, n, f, par)),
    new($"VLC C 16-bit ({isa})", (d, p, c, n, f, par) => vlcIsa.Render(NativeYadif.C16, d, p, c, n, f, par)),
};
foreach (YadifImplementation impl in csharp)
{
    var y8 = new Yadif(impl, 8);
    var y10 = new Yadif(impl, 10);
    variants8.Add(new($"C# {y8.Implementation}", (d, p, c, n, f, par) => y8.Render(p, c, n, d, f, par)));
    variants10.Add(new($"C# {y10.Implementation}", (d, p, c, n, f, par) => y10.Render(p, c, n, d, f, par)));
}

Console.WriteLine("# Yadif: VLC's C and assembly vs C#");
Console.WriteLine();
Console.WriteLine($"- Runtime: {RuntimeInformation.FrameworkDescription}, Native AOT {isa}; VLC's C: clang -O3 -funroll-loops -fomit-frame-pointer; asm: NASM");
Console.WriteLine($"- CPU {cpu} only, high priority; AVX2 {Avx2.IsSupported}, AVX-512 {Avx512BW.IsSupported}");
Console.WriteLine();

// Correctness sweep on random pictures.
int mismatches = 0;
foreach (int bits in new[] { 8, 10 })
{
    List<Variant> variants = bits == 8 ? variants8 : variants10;
    foreach ((int w, int h) in new[] { (64, 48), (97, 61), (33, 9), (7, 5), (720, 576) })
    {
        using var prev = new Picture(w, h, bits, seed: 1);
        using var cur = new Picture(w, h, bits, seed: 2);
        using var next = new Picture(w, h, bits, seed: 3);
        foreach (int field in new[] { 0, 1 })
        {
            foreach (int parity in new[] { 0, 1, 2 })
            {
                byte[]? reference = null;
                foreach (Variant v in variants)
                {
                    using var dst = new Picture(w, h, bits, seed: 4);
                    v.Render(dst.Planes, prev.Planes, cur.Planes, next.Planes, field, parity);
                    byte[] result = dst.Visible();
                    reference ??= result;
                    if (!reference.AsSpan().SequenceEqual(result))
                    {
                        Console.WriteLine($"MISMATCH: {v.Name}, {bits}-bit {w}x{h} field {field} parity {parity}");
                        mismatches++;
                    }
                }
            }
        }
    }
}
Console.WriteLine(mismatches == 0
    ? "Random pictures (8 and 10 bits, 5 sizes, both fields, parities 0/1/2): every variant matches VLC's C."
    : $"Random pictures: {mismatches} mismatches.");

// The first picture: RenderX, on the same kinds of pictures and on the real
// frames (it works on bytes, whatever the bit depth).
int xMismatches = 0;
foreach (int bits in new[] { 8, 10 })
{
    foreach ((int w, int h) in new[] { (64, 48), (97, 61), (33, 9), (7, 5), (720, 576), (1920, 1080) })
    {
        foreach (bool combed in new[] { false, true })
        {
            using var src = new Picture(w, h, bits, seed: 5);
            if (combed)
            {
                src.Comb(seed: 6);
            }
            using var vlcOut = new Picture(w, h, bits, seed: 4);
            using var csOut = new Picture(w, h, bits, seed: 4);
            vlc.RenderX(vlcOut.Planes, src.Planes);
            XDeinterlace.Render(src.Planes, csOut.Planes);
            if (!vlcOut.Visible().AsSpan().SequenceEqual(csOut.Visible()))
            {
                Console.WriteLine($"MISMATCH: C# XDeinterlace, {bits}-bit {w}x{h}{(combed ? ", combed" : "")}");
                xMismatches++;
            }
        }
    }
    using var frames = Picture.LoadFrames(Path.Combine(dir, "..", bits == 8 ? "frames-8bit.yuv" : "frames-10bit.yuv"), 1920, 1080, bits, 3);
    using var vlcFrame = new Picture(1920, 1080, bits, seed: 4);
    using var csFrame = new Picture(1920, 1080, bits, seed: 4);
    vlc.RenderX(vlcFrame.Planes, frames.Pictures[0].Planes);
    XDeinterlace.Render(frames.Pictures[0].Planes, csFrame.Planes);
    if (!vlcFrame.Visible().AsSpan().SequenceEqual(csFrame.Visible()))
    {
        Console.WriteLine($"MISMATCH: C# XDeinterlace, {bits}-bit 1080i frame");
        xMismatches++;
    }
}
Console.WriteLine(xMismatches == 0
    ? "X, yadif's first picture (8 and 10 bits, 6 sizes random and combed, a 1080i frame): C# matches VLC's C."
    : $"X: {xMismatches} mismatches.");
mismatches += xMismatches;
Console.WriteLine();

foreach (int bits in new[] { 8, 10 })
{
    List<Variant> variants = bits == 8 ? variants8 : variants10;
    string file = Path.Combine(dir, "..", bits == 8 ? "frames-8bit.yuv" : "frames-10bit.yuv");
    using var frames = Picture.LoadFrames(file, 1920, 1080, bits, 3);
    Picture prev = frames.Pictures[0], cur = frames.Pictures[1], next = frames.Pictures[2];
    var outputs = variants.Select(_ => new Picture(1920, 1080, bits, seed: 4)).ToArray();

    // yadif: field 0 kept, parity 1 (RenderYadifSingle).
    void RunFrame(int v) => variants[v].Render(outputs[v].Planes, prev.Planes, cur.Planes, next.Planes, 0, 1);

    var identical = new string[variants.Count];
    byte[]? reference = null;
    for (int v = 0; v < variants.Count; v++)
    {
        RunFrame(v);
        byte[] result = outputs[v].Visible();
        reference ??= result;
        long diff = 0;
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] != reference[i])
            {
                diff++;
            }
        }
        identical[v] = diff == 0 ? "yes" : $"NO ({diff} bytes differ)";
    }

    var samples = variants.Select(_ => new List<double>()).ToArray();
    for (int round = 0; round < rounds; round++)
    {
        for (int k = 0; k < variants.Count; k++)
        {
            int v = (k + round) % variants.Count;
            for (int f = 0; f < framesPerRound; f++)
            {
                long start = Stopwatch.GetTimestamp();
                RunFrame(v);
                samples[v].Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        }
    }

    // The baseline is what VLC runs: the SSSE3 assembly for 8 bits, the
    // 16-bit C as compiled for baseline x86-64 for 10 bits.
    int baseline = bits == 8 ? variants.FindIndex(v => v.Name == "VLC SSSE3 asm") : 0;
    double baseP10 = Percentile(samples[baseline], 0.10);
    Console.WriteLine($"## {bits}-bit, 1920x1080 4:2:0, one yadif frame (3 planes)");
    Console.WriteLine();
    Console.WriteLine($"{rounds} rounds x {framesPerRound} frames per variant, variant order rotated each round.");
    Console.WriteLine();
    Console.WriteLine($"| Variant | p10 ms/frame | median ms/frame | vs {variants[baseline].Name}, p10 (<1 = faster) | same output as VLC's C |");
    Console.WriteLine("|---|---:|---:|---:|---|");
    for (int v = 0; v < variants.Count; v++)
    {
        double p10 = Percentile(samples[v], 0.10);
        Console.WriteLine($"| {variants[v].Name} | {p10:F2} | {Percentile(samples[v], 0.5):F2} | {p10 / baseP10:F2}x | {identical[v]} |");
    }
    Console.WriteLine();
    foreach (Picture o in outputs)
    {
        o.Dispose();
    }
}
return mismatches == 0 ? 0 : 1;

// Nearest-rank percentile: the time that a fraction q of frames beat.
static double Percentile(List<double> values, double q)
{
    var sorted = values.Order().ToArray();
    return sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
}

internal delegate void RenderFn(VLCPlane[] dst, VLCPlane[] prev, VLCPlane[] cur, VLCPlane[] next, int field, int parity);

internal sealed record Variant(string Name, RenderFn Render);

/// <summary>yadif_render of c/yadif.c, from one of the two native builds.</summary>
internal sealed unsafe class NativeYadif
{
    public const int C = 0, Sse2 = 1, Ssse3 = 2, C16 = 3;

    private readonly delegate* unmanaged<int, int, VLCPlane*, VLCPlane*, VLCPlane*, VLCPlane*, int, int, void> _render;

    private readonly delegate* unmanaged<int, VLCPlane*, VLCPlane*, void> _renderX;

    public NativeYadif(string path)
    {
        nint library = NativeLibrary.Load(path);
        _render = (delegate* unmanaged<int, int, VLCPlane*, VLCPlane*, VLCPlane*, VLCPlane*, int, int, void>)
            NativeLibrary.GetExport(library, "yadif_render");
        _renderX = (delegate* unmanaged<int, VLCPlane*, VLCPlane*, void>)NativeLibrary.GetExport(library, "x_render");
    }

    public void Render(int impl, VLCPlane[] dst, VLCPlane[] prev, VLCPlane[] cur, VLCPlane[] next, int field, int parity)
    {
        fixed (VLCPlane* d = dst, p = prev, c = cur, n = next)
        {
            _render(impl, dst.Length, d, p, c, n, field, parity);
        }
    }

    /// <summary>RenderX of algo_x.c, yadif's first picture.</summary>
    public void RenderX(VLCPlane[] dst, VLCPlane[] src)
    {
        fixed (VLCPlane* d = dst, s = src)
        {
            _renderX(dst.Length, d, s);
        }
    }
}

/// <summary>
/// A 4:2:0 picture in native memory laid out like VLC's picture_New: pitch
/// rounded up to 64 bytes, and spare lines after the visible ones (VLC's
/// 16-bit path writes and reads past the visible lines).
/// </summary>
internal sealed unsafe class Picture : IDisposable
{
    private const int SpareLines = 8;
    private readonly nint[] _memory = new nint[3];

    public Picture(int width, int height, int bits, int seed)
    {
        int pixelSize = bits > 8 ? 2 : 1;
        Planes = new VLCPlane[3];
        var random = new Random(seed);
        for (int i = 0; i < 3; i++)
        {
            int w = i == 0 ? width : (width + 1) / 2;
            int h = i == 0 ? height : (height + 1) / 2;
            int pitch = (w * pixelSize + 63) & ~63;
            int lines = h + SpareLines;
            nuint size = (nuint)(pitch * lines);
            _memory[i] = (nint)NativeMemory.AlignedAlloc(size, 64);
            var bytes = new Span<byte>((void*)_memory[i], (int)size);
            if (pixelSize == 1)
            {
                random.NextBytes(bytes);
            }
            else
            {
                Span<ushort> samples = MemoryMarshal.Cast<byte, ushort>(bytes);
                for (int s = 0; s < samples.Length; s++)
                {
                    samples[s] = (ushort)random.Next(1 << bits);
                }
            }
            Planes[i] = new VLCPlane
            {
                Pixels = _memory[i],
                Lines = lines,
                Pitch = pitch,
                PixelPitch = pixelSize,
                VisibleLines = h,
                VisiblePitch = w * pixelSize,
            };
        }
    }

    public VLCPlane[] Planes { get; }

    /// <summary>
    /// Makes every line, spare ones included, a combed field: bytes around 60
    /// on even lines and 180 on odd ones, with a little noise, so that X sees
    /// interlacing in every block.
    /// </summary>
    public void Comb(int seed)
    {
        var random = new Random(seed);
        foreach (VLCPlane plane in Planes)
        {
            for (int line = 0; line < plane.Lines; line++)
            {
                var bytes = new Span<byte>((byte*)plane.Pixels + line * plane.Pitch, plane.Pitch);
                foreach (ref byte b in bytes)
                {
                    b = (byte)((line % 2 == 0 ? 60 : 180) + random.Next(16));
                }
            }
        }
    }

    public byte[] Visible()
    {
        var bytes = new List<byte>();
        foreach (VLCPlane plane in Planes)
        {
            for (int line = 0; line < plane.VisibleLines; line++)
            {
                bytes.AddRange(new ReadOnlySpan<byte>((byte*)plane.Pixels + line * plane.Pitch, plane.VisiblePitch).ToArray());
            }
        }
        return bytes.ToArray();
    }

    /// <summary>Consecutive raw 4:2:0 frames (yuv420p, or yuv420p10le for more than 8 bits).</summary>
    public static Frames LoadFrames(string path, int width, int height, int bits, int count)
    {
        byte[] raw = File.ReadAllBytes(path);
        var pictures = new Picture[count];
        int offset = 0;
        for (int f = 0; f < count; f++)
        {
            var picture = new Picture(width, height, bits, seed: f);
            foreach (VLCPlane plane in picture.Planes)
            {
                for (int line = 0; line < plane.VisibleLines; line++)
                {
                    raw.AsSpan(offset, plane.VisiblePitch).CopyTo(
                        new Span<byte>((byte*)plane.Pixels + line * plane.Pitch, plane.VisiblePitch));
                    offset += plane.VisiblePitch;
                }
            }
            pictures[f] = picture;
        }
        if (offset != raw.Length)
        {
            throw new InvalidDataException($"{path}: expected {count} frames of {width}x{height}, {bits} bits");
        }
        return new Frames(pictures);
    }

    public void Dispose()
    {
        foreach (nint memory in _memory)
        {
            NativeMemory.AlignedFree((void*)memory);
        }
    }
}

internal sealed class Frames(Picture[] pictures) : IDisposable
{
    public Picture[] Pictures { get; } = pictures;

    public void Dispose()
    {
        foreach (Picture p in Pictures)
        {
            p.Dispose();
        }
    }
}
