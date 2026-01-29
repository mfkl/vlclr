using VLCLR.Types;
using VLCLR.Native;

namespace VLCLR;

/// <summary>
/// High-level wrapper for VLC logging.
/// Provides a clean C# API for logging messages through VLC's logging system.
/// Uses direct P/Invoke to libvlccore with "%s" format to handle variadic logging.
/// </summary>
public sealed class VLCLogger
{
    private const string ModuleName = "dotnet";
    private readonly nint _vlcObject;

    /// <summary>
    /// Creates a logger bound to a specific VLC object.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    public VLCLogger(nint vlcObject)
    {
        _vlcObject = vlcObject;
    }

    /// <summary>
    /// Log an informational message.
    /// </summary>
    public void Info(string message)
    {
        LogInternal((int)VLCLogType.Info, message);
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    public void Error(string message)
    {
        LogInternal((int)VLCLogType.Error, message);
    }

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public void Warning(string message)
    {
        LogInternal((int)VLCLogType.Warning, message);
    }

    /// <summary>
    /// Log a debug message.
    /// </summary>
    public void Debug(string message)
    {
        LogInternal((int)VLCLogType.Debug, message);
    }

    /// <summary>
    /// Log a message with a specific type.
    /// </summary>
    public void Log(VLCLogType type, string message)
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
        VLCCore.Log(_vlcObject, type, ModuleName, "", 0, "", "%s", message);
    }
}
