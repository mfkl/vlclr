// VLC chroma description structure
// Source: vlc/include/vlc_fourcc.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Chroma description returned by vlc_fourcc_GetChromaDescription
/// (vlc_chroma_description_t from vlc_fourcc.h). Member offsets come from
/// <see cref="Interop.ChromaDescription.Offsets"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.ChromaDescription.MaxSizeOf)]
public struct VLCChromaDescription
{
    /// <summary>Chroma FourCC (fcc)</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.Fcc)] public uint Fourcc;

    /// <summary>Chroma subtype (subtype, enum vlc_chroma_subtype)</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.Subtype)] public int Subtype;

    /// <summary>Number of planes (plane_count)</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.PlaneCount)] public uint PlaneCount;

    /// <summary>Plane dimensions relative to the picture (p[0])</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.P)] public VLCChromaPlaneSize Plane0;

    /// <summary>Plane dimensions relative to the picture (p[1])</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.P + 1 * Interop.ChromaDescriptionP.MaxSizeOf)] public VLCChromaPlaneSize Plane1;

    /// <summary>Plane dimensions relative to the picture (p[2])</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.P + 2 * Interop.ChromaDescriptionP.MaxSizeOf)] public VLCChromaPlaneSize Plane2;

    /// <summary>Plane dimensions relative to the picture (p[3])</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.P + 3 * Interop.ChromaDescriptionP.MaxSizeOf)] public VLCChromaPlaneSize Plane3;

    /// <summary>Bytes per pixel for a plane (pixel_size)</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.PixelSize)] public uint PixelSize;

    /// <summary>Bits per pixel actually used in a plane (pixel_bits)</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.PixelBits)] public uint PixelBits;

    /// <summary>Average number of bits used by one color (color_bits)</summary>
    [FieldOffset(Interop.ChromaDescription.Offsets.ColorBits)] public float ColorBits;

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
/// A plane's width and height as fractions of the picture's (the anonymous
/// p[] entry of vlc_chroma_description_t).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.ChromaDescriptionP.MaxSizeOf)]
public struct VLCChromaPlaneSize
{
    /// <summary>Width ratio (w)</summary>
    [FieldOffset(Interop.ChromaDescriptionP.Offsets.W)] public VLCRational Width;

    /// <summary>Height ratio (h)</summary>
    [FieldOffset(Interop.ChromaDescriptionP.Offsets.H)] public VLCRational Height;
}

/// <summary>
/// Unsigned fraction (vlc_rational_t from vlc_common.h)
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.Rational.MaxSizeOf)]
public struct VLCRational
{
    /// <summary>Numerator (num)</summary>
    [FieldOffset(Interop.Rational.Offsets.Num)] public uint Num;

    /// <summary>Denominator (den)</summary>
    [FieldOffset(Interop.Rational.Offsets.Den)] public uint Den;
}
