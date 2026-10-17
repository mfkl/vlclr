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
    public const uint RV32 = Interop.VlcFourcc.CodecXrgb; // "RV32" - 32-bit RGB, memory order X,R,G,B (VLC_CODEC_XRGB)
    public const uint RV24 = Interop.VlcFourcc.CodecRgb24; // "RV24" - 24-bit RGB, memory order R,G,B (VLC_CODEC_RGB24)
    public const uint XRGB = Interop.VlcFourcc.CodecXrgb; // "RV32" - memory order X,R,G,B (same value as RV32)
    public const uint RGBX = Interop.VlcFourcc.CodecRgbx; // "RGBX" - memory order R,G,B,X
    public const uint BGRX = Interop.VlcFourcc.CodecBgrx; // "BGRX" - memory order B,G,R,X
    public const uint XBGR = Interop.VlcFourcc.CodecXbgr; // "XBGR" - memory order X,B,G,R
    public const uint RGB24 = Interop.VlcFourcc.CodecRgb24; // "RV24" - memory order R,G,B (same value as RV24)
    public const uint BGR24 = Interop.VlcFourcc.CodecBgr24; // "BGR3" - memory order B,G,R
    public const uint ARGB = Interop.VlcFourcc.CodecArgb; // "ARGB" - memory order A,R,G,B
    public const uint ABGR = Interop.VlcFourcc.CodecAbgr; // "ABGR" - memory order A,B,G,R
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
            XRGB or RGBX or BGRX or XBGR => 4,
            RGB24 or BGR24 => 3,
            RGBA or BGRA or ARGB or ABGR => 4,
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
    /// Gets the byte offsets of the R, G, B and A components of a packed RGB chroma,
    /// as laid out in memory (VLC names these formats by memory address order).
    /// </summary>
    /// <param name="chroma">VLC chroma fourcc.</param>
    /// <param name="r">Byte offset of red within a pixel.</param>
    /// <param name="g">Byte offset of green within a pixel.</param>
    /// <param name="b">Byte offset of blue within a pixel.</param>
    /// <param name="a">Byte offset of alpha, or -1 when the format has no alpha channel
    /// (an ignored "X" byte counts as no alpha).</param>
    /// <returns>True for XRGB (RV32), RGBX, BGRX, XBGR, RGB24 (RV24), BGR24, RGBA, BGRA, ARGB and ABGR.</returns>
    public static bool TryGetRgbLayout(uint chroma, out int r, out int g, out int b, out int a)
    {
        switch (chroma)
        {
            case XRGB: r = 1; g = 2; b = 3; a = -1; return true;
            case RGBX: r = 0; g = 1; b = 2; a = -1; return true;
            case BGRX: r = 2; g = 1; b = 0; a = -1; return true;
            case XBGR: r = 3; g = 2; b = 1; a = -1; return true;
            case RGB24: r = 0; g = 1; b = 2; a = -1; return true;
            case BGR24: r = 2; g = 1; b = 0; a = -1; return true;
            case RGBA: r = 0; g = 1; b = 2; a = 3; return true;
            case BGRA: r = 2; g = 1; b = 0; a = 3; return true;
            case ARGB: r = 1; g = 2; b = 3; a = 0; return true;
            case ABGR: r = 3; g = 2; b = 1; a = 0; return true;
            default: r = g = b = a = -1; return false;
        }
    }

    /// <summary>
    /// Check if a chroma format stores its colour bytes in B,G,R order at byte offsets 0..2
    /// (BGRA, BGRX, BGR24). RV32 (XRGB) and RV24 (RGB24) are not BGR; use
    /// <see cref="TryGetRgbLayout"/> for the exact component positions.
    /// </summary>
    public static bool IsBgraFormat(uint chroma)
    {
        return chroma is BGRA or BGRX or BGR24;
    }

    /// <summary>
    /// Check if a chroma format has an alpha channel (RGBA, BGRA, ARGB, ABGR).
    /// Formats with an ignored X byte (RV32, RGBX, BGRX, XBGR) have none.
    /// </summary>
    public static bool HasAlphaChannel(uint chroma)
    {
        return chroma is RGBA or BGRA or ARGB or ABGR;
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
