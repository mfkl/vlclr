using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Filters.Video;
using VLCLR.Native;

namespace YadifNative;

public static unsafe class Exports
{
    // One Yadif per bit depth, created on first use, as a plugin would at open.
    private static Yadif? s_yadif8;
    private static Yadif? s_yadif16;

    /// <summary>
    /// yadif_render of the harness's c/yadif.c: impl is a YadifImplementation
    /// (0 = Auto); the bit depth follows the planes' pixel pitch.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "yadif_render", CallConvs = [typeof(CallConvCdecl)])]
    public static void Render(int impl, int planes, VLCPlane* dst, VLCPlane* prev, VLCPlane* cur, VLCPlane* next, int field, int parity)
    {
        bool wide = cur[0].PixelPitch == 2;
        Yadif yadif = wide
            ? s_yadif16 ??= new Yadif((YadifImplementation)impl, 10)
            : s_yadif8 ??= new Yadif((YadifImplementation)impl, 8);
        yadif.Render(new ReadOnlySpan<VLCPlane>(prev, planes), new ReadOnlySpan<VLCPlane>(cur, planes),
            new ReadOnlySpan<VLCPlane>(next, planes), new ReadOnlySpan<VLCPlane>(dst, planes), field, parity);
    }

    /// <summary>Bytes allocated on the GC heap so far, collections, and the heap size.</summary>
    [UnmanagedCallersOnly(EntryPoint = "yadif_gc_info", CallConvs = [typeof(CallConvCdecl)])]
    public static void GcInfo(long* allocated, int* collections, long* heap)
    {
        *allocated = GC.GetTotalAllocatedBytes(precise: true);
        *collections = GC.CollectionCount(0);
        *heap = GC.GetTotalMemory(forceFullCollection: false);
    }
}
