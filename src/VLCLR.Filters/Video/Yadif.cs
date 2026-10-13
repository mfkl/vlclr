// Yadif deinterlacing of planar YUV pictures with exactly the output of VLC's
// deinterlace filter in yadif and yadif2x modes (modules/video_filter/
// deinterlace/algo_yadif.c), in several implementations of the same kernel.
//
// GPL-2.0-or-later, like the FFmpeg C code (yadif.h) the kernels are ported from.

using System.Runtime.CompilerServices;
using VLCLR.Native;

namespace VLCLR.Filters.Video;

/// <summary>
/// The code path a <see cref="Yadif"/> uses. All of them produce the same
/// output, sample for sample.
/// </summary>
public enum YadifImplementation
{
    /// <summary>The fastest implementation the build, the CPU and the bit depth support.</summary>
    Auto,

    /// <summary>A line-for-line port of VLC's C kernel.</summary>
    Reference,

    /// <summary>
    /// The same arithmetic written for RyuJIT: branch-free absolute values,
    /// min and max, no ref locals, native-sized offsets. Scalar, no SIMD.
    /// </summary>
    Scalar,

    /// <summary>
    /// 8 pixels at a time in 16-bit lanes, the structure of VLC's SSSE3
    /// assembly, with the cross-platform Vector128 API (SSE, NEON). Up to 12 bits.
    /// </summary>
    Vector128,

    /// <summary>16 pixels at a time with AVX2. Up to 12 bits.</summary>
    Vector256,

    /// <summary>32 pixels at a time with AVX-512 BW. Up to 12 bits.</summary>
    Vector512,
}

/// <summary>
/// Yadif ("yet another deinterlacing filter"): rebuilds the missing field of
/// an interlaced picture from the lines above and below and from the
/// previous and next pictures, with exactly the output of VLC's deinterlace
/// filter in yadif and yadif2x modes. Stateless: the caller keeps the three
/// pictures.
/// </summary>
public sealed unsafe class Yadif
{
    private const int MaxPlanes = 5; // PICTURE_PLANE_MAX

