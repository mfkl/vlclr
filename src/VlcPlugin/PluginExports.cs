using System;
using System.Runtime.InteropServices;

namespace VlcPlugin;

/// <summary>
/// Native entry points called by the C glue layer.
/// These functions are exported from the native DLL.
/// </summary>
public static unsafe class PluginExports
{
    private static PluginState? _state;

    /// <summary>
    /// Called when VLC loads the plugin.
    /// </summary>
    /// <param name="vlcObject">Opaque pointer to vlc_object_t</param>
    /// <returns>0 for success, -1 for failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "DotNetPluginOpen")]
    public static int Open(nint vlcObject)
    {
        try
        {
            _state = new PluginState(vlcObject);
            _state.Initialize();
            return 0; // VLC_SUCCESS
        }
        catch
        {
            return -1; // VLC_EGENERIC
        }
    }

    /// <summary>
    /// Called when VLC unloads the plugin.
    /// </summary>
    /// <param name="vlcObject">Opaque pointer to vlc_object_t</param>
    [UnmanagedCallersOnly(EntryPoint = "DotNetPluginClose")]
    public static void Close(nint vlcObject)
    {
        try
        {
            _state?.Cleanup();
            _state = null;
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }
}
