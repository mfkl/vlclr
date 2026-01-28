// VlcTypes.cs - VLC Type Definitions
// Source: vlc/include/vlc_messages.h, vlc/include/vlc_variables.h, vlc/include/vlc_player.h
// VLC Version: 4.0.6
//
// Note: These bindings are maintained manually because function calls require
// a C bridge layer due to variadic functions in VLC.
//
// To update: Compare with VLC header files and update types as needed.
// Reference: vlc/include/vlc_*.h

namespace VlcPlugin.Types;

/// <summary>
/// VLC log message types.
/// Source: vlc_messages.h, enum vlc_log_type (lines 44-50)
/// </summary>
public enum VlcLogType
{
    /// <summary>Important information (VLC_MSG_INFO)</summary>
    Info = 0,

    /// <summary>Error (VLC_MSG_ERR)</summary>
    Error = 1,

    /// <summary>Warning (VLC_MSG_WARN)</summary>
    Warning = 2,

    /// <summary>Debug (VLC_MSG_DBG)</summary>
    Debug = 3
}

/// <summary>
/// VLC player states.
/// Source: vlc_player.h, enum vlc_player_state
/// </summary>
public enum VlcPlayerState
{
    /// <summary>Player is stopped</summary>
    Stopped = 0,

    /// <summary>Player is starting (loading media)</summary>
    Started = 1,

    /// <summary>Player is actively playing</summary>
    Playing = 2,

    /// <summary>Player is paused</summary>
    Paused = 3,

    /// <summary>Player is stopping</summary>
    Stopping = 4
}

/// <summary>
/// VLC variable atomic operations.
/// Source: vlc_variables.h, enum vlc_var_atomic_op (lines 110-115)
/// </summary>
public enum VlcVarAtomicOp
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

/// <summary>
/// VLC value union structure.
/// Source: vlc_variables.h, vlc_value_t (lines 120-129)
/// Note: In C# we represent this as separate fields since unions aren't directly supported.
/// </summary>
public struct VlcValue
{
    /// <summary>Integer value (i_int in C)</summary>
    public long IntValue;

    /// <summary>Boolean value (b_bool in C)</summary>
    public bool BoolValue;

    /// <summary>Float value (f_float in C)</summary>
    public float FloatValue;

    /// <summary>String value pointer (psz_string in C) - use with VlcInterop.PtrToStringUtf8</summary>
    public nint StringPtr;

    /// <summary>Address/pointer value (p_address in C)</summary>
    public nint AddressValue;

    /// <summary>X coordinate (coords.x in C)</summary>
    public int CoordsX;

    /// <summary>Y coordinate (coords.y in C)</summary>
    public int CoordsY;
}

/// <summary>
/// VLC log message structure.
/// Source: vlc_messages.h, struct vlc_log_t (lines 55-65)
/// </summary>
public struct VlcLog
{
    /// <summary>Emitter (temporarily) unique object ID or 0</summary>
    public nuint ObjectId;

    /// <summary>Emitter object type name</summary>
    public nint ObjectTypePtr;

    /// <summary>Emitter module (source code)</summary>
    public nint ModulePtr;

    /// <summary>Additional header (used by VLM media)</summary>
    public nint HeaderPtr;

    /// <summary>Source code file name or NULL</summary>
    public nint FilePtr;

    /// <summary>Source code file line number or -1</summary>
    public int Line;

    /// <summary>Source code calling function name or NULL</summary>
    public nint FuncPtr;

    /// <summary>Emitter thread ID</summary>
    public nuint ThreadId;
}
