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
    public const uint RV32 = 0x32335652; // "RV32" - 32-bit RGB (actually BGRX)
    public const uint RV24 = 0x34325652; // "RV24" - 24-bit RGB (actually BGR)
    public const uint RGBA = 0x41424752; // "RGBA"
    public const uint BGRA = 0x41524742; // "BGRA"
    public const uint I420 = 0x30323449; // "I420" - YUV 4:2:0 planar
    public const uint YV12 = 0x32315659; // "YV12" - YUV 4:2:0 planar

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
}
