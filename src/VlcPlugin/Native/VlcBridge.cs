using System.Runtime.InteropServices;

namespace VlcPlugin.Native;

/// <summary>
/// P/Invoke declarations for the C glue layer bridge functions.
/// These are exported from libhello_csharp_plugin.dll.
/// </summary>
internal static partial class VlcBridge
{
    /// <summary>
    /// Log a message through VLC's logging system.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="type">Log type: 0=INFO, 1=ERR, 2=WARN, 3=DBG</param>
    /// <param name="message">UTF-8 message string</param>
    [LibraryImport("libhello_csharp_plugin", EntryPoint = "csharp_bridge_log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Log(nint vlcObject, int type, string message);
}

/// <summary>
/// VLC log message types matching vlc_messages.h enum vlc_log_type.
/// </summary>
public enum VlcLogType
{
    /// <summary>Important information</summary>
    Info = 0,
    /// <summary>Error</summary>
    Error = 1,
    /// <summary>Warning</summary>
    Warning = 2,
    /// <summary>Debug</summary>
    Debug = 3
}
