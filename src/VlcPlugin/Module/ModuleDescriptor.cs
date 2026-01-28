// VLC Module Descriptor - Pure C# implementation
// Exports vlc_entry, vlc_entry_api_version, vlc_entry_copyright
// Replaces the C glue layer's vlc_module_begin/end macros

using System.Runtime.InteropServices;
using static VlcPlugin.Module.VlcModuleProperties;
using static VlcPlugin.Module.VlcApi;

namespace VlcPlugin.Module;

/// <summary>
/// VLC module descriptor implemented in pure C# using Native AOT.
/// Exports the vlc_entry function that VLC calls to register the plugin.
/// </summary>
public static unsafe class ModuleDescriptor
{
    // Keep delegate instances alive to prevent GC
    private static VlcSetCreateDelegate? s_setCreate;
    private static VlcSetStringDelegate? s_setString;
    private static VlcSetIntDelegate? s_setInt;
    private static VlcSetCallbackDelegate? s_setCallback;

    // Keep callback pointers pinned
    private static nint s_openCallback;
    private static nint s_closeCallback;

    /// <summary>
    /// VLC plugin entry point. Called by VLC to register the module.
    /// Signature: int vlc_entry(vlc_set_cb vlc_set, void* opaque)
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry")]
    public static int VlcEntry(nint vlcSetPtr, nint opaque)
    {
        try
        {
            // Cache delegates - they all point to the same variadic function,
            // but we need different delegate types for different argument patterns
            s_setCreate = VlcSetHelpers.GetDelegate<VlcSetCreateDelegate>(vlcSetPtr);
            s_setString = VlcSetHelpers.GetDelegate<VlcSetStringDelegate>(vlcSetPtr);
            s_setInt = VlcSetHelpers.GetDelegate<VlcSetIntDelegate>(vlcSetPtr);
            s_setCallback = VlcSetHelpers.GetDelegate<VlcSetCallbackDelegate>(vlcSetPtr);

            // Get callback function pointers for interface Open/Close
            s_openCallback = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&InterfaceOpen;
            s_closeCallback = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&InterfaceClose;

            // Create main module
            nint module;
            if (s_setCreate(opaque, 0, VLC_MODULE_CREATE, &module) != 0)
                return -1;

            // Set module name (must match the DLL name minus lib prefix and extension)
            if (s_setString(opaque, module, VLC_MODULE_NAME, "dotnet_plugin") != 0)
                return -1;

            // Set display names
            if (s_setString(opaque, module, VLC_MODULE_SHORTNAME, ".NET Plugin") != 0)
                return -1;

            if (s_setString(opaque, module, VLC_MODULE_DESCRIPTION, ".NET Native AOT Plugin") != 0)
                return -1;

            // Set capability: interface module with score 0
            if (s_setString(opaque, module, VLC_MODULE_CAPABILITY, "interface") != 0)
                return -1;

            if (s_setInt(opaque, module, VLC_MODULE_SCORE, 0) != 0)
                return -1;

            // Register open callback
            if (s_setCallback(opaque, module, VLC_MODULE_CB_OPEN, "InterfaceOpen", s_openCallback) != 0)
                return -1;

            // Register close callback
            if (s_setCallback(opaque, module, VLC_MODULE_CB_CLOSE, "InterfaceClose", s_closeCallback) != 0)
                return -1;

            return 0; // Success
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Returns the VLC API version this plugin was built for.
    /// VLC uses this to check plugin compatibility.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_api_version")]
    public static nint VlcEntryApiVersion()
    {
        return (nint)(delegate* unmanaged[Cdecl]<nint>)&GetApiVersionString;
    }

    /// <summary>
    /// Returns the copyright string for this plugin.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright")]
    public static nint VlcEntryCopyright()
    {
        return (nint)(delegate* unmanaged[Cdecl]<nint>)&GetCopyrightString;
    }

    // Static UTF-8 bytes for version string "4.0.6\0"
    private static ReadOnlySpan<byte> ApiVersionBytes => "4.0.6\0"u8;
    private static byte[]? s_apiVersionPinned;

    // Static UTF-8 bytes for copyright string
    private static ReadOnlySpan<byte> CopyrightBytes => "Copyright (C) VideoLabs\0"u8;
    private static byte[]? s_copyrightPinned;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static nint GetApiVersionString()
    {
        // Pin the string in memory (will live forever)
        s_apiVersionPinned ??= ApiVersionBytes.ToArray();
        fixed (byte* ptr = s_apiVersionPinned)
        {
            return (nint)ptr;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static nint GetCopyrightString()
    {
        s_copyrightPinned ??= CopyrightBytes.ToArray();
        fixed (byte* ptr = s_copyrightPinned)
        {
            return (nint)ptr;
        }
    }

    /// <summary>
    /// Interface module open callback - called by VLC when loading the plugin.
    /// Signature must match: int (*)(vlc_object_t*)
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int InterfaceOpen(nint vlcObject)
    {
        // Call the implementation directly (not the exported method)
        return InterfaceOpenImpl(vlcObject);
    }

    /// <summary>
    /// Interface module close callback - called by VLC when unloading the plugin.
    /// Signature must match: void (*)(vlc_object_t*)
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void InterfaceClose(nint vlcObject)
    {
        InterfaceCloseImpl(vlcObject);
    }

    // Internal implementations that can be called from managed code
    private static PluginState? s_pluginState;

    private static int InterfaceOpenImpl(nint vlcObject)
    {
        try
        {
            s_pluginState = new PluginState(vlcObject);
            s_pluginState.Initialize();
            return 0; // VLC_SUCCESS
        }
        catch
        {
            return -1; // VLC_EGENERIC
        }
    }

    private static void InterfaceCloseImpl(nint vlcObject)
    {
        try
        {
            s_pluginState?.Cleanup();
            s_pluginState = null;
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }
}
