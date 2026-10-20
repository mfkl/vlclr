using System.Runtime.InteropServices;
using VLCLR.Native;

namespace VLCLR.Tests.Filters;

// A 4:2:0 picture in native memory with padded lines and spare lines
// below, all filled: yadif reads three samples before and after each
// line and two lines above and below, X a line below the picture.
internal sealed unsafe class TestPicture : IDisposable
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

    /// <summary>Sets every byte of each line, padding and spare lines included, to <paramref name="value"/> of its line.</summary>
    public void Fill(Func<int, byte> value)
    {
        foreach (VLCPlane plane in Planes)
        {
            for (int line = 0; line < plane.Lines; line++)
            {
                new Span<byte>((byte*)plane.Pixels + line * plane.Pitch, plane.Pitch).Fill(value(line));
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
