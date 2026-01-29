// VLC variable atomic operations enumeration
// Source: vlc/include/vlc_variables.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// VLC variable atomic operations.
/// Source: vlc_variables.h, enum vlc_var_atomic_op (lines 110-115)
/// </summary>
public enum VLCVarAtomicOp
{
    /// <summary>Invert a boolean value (param ignored)</summary>
    BoolToggle = 0,

    /// <summary>Add parameter to an integer value</summary>
    IntegerAdd = 1,

    /// <summary>Binary OR over an integer bits field</summary>
    IntegerOr = 2,

    /// <summary>Binary NAND over an integer bits field</summary>
    IntegerNand = 3
}
