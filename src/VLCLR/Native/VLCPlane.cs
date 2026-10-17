// VLC plane structure
// Source: vlc/include/vlc_picture.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Description of a planar graphic field (plane_t from vlc_picture.h).
/// Layout and size come from <see cref="Interop.Plane"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.Plane.MaxSizeOf)]
public struct VLCPlane
{
    /// <summary>Start of the plane's data (p_pixels)</summary>
    [FieldOffset(Interop.Plane.Offsets.Pixels)]
    public nint Pixels;

    /// <summary>Number of lines, including margins (i_lines)</summary>
    [FieldOffset(Interop.Plane.Offsets.Lines)]
    public int Lines;

    /// <summary>Number of bytes in a line, including margins (i_pitch)</summary>
    [FieldOffset(Interop.Plane.Offsets.Pitch)]
    public int Pitch;

    /// <summary>Size of a macropixel, defaults to 1 (i_pixel_pitch)</summary>
    [FieldOffset(Interop.Plane.Offsets.PixelPitch)]
    public int PixelPitch;

    /// <summary>How many visible lines are there? (i_visible_lines)</summary>
    [FieldOffset(Interop.Plane.Offsets.VisibleLines)]
    public int VisibleLines;

    /// <summary>How many bytes for visible pixels are there? (i_visible_pitch)</summary>
    [FieldOffset(Interop.Plane.Offsets.VisiblePitch)]
    public int VisiblePitch;
}
