using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using VLCLR.Filters.Video;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests.Filters;

public unsafe class GaussianBlurTests
{
    // A planar YUV picture in native memory with padded lines, laid out like
    // VLC's: the luma pitch is the chroma pitch times the subsampling. VLC's
    // filter reads up to two bytes past the visible width, so the padding is
    // part of the input and is filled too.
    private sealed class TestPicture : IDisposable
    {
        private const int Padding = 32;
        private readonly nint[] _memory;

        public TestPicture(int width, int height, int chromaXShift, int chromaYShift, int seed)
        {
            Planes = new VLCPlane[3];
            _memory = new nint[3];
            var random = new Random(seed);
            int chromaPitch = (width >> chromaXShift) + Padding;
            for (int i = 0; i < 3; i++)
            {
                // Chroma sizes round down, as in VLC (width * num / den).
                int visiblePitch = i == 0 ? width : width >> chromaXShift;
                int visibleLines = i == 0 ? height : height >> chromaYShift;
                int pitch = i == 0 ? chromaPitch << chromaXShift : chromaPitch;
                int size = pitch * visibleLines;
                _memory[i] = (nint)NativeMemory.Alloc((nuint)size);
                random.NextBytes(new Span<byte>((void*)_memory[i], size));
                Planes[i] = new VLCPlane
                {
                    Pixels = _memory[i],
                    Lines = visibleLines,
                    Pitch = pitch,
                    PixelPitch = 1,
                    VisibleLines = visibleLines,
                    VisiblePitch = visiblePitch,
                };
            }
        }

        public VLCPlane[] Planes { get; }

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

        public void Dispose()
        {
            foreach (nint memory in _memory)
            {
                NativeMemory.Free((void*)memory);
            }
        }
    }

    private static byte[] Blur(float sigma, GaussianBlurImplementation implementation,
        int width, int height, int chromaXShift, int chromaYShift)
    {
        using var input = new TestPicture(width, height, chromaXShift, chromaYShift, seed: width * 1000 + height);
        using var output = new TestPicture(width, height, chromaXShift, chromaYShift, seed: 1);
        using var blur = new GaussianBlur(sigma, implementation);
        blur.Apply(input.Planes, output.Planes);
        return output.Visible();
    }

    public static TheoryData<int, int, int, int, float> Pictures()
    {
        var data = new TheoryData<int, int, int, int, float>();
        foreach (float sigma in new[] { 0.7f, 2f, 4f, 8f })
        {
            data.Add(64, 48, 1, 1, sigma);   // I420
            data.Add(97, 61, 1, 1, sigma);   // I420, odd size
            data.Add(150, 40, 1, 0, sigma);  // I422
            data.Add(40, 40, 1, 1, sigma);   // interior narrower than one SIMD block at sigma 8
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void EveryImplementation_MatchesTheReference(int width, int height, int chromaXShift, int chromaYShift, float sigma)
    {
        byte[] reference = Blur(sigma, GaussianBlurImplementation.Reference, width, height, chromaXShift, chromaYShift);

        var implementations = new List<GaussianBlurImplementation> { GaussianBlurImplementation.Tuned };
        if (Avx2.IsSupported)
        {
            implementations.Add(GaussianBlurImplementation.Simd);
            implementations.Add(GaussianBlurImplementation.SimdMax);
        }
        foreach (GaussianBlurImplementation implementation in implementations)
        {
            byte[] result = Blur(sigma, implementation, width, height, chromaXShift, chromaYShift);
            Assert.True(reference.AsSpan().SequenceEqual(result),
                $"{implementation} differs from the reference ({width}x{height}, sigma {sigma})");
        }
    }

    [Fact]
    public void Apply_RejectsChromaPlanesWiderThanTheLumaLayout()
    {
        using var input = new TestPicture(64, 48, 1, 1, seed: 3);
        using var output = new TestPicture(64, 48, 1, 1, seed: 1);
        VLCPlane[] planes = input.Planes.ToArray();
        planes[1].Pitch = input.Planes[0].Pitch;   // chroma pitch << 1 now exceeds the luma pitch
        using var blur = new GaussianBlur(2f);
        Assert.Throws<ArgumentException>(() => blur.Apply(planes, output.Planes));
    }

    [Fact]
    public void Apply_RejectsChromaPlanesThatAreNotHalfOrFullSize()
    {
        using var input = new TestPicture(97, 61, 1, 1, seed: 3);
        using var output = new TestPicture(97, 61, 1, 1, seed: 1);
        VLCPlane[] planes = input.Planes.ToArray();
        planes[1].VisiblePitch = 49;   // rounded up: 97 / 49 - 1 would read as no subsampling
        using var blur = new GaussianBlur(2f);
        Assert.Throws<ArgumentException>(() => blur.Apply(planes, output.Planes));
    }

    [Fact]
    public void Auto_PicksTheFastestAvailableImplementation()
    {
        using var blur = new GaussianBlur(4f);
        Assert.Equal(Avx2.IsSupported ? GaussianBlurImplementation.SimdMax : GaussianBlurImplementation.Tuned,
            blur.Implementation);
        Assert.Equal(25, blur.WindowSize);
    }

    [Fact]
    public void Simd_WithATinySigma_FallsBackToTuned()
    {
        if (!Avx2.IsSupported)
        {
            Assert.Throws<PlatformNotSupportedException>(() => new GaussianBlur(0.3f, GaussianBlurImplementation.Simd));
            return;
        }
        using var blur = new GaussianBlur(0.3f, GaussianBlurImplementation.SimdMax);
        Assert.Equal(GaussianBlurImplementation.Tuned, blur.Implementation);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void Constructor_RejectsANonPositiveSigma(float sigma)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GaussianBlur(sigma));
    }

    [Fact]
    public void Apply_RebuildsItsBuffersWhenThePictureSizeChanges()
    {
        using var blur = new GaussianBlur(4f);
        using (var small = new TestPicture(64, 48, 1, 1, seed: 7))
        using (var smallOutput = new TestPicture(64, 48, 1, 1, seed: 1))
        {
            blur.Apply(small.Planes, smallOutput.Planes);
        }

        using var input = new TestPicture(97, 61, 1, 1, seed: 97 * 1000 + 61);
        using var output = new TestPicture(97, 61, 1, 1, seed: 1);
        blur.Apply(input.Planes, output.Planes);

        Assert.Equal(Blur(4f, GaussianBlurImplementation.Reference, 97, 61, 1, 1), output.Visible());
    }
}
