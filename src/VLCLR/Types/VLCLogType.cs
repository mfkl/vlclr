// VLC log type enumeration
// Source: vlc/include/vlc_messages.h
// VLC Version: 4.0.6

namespace VLCLR.Types;

/// <summary>
/// VLC log message types.
/// Source: vlc_messages.h, enum vlc_log_type (lines 44-50)
/// </summary>
public enum VLCLogType
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
