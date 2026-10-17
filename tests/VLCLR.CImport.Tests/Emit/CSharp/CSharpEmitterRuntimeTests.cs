using System.Reflection;

namespace VLCLR.CImport.Tests.Emit.CSharp;

/// <summary>
/// Compiles the generated code together with a probe and runs it: struct sizes
/// and member offsets for the running ABI, bitfield semantics.
/// </summary>
public class CSharpEmitterRuntimeTests
{
    private static long[] RunProbe(string caseName, string probe)
    {
        var result = CSharpCompilationHelper.Compile(
            CSharpEmitterGoldenTests.Emit(caseName).Files,
            [new KeyValuePair<string, string>("Probe.cs", probe)]);
        var assembly = result.Load();
        var method = assembly.GetType("Test.Interop.Probe", throwOnError: true)!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        return (long[])method.Invoke(null, null)!;
    }

    private static long[] ForRunningAbi(long[] windows, long[] sysv) => OperatingSystem.IsWindows() ? windows : sysv;

    [Fact]
    public void Divergent_structs_report_the_layout_of_the_running_abi()
    {
        const string probe = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace Test.Interop;
            public static unsafe class Probe
            {
                static long Off(void* owner, void* member) => (byte*)member - (byte*)owner;

                public static long[] Run()
                {
                    VideoFormat v = default;
                    EsFormat e = default;
                    Filter f = default;
                    VideoFormatDovi d = default;
                    long palette, pextra, fmtOutExtra, ops, psys;
                    fixed (VideoPalette** p = &v.PPalette) palette = Off(&v, p);
                    fixed (void** p = &e.PExtra) pextra = Off(&e, p);
                    fixed (void** p = &f.FmtOut.PExtra) fmtOutExtra = Off(&f, p);
                    fixed (void** p = &f.PSys) psys = Off(&f, p);
                    fixed (VlcFilterOperations** p = &f.Ops) ops = Off(&f, p);

                    f.FmtOut.PExtra = (void*)5;
                    d.Profile = 100;
                    d.Level = 33;
                    d.BlPresent = 1;
                    uint* raw = (uint*)&d;
                    return new long[]
                    {
                        Filter.SizeOf, EsFormat.SizeOf, VideoFormat.SizeOf, VideoFormatDovi.SizeOf,        // 0-3
                        sizeof(Filter), sizeof(EsFormat),                                                  // 4-5
                        Off(&v, Unsafe.AsPointer(ref v.IWidth)), Off(&v, Unsafe.AsPointer(ref v.IHeight)), // 6-7
                        palette, Off(&v, Unsafe.AsPointer(ref v.Orientation)),                             // 8-9
                        pextra, Off(&e, Unsafe.AsPointer(ref e.IExtra)), Off(&e, Unsafe.AsPointer(ref e.Video)), // 10-12
                        Off(&f, Unsafe.AsPointer(ref f.FmtOut)), psys, ops, fmtOutExtra,                   // 13-16
                        *(long*)((byte*)&f + (OperatingSystem.IsWindows() ? 80 + 48 : 72 + 40)),           // 17: what we wrote through the ref
                        d.Profile, d.Level, d.BlPresent,                                                   // 18-20
                        OperatingSystem.IsWindows() ? raw[1] & 0x7F : (raw[0] >> 16) & 0x7F,              // 21: raw storage of Profile
                    };
                }
            }
            """;

        var actual = RunProbe("divergent", probe);

        Assert.Equal(
            ForRunningAbi(
                windows: [168, 72, 40, 8, 168, 72, 12, 16, 24, 32, 48, 56, 8, 80, 152, 160, 128, 5, 100, 33, 1, 100],
                sysv: [152, 64, 32, 4, 168, 72, 8, 12, 16, 24, 40, 48, 8, 72, 136, 144, 112, 5, 100, 33, 1, 100]),
            actual);
    }

    [Fact]
    public void Flattened_unions_and_anonymous_members_sit_at_their_absolute_offsets()
    {
        const string probe = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace Test.Interop;
            public static unsafe class Probe
            {
                static long Off(void* owner, void* member) => (byte*)member - (byte*)owner;

                public static long[] Run()
                {
                    Rec r = default;
                    Value v = default;
                    r.SHi = 0x1234;
                    r.Stats.ISum = 9;
                    v.IInt = 0x01020304;
                    return new long[]
                    {
                        Rec.SizeOf, Off(&r, Unsafe.AsPointer(ref r.IA)), Off(&r, Unsafe.AsPointer(ref r.FA)),
                        Off(&r, Unsafe.AsPointer(ref r.SLo)), Off(&r, Unsafe.AsPointer(ref r.SHi)),
                        Off(&r, Unsafe.AsPointer(ref r.Stats)), Off(&r, Unsafe.AsPointer(ref r.Val)),
                        *(short*)((byte*)&r + 6), *(int*)((byte*)&r + 12),
                        Off(&v, &v.PszString), *(byte*)&v,
                    };
                }
            }
            """;

        Assert.Equal([24, 4, 4, 4, 6, 8, 16, 0x1234, 9, 0, 4], RunProbe("unions", probe));
    }

