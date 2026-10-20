// Line-for-line port of VLC's "X" deinterlacer
// (modules/video_filter/deinterlace/algo_x.c, the C path of RenderX), which
// VLC's yadif renders the first picture with, before it has a history.
// Kept as the C has it, quirks included: it works on bytes whatever the
// sample size, XDeintNxN passes width and height to XDeintNxNDetect swapped,
// and blocks read and write up to two lines past the band they are in.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

using System.Runtime.CompilerServices;
using VLCLR.Native;

namespace VLCLR.Filters.Video;

/// <summary>
/// VLC's "X" deinterlacer: per 8x8 block, detects combing and either
/// interpolates the second field along edges or blends the two fields, with
/// exactly the output of VLC's RenderX. Works on one picture, without history.
/// </summary>
public static unsafe class XDeinterlace
{
    /// <summary>
    /// Deinterlaces <paramref name="input"/> into <paramref name="output"/>,
    /// as VLC's RenderX: every plane, in bytes. Like VLC's filter, blocks read
    /// up to two lines below the visible ones and four bytes before and five
    /// after each line, and write up to one line below, which VLC's picture
    /// buffers allow.
    /// </summary>
    /// <exception cref="ArgumentException">The output has fewer planes than the input.</exception>
    public static void Render(ReadOnlySpan<VLCPlane> input, ReadOnlySpan<VLCPlane> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output has fewer planes than the input.", nameof(output));
        }

