// VLC D3D11 picture structures
// Sources:
// - vlc/include/vlc_picture.h
// - vlc/modules/video_chroma/d3d11_fmt.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// The common prefix of VLC's picture_context_t.
/// Layout and size come from <see cref="Interop.PictureContext"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.PictureContext.MaxSizeOf)]
public struct VLCPictureContext
{
    [FieldOffset(Interop.PictureContext.Offsets.Destroy)]
    public nint Destroy;

    [FieldOffset(Interop.PictureContext.Offsets.Copy)]
    public nint Copy;

    [FieldOffset(Interop.PictureContext.Offsets.Vctx)]
    public nint VideoContext;
}

/// <summary>
/// Managed ABI view of VLC's picture_sys_d3d11_t.
/// It is declared in modules/video_chroma/d3d11_fmt.h, which is not a public VLC header, so it
/// has no generated counterpart and its layout stays hand-written.
/// COM pointers and handles are borrowed from the current VLC picture.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 104)]
public struct VLCD3D11PictureSystem
{
    [FieldOffset(0)]
    public nint Texture0;

    [FieldOffset(8)]
    public nint Texture1;

    [FieldOffset(16)]
    public nint Texture2;

    [FieldOffset(24)]
    public nint Texture3;

    [FieldOffset(32)]
    public uint ArraySlice;

    [FieldOffset(40)]
    public nint VideoProcessorInputView;

    [FieldOffset(48)]
    public nint VideoProcessorOutputView;

    [FieldOffset(56)]
    public nint ShaderResourceView0;

    [FieldOffset(64)]
    public nint ShaderResourceView1;

    [FieldOffset(72)]
    public nint ShaderResourceView2;

    [FieldOffset(80)]
    public nint ShaderResourceView3;

    [FieldOffset(88)]
    public nint SharedHandle;

    [FieldOffset(96)]
    public byte OwnsSharedHandle;
}

/// <summary>
/// Managed ABI view of VLC's d3d11_pic_context (modules/video_chroma/d3d11_fmt.h, not a public
/// header, hence hand-written). The embedded picture_context_t prefix uses its generated size.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct VLCD3D11PictureContext
{
    [FieldOffset(0)]
    public VLCPictureContext Picture;

    [FieldOffset(Interop.PictureContext.MaxSizeOf)]
    public VLCD3D11PictureSystem System;
}

/// <summary>
/// A borrowed D3D11 video surface for the duration of a VLC frame callback.
/// The plugin must copy or consume it before returning the picture to VLC.
/// </summary>
public readonly record struct VLCD3D11Surface(
    nint Texture,
    uint ArraySlice,
    nint VideoProcessorInputView);
