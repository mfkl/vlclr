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

    // Keep callback pointers pinned for interface module
    private static nint s_intfOpenCallback;
    private static nint s_intfCloseCallback;

    // Keep callback pointers pinned for video filter module
    private static nint s_filterOpenCallback;
    private static nint s_filterCloseCallback;

    // Static pinned byte arrays for all strings used in vlc_entry
    // These must remain pinned for the lifetime of the plugin
    private static readonly byte[] s_apiVersionBytes = "4.0.6\0"u8.ToArray();
    private static readonly byte[] s_copyrightBytes = "Copyright (C) VideoLabs\0"u8.ToArray();
    private static readonly byte[] s_moduleNameBytes = "dotnet_plugin\0"u8.ToArray();
    private static readonly byte[] s_shortNameBytes = ".NET Plugin\0"u8.ToArray();
    private static readonly byte[] s_descriptionBytes = ".NET Native AOT Interface Plugin\0"u8.ToArray();
    private static readonly byte[] s_capabilityBytes = "interface\0"u8.ToArray();
    private static readonly byte[] s_openNameBytes = "Open\0"u8.ToArray();
    private static readonly byte[] s_closeNameBytes = "Close\0"u8.ToArray();

    // GCHandles to keep strings pinned
    private static GCHandle s_apiVersionHandle;
    private static GCHandle s_copyrightHandle;
    private static GCHandle s_moduleNameHandle;
    private static GCHandle s_shortNameHandle;
    private static GCHandle s_descriptionHandle;
    private static GCHandle s_capabilityHandle;
    private static GCHandle s_openNameHandle;
    private static GCHandle s_closeNameHandle;

    // Pointers to pinned strings
    private static nint s_apiVersionPtr;
    private static nint s_copyrightPtr;
    private static nint s_moduleNamePtr;
    private static nint s_shortNamePtr;
    private static nint s_descriptionPtr;
    private static nint s_capabilityPtr;
    private static nint s_openNamePtr;
    private static nint s_closeNamePtr;

    // Static constructor to pin all strings
    static ModuleDescriptor()
    {
        s_apiVersionHandle = GCHandle.Alloc(s_apiVersionBytes, GCHandleType.Pinned);
        s_apiVersionPtr = s_apiVersionHandle.AddrOfPinnedObject();

        s_copyrightHandle = GCHandle.Alloc(s_copyrightBytes, GCHandleType.Pinned);
        s_copyrightPtr = s_copyrightHandle.AddrOfPinnedObject();

        s_moduleNameHandle = GCHandle.Alloc(s_moduleNameBytes, GCHandleType.Pinned);
        s_moduleNamePtr = s_moduleNameHandle.AddrOfPinnedObject();

        s_shortNameHandle = GCHandle.Alloc(s_shortNameBytes, GCHandleType.Pinned);
        s_shortNamePtr = s_shortNameHandle.AddrOfPinnedObject();

        s_descriptionHandle = GCHandle.Alloc(s_descriptionBytes, GCHandleType.Pinned);
        s_descriptionPtr = s_descriptionHandle.AddrOfPinnedObject();

        s_capabilityHandle = GCHandle.Alloc(s_capabilityBytes, GCHandleType.Pinned);
        s_capabilityPtr = s_capabilityHandle.AddrOfPinnedObject();

        s_openNameHandle = GCHandle.Alloc(s_openNameBytes, GCHandleType.Pinned);
        s_openNamePtr = s_openNameHandle.AddrOfPinnedObject();

        s_closeNameHandle = GCHandle.Alloc(s_closeNameBytes, GCHandleType.Pinned);
        s_closeNamePtr = s_closeNameHandle.AddrOfPinnedObject();
    }

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
            s_intfOpenCallback = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&InterfaceOpen;
            s_intfCloseCallback = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&InterfaceClose;

            // Get callback function pointers for video filter Open/Close
            s_filterOpenCallback = (nint)(delegate* unmanaged[Cdecl]<nint, int>)&FilterOpen;
            s_filterCloseCallback = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FilterClose;

            // ==========================================
            // Register main module (interface)
            // ==========================================
            nint module;
            if (s_setCreate(opaque, 0, VLC_MODULE_CREATE, &module) != 0)
                return -1;

            // Set module name (must match the DLL name minus lib prefix and extension)
            if (s_setString(opaque, module, VLC_MODULE_NAME, s_moduleNamePtr) != 0)
                return -1;

            // Set display names
            if (s_setString(opaque, module, VLC_MODULE_SHORTNAME, s_shortNamePtr) != 0)
                return -1;

            if (s_setString(opaque, module, VLC_MODULE_DESCRIPTION, s_descriptionPtr) != 0)
                return -1;

            // Set capability: interface module with score 0
            if (s_setString(opaque, module, VLC_MODULE_CAPABILITY, s_capabilityPtr) != 0)
                return -1;

            if (s_setInt(opaque, module, VLC_MODULE_SCORE, 0) != 0)
                return -1;

            // Register open callback
            if (s_setCallback(opaque, module, VLC_MODULE_CB_OPEN, s_openNamePtr, s_intfOpenCallback) != 0)
                return -1;

            // Register close callback
            if (s_setCallback(opaque, module, VLC_MODULE_CB_CLOSE, s_closeNamePtr, s_intfCloseCallback) != 0)
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
    /// Returns pointer to null-terminated UTF-8 string "4.0.6".
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_api_version")]
    public static nint VlcEntryApiVersion()
    {
        return s_apiVersionPtr;
    }

    /// <summary>
    /// Returns the copyright string for this plugin.
    /// Returns pointer to null-terminated UTF-8 string.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright")]
    public static nint VlcEntryCopyright()
    {
        return s_copyrightPtr;
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

    /// <summary>
    /// Video filter module open callback - called by VLC when activating the filter.
    /// Signature must match: int (*)(vlc_object_t*)
    /// The vlcObject is actually a filter_t* for video filters.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int FilterOpen(nint filterPtr)
    {
        return FilterOpenImpl(filterPtr);
    }

    /// <summary>
    /// Video filter module close callback - called by VLC when deactivating the filter.
    /// Signature must match: void (*)(vlc_object_t*)
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void FilterClose(nint filterPtr)
    {
        FilterCloseImpl(filterPtr);
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

    // Video filter state
    private static nint s_currentFilterPtr;

    private static int FilterOpenImpl(nint filterPtr)
    {
        try
        {
            s_currentFilterPtr = filterPtr;
            // The filter_t structure contains format info we need to read
            // For now, we'll defer detailed implementation to Phase 3
            // Just log and return success to test registration
            Console.Error.WriteLine("[VlcPlugin] FilterOpen called");
            return 0; // VLC_SUCCESS
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VlcPlugin] FilterOpen failed: {ex.Message}");
            return -1; // VLC_EGENERIC
        }
    }

    private static void FilterCloseImpl(nint filterPtr)
    {
        try
        {
            Console.Error.WriteLine("[VlcPlugin] FilterClose called");
            s_currentFilterPtr = nint.Zero;
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }
}
