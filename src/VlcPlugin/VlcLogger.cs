using VlcPlugin.Generated;
using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// High-level wrapper for VLC logging.
/// Provides a clean C# API for logging messages through VLC's logging system.
/// Uses direct P/Invoke to libvlccore with "%s" format to handle variadic logging.
/// </summary>
public sealed class VlcLogger
{
    private const string ModuleName = "dotnet";
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
        LogInternal((int)VlcLogType.Info, message);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    public void Error(string message)
    {
        LogInternal((int)VlcLogType.Error, message);
    }

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public void Warning(string message)
    {
        LogInternal((int)VlcLogType.Warning, message);
    }

    /// <summary>
    /// Log a debug message.
    /// </summary>
    public void Debug(string message)
    {
        LogInternal((int)VlcLogType.Debug, message);
    }

    /// <summary>
    /// Log a message with a specific type.
    /// </summary>
    public void Log(VlcLogType type, string message)
    {
        LogInternal((int)type, message);
    }

    /// <summary>
    /// Internal logging method that calls VLC's variadic log function.
    /// Uses "%s" format and passes the pre-formatted message as the single argument.
    /// </summary>
    private void LogInternal(int type, string message)
    {
        // VLC's vlc_object_Log is variadic: (obj, type, module, file, line, func, format, ...)
        // We use "%s" as format and pass the message as a single argument
        VlcCore.Log(_vlcObject, type, ModuleName, "", 0, "", "%s", message);
    }
}
