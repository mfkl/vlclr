using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using VLCLR.Filters.Video;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests.Filters;

// Every implementation against the line-for-line port. The port itself is
// checked against VLC's C and assembly, sample for sample, by
// benchmarks/YadifCompare/Harness.
public unsafe class YadifTests
{
    // A 4:2:0 picture in native memory with padded lines and spare lines
    // below, all filled: yadif reads three samples before and after each
    // line and two lines above and below.
    private sealed class TestPicture : IDisposable
    {
        private const int PaddingSamples = 16;
        private const int SpareLines = 4;
        private readonly nint[] _memory = new nint[3];

        public TestPicture(int width, int height, int bitDepth, int seed)
        {
            int pixelSize = bitDepth > 8 ? 2 : 1;
            var random = new Random(seed);
            Planes = new VLCPlane[3];
            for (int i = 0; i < 3; i++)
            {
                int w = i == 0 ? width : (width + 1) / 2;
                int h = i == 0 ? height : (height + 1) / 2;
                int pitch = (w + PaddingSamples) * pixelSize;
                int size = pitch * (h + SpareLines);
                _memory[i] = (nint)NativeMemory.Alloc((nuint)size);
                var bytes = new Span<byte>((void*)_memory[i], size);
                if (pixelSize == 1)
                {
                    random.NextBytes(bytes);
                }
                else
                {
                    foreach (ref ushort sample in MemoryMarshal.Cast<byte, ushort>(bytes))
                    {
                        sample = (ushort)random.Next(1 << bitDepth);
                    }
                }
                Planes[i] = new VLCPlane
                {
                    Pixels = _memory[i],
                    Lines = h + SpareLines,
                    Pitch = pitch,
                    PixelPitch = pixelSize,
                    VisibleLines = h,
                    VisiblePitch = w * pixelSize,
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

        public ReadOnlySpan<byte> Line(int plane, int line)
            => new((byte*)Planes[plane].Pixels + line * Planes[plane].Pitch, Planes[plane].VisiblePitch);

        public void Dispose()
        {
            foreach (nint memory in _memory)
            {
                NativeMemory.Free((void*)memory);
            }
        }
    }

    private static byte[] Render(YadifImplementation implementation, int width, int height, int bitDepth, int field, int parity)
    {
        using var previous = new TestPicture(width, height, bitDepth, seed: 1);
        using var current = new TestPicture(width, height, bitDepth, seed: 2);
        using var next = new TestPicture(width, height, bitDepth, seed: 3);
        using var output = new TestPicture(width, height, bitDepth, seed: 4);
        new Yadif(implementation, bitDepth).Render(previous.Planes, current.Planes, next.Planes, output.Planes, field, parity);
        return output.Visible();
    }

    private static List<YadifImplementation> SupportedImplementations()
    {
        var implementations = new List<YadifImplementation> { YadifImplementation.Scalar };
        if (Vector128.IsHardwareAccelerated)
        {
            implementations.Add(YadifImplementation.Vector128);
        }
        if (Avx2.IsSupported)
        {
            implementations.Add(YadifImplementation.Vector256);
        }
        if (Avx512BW.IsSupported)
        {
            implementations.Add(YadifImplementation.Vector512);
        }
        return implementations;
    }

    public static TheoryData<int, int, int> Pictures()
    {
        var data = new TheoryData<int, int, int>();
        foreach (int bitDepth in new[] { 8, 10, 12, 16 })
        {
            data.Add(64, 48, bitDepth);
            data.Add(97, 61, bitDepth);   // odd sizes: partial vector blocks
            data.Add(33, 9, bitDepth);
            data.Add(7, 5, bitDepth);     // narrower than any vector
            data.Add(130, 7, bitDepth);   // only edge lines (mode 2) in the chroma planes
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pictures))]
    public void EveryImplementation_MatchesTheReference(int width, int height, int bitDepth)
    {
        foreach (int field in new[] { 0, 1 })
        {
            foreach (int parity in new[] { 0, 1, 2 })
            {
                byte[] reference = Render(YadifImplementation.Reference, width, height, bitDepth, field, parity);
                foreach (YadifImplementation implementation in SupportedImplementations())
                {
                    byte[] result = Render(implementation, width, height, bitDepth, field, parity);
                    Assert.True(reference.AsSpan().SequenceEqual(result),
                        $"{implementation} differs from the reference ({width}x{height}, {bitDepth} bits, field {field}, parity {parity})");
                }
            }
        }
    }

    [Fact]
    public void Render_CopiesTheKeptFieldAndDuplicatesTheEdgeLines()
    {
        using var previous = new TestPicture(64, 48, 8, seed: 1);
        using var current = new TestPicture(64, 48, 8, seed: 2);
        using var next = new TestPicture(64, 48, 8, seed: 3);
        using var output = new TestPicture(64, 48, 8, seed: 4);
        new Yadif(YadifImplementation.Reference).Render(previous.Planes, current.Planes, next.Planes, output.Planes, field: 0, parity: 1);

        for (int line = 2; line < 46; line += 2)
        {
            Assert.True(current.Line(0, line).SequenceEqual(output.Line(0, line)), $"kept line {line} differs");
        }
        Assert.True(output.Line(0, 1).SequenceEqual(output.Line(0, 0)));
        Assert.True(output.Line(0, 46).SequenceEqual(output.Line(0, 47)));
        Assert.False(current.Line(0, 1).SequenceEqual(output.Line(0, 1)), "a rebuilt line is a plain copy");
    }

    [Fact]
    public void Render_WithParity2_CopiesThePicture()
    {
        using var previous = new TestPicture(64, 48, 8, seed: 1);
        using var current = new TestPicture(64, 48, 8, seed: 2);
        using var next = new TestPicture(64, 48, 8, seed: 3);
        using var output = new TestPicture(64, 48, 8, seed: 4);
        new Yadif().Render(previous.Planes, current.Planes, next.Planes, output.Planes, field: 0, parity: 2);

        for (int line = 1; line < 47; line++)
        {
            Assert.True(current.Line(0, line).SequenceEqual(output.Line(0, line)), $"line {line} differs");
        }
    }

    [Fact]
    public void Auto_PicksTheWidestSupportedVectors()
    {
        var expected = Avx512BW.IsSupported ? YadifImplementation.Vector512
            : Avx2.IsSupported ? YadifImplementation.Vector256
            : Vector128.IsHardwareAccelerated ? YadifImplementation.Vector128
            : YadifImplementation.Scalar;
        Assert.Equal(expected, new Yadif().Implementation);
        Assert.Equal(expected, new Yadif(bitDepth: 12).Implementation);
    }

    [Theory]
    [InlineData(YadifImplementation.Auto)]
    [InlineData(YadifImplementation.Vector128)]
    public void VectorImplementations_Above12Bits_FallBackToScalar(YadifImplementation requested)
    {
        if (requested == YadifImplementation.Vector128 && !Vector128.IsHardwareAccelerated)
        {
            return;
        }
        Assert.Equal(YadifImplementation.Scalar, new Yadif(requested, bitDepth: 16).Implementation);
    }

    [Fact]
    public void Constructor_RejectsUnsupportedVectors()
    {
        if (!Avx512BW.IsSupported)
        {
            Assert.Throws<PlatformNotSupportedException>(() => new Yadif(YadifImplementation.Vector512));
        }
    }

    [Theory]
    [InlineData(7)]
    [InlineData(17)]
    public void Constructor_RejectsBitDepthsOutside8To16(int bitDepth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Yadif(bitDepth: bitDepth));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(0, 3)]
    [InlineData(-1, 0)]
    public void Render_RejectsAnInvalidFieldOrParity(int field, int parity)
    {
        using var picture = new TestPicture(16, 8, 8, seed: 1);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Yadif().Render(picture.Planes, picture.Planes, picture.Planes, picture.Planes, field, parity));
    }

    [Fact]
    public void Render_RejectsPicturesWithDifferentPitches()
    {
        using var picture = new TestPicture(16, 8, 8, seed: 1);
        using var other = new TestPicture(24, 8, 8, seed: 2);
        Assert.Throws<ArgumentException>(
            () => new Yadif().Render(other.Planes, picture.Planes, picture.Planes, picture.Planes, 0, 1));
    }
}
