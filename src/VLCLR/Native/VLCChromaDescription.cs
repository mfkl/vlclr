// VLC chroma description structure
// Source: vlc/include/vlc_fourcc.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Chroma description returned by vlc_fourcc_GetChromaDescription
/// (vlc_chroma_description_t from vlc_fourcc.h).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCChromaDescription
{
    /// <summary>Chroma FourCC (fcc)</summary>
    public uint Fourcc;

    /// <summary>Chroma subtype (subtype, enum vlc_chroma_subtype)</summary>
    public int Subtype;

    /// <summary>Number of planes (plane_count)</summary>
    public uint PlaneCount;

    /// <summary>Plane dimensions relative to the picture (p[4])</summary>
    public VLCChromaPlaneSize Plane0;
    public VLCChromaPlaneSize Plane1;
    public VLCChromaPlaneSize Plane2;
    public VLCChromaPlaneSize Plane3;

    /// <summary>Bytes per pixel for a plane (pixel_size)</summary>
    public uint PixelSize;

    /// <summary>Bits per pixel actually used in a plane (pixel_bits)</summary>
    public uint PixelBits;

    /// <summary>Average number of bits used by one color (color_bits)</summary>
    public float ColorBits;

    /// <summary>
    /// Get a plane's dimensions by index (safe accessor)
    /// </summary>
    public readonly VLCChromaPlaneSize GetPlane(int index)
    {
        return index switch
        {
            0 => Plane0,
            1 => Plane1,
            2 => Plane2,
            3 => Plane3,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}

/// <summary>
/// A plane's width and height as fractions of the picture's.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCChromaPlaneSize
{
    /// <summary>Width ratio (w)</summary>
    public VLCRational Width;

    /// <summary>Height ratio (h)</summary>
    public VLCRational Height;
}

/// <summary>
/// Unsigned fraction (vlc_rational_t from vlc_common.h)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCRational
{
    /// <summary>Numerator (num)</summary>
    public uint Num;

    /// <summary>Denominator (den)</summary>
    public uint Den;
}
