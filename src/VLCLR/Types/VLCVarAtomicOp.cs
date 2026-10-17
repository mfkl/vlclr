// VLC variable atomic operations enumeration
// Source: vlc/include/vlc_variables.h
// VLC Version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Types;

/// <summary>
/// VLC variable atomic operations.
/// Source: vlc_variables.h, enum vlc_var_atomic_op (lines 110-115)
/// </summary>
public enum VLCVarAtomicOp
{
    /// <summary>Invert a boolean value (param ignored)</summary>
    BoolToggle = (int)Interop.VarAtomicOp.BoolToggle,

    /// <summary>Add parameter to an integer value</summary>
    IntegerAdd = (int)Interop.VarAtomicOp.IntegerAdd,

    /// <summary>Binary OR over an integer bits field</summary>
    IntegerOr = (int)Interop.VarAtomicOp.IntegerOr,

    /// <summary>Binary NAND over an integer bits field</summary>
    IntegerNand = (int)Interop.VarAtomicOp.IntegerNand
}
