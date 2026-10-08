using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// VLC 3 D3D11 constants reachable from <c>picture_sys_t</c> at pinned source
/// commit 5fbc6904a2e4a96e9956a5d8503ac4db4ebaf702.
/// </summary>
public static class VLCD3D11PictureConstants
{
    public const int MaxShaderViews = 4;
    public const int KnownDxgiIndex = 0;

    public const int DxgiFormatUnknown = 0;
    public const int DxgiFormatR16G16UNorm = 35;
    public const int DxgiFormatR8G8UNorm = 49;
    public const int DxgiFormatR16UNorm = 56;
    public const int DxgiFormatR8UNorm = 61;
    public const int DxgiFormatB8G8R8A8UNorm = 87;
    public const int DxgiFormatNV12 = 103;
    public const int DxgiFormatP010 = 104;
}

/// <summary>
/// The texture/resource union in VLC 3's D3D11 <c>picture_sys_t</c>.
/// Texture and resource fields at the same index are aliases for the same COM
/// interface pointer and therefore never represent two separately owned refs.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct VLCD3D11PictureResourceUnion
{
    /// <summary>Owned ID3D11Texture2D reference for plane 0; released once at destruction.</summary>
    [FieldOffset(0)] public nint Texture0;
    /// <summary>Alias of Texture0 typed as ID3D11Resource; it adds no reference.</summary>
    [FieldOffset(0)] public nint Resource0;

    /// <summary>Owned ID3D11Texture2D reference for plane 1; released once at destruction.</summary>
    [FieldOffset(8)] public nint Texture1;
    /// <summary>Alias of Texture1 typed as ID3D11Resource; it adds no reference.</summary>
    [FieldOffset(8)] public nint Resource1;

    /// <summary>Owned ID3D11Texture2D reference for plane 2; released once at destruction.</summary>
    [FieldOffset(16)] public nint Texture2;
    /// <summary>Alias of Texture2 typed as ID3D11Resource; it adds no reference.</summary>
    [FieldOffset(16)] public nint Resource2;

    /// <summary>Owned ID3D11Texture2D reference for plane 3; released once at destruction.</summary>
    [FieldOffset(24)] public nint Texture3;
    /// <summary>Alias of Texture3 typed as ID3D11Resource; it adds no reference.</summary>
    [FieldOffset(24)] public nint Resource3;
}

/// <summary>
/// Win64 layout of VLC 3's D3D11 <c>picture_sys_t</c>. The containing vout owns
/// this allocation for opaque pictures. Every non-null COM field represents one
/// owned reference; destruction must release each once, then free this struct.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCD3D11PictureSys
{
    /// <summary>Owned ID3D11VideoDecoderOutputView reference; may be null for pool pictures.</summary>
    public nint Decoder;

    /// <summary>Four owned texture references, also addressable through ID3D11Resource aliases.</summary>
    public VLCD3D11PictureResourceUnion Textures;

    /// <summary>Owned ID3D11DeviceContext reference; released once at picture destruction.</summary>
    public nint Context;

    /// <summary>Texture-array slice used by this picture.</summary>
    public uint SliceIndex;

    private uint _padding;

    /// <summary>Owned ID3D11VideoProcessorInputView reference; null unless used as processor input.</summary>
    public nint ProcessorInput;

    /// <summary>Owned ID3D11VideoProcessorOutputView reference; null unless used as processor output.</summary>
    public nint ProcessorOutput;

    /// <summary>Owned ID3D11ShaderResourceView reference for plane 0; released once at destruction.</summary>
    public nint ResourceView0;
    /// <summary>Owned ID3D11ShaderResourceView reference for plane 1; released once at destruction.</summary>
    public nint ResourceView1;
    /// <summary>Owned ID3D11ShaderResourceView reference for plane 2; released once at destruction.</summary>
    public nint ResourceView2;
    /// <summary>Owned ID3D11ShaderResourceView reference for plane 3; released once at destruction.</summary>
    public nint ResourceView3;

    /// <summary>DXGI_FORMAT value describing the texture allocation.</summary>
    public int FormatTexture;

    private uint _tailPadding;
}
