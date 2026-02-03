// VLC Safe Execute helpers
// Exception-safe callback wrappers for VLC plugin callbacks
// VLC Version: 4.0.6

namespace VLCLR.Plugin;

/// <summary>
/// Exception-safe execution helpers for VLC callbacks.
/// Managed exceptions escaping to native code crash the process,
/// so all callbacks must be wrapped in try/catch blocks.
/// </summary>
public static class VLCSafeExecute
{
    /// <summary>
    /// Executes a video filter callback safely.
    /// On exception, logs error via VLC logger and returns the input picture unchanged.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="picturePtr">Pointer to picture_t</param>
    /// <param name="callback">The actual filter callback to execute</param>
    /// <returns>The picture pointer (same as input on error)</returns>
    public static nint FilterVideo(nint filterPtr, nint picturePtr, Func<nint, nint, nint> callback)
    {
        try
        {
            return callback(filterPtr, picturePtr);
        }
        catch (Exception ex)
        {
            LogError(filterPtr, "FilterVideo", ex);
            return picturePtr;
        }
    }

    /// <summary>
    /// Executes a text renderer callback safely.
    /// On exception, logs error via VLC logger and returns null (no region).
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="regionPtr">Pointer to input subpicture_region_t</param>
    /// <param name="chromaListPtr">Pointer to chroma list</param>
    /// <param name="callback">The actual render callback to execute</param>
    /// <returns>Pointer to output region, or 0 on error</returns>
    public static nint RenderText(nint filterPtr, nint regionPtr, nint chromaListPtr,
        Func<nint, nint, nint, nint> callback)
    {
        try
        {
            return callback(filterPtr, regionPtr, chromaListPtr);
        }
        catch (Exception ex)
        {
            LogError(filterPtr, "RenderText", ex);
            return 0;
        }
    }

    /// <summary>
    /// Executes a filter Open callback safely.
    /// On exception, logs error and returns failure code.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="callback">The actual open callback to execute</param>
    /// <returns>0 on success, -1 on failure</returns>
    public static int Open(nint filterPtr, Func<nint, int> callback)
    {
        try
        {
            return callback(filterPtr);
        }
        catch (Exception ex)
        {
            LogError(filterPtr, "Open", ex);
            return -1;
        }
    }

    /// <summary>
    /// Executes a filter Close callback safely.
    /// On exception, logs error and continues (best-effort cleanup).
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="callback">The actual close callback to execute</param>
    public static void Close(nint filterPtr, Action<nint> callback)
    {
        try
        {
            callback(filterPtr);
        }
        catch (Exception ex)
        {
            LogError(filterPtr, "Close", ex);
        }
    }

    /// <summary>
    /// Executes a filter Flush callback safely.
    /// On exception, logs error and continues.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    /// <param name="callback">The actual flush callback to execute</param>
    public static void Flush(nint filterPtr, Action<nint> callback)
    {
        try
        {
            callback(filterPtr);
        }
        catch (Exception ex)
        {
            LogError(filterPtr, "Flush", ex);
        }
    }

    /// <summary>
    /// Executes a callback that returns a boolean result safely.
    /// On exception, logs error and returns false.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t or other VLC object</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="callback">The callback to execute</param>
    /// <returns>True on success, false on error</returns>
    public static bool Execute(nint filterPtr, string operationName, Func<bool> callback)
    {
        try
        {
            return callback();
        }
        catch (Exception ex)
        {
            LogError(filterPtr, operationName, ex);
            return false;
        }
    }

    /// <summary>
    /// Executes a void callback safely.
    /// On exception, logs error and continues.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t or other VLC object</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="callback">The callback to execute</param>
    public static void Execute(nint filterPtr, string operationName, Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            LogError(filterPtr, operationName, ex);
        }
    }

    /// <summary>
    /// Logs an error through VLC's logging system.
    /// </summary>
    private static void LogError(nint vlcObjectPtr, string operationName, Exception ex)
    {
        try
        {
            var logger = new VLCLogger(vlcObjectPtr);
            logger.Error($"[VLCLR] Exception in {operationName}: {ex.Message}");
        }
        catch
        {
            // Ignore logging failures - we're already in an error state
        }
    }
}
