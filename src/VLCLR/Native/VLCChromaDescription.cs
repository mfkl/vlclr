// VLC chroma description structure
// Source: vlc/include/vlc_fourcc.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Chroma description returned by vlc_fourcc_GetChromaDescription
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCChromaDescription
{
    /// <summary>Number of planes</summary>
    public uint PlaneCount;

    /// <summary>Pixel size in bytes</summary>
    public uint PixelSize;

    /// <summary>Pixel size in bits</summary>
    public uint PixelBits;

    // Additional fields we don't need but exist in the structure
    // plane width/height divisors, etc.
}
