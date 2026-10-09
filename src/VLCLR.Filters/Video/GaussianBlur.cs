// Gaussian blur of planar YUV pictures with exactly the output of VLC's
// gaussianblur video filter (modules/video_filter/gaussianblur.c, integer
// build), in several implementations of the same arithmetic.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Plugin;

namespace VLCLR.Filters.Video;

/// <summary>
/// The code path a <see cref="GaussianBlur"/> uses. All of them produce the
/// same output, byte for byte.
/// </summary>
public enum GaussianBlurImplementation
{
    /// <summary>The fastest implementation the build and the CPU support.</summary>
    Auto,

    /// <summary>A line-for-line port of VLC's C code.</summary>
    Reference,

    /// <summary>
    /// The same arithmetic, restructured: division by a multiplication, row-wise
    /// vertical pass, symmetric taps, border split. Scalar, no SIMD.
    /// </summary>
    Tuned,

    /// <summary>AVX2: 16-bit horizontal pass, merged chroma taps, exact float division.</summary>
    Simd,

    /// <summary>AVX2 with wider loops, vpmaddwd in the vertical pass and vectorized borders.</summary>
    SimdMax,
}

/// <summary>
/// Gaussian blur of planar YUV pictures (I420, YV12, I422) with exactly the
/// output of VLC's gaussianblur filter. Buffers are allocated on the first
/// <see cref="Apply(VLCFrame, VLCFrame)"/> and reused while the picture size
/// stays the same.
/// </summary>
public sealed unsafe class GaussianBlur : IDisposable
{
    private const uint ChromaI422 = 0x32323449; // VLC_CODEC_I422, 'I','4','2','2'
    private const int MaxPlanes = 5;            // PICTURE_PLANE_MAX

    private readonly int[] _distribution;
    private readonly int _dim;
    private readonly GaussianBlurTuned.Setup? _tunedSetup;

    // Luma geometry the buffers below were built for.
    private int _lines = -1;
    private int _visiblePitch = -1;
    private int _pitch = -1;
    private int* _buffer;
    private int* _scale;
    private int* _accumulator;
    private ushort* _buffer16;
    private readonly nint[] _divisors = new nint[MaxPlanes];
    private readonly nint[] _reciprocals = new nint[MaxPlanes];
    private bool _disposed;

    /// <summary>
    /// Creates a blur. <paramref name="sigma"/> is the width of the bell curve,
    /// as VLC's --gaussianblur-sigma: neighbours up to 3 x sigma pixels away count.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">sigma is not greater than zero.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// <see cref="GaussianBlurImplementation.Simd"/> or <see cref="GaussianBlurImplementation.SimdMax"/>
    /// was requested without AVX2 in the build or on the CPU.
    /// </exception>
    public GaussianBlur(float sigma, GaussianBlurImplementation implementation = GaussianBlurImplementation.Auto)
    {
        if (!(sigma > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "sigma must be greater than zero.");
        }
        Sigma = sigma;
        _distribution = GaussianBlurReference.Distribution(sigma, out _dim);
        Implementation = Resolve(implementation, _distribution);
        if (Implementation == GaussianBlurImplementation.Tuned)
        {
            _tunedSetup = GaussianBlurTuned.Prepare(_distribution, _dim);
        }
    }

    ~GaussianBlur() => FreeBuffers();

    /// <summary>The width of the bell curve.</summary>
    public float Sigma { get; }

    /// <summary>Width of the blur window in pixels (2 x 3 x sigma + 1).</summary>
    public int WindowSize => 2 * _dim + 1;

    /// <summary>
    /// The implementation in use. Differs from the requested one for
    /// <see cref="GaussianBlurImplementation.Auto"/>, and for the SIMD versions
    /// with a tiny sigma (below about 0.6), whose weights do not fit their
    /// 16-bit lanes: those use <see cref="GaussianBlurImplementation.Tuned"/>.
    /// </summary>
    public GaussianBlurImplementation Implementation { get; }

    /// <summary>Whether pictures of this chroma can be blurred: I420, YV12 or I422.</summary>
    public static bool IsSupportedChroma(uint chroma)
        => chroma == VLCFourCC.I420 || chroma == VLCFourCC.YV12 || chroma == ChromaI422;

