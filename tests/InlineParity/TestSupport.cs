using System.Runtime.InteropServices;
using System.Text;
using InlineParity.Native;
using Xunit.Sdk;

// The stand-ins in the C library hold global state, so tests must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace InlineParity;

/// <summary>Deterministic inputs shared by the tests.</summary>
internal static class Cases
{
    public const int Seed = 20250607;
    public const int RandomCount = 1000;

    public static Random NewRandom(int salt = 0) => new(Seed + salt);

    /// <summary>A uniformly random 64-bit pattern.</summary>
    public static long Full(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToInt64(bytes);
    }

    /// <summary>Edge values for a signed 64-bit integer.</summary>
    public static readonly long[] EdgeInt64 =
    [
        0, 1, -1, 2, -2, 9, 10, 11, 999, 1000, 1001, -999, -1000, -1001,
        999_999, 1_000_000, 1_000_001, -1_000_000, -1_000_001,
        int.MaxValue, int.MinValue, (long)int.MaxValue + 1, (long)int.MinValue - 1,
        uint.MaxValue, (long)uint.MaxValue + 1,
        long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1,
        0x0101_0101_0101_0101, unchecked((long)0xFEDC_BA98_7654_3210),
    ];

    /// <summary>Edge values for a 32-bit unsigned integer (fourcc, capability masks).</summary>
    public static readonly uint[] EdgeUInt32 =
    [
        0, 1, 0xFF, 0x100, 0x7F7F7F7F, 0x80808080, 0xFFFFFFFF, 0x80000000, 0x7FFFFFFF,
        0x30323449, 0x32315659, 0x32335652, 0x41424752,
    ];

    /// <summary>Edge values and seeded random values, each limited to |v| &lt;= <paramref name="limit"/>.</summary>
    public static IEnumerable<long> Int64Within(long limit, int salt)
    {
        foreach (long v in EdgeInt64)
        {
            if (v >= -limit && v <= limit)
            {
                yield return v;
            }
        }

        yield return limit;
        yield return -limit;

        Random random = NewRandom(salt);
        for (int i = 0; i < RandomCount; i++)
        {
            // Spread magnitudes: a random number of bits, then a random sign.
            int bits = random.Next(1, 64);
            long magnitude = Full(random) & ((1L << bits) - 1);
            magnitude = Math.Min(magnitude, limit);
            yield return random.Next(2) == 0 ? magnitude : -magnitude;
        }
    }

    public static IEnumerable<long> AnyInt64(int salt)
    {
        foreach (long v in EdgeInt64)
        {
            yield return v;
        }

        Random random = NewRandom(salt);
        for (int i = 0; i < RandomCount; i++)
        {
            yield return Full(random);
        }
    }

    public static IEnumerable<uint> AnyUInt32(int salt)
    {
        foreach (uint v in EdgeUInt32)
        {
            yield return v;
        }

        Random random = NewRandom(salt);
        for (int i = 0; i < RandomCount; i++)
        {
            yield return (uint)random.NextInt64(0, 1L << 32);
        }
    }

    /// <summary>Variable names: ASCII, empty, long, non-ASCII.</summary>
    public static IEnumerable<string> Names(int salt)
    {
        yield return "x";
        yield return "video-filter";
        yield return "";
        yield return "naïve-ñame-日本語-🎞";
        yield return new string('n', 100);

        Random random = NewRandom(salt);
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_ äöü日本";
        for (int i = 0; i < 100; i++)
        {
            yield return RandomText(random, alphabet, random.Next(1, 40));
        }
    }

    /// <summary>String values: null is yielded by callers that accept it; no NUL, no lone surrogates.</summary>
    public static IEnumerable<string> Strings(int salt)
    {
        yield return "";
        yield return "a";
        yield return "100% sure %s %d %%";
        yield return "tab\tnewline\nend";
        yield return "ünïcödé 日本語 🎞";
        yield return new string('x', 300);

        Random random = NewRandom(salt);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789%/\\:.-éß日本";
        for (int i = 0; i < 100; i++)
        {
            yield return RandomText(random, alphabet, random.Next(0, 60));
        }
    }

    private static string RandomText(Random random, string alphabet, int length)
    {
        StringBuilder text = new(length);
        for (int i = 0; i < length; i++)
        {
            text.Append(alphabet[random.Next(alphabet.Length)]);
        }

        return text.ToString();
    }
}

internal static class ParityAssert
{
    public static void Equal<T>(T c, T managed, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(c, managed))
        {
            throw new XunitException($"{context}: C original = {c}, managed = {managed}");
        }
    }

    public static void Same(LogSnapshot c, LogSnapshot managed, string context)
    {
        if (c != managed)
        {
            throw new XunitException($"{context}: recorded calls differ{Environment.NewLine}  C original: {c}{Environment.NewLine}  managed:    {managed}");
        }
    }

    public static void FloatBitsEqual(float c, float managed, string context)
        => Equal(BitConverter.SingleToInt32Bits(c), BitConverter.SingleToInt32Bits(managed), context);
}

/// <summary>Runs an action against freshly reset stand-ins and captures what they saw.</summary>
internal static unsafe class Harness
{
    public static (T Result, LogSnapshot Log) Run<T>(ParityNative.ScriptSetup? script, Func<T> action)
    {
        ParityNative.Reset(script);
        T result = action();
        return (result, ParityNative.Snapshot());
    }

    /// <summary>Takes ownership of a malloc'ed C string, frees it, returns its text (null for NULL).</summary>
    public static string? TakeCString(nint pointer)
    {
        if (pointer == 0)
        {
            return null;
        }

        string text = Marshal.PtrToStringUTF8(pointer)!;
        ParityNative.parity_free(pointer);
        return text;
    }
}
