/**
 * VLC Plugin Entry Point
 * C glue that bridges VLC's plugin system to .NET Native AOT code.
 */

/* VLC requires MODULE_NAME and VLC_DYNAMIC_PLUGIN before headers */
#define MODULE_NAME dotnet_bridge
#define VLC_DYNAMIC_PLUGIN

/* LGPL 2.1+ license for VLC compatibility */
#define VLC_MODULE_LICENSE VLC_LICENSE_LGPL_2_1_PLUS

#include <vlc_common.h>
#include <vlc_plugin.h>
#include <vlc_interface.h>

#include "dotnet_bridge.h"

/**
 * Open callback - called when VLC loads this interface plugin.
 */
static int Open(vlc_object_t *obj)
{
    /* Initialize bridge to .NET DLL */
    if (dotnet_bridge_init() != 0)
    {
        msg_Err(obj, "Failed to initialize .NET bridge");
        return VLC_EGENERIC;
    }

    /* Forward to .NET implementation */
    int result = dotnet_plugin_open((void*)obj);
    if (result != 0)
    {
        msg_Err(obj, ".NET plugin open returned error: %d", result);
        dotnet_bridge_cleanup();
        return VLC_EGENERIC;
    }

    msg_Info(obj, ".NET plugin opened successfully");
    return VLC_SUCCESS;
}

/**
 * Close callback - called when VLC unloads this interface plugin.
 */
static void Close(vlc_object_t *obj)
{
    if (dotnet_plugin_close)
    {
        dotnet_plugin_close((void*)obj);
    }

    dotnet_bridge_cleanup();
    msg_Info(obj, ".NET plugin closed");
}

/* VLC module descriptor */
vlc_module_begin()
    set_shortname(".NET Plugin")
    set_description(".NET Native AOT Plugin")
    set_capability("interface", 0)
    set_callbacks(Open, Close)
vlc_module_end()
