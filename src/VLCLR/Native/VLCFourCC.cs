// VLC FourCC utilities
// Helpers for working with VLC chroma/fourcc format codes
// VLC Version: 4.0.6

namespace VLCLR.Native;

/// <summary>
/// Utilities for working with VLC FourCC (four character code) values.
/// FourCC codes identify pixel formats in VLC.
/// </summary>
public static class VLCFourCC
{
    // Common VLC chroma fourcc values
    public const uint RV32 = Interop.VlcFourcc.CodecXrgb; // "RV32" - 32-bit RGB (VLC_CODEC_XRGB)
    public const uint RV24 = Interop.VlcFourcc.CodecRgb24; // "RV24" - 24-bit RGB (VLC_CODEC_RGB24)
    public const uint RGBA = Interop.VlcFourcc.CodecRgba; // "RGBA"
    public const uint BGRA = Interop.VlcFourcc.CodecBgra; // "BGRA"
    public const uint I420 = Interop.VlcFourcc.CodecI420; // "I420" - YUV 4:2:0 planar
    public const uint YV12 = Interop.VlcFourcc.CodecYv12; // "YV12" - YUV 4:2:0 planar
    public const uint D3D11Opaque = Interop.VlcFourcc.CodecD3d11Opaque; // "DX11" - D3D11 8-bit video surface
    public const uint D3D11Opaque10Bit = Interop.VlcFourcc.CodecD3d11Opaque10b; // "DX10" - D3D11 10-bit video surface
    public const uint D3D11OpaqueRgba = Interop.VlcFourcc.CodecD3d11OpaqueRgba; // "DXRG"
    public const uint D3D11OpaqueBgra = Interop.VlcFourcc.CodecD3d11OpaqueBgra; // "DAGR"
    public const uint D3D11OpaqueAlpha = Interop.VlcFourcc.CodecD3d11OpaqueAlpha; // "DA11"

    // Common native little-endian audio formats on Windows.
    public const uint F32L = Interop.VlcFourcc.CodecF32l; // "f32l" - interleaved 32-bit float
    public const uint S16L = Interop.VlcFourcc.CodecS16l; // "s16l" - interleaved signed 16-bit PCM

    /// <summary>
    /// Convert a FourCC uint to its 4-character string representation.
    /// </summary>
    public static string ToString(uint fourcc)
    {
        char c1 = (char)(fourcc & 0xFF);
        char c2 = (char)((fourcc >> 8) & 0xFF);
        char c3 = (char)((fourcc >> 16) & 0xFF);
        char c4 = (char)((fourcc >> 24) & 0xFF);
        return $"{c1}{c2}{c3}{c4}";
    }

    /// <summary>
    /// Get bytes per pixel for a VLC chroma format.
    /// Returns 0 if the format is unknown.
    /// </summary>
    public static int GetBytesPerPixel(uint chroma)
    {
        return chroma switch
        {
            RV32 => 4,
            RV24 => 3,
            RGBA => 4,
            BGRA => 4,
            I420 => 1, // Y plane only
            YV12 => 1, // Y plane only
            _ => GuessFromFourcc(chroma)
        };
    }

    /// <summary>
    /// Guess bytes per pixel from fourcc pattern when not explicitly known.
    /// </summary>
    private static int GuessFromFourcc(uint chroma)
    {
        string fourcc = ToString(chroma);

        // Patterns for 32-bit formats
        if (fourcc.Contains("32") || fourcc.Contains("RGBA") || fourcc.Contains("BGRA") || fourcc.Contains("ARGB"))
            return 4;

        // Patterns for 24-bit formats
        if (fourcc.Contains("24") || fourcc.Contains("RGB"))
            return 3;

        // Patterns for planar YUV - Y plane is 1 byte per pixel
        if (fourcc.StartsWith("I4") || fourcc.StartsWith("YV") || fourcc.StartsWith("NV"))
            return 1;

        return 0; // Unknown
    }

    /// <summary>
    /// Check if a chroma format uses BGRA byte ordering (vs RGBA).
    /// RV32 and RV24 are actually BGR(A) in VLC.
    /// </summary>
    public static bool IsBgraFormat(uint chroma)
    {
        return chroma switch
        {
            RV32 => true,  // BGRX
            RV24 => true,  // BGR
            BGRA => true,
            _ => false
        };
    }

    /// <summary>
    /// Check if a chroma format has an alpha channel.
    /// </summary>
    public static bool HasAlphaChannel(uint chroma)
    {
        return chroma switch
        {
            RGBA => true,
            BGRA => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks whether a chroma value represents a GPU-backed D3D11 picture.
    /// </summary>
    public static bool IsD3D11Opaque(uint chroma)
    {
        return chroma is
            D3D11Opaque or
            D3D11Opaque10Bit or
            D3D11OpaqueRgba or
            D3D11OpaqueBgra or
            D3D11OpaqueAlpha;
    }
}