    /// <summary>Blurs <paramref name="input"/> into <paramref name="output"/>, a picture of the same format.</summary>
    public void Apply(VLCFrame input, VLCFrame output)
    {
        int count = Math.Min(input.PlaneCount, MaxPlanes);
        Span<VLCPlane> inputPlanes = stackalloc VLCPlane[count];
        Span<VLCPlane> outputPlanes = stackalloc VLCPlane[count];
        for (int i = 0; i < count; i++)
        {
            inputPlanes[i] = input.GetPlane(i);
            outputPlanes[i] = output.GetPlane(i);
        }
        Apply(inputPlanes, outputPlanes);
    }

    /// <summary>
    /// Blurs the planes of a picture: luma first, then the chroma planes, which
    /// may be subsampled horizontally and vertically by 2 (sizes rounded down,
    /// as VLC does). The layout must be VLC's: like VLC's filter, the blur reads the luma-sized normalization
    /// table with each chroma line's pitch times the subsampling, which must
    /// not exceed the luma pitch. Like VLC's filter, the horizontal pass reads
    /// up to two bytes past the visible width of each line, which VLC's
    /// picture buffers allow.
    /// </summary>
    /// <exception cref="ArgumentException">The planes do not have VLC's layout.</exception>
    public void Apply(ReadOnlySpan<VLCPlane> input, ReadOnlySpan<VLCPlane> output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output picture has fewer planes than the input.", nameof(output));
        }
        if (input.Length > MaxPlanes)
        {
            throw new ArgumentException($"At most {MaxPlanes} planes are supported.", nameof(input));
        }
        if (input.Length == 0 || input[0].VisibleLines <= 0 || input[0].Pitch <= 0)
        {
            return;
        }

