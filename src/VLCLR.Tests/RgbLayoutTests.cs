using System.Runtime.InteropServices;
using VLCLR.Imaging;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

public class RgbLayoutTests
{
    public static TheoryData<uint, int, int, int, int, int> Layouts => new()
    {
        { VLCFourCC.RV32, 1, 2, 3, -1, 4 },
        { VLCFourCC.RGBX, 0, 1, 2, -1, 4 },
        { VLCFourCC.BGRX, 2, 1, 0, -1, 4 },
        { VLCFourCC.XBGR, 3, 2, 1, -1, 4 },
        { VLCFourCC.RV24, 0, 1, 2, -1, 3 },
        { VLCFourCC.BGR24, 2, 1, 0, -1, 3 },
        { VLCFourCC.RGBA, 0, 1, 2, 3, 4 },
        { VLCFourCC.BGRA, 2, 1, 0, 3, 4 },
        { VLCFourCC.ARGB, 1, 2, 3, 0, 4 },
        { VLCFourCC.ABGR, 3, 2, 1, 0, 4 },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void TryGetRgbLayout_ReturnsMemoryOrder(uint chroma, int r, int g, int b, int a, int bpp)
    {
        Assert.True(VLCFourCC.TryGetRgbLayout(chroma, out int rr, out int gg, out int bb, out int aa));
        Assert.Equal((r, g, b, a), (rr, gg, bb, aa));
        Assert.Equal(bpp, VLCFourCC.GetBytesPerPixel(chroma));
        Assert.Equal(a >= 0, VLCFourCC.HasAlphaChannel(chroma));
        Assert.Equal(r == 2 && g == 1 && b == 0, VLCFourCC.IsBgraFormat(chroma));
    }

    [Fact]
    public void TryGetRgbLayout_NonRgb_ReturnsFalse()
    {
        Assert.False(VLCFourCC.TryGetRgbLayout(VLCFourCC.I420, out _, out _, out _, out _));
    }

    [Fact]
    public void Rv32AndRv24_AreNotBgr()
    {
        Assert.False(VLCFourCC.IsBgraFormat(VLCFourCC.RV32));
        Assert.False(VLCFourCC.IsBgraFormat(VLCFourCC.RV24));
    }

    [Fact]
    public void FourCC_NewConstants_HaveExpectedValues()
    {
        Assert.Equal("RGBX", VLCFourCC.ToString(VLCFourCC.RGBX));
        Assert.Equal("BGRX", VLCFourCC.ToString(VLCFourCC.BGRX));
        Assert.Equal("XBGR", VLCFourCC.ToString(VLCFourCC.XBGR));
        Assert.Equal("BGR3", VLCFourCC.ToString(VLCFourCC.BGR24));
        Assert.Equal("ARGB", VLCFourCC.ToString(VLCFourCC.ARGB));
        Assert.Equal("ABGR", VLCFourCC.ToString(VLCFourCC.ABGR));
    }

    // Opaque overlay pixel R=10, G=20, B=30 composited into a 1x1 frame prefilled with 0xEE.
    [Theory]
    [MemberData(nameof(Layouts))]
    public unsafe void Composite_PlacesChannelsPerLayout(uint chroma, int r, int g, int b, int a, int bpp)
    {
        nint frame = Marshal.AllocHGlobal(4);
        try
        {
            for (int i = 0; i < 4; i++) Marshal.WriteByte(frame, i, 0xEE);
            Assert.True(FrameCompositor.Composite(frame, 4, bpp, 1, chroma, new byte[] { 10, 20, 30, 255 }, 1, 1));
            byte* p = (byte*)frame;
            Assert.Equal(10, p[r]);
            Assert.Equal(20, p[g]);
            Assert.Equal(30, p[b]);
            if (a >= 0) Assert.Equal(255, p[a]);
            else if (bpp == 4)
            {
                // The ignored X byte stays untouched.
                int x = 6 - r - g - b;
                Assert.Equal(0xEE, p[x]);
            }
            if (bpp == 3) Assert.Equal(0xEE, p[3]);
        }
        finally { Marshal.FreeHGlobal(frame); }
    }

    [Fact]
    public unsafe void Composite_Xrgb_BlendsHalfAlphaPerChannel()
    {
        nint frame = Marshal.AllocHGlobal(4);
        try
        {
            // X, R=100, G=100, B=100
            byte* p = (byte*)frame;
            p[0] = 7; p[1] = 100; p[2] = 100; p[3] = 100;
            Assert.True(FrameCompositor.Composite(frame, 4, 4, 1, VLCFourCC.RV32, new byte[] { 200, 0, 100, 255 }, 1, 1, opacity: 0.5f));
            int a = (int)(0.5f * 255f + 0.5f); // 128
            Assert.Equal(7, p[0]);
            Assert.Equal((200 * a + 100 * (255 - a)) / 255, p[1]);
            Assert.Equal((0 * a + 100 * (255 - a)) / 255, p[2]);
            Assert.Equal((100 * a + 100 * (255 - a)) / 255, p[3]);
        }
        finally { Marshal.FreeHGlobal(frame); }
    }

    [Fact]
    public unsafe void FillRect_Rgb24_UsesRgbOrder()
    {
        nint frame = Marshal.AllocHGlobal(6);
        try
        {
            byte* p = (byte*)frame;
            for (int i = 0; i < 6; i++) p[i] = 0;
            FrameCompositor.FillRect(frame, 6, VLCFourCC.RV24, 1, 0, 1, 1, 1, 2, 3);
            Assert.Equal(new byte[] { 0, 0, 0, 1, 2, 3 }, new ReadOnlySpan<byte>(p, 6).ToArray());
        }
        finally { Marshal.FreeHGlobal(frame); }
    }
}
