using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using VLCLR.Types;
using Gen = VLCLR.Native.Interop;
using Xunit;

namespace VLCLR.Tests.Migration;

public class VLCCoreLayoutTests
{
    private static int Off(Type t, string f) => Marshal.OffsetOf(t, f).ToInt32();

    [Fact]
    public void ValueNative_IsEightByteUnionLikeGenerated()
    {
        Assert.Equal(8, Gen.Value.SizeOf);
        Assert.Equal(Gen.Value.SizeOf, Unsafe.SizeOf<VLCValueNative>());
        Assert.Equal(Gen.Value.Offsets.Int, Off(typeof(VLCValueNative), nameof(VLCValueNative.Integer)));
        Assert.Equal(Gen.Value.Offsets.Bool, Off(typeof(VLCValueNative), nameof(VLCValueNative.Bool)));
        Assert.Equal(Gen.Value.Offsets.Float, Off(typeof(VLCValueNative), nameof(VLCValueNative.Float)));
        Assert.Equal(Gen.Value.Offsets.String, Off(typeof(VLCValueNative), nameof(VLCValueNative.String)));
        Assert.Equal(Gen.Value.Offsets.Address, Off(typeof(VLCValueNative), nameof(VLCValueNative.Address)));
        Assert.Equal(Gen.Value.Offsets.Coords + Gen.ValueCoords.Offsets.X, Off(typeof(VLCValueNative), nameof(VLCValueNative.CoordX)));
        Assert.Equal(Gen.Value.Offsets.Coords + Gen.ValueCoords.Offsets.Y, Off(typeof(VLCValueNative), nameof(VLCValueNative.CoordY)));
    }

    [Fact]
    public void ValueNative_ReinterpretsAsGeneratedValue()
    {
        var v = new VLCValueNative { CoordX = 7, CoordY = -3 };
        var g = Unsafe.As<VLCValueNative, Gen.Value>(ref v);
        Assert.Equal(7, g.Coords.X);
        Assert.Equal(-3, g.Coords.Y);
    }

    [Fact]
    public void Log_MatchesGenerated()
    {
        Assert.Equal(Gen.Log.SizeOf, Unsafe.SizeOf<VLCLog>());
        Assert.Equal(Gen.Log.Offsets.ObjectId, Off(typeof(VLCLog), nameof(VLCLog.ObjectId)));
        Assert.Equal(Gen.Log.Offsets.ObjectType, Off(typeof(VLCLog), nameof(VLCLog.ObjectTypePtr)));
        Assert.Equal(Gen.Log.Offsets.Module, Off(typeof(VLCLog), nameof(VLCLog.ModulePtr)));
        Assert.Equal(Gen.Log.Offsets.Header, Off(typeof(VLCLog), nameof(VLCLog.HeaderPtr)));
        Assert.Equal(Gen.Log.Offsets.File, Off(typeof(VLCLog), nameof(VLCLog.FilePtr)));
        Assert.Equal(Gen.Log.Offsets.Line, Off(typeof(VLCLog), nameof(VLCLog.Line)));
        Assert.Equal(Gen.Log.Offsets.Func, Off(typeof(VLCLog), nameof(VLCLog.FuncPtr)));
        Assert.Equal(Gen.Log.Offsets.Tid, Off(typeof(VLCLog), "_threadId"));
    }

    [Fact]
    public void Log_ThreadIdUsesCLongWidth()
    {
        var log = new VLCLog { ThreadId = 42 };
        Assert.Equal((nuint)42, log.ThreadId);

        // Writing the thread id must touch only sizeof(unsigned long) bytes.
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<VLCLog>()];
        bytes.Fill(0xFF);
        ref VLCLog l = ref MemoryMarshal.AsRef<VLCLog>(bytes);
        l.ThreadId = 1;
        int tid = Gen.Log.Offsets.Tid;
        int width = Unsafe.SizeOf<CULong>();
        Assert.Equal(1, bytes[tid]);
        for (int i = 1; i < width; i++) Assert.Equal(0, bytes[tid + i]);
        for (int i = width; tid + i < bytes.Length; i++) Assert.Equal(0xFF, bytes[tid + i]);
    }
}
