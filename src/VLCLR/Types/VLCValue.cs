// VLC value union structure
// Source: vlc/include/vlc_variables.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// VLC value union structure.
/// Source: vlc_variables.h, vlc_value_t (lines 120-129)
/// Note: In C# we represent this as separate fields since unions aren't directly supported.
/// </summary>
public struct VLCValue
{
    /// <summary>Integer value (i_int in C)</summary>
    public long IntValue;

    /// <summary>Boolean value (b_bool in C)</summary>
    public bool BoolValue;

    /// <summary>Float value (f_float in C)</summary>
    public float FloatValue;

    /// <summary>String value pointer (psz_string in C) - use with VLCInterop.PtrToStringUtf8</summary>
    public nint StringPtr;

    /// <summary>Address/pointer value (p_address in C)</summary>
    public nint AddressValue;

    /// <summary>X coordinate (coords.x in C)</summary>
    public int CoordsX;

    /// <summary>Y coordinate (coords.y in C)</summary>
    public int CoordsY;
}
