using System.Runtime.InteropServices;

namespace VLCLR.Native;

// VLC 3 shares VA-API pictures between a video output and the avcodec "vaapi"
// decoder through structures that are private to modules/hw/vaapi/vlc_vaapi.c
// (VLC 3.0.24, source commit 6de05adcbaf2e8b85fe86aad4169393098628119). Each
// plugin compiles its own copy of that file, so a managed video output that
// allocates the picture pool must lay its allocations out exactly like this.
//
// The decoder (direct rendering) reads the display from the first pool picture:
// vlc_vaapi_PicSysHoldInstance(p_sys) takes a reference on
// p_sys->instance->va_inst and returns its display. It later releases it with
// vlc_vaapi_ReleaseInstance, which on the last reference calls vaTerminate,
// then native_destroy_cb(native), then free(inst). The instance must therefore
// come from the C library's malloc (NativeMemory.Alloc), and the callback must
// stay callable for as long as any decoder can hold the instance.
//
// Every field is a pointer or 32-bit integer, so the layout is the same on all
// 64-bit Linux targets.

/// <summary><c>struct vlc_vaapi_instance</c>: a refcounted VADisplay.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVaapiInstance
{
    /// <summary>The initialized VADisplay.</summary>
    public nint Display;
    /// <summary>Native display handle passed to <see cref="NativeDestroy"/> (the DRM fd for DRM displays).</summary>
    public nint Native;
    /// <summary><c>void (*)(VANativeDisplay)</c>, called after vaTerminate on the last release; may be null.</summary>
    public nint NativeDestroy;
    /// <summary>atomic_uint reference count; starts at 1 for the creator.</summary>
    public uint ReferenceCount;
    private uint _padding;
}

/// <summary>
/// <c>struct pic_sys_vaapi_instance</c>: the surfaces shared by one pool. The
/// <see cref="RenderTargets"/> array is a C flexible array member: the
/// allocation is <see cref="HeaderSize"/> + count * sizeof(VASurfaceID).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVaapiPoolInstance
{
    /// <summary>Size of the fixed part; the surface IDs start here.</summary>
    public const int HeaderSize = 28;

    /// <summary>atomic_int count of live pool pictures; the last one destroys the surfaces.</summary>
    public int PictureReferenceCount;
    private int _padding;
    /// <summary>The display these surfaces belong to (held through <see cref="Instance"/>).</summary>
    public nint Display;
    /// <summary>The <see cref="VLCVaapiInstance"/> holding the display.</summary>
    public nint Instance;
    /// <summary>Number of surfaces in the trailing array.</summary>
    public uint RenderTargetCount;
    // VASurfaceID render_targets[] follows at HeaderSize.

    /// <summary>The address of the trailing VASurfaceID array.</summary>
    public static unsafe uint* RenderTargets(VLCVaapiPoolInstance* instance) =>
        (uint*)((byte*)instance + HeaderSize);
}

/// <summary>
/// <c>struct vaapi_pic_ctx</c>: the <c>picture_context_t</c> attached to a
/// decoded picture. <see cref="PictureReference"/> holds the pool picture.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVaapiPictureContext
{
    /// <summary><c>void (*destroy)(picture_context_t *)</c>.</summary>
    public nint Destroy;
    /// <summary><c>picture_context_t *(*copy)(picture_context_t *)</c>.</summary>
    public nint Copy;
    /// <summary>The VASurfaceID holding the decoded image.</summary>
    public uint Surface;
    private uint _padding;
    /// <summary>The pool picture (<c>picture_t *</c>) that owns <see cref="Surface"/>.</summary>
    public nint PictureReference;
}

/// <summary>
/// VLC 3's VA-API <c>picture_sys_t</c>, allocated by the pool owner for every
/// pool picture.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVaapiPictureSys
{
    /// <summary>The <see cref="VLCVaapiPoolInstance"/> shared by the pool.</summary>
    public nint Instance;
    /// <summary>The context the decoder attaches to the picture (<c>pic->context = &amp;ctx</c>).</summary>
    public VLCVaapiPictureContext Context;
}

/// <summary>Sizes checked by tests/AbiValidation against mirrors of the same source.</summary>
public static class VLCVaapiLayout
{
    public const int InstanceSize = 32;
    public const int PictureContextSize = 32;
    public const int PictureSysSize = 40;
}
