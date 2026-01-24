using VlcPlugin.Generated;
using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// High-level wrapper for VLC logging.
/// Provides a clean C# API for logging messages through VLC's logging system.
/// </summary>
public sealed class VlcLogger
{
    private readonly nint _vlcObject;

    /// <summary>
    /// Creates a logger bound to a specific VLC object.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    public VlcLogger(nint vlcObject)
    {
        _vlcObject = vlcObject;
    }

    /// <summary>
    /// Log an informational message.
    /// </summary>
    public void Info(string message)
    {
        VlcBridge.Log(_vlcObject, (int)VlcLogType.Info, message);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    public void Error(string message)
    {
        VlcBridge.Log(_vlcObject, (int)VlcLogType.Error, message);
    }

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public void Warning(string message)
    {
        VlcBridge.Log(_vlcObject, (int)VlcLogType.Warning, message);
    }

    /// <summary>
    /// Log a debug message.
    /// </summary>
    public void Debug(string message)
    {
        VlcBridge.Log(_vlcObject, (int)VlcLogType.Debug, message);
    }

    /// <summary>
    /// Log a message with a specific type.
    /// </summary>
    public void Log(VlcLogType type, string message)
    {
        VlcBridge.Log(_vlcObject, (int)type, message);
    }
}
