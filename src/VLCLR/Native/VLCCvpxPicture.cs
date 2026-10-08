using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// <c>struct cvpxpic_ctx</c>: the <c>picture_context_t</c> that VLC 3 attaches
/// to every CoreVideo picture (the <c>VLC_CODEC_CVPX_*</c> chromas) on macOS.
/// </summary>
/// <remarks>
/// The structure is private to modules/codec/vt_utils.c (VLC 3.0.24, source
/// commit 6de05adcbaf2e8b85fe86aad4169393098628119), which the videotoolbox
/// decoder and the cvpx converter each compile. Both attach it with
/// <c>cvpxpic_attach</c>, and <c>cvpxpic_get_ref</c> reads
/// <see cref="PixelBuffer"/> back, so a video output can do the same with
/// <c>picture->context</c>. The buffer is valid while the picture is held;
/// retain it (<c>CVPixelBufferRetain</c>) to keep it beyond
/// <c>picture_Release</c>. VLC's pools request IOSurface-backed buffers, so
/// <c>CVPixelBufferGetIOSurface</c> returns a surface that can be shared with
/// the GPU.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct VLCCvpxPictureContext
{
    /// <summary><c>void (*destroy)(picture_context_t *)</c>.</summary>
    public nint Destroy;
    /// <summary><c>picture_context_t *(*copy)(picture_context_t *)</c>.</summary>
    public nint Copy;
    /// <summary>The <c>CVPixelBufferRef</c> holding the image.</summary>
    public nint PixelBuffer;
    /// <summary>The picture's field count when it was attached.</summary>
    public uint FieldCount;
    /// <summary>atomic_uint reference count shared by copies of the context.</summary>
    public uint ReferenceCount;
    /// <summary><c>void (*)(CVPixelBufferRef, void *, unsigned)</c>, or null.</summary>
    public nint OnReleased;
    /// <summary>Userdata for <see cref="OnReleased"/>.</summary>
    public nint OnReleasedData;
}

/// <summary>Sizes checked by tests/AbiValidation against a mirror of the same source.</summary>
public static class VLCCvpxLayout
{
    public const int PictureContextSize = 48;
}
