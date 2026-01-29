// VLC plane structure
// Source: vlc/include/vlc_picture.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Description of a planar graphic field (plane_t from vlc_picture.h)
/// Size on 64-bit: 32 bytes (28 bytes + 4 padding for 8-byte alignment in arrays)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCPlane
{
    /// <summary>Start of the plane's data (p_pixels)</summary>
    public nint Pixels;            // offset 0, size 8

    /// <summary>Number of lines, including margins (i_lines)</summary>
    public int Lines;              // offset 8, size 4

    /// <summary>Number of bytes in a line, including margins (i_pitch)</summary>
    public int Pitch;              // offset 12, size 4

    /// <summary>Size of a macropixel, defaults to 1 (i_pixel_pitch)</summary>
    public int PixelPitch;         // offset 16, size 4

    /// <summary>How many visible lines are there? (i_visible_lines)</summary>
    public int VisibleLines;       // offset 20, size 4

    /// <summary>How many bytes for visible pixels are there? (i_visible_pitch)</summary>
    public int VisiblePitch;       // offset 24, size 4

    // Padding for 8-byte alignment in arrays (pointer at start requires 8-byte struct alignment)
    private int _padding;          // offset 28, size 4
    // Total: 32 bytes
}
