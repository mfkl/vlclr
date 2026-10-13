// Line-for-line port of yadif's line kernel as VLC has it
// (modules/video_filter/deinterlace/yadif.h, the FILTER and CHECK macros),
// for 8-bit and 16-bit samples.
//
// GPL-2.0-or-later, like the FFmpeg C code it is ported from.

using System.Runtime.CompilerServices;

namespace VLCLR.Filters.Video;

/// <summary>
/// Yadif's line kernel, statement for statement as in the C. Computes one
/// missing line of a field: <paramref name="w"/> pixels of
/// <paramref name="dst"/> from the lines above (<paramref name="mrefs"/>) and
/// below (<paramref name="prefs"/>) in the current picture and the same
/// positions in the previous and next pictures.
/// </summary>
internal static unsafe class YadifReference
{
    /// <summary>
    /// One line. <typeparamref name="T"/> is <see cref="byte"/> or
    /// <see cref="ushort"/>; <paramref name="prefs"/> and <paramref name="mrefs"/>
    /// are offsets in samples, not bytes. <paramref name="parity"/> 1 takes the
    /// temporal neighbours from prev and cur, 0 from cur and next.
    /// <paramref name="mode"/> 2 skips the check against the lines two above
    /// and below, which the first and last lines do not have.
    /// </summary>
    public static void Line<T>(T* dst, T* prev, T* cur, T* next, int w, int prefs, int mrefs, int parity, int mode)
        where T : unmanaged
    {
        T* prev2 = parity != 0 ? prev : cur;
        T* next2 = parity != 0 ? cur : next;
        for (int x = 0; x < w; x++)
        {
            int c = Load(cur + mrefs);
            int d = (Load(prev2) + Load(next2)) >> 1;
            int e = Load(cur + prefs);
            int temporal_diff0 = Math.Abs(Load(prev2) - Load(next2));
            int temporal_diff1 = (Math.Abs(Load(prev + mrefs) - c) + Math.Abs(Load(prev + prefs) - e)) >> 1;
            int temporal_diff2 = (Math.Abs(Load(next + mrefs) - c) + Math.Abs(Load(next + prefs) - e)) >> 1;
            int diff = Max3(temporal_diff0 >> 1, temporal_diff1, temporal_diff2);
            int spatial_pred = (c + e) >> 1;
            int spatial_score = Math.Abs(Load(cur + mrefs - 1) - Load(cur + prefs - 1)) + Math.Abs(c - e)
                              + Math.Abs(Load(cur + mrefs + 1) - Load(cur + prefs + 1)) - 1;

            // CHECK(-1) CHECK(-2) }} CHECK(1) CHECK(2) }}: the second
            // check of each direction only runs if the first one won.
            if (Check(cur, mrefs, prefs, -1, ref spatial_score, ref spatial_pred))
            {
                Check(cur, mrefs, prefs, -2, ref spatial_score, ref spatial_pred);
            }
            if (Check(cur, mrefs, prefs, 1, ref spatial_score, ref spatial_pred))
            {
                Check(cur, mrefs, prefs, 2, ref spatial_score, ref spatial_pred);
            }

            if (mode < 2)
            {
                int b = (Load(prev2 + 2 * mrefs) + Load(next2 + 2 * mrefs)) >> 1;
                int f = (Load(prev2 + 2 * prefs) + Load(next2 + 2 * prefs)) >> 1;
                int max = Max3(d - e, d - c, Math.Min(b - c, f - e));
                int min = Min3(d - e, d - c, Math.Max(b - c, f - e));

                diff = Max3(diff, min, -max);
            }

            if (spatial_pred > d + diff)
            {
                spatial_pred = d + diff;
            }
            else if (spatial_pred < d - diff)
            {
                spatial_pred = d - diff;
            }

            Store(dst, spatial_pred);

            dst++;
            cur++;
            prev++;
            next++;
            prev2++;
            next2++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Check<T>(T* cur, int mrefs, int prefs, int j, ref int spatial_score, ref int spatial_pred)
        where T : unmanaged
    {
        int score = Math.Abs(Load(cur + mrefs - 1 + j) - Load(cur + prefs - 1 - j))
                  + Math.Abs(Load(cur + mrefs + j) - Load(cur + prefs - j))
                  + Math.Abs(Load(cur + mrefs + 1 + j) - Load(cur + prefs + 1 - j));
        if (score < spatial_score)
        {
            spatial_score = score;
            spatial_pred = (Load(cur + mrefs + j) + Load(cur + prefs - j)) >> 1;
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Load<T>(T* p) where T : unmanaged
        => typeof(T) == typeof(byte) ? *(byte*)p : *(ushort*)p;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Store<T>(T* p, int value) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            *(byte*)p = (byte)value;
        }
        else
        {
            *(ushort*)p = (ushort)value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Max3(int a, int b, int c) => Math.Max(Math.Max(a, b), c);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Min3(int a, int b, int c) => Math.Min(Math.Min(a, b), c);
}
