using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Plugin;
using Xunit;

namespace VLCLR.Tests;

public class VLCFilterContextNewPictureTests
{
    private const nint AllocatedPicture = 0x1234_5678;
    private static nint _calledWith;

    [UnmanagedCallersOnly]
    private static nint BufferNew(nint filter)
    {
        _calledWith = filter;
        return AllocatedPicture;
    }

    [Fact]
    public unsafe void NewPicture_UsesTheOwnerAllocator_PassingTheFilter()
    {
        int filterSize = Marshal.SizeOf<VLCFilter>();
        nint filterMemory = Marshal.AllocHGlobal(filterSize);
        nint callbacksMemory = Marshal.AllocHGlobal(Marshal.SizeOf<VLCFilterVideoCallbacks>());
        try
        {
            new Span<byte>((void*)filterMemory, filterSize).Clear();
            var callbacks = (VLCFilterVideoCallbacks*)callbacksMemory;
            callbacks->BufferNew = (nint)(delegate* unmanaged<nint, nint>)&BufferNew;
            callbacks->HoldDevice = 0;
            ((VLCFilter*)filterMemory)->Owner.Callbacks = callbacksMemory;
            _calledWith = 0;

            nint picture = new VLCFilterContext(filterMemory).NewPicture();

            Assert.Equal(AllocatedPicture, picture);
            Assert.Equal(filterMemory, _calledWith);
        }
        finally
        {
            Marshal.FreeHGlobal(callbacksMemory);
            Marshal.FreeHGlobal(filterMemory);
        }
    }

    [Fact]
    public void NewPicture_WithoutFilter_ReturnsZero()
    {
        Assert.Equal(0, new VLCFilterContext(0).NewPicture());
    }
}
