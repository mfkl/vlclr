using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Plugin;
using Xunit;

namespace VLCLR.Tests;

public sealed class VLCD3D11PictureTests
{
    [Fact]
    public void D3D11PictureStructures_MatchVlcX64Layout()
    {
        Assert.Equal(24, Marshal.SizeOf<VLCPictureContext>());
        Assert.Equal(104, Marshal.SizeOf<VLCD3D11PictureSystem>());
        Assert.Equal(128, Marshal.SizeOf<VLCD3D11PictureContext>());

        Assert.Equal(
            32,
            Marshal.OffsetOf<VLCD3D11PictureSystem>(
                nameof(VLCD3D11PictureSystem.ArraySlice)).ToInt32());
        Assert.Equal(
            40,
            Marshal.OffsetOf<VLCD3D11PictureSystem>(
                nameof(VLCD3D11PictureSystem.VideoProcessorInputView))
                .ToInt32());
        Assert.Equal(
            24,
            Marshal.OffsetOf<VLCD3D11PictureContext>(
                nameof(VLCD3D11PictureContext.System)).ToInt32());
    }

    [Fact]
    public unsafe void Frame_ReturnsBorrowedD3D11Surface()
    {
        VLCD3D11PictureContext pictureContext = new()
        {
            System = new VLCD3D11PictureSystem
            {
                Texture0 = (nint)0x1234,
                ArraySlice = 7,
                VideoProcessorInputView = (nint)0x5678
            }
        };
        VLCPicture picture = new()
        {
            Format = new VLCVideoFormat
            {
                Chroma = VLCFourCC.D3D11Opaque,
                VisibleWidth = 1920,
                VisibleHeight = 1080
            },
            Context = (nint)(&pictureContext)
        };
        var frame = new VLCFrame(
            (nint)(&picture),
            new VLCFilterContext(0));

        bool found = frame.TryGetD3D11Surface(
            out VLCD3D11Surface surface);

        Assert.True(found);
        Assert.Equal((nint)0x1234, surface.Texture);
        Assert.Equal((uint)7, surface.ArraySlice);
        Assert.Equal((nint)0x5678, surface.VideoProcessorInputView);
    }

    [Fact]
    public unsafe void Frame_RejectsCpuPicture()
    {
        VLCPicture picture = new()
        {
            Format = new VLCVideoFormat { Chroma = VLCFourCC.I420 }
        };
        var frame = new VLCFrame(
            (nint)(&picture),
            new VLCFilterContext(0));

        Assert.False(frame.TryGetD3D11Surface(out _));
    }
}
