// VLC log message structure
// Source: vlc/include/vlc_messages.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// VLC log message structure.
/// Source: vlc_messages.h, struct vlc_log_t (lines 55-65)
/// </summary>
public struct VLCLog
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