        // Copy image and skip lines
        for (int plane = 0; plane < input.Length; plane++)
        {
            VLCPlane outPlane = output[plane];
            int mby = (outPlane.VisibleLines + 7) / 8 - 1;
            int mbx = outPlane.VisiblePitch / 8;

            int mody = outPlane.VisibleLines - 8 * mby;
            int modx = outPlane.VisiblePitch - 8 * mbx;

            int iDst = outPlane.Pitch;
            int iSrc = input[plane].Pitch;

            int y;
            for (y = 0; y < mby; y++)
            {
                byte* dst = (byte*)outPlane.Pixels + 8 * y * iDst;
                byte* src = (byte*)input[plane].Pixels + 8 * y * iSrc;

                Band8x8(dst, iDst, src, iSrc, mbx, modx);
            }

            // Last line (C only)
            if (mody != 0)
            {
                byte* dst = (byte*)outPlane.Pixels + 8 * y * iDst;
                byte* src = (byte*)input[plane].Pixels + 8 * y * iSrc;

                for (int x = 0; x < mbx; x++)
                {
                    NxN(dst, iDst, src, iSrc, 8, mody);

                    dst += 8;
                    src += 8;
                }

                if (modx != 0)
                {
                    NxN(dst, iDst, src, iSrc, modx, mody);
                }
            }
        }
    }

    private static int Ssd(int a) => a * a;

    // XDeint8x8DetectC: is the 8x8 block interlaced? Reads 8x10.
    private static bool Detect8x8(byte* src, int iSrc)
    {
        // Detect interlacing
        int fc = 0;
        for (int y = 0; y < 7; y += 2)
        {
            int ff = 0, fr = 0;
            for (int x = 0; x < 8; x++)
            {
                fr += Ssd(src[x] - src[1 * iSrc + x]) +
                      Ssd(src[iSrc + x] - src[2 * iSrc + x]);
                ff += Ssd(src[x] - src[2 * iSrc + x]) +
                      Ssd(src[iSrc + x] - src[3 * iSrc + x]);
            }
            if (ff < 6 * fr / 8 && fr > 32)
            {
                fc++;
            }

            src += 2 * iSrc;
        }

        return fc >= 1;
    }

    // XDeint8x8MergeC
    private static void Merge8x8(byte* dst, int iDst, byte* src1, int iSrc1, byte* src2, int iSrc2)
    {
        // Progressive
        for (int y = 0; y < 8; y += 2)
        {
            Unsafe.CopyBlockUnaligned(dst, src1, 8);
            dst += iDst;

            for (int x = 0; x < 8; x++)
            {
                dst[x] = (byte)((src1[x] + 6 * src2[x] + src1[iSrc1 + x] + 4) >> 3);
            }
            dst += iDst;

            src1 += iSrc1;
            src2 += iSrc2;
        }
    }

    // XDeint8x8FieldEC: (1,0,1) for the blocks at the ends of a band. Reads 8x9.
    private static void FieldE8x8(byte* dst, int iDst, byte* src, int iSrc)
    {
        // Interlaced
        for (int y = 0; y < 8; y += 2)
        {
            Unsafe.CopyBlockUnaligned(dst, src, 8);
            dst += iDst;

            for (int x = 0; x < 8; x++)
            {
                dst[x] = (byte)((src[x] + src[2 * iSrc + x]) >> 1);
            }
            dst += 1 * iDst;
            src += 2 * iSrc;
        }
    }

    // XDeint8x8FieldC: edge oriented interpolation. Reads 4 bytes before and
    // 5 after, and one line more.
    private static void Field8x8(byte* dst, int iDst, byte* src, int iSrc)
    {
        // Interlaced
        for (int y = 0; y < 8; y += 2)
        {
            Unsafe.CopyBlockUnaligned(dst, src, 8);
            dst += iDst;

            for (int x = 0; x < 8; x++)
            {
                byte* src2 = &src[2 * iSrc];
                int c0 = Math.Abs(src[x - 4] - src2[x - 2]) + Math.Abs(src[x - 3] - src2[x - 1]) +
                         Math.Abs(src[x - 2] - src2[x + 0]) + Math.Abs(src[x - 1] - src2[x + 1]) +
                         Math.Abs(src[x + 0] - src2[x + 2]) + Math.Abs(src[x + 1] - src2[x + 3]) +
                         Math.Abs(src[x + 2] - src2[x + 4]) + Math.Abs(src[x + 3] - src2[x + 5]);

                int c1 = Math.Abs(src[x - 3] - src2[x - 3]) + Math.Abs(src[x - 2] - src2[x - 2]) +
                         Math.Abs(src[x - 1] - src2[x - 1]) + Math.Abs(src[x + 0] - src2[x + 0]) +
                         Math.Abs(src[x + 1] - src2[x + 1]) + Math.Abs(src[x + 2] - src2[x + 2]) +
                         Math.Abs(src[x + 3] - src2[x + 3]) + Math.Abs(src[x + 4] - src2[x + 4]);

                int c2 = Math.Abs(src[x - 2] - src2[x - 4]) + Math.Abs(src[x - 1] - src2[x - 3]) +
                         Math.Abs(src[x + 0] - src2[x - 2]) + Math.Abs(src[x + 1] - src2[x - 1]) +
                         Math.Abs(src[x + 2] - src2[x + 0]) + Math.Abs(src[x + 3] - src2[x + 1]) +
                         Math.Abs(src[x + 4] - src2[x + 2]) + Math.Abs(src[x + 5] - src2[x + 3]);

                if (c0 < c1 && c1 <= c2)
                {
                    dst[x] = (byte)((src[x - 1] + src2[x + 1]) >> 1);
                }
                else if (c2 < c1 && c1 <= c0)
                {
                    dst[x] = (byte)((src[x + 1] + src2[x - 1]) >> 1);
                }
                else
                {
                    dst[x] = (byte)((src[x + 0] + src2[x + 0]) >> 1);
                }
            }

            dst += 1 * iDst;
            src += 2 * iSrc;
        }
    }

    // XDeintNxNDetect, with its parameters in the C's order: height, then width.
    private static bool DetectNxN(byte* src, int iSrc, int height, int width)
    {
        // Detect interlacing
        int ff = 0, fr = 0;
        int fc = 0;
        for (int y = 0; y < height - 2; y += 2)
        {
            byte* s = &src[y * iSrc];
            for (int x = 0; x < width; x++)
            {
                fr += Ssd(s[x] - s[1 * iSrc + x]);
                ff += Ssd(s[x] - s[2 * iSrc + x]);
            }
            if (ff < fr && fr > width / 2)
            {
                fc++;
            }
        }

        return fc >= 2;
    }

    // XDeintNxNFrame
    private static void FrameNxN(byte* dst, int iDst, byte* src, int iSrc, int width, int height)
    {
        // Progressive
        for (int y = 0; y < height; y += 2)
        {
            Unsafe.CopyBlockUnaligned(dst, src, (uint)width);
            dst += iDst;

            if (y < height - 2)
            {
                for (int x = 0; x < width; x++)
                {
                    dst[x] = (byte)((src[x] + 2 * src[1 * iSrc + x] + src[2 * iSrc + x] + 2) >> 2);
                }
            }
            else
            {
                // Blend last line
                for (int x = 0; x < width; x++)
                {
                    dst[x] = (byte)((src[x] + src[1 * iSrc + x]) >> 1);
                }
            }
            dst += 1 * iDst;
            src += 2 * iSrc;
        }
    }

    // XDeintNxNField
    private static void FieldNxN(byte* dst, int iDst, byte* src, int iSrc, int width, int height)
    {
        // Interlaced
        for (int y = 0; y < height; y += 2)
        {
            Unsafe.CopyBlockUnaligned(dst, src, (uint)width);
            dst += iDst;

            if (y < height - 2)
            {
                for (int x = 0; x < width; x++)
                {
                    dst[x] = (byte)((src[x] + src[2 * iSrc + x]) >> 1);
                }
            }
            else
            {
                // Blend last line
                for (int x = 0; x < width; x++)
                {
                    dst[x] = (byte)((src[x] + src[iSrc + x]) >> 1);
                }
            }
            dst += 1 * iDst;
            src += 2 * iSrc;
        }
    }

    // XDeintNxN: the C passes width as the height and height as the width.
    private static void NxN(byte* dst, int iDst, byte* src, int iSrc, int width, int height)
    {
        if (DetectNxN(src, iSrc, width, height))
        {
            FieldNxN(dst, iDst, src, iSrc, width, height);
        }
        else
        {
            FrameNxN(dst, iDst, src, iSrc, width, height);
        }
    }

    // XDeintBand8x8C
    private static void Band8x8(byte* dst, int iDst, byte* src, int iSrc, int mbx, int modx)
    {
        for (int x = 0; x < mbx; x++)
        {
            if (Detect8x8(src, iSrc))
            {
                if (x == 0 || x == mbx - 1)
                {
                    FieldE8x8(dst, iDst, src, iSrc);
                }
                else
                {
                    Field8x8(dst, iDst, src, iSrc);
                }
            }
            else
            {
                Merge8x8(dst, iDst, &src[0 * iSrc], 2 * iSrc, &src[1 * iSrc], 2 * iSrc);
            }

            dst += 8;
            src += 8;
        }

        if (modx != 0)
        {
            NxN(dst, iDst, src, iSrc, modx, 8);
        }
    }
}