    /// <summary>
    /// Creates a deinterlacer for samples of <paramref name="bitDepth"/> bits:
    /// 8 for one byte per sample, up to 16 for two.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The bit depth is not between 8 and 16.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// A vector implementation was requested that the build (instruction set) or the CPU does not support.
    /// </exception>
    public Yadif(YadifImplementation implementation = YadifImplementation.Auto, int bitDepth = 8)
    {
        if (bitDepth is < 8 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "The bit depth must be between 8 and 16.");
        }
        BitDepth = bitDepth;
        Implementation = Resolve(implementation);
    }

    /// <summary>Bits per sample of the pictures.</summary>
    public int BitDepth { get; }

    /// <summary>The implementation in use. Differs from the requested one for <see cref="YadifImplementation.Auto"/>.</summary>
    public YadifImplementation Implementation { get; }

    /// <summary>
    /// Renders one deinterlaced picture into <paramref name="output"/> from
    /// three consecutive pictures of the same format and layout, as VLC's
    /// RenderYadif. The lines of <paramref name="current"/> whose index modulo
    /// 2 is <paramref name="field"/> are copied, the others are rebuilt.
    /// <paramref name="parity"/> says where the missing field is in time:
    /// 1 between <paramref name="previous"/> and <paramref name="current"/>
    /// (the first field of a frame), 0 between <paramref name="current"/> and
    /// <paramref name="next"/> (the second), 2 nowhere: a repeated field, the
    /// whole picture is copied. The first and last lines are copies of their
    /// neighbours. Like VLC's filter, the kernel reads up to three samples
    /// before and after each line and two lines above and below, which VLC's
    /// picture buffers allow.
    /// </summary>
    /// <exception cref="ArgumentException">The pictures do not have the same layout.</exception>
    public void Render(ReadOnlySpan<VLCPlane> previous, ReadOnlySpan<VLCPlane> current, ReadOnlySpan<VLCPlane> next,
        ReadOnlySpan<VLCPlane> output, int field, int parity)
    {
        if (field is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "The field must be 0 or 1.");
        }
        if (parity is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(parity), parity, "The parity must be 0, 1 or 2.");
        }
        int planes = output.Length;
        if (planes > MaxPlanes || previous.Length < planes || current.Length < planes || next.Length < planes)
        {
            throw new ArgumentException("The pictures do not have the same number of planes.", nameof(output));
        }
        int pixelSize = BitDepth > 8 ? 2 : 1;
        for (int n = 0; n < planes; n++)
        {
            VLCPlane cur = current[n];
            if (previous[n].Pitch != cur.Pitch || next[n].Pitch != cur.Pitch)
            {
                throw new ArgumentException($"Plane {n} does not have the same pitch in the three input pictures.", nameof(current));
            }
            VLCPlane dst = output[n];
            if (dst.VisiblePitch > cur.VisiblePitch || dst.VisibleLines > cur.VisibleLines
                || dst.VisiblePitch > dst.Pitch || dst.VisiblePitch % pixelSize != 0 || cur.Pitch % pixelSize != 0)
            {
                throw new ArgumentException($"Plane {n} of the output does not fit the input.", nameof(output));
            }
        }

        for (int n = 0; n < planes; n++)
        {
            if (pixelSize == 2)
            {
                Plane<ushort>(previous[n], current[n], next[n], output[n], field, parity);
            }
            else
            {
                Plane<byte>(previous[n], current[n], next[n], output[n], field, parity);
            }
        }
    }

    // The vector kernels compute in 16-bit lanes, exact up to 12 bits: deeper
    // samples fall back to Scalar, as Auto does on hardware without SIMD.
    private YadifImplementation Resolve(YadifImplementation requested)
    {
        bool lanesFit = BitDepth <= YadifVector128.MaxBitDepth;
        bool supported = requested switch
        {
            YadifImplementation.Vector128 => YadifVector128.IsSupported,
            YadifImplementation.Vector256 => YadifVector256.IsSupported,
            YadifImplementation.Vector512 => YadifVector512.IsSupported,
            _ => true,
        };
        if (!supported)
        {
            throw new PlatformNotSupportedException($"{requested} is not supported by the build or the CPU.");
        }
        if (requested == YadifImplementation.Auto)
        {
            requested = YadifVector512.IsSupported ? YadifImplementation.Vector512
                : YadifVector256.IsSupported ? YadifImplementation.Vector256
                : YadifVector128.IsSupported ? YadifImplementation.Vector128
                : YadifImplementation.Scalar;
        }
        if (!lanesFit && requested is YadifImplementation.Vector128 or YadifImplementation.Vector256 or YadifImplementation.Vector512)
        {
            return YadifImplementation.Scalar;
        }
        return requested;
    }

    // The per-plane loop of RenderYadif: copy the kept lines, filter the
    // others, duplicate the first and last lines.
    private void Plane<T>(VLCPlane previous, VLCPlane current, VLCPlane next, VLCPlane output, int field, int parity)
        where T : unmanaged
    {
        byte* dstPixels = (byte*)output.Pixels;
        byte* prevPixels = (byte*)previous.Pixels;
        byte* curPixels = (byte*)current.Pixels;
        byte* nextPixels = (byte*)next.Pixels;
        int pitch = current.Pitch;
        int dstPitch = output.Pitch;
        int lines = output.VisibleLines;
        int visiblePitch = output.VisiblePitch;
        int width = visiblePitch / sizeof(T);
        int stride = pitch / sizeof(T);

        for (int y = 1; y < lines - 1; y++)
        {
            byte* dstLine = dstPixels + (nint)y * dstPitch;
            if (y % 2 == field || parity == 2)
            {
                Unsafe.CopyBlockUnaligned(dstLine, curPixels + (nint)y * pitch, (uint)visiblePitch);
            }
            else
            {
                // Spatial checks only when enough data
                int mode = y >= 2 && y < lines - 2 ? 0 : 2;
                int prefs = y < lines - 2 ? stride : -stride;
                int mrefs = y - 1 != 0 ? -stride : stride;
                Line((T*)dstLine, (T*)(prevPixels + (nint)y * pitch), (T*)(curPixels + (nint)y * pitch),
                    (T*)(nextPixels + (nint)y * pitch), width, prefs, mrefs, parity, mode);
            }

            // We duplicate the first and last lines
            if (y == 1)
            {
                Unsafe.CopyBlockUnaligned(dstLine - dstPitch, dstLine, (uint)dstPitch);
            }
            else if (y == lines - 2)
            {
                Unsafe.CopyBlockUnaligned(dstLine + dstPitch, dstLine, (uint)dstPitch);
            }
        }
    }

    private void Line<T>(T* dst, T* prev, T* cur, T* next, int w, int prefs, int mrefs, int parity, int mode)
        where T : unmanaged
    {
        switch (Implementation)
        {
            case YadifImplementation.Vector512:
                YadifVector512.Line(dst, prev, cur, next, w, prefs, mrefs, parity, mode);
                break;
            case YadifImplementation.Vector256:
                YadifVector256.Line(dst, prev, cur, next, w, prefs, mrefs, parity, mode);
                break;
            case YadifImplementation.Vector128:
                YadifVector128.Line(dst, prev, cur, next, w, prefs, mrefs, parity, mode);
                break;
            case YadifImplementation.Scalar:
                YadifScalar.Line(dst, prev, cur, next, w, prefs, mrefs, parity, mode);
                break;
            default:
                YadifReference.Line(dst, prev, cur, next, w, prefs, mrefs, parity, mode);
                break;
        }
    }
}