    [Fact]
    public void Inline_arrays_follow_the_offsets_of_the_running_abi()
    {
        const string probe = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace Test.Interop;
            public static unsafe class Probe
            {
                static long Off(void* owner, void* member) => (byte*)member - (byte*)owner;

                public static long[] Run()
                {
                    Darr d = default;
                    d.IArr[1] = 7;
                    d.Longs[1] = new CLong(11);
                    Arrays a = default;
                    a.M[1][2] = 5;
                    a.Pts[3].Y = 6;
                    a.Names[2] = (nint)0x40;
                    return new long[]
                    {
                        Darr.SizeOf, Off(&d, Unsafe.AsPointer(ref d.IArr)), Off(&d, Unsafe.AsPointer(ref d.Longs)),
                        *(int*)((byte*)&d + (OperatingSystem.IsWindows() ? 4 : 8) + 4),
                        *(int*)((byte*)&d + (OperatingSystem.IsWindows() ? 16 : 24) + (OperatingSystem.IsWindows() ? 4 : 8)),
                        *(int*)((byte*)&a + 96 + 12 + 8), *(int*)((byte*)&a + 24 + 24 + 4), *(long*)((byte*)&a + 56 + 16),
                        sizeof(Arrays.NamesArray), sizeof(Arrays.MArray),
                    };
                }
            }
            """;

        Assert.Equal(
            ForRunningAbi(
                windows: [24, 4, 16, 7, 11, 5, 6, 0x40, 24, 24],
                sysv: [40, 8, 24, 7, 11, 5, 6, 0x40, 24, 24]),
            RunProbe("arrays", probe));
    }

    [Fact]
    public void Long_types_follow_the_size_of_the_running_abi()
    {
        const string probe = """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            namespace Test.Interop;
            public static unsafe class Probe
            {
                static long Off(void* owner, void* member) => (byte*)member - (byte*)owner;

                public static long[] Run()
                {
                    Longs s = default;
                    s.Bl = new CLong(-3);
                    s.Ul = new CULong(7);
                    return new long[]
                    {
                        Longs.SizeOf, Unsafe.SizeOf<CLong>(), Off(&s, Unsafe.AsPointer(ref s.Ul)), Off(&s, Unsafe.AsPointer(ref s.Ll)),
                        Off(&s, Unsafe.AsPointer(ref s.Size)), Off(&s, Unsafe.AsPointer(ref s.Tick)),
                        (long)s.Bl.Value, (long)s.Ul.Value,
                    };
                }
            }
            """;

        Assert.Equal(
            ForRunningAbi(
                windows: [40, 4, 4, 8, 16, 24, -3, 7],
                sysv: [48, 8, 8, 16, 24, 32, -3, 7]),
            RunProbe("clong", probe));
    }

    [Fact]
    public void Bitfields_set_and_get_without_touching_their_neighbours()
    {
        const string probe = """
            using System;
            using System.Numerics;
            using System.Runtime.InteropServices;
            namespace Test.Interop;
            public static unsafe class Probe
            {
                delegate void SetBits(ref Bits b, ulong v);

                static (ulong Lo, ulong Hi) Raw(Bits b) => (*(ulong*)&b, ((ulong*)&b)[1]);

                static void Fill(ref Bits b, byte pattern)
                {
                    fixed (Bits* p = &b) new Span<byte>(p, sizeof(Bits)).Fill(pattern);
                }

                public static long[] Run()
                {
                    var fields = new (int Bit, int Width, Func<Bits, ulong> Get, SetBits Set)[]
                    {
                        (32, 3, b => b.A, (ref Bits b, ulong v) => b.A = (uint)v),
                        (35, 5, b => b.B, (ref Bits b, ulong v) => b.B = (uint)v),
                        (40, 4, b => (ulong)b.C & 0xF, (ref Bits b, ulong v) => b.C = (int)((long)(v << 60) >> 60)),
                        (44, 1, b => b.D ? 1UL : 0UL, (ref Bits b, ulong v) => b.D = v != 0),
                        (45, 2, b => (ulong)b.E, (ref Bits b, ulong v) => b.E = (Mode)v),
                        (47, 17, b => b.F, (ref Bits b, ulong v) => b.F = (uint)v),
                        (64, 9, b => (ulong)b.G.Value, (ref Bits b, ulong v) => b.G = new CULong((nuint)v)),
                        (73, 7, b => (ulong)b.H.Value & 0x7F, (ref Bits b, ulong v) => b.H = new CLong((nint)((long)(v << 57) >> 57))),
                    };

                    var results = new System.Collections.Generic.List<long>();

                    // Flip every field in turn over two background patterns: only its own bits may change.
                    foreach (var pattern in new byte[] { 0xAA, 0x55 })
                    {
                        foreach (var f in fields)
                        {
                            Bits b = default;
                            Fill(ref b, pattern);
                            var before = Raw(b);
                            var expected = ~f.Get(b) & ((1UL << f.Width) - 1);
                            f.Set(ref b, expected);
                            var after = Raw(b);
                            var changed = new UInt128(before.Hi ^ after.Hi, before.Lo ^ after.Lo);
                            var mask = ((UInt128.One << f.Width) - 1) << f.Bit;
                            var outside = UInt128.PopCount(changed & ~mask);
                            var inside = UInt128.PopCount(changed & mask);
                            results.Add((long)outside + Math.Abs((long)inside - f.Width) + (f.Get(b) == expected ? 0 : 1));
                        }
                    }

                    // Concrete values.
                    Bits z = default;
                    z.A = 5;
                    var aByte = *((byte*)&z + 4);
                    var aRead = z.A;
                    var bRead = z.B;

                    Bits ones = default;
                    Fill(ref ones, 0xFF);
                    ones.A = 0;
                    var clearedByte = *((byte*)&ones + 4);

                    Bits s = default;
                    s.C = -3;
                    var cMinus3 = s.C;
                    var cByte = *((byte*)&s + 5);
                    s.C = 7;
                    var c7 = s.C;
                    s.C = 8;
                    var c8 = s.C;       // 8 does not fit a signed 4-bit field: wraps to -8

                    Bits w = default;
                    w.F = 0x1ABCD;
                    var fRead = w.F;
                    var fBytes = *((byte*)&w + 5) >> 7 | *((byte*)&w + 6) << 1 | *((byte*)&w + 7) << 9;

                    Bits t = default;
                    t.H = new CLong(-5);
                    t.E = Mode.ModeAuto;
                    t.D = true;
                    t.B = 32;           // 32 does not fit 5 bits: truncated to 0

                    results.AddRange(new long[]
                    {
                        aByte, aRead, bRead, clearedByte,
                        cMinus3, cByte & 0x0F, c7, c8,
                        fRead, fBytes,
                        (long)t.H.Value, (long)t.E, t.D ? 1 : 0, t.B,
                    });
                    return results.ToArray();
                }
            }
            """;

        var actual = RunProbe("bitfields", probe);

        Assert.Equal(new long[16], actual[..16]);
        Assert.Equal<long>([5, 5, 0, 0xF8, -3, 13, 7, -8, 0x1ABCD, 0x1ABCD, -5, 2, 1, 0], actual[16..]);
    }
}