        VLCPlane luma = input[0];
        for (int i = 0; i < input.Length; i++)
        {
            VLCPlane plane = input[i];
            if (plane.VisibleLines <= 0 || plane.VisiblePitch <= 0
                || plane.Pitch < plane.VisiblePitch || output[i].Pitch < plane.VisiblePitch)
            {
                throw new ArgumentException($"Plane {i} is empty or its pitch has no room for the visible width.", nameof(input));
            }
            // The subsampling is derived from the sizes, as in VLC's filter:
            // planes must be the luma size divided by 1 or 2, rounded down.
            int xFactor = luma.VisiblePitch / plane.VisiblePitch - 1;
            int yFactor = luma.VisibleLines / plane.VisibleLines - 1;
            if (xFactor is < 0 or > 1 || yFactor is < 0 or > 1
                || luma.VisiblePitch >> xFactor != plane.VisiblePitch || luma.VisibleLines >> yFactor != plane.VisibleLines)
            {
                throw new ArgumentException($"Plane {i} is not the luma size divided by 1 or 2.", nameof(input));
            }
            if ((long)plane.Pitch << xFactor > luma.Pitch)
            {
                throw new ArgumentException($"Plane {i}'s pitch times its subsampling exceeds the luma pitch.", nameof(input));
            }
        }
        Prepare(luma);
        fixed (int* distribution = _distribution)
        {
            for (int i = 0; i < input.Length; i++)
            {
                VLCPlane inPlane = input[i];
                VLCPlane outPlane = output[i];
                int xFactor = luma.VisiblePitch / inPlane.VisiblePitch - 1;
                int yFactor = luma.VisibleLines / inPlane.VisibleLines - 1;
                byte* source = (byte*)inPlane.Pixels;
                byte* destination = (byte*)outPlane.Pixels;

                switch (Implementation)
                {
                    case GaussianBlurImplementation.Simd:
                    case GaussianBlurImplementation.SimdMax:
                        if (_divisors[i] == 0)
                        {
                            nuint count = (nuint)inPlane.VisibleLines * (nuint)inPlane.VisiblePitch;
                            _divisors[i] = (nint)NativeMemory.Alloc(count, sizeof(int));
                            _reciprocals[i] = (nint)NativeMemory.Alloc(count, sizeof(float));
                            GaussianBlurSimd.ScaleTables(_scale, inPlane.Pitch, inPlane.VisibleLines, inPlane.VisiblePitch,
                                xFactor, yFactor, (int*)_divisors[i], (float*)_reciprocals[i]);
                        }
                        if (Implementation == GaussianBlurImplementation.SimdMax)
                        {
                            GaussianBlurSimdMax.Plane(source, inPlane.Pitch, inPlane.VisibleLines, inPlane.VisiblePitch,
                                destination, outPlane.Pitch, xFactor, yFactor, _buffer16, distribution, _dim,
                                (int*)_divisors[i], (float*)_reciprocals[i]);
                        }
                        else
                        {
                            GaussianBlurSimd.Plane(source, inPlane.Pitch, inPlane.VisibleLines, inPlane.VisiblePitch,
                                destination, outPlane.Pitch, xFactor, yFactor, _buffer16, distribution, _dim,
                                (int*)_divisors[i], (float*)_reciprocals[i]);
                        }
                        break;
                    case GaussianBlurImplementation.Tuned:
                        GaussianBlurTuned.Plane(source, inPlane.Pitch, inPlane.VisibleLines, inPlane.VisiblePitch,
                            destination, outPlane.Pitch, xFactor, yFactor, _buffer, _scale, distribution, _dim,
                            _tunedSetup!, _accumulator);
                        break;
                    default:
                        GaussianBlurReference.Plane(source, inPlane.Pitch, inPlane.VisibleLines, inPlane.VisiblePitch,
                            destination, outPlane.Pitch, xFactor, yFactor, _buffer, _scale, distribution, _dim);
                        break;
                }
            }
        }
    }

    /// <summary>Frees the buffers.</summary>
    public void Dispose()
    {
        FreeBuffers();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static GaussianBlurImplementation Resolve(GaussianBlurImplementation requested, int[] distribution)
    {
        bool simd = requested is GaussianBlurImplementation.Simd or GaussianBlurImplementation.SimdMax;
        if (simd && !GaussianBlurSimd.IsSupported)
        {
            throw new PlatformNotSupportedException(
                $"{requested} needs AVX2, in the build (x86-64-v3) and on the CPU.");
        }
        if (requested == GaussianBlurImplementation.Auto)
        {
            requested = GaussianBlurSimd.IsSupported ? GaussianBlurImplementation.SimdMax : GaussianBlurImplementation.Tuned;
        }
        if (requested is GaussianBlurImplementation.Simd or GaussianBlurImplementation.SimdMax
            && !GaussianBlurSimd.Fits(distribution))
        {
            return GaussianBlurImplementation.Tuned;
        }
        return requested;
    }

    // Buffers sized from the luma plane, rebuilt when it changes.
    private void Prepare(VLCPlane luma)
    {
        if (luma.VisibleLines == _lines && luma.VisiblePitch == _visiblePitch && luma.Pitch == _pitch)
        {
            return;
        }
        FreeBuffers();
        _lines = luma.VisibleLines;
        _visiblePitch = luma.VisiblePitch;
        _pitch = luma.Pitch;

        nuint elements = (nuint)luma.VisibleLines * (nuint)luma.Pitch;
        _scale = (int*)NativeMemory.Alloc(elements, sizeof(int));
        fixed (int* distribution = _distribution)
        {
            GaussianBlurReference.Scale(_scale, distribution, _dim, luma.VisibleLines, luma.VisiblePitch, luma.Pitch);
        }
        if (Implementation is GaussianBlurImplementation.Simd or GaussianBlurImplementation.SimdMax)
        {
            _buffer16 = (ushort*)NativeMemory.Alloc(elements, sizeof(ushort));
        }
        else
        {
            _buffer = (int*)NativeMemory.Alloc(elements, sizeof(int));
        }
        if (Implementation == GaussianBlurImplementation.Tuned)
        {
            _accumulator = (int*)NativeMemory.Alloc((nuint)luma.Pitch, sizeof(int));
        }
    }

    private void FreeBuffers()
    {
        NativeMemory.Free(_buffer);
        NativeMemory.Free(_scale);
        NativeMemory.Free(_accumulator);
        NativeMemory.Free(_buffer16);
        _buffer = null;
        _scale = null;
        _accumulator = null;
        _buffer16 = null;
        for (int i = 0; i < MaxPlanes; i++)
        {
            NativeMemory.Free((void*)_divisors[i]);
            NativeMemory.Free((void*)_reciprocals[i]);
            _divisors[i] = 0;
            _reciprocals[i] = 0;
        }
        _lines = _visiblePitch = _pitch = -1;
    }
}
