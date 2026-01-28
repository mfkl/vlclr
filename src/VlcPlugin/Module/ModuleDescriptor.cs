// VLC Module Descriptor - Pure C# implementation
// Exports vlc_entry, vlc_entry_api_version, vlc_entry_copyright
// Replaces the C glue layer's vlc_module_begin/end macros

using System.Runtime.InteropServices;
using VlcPlugin.Native;
using static VlcPlugin.Module.VlcModuleProperties;
using static VlcPlugin.Module.VlcApi;
using static VlcPlugin.Module.VlcConfigTypes;
using static VlcPlugin.Module.VlcConfigSubcategory;

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
    private static VlcSetShortcuts3Delegate? s_setShortcuts;
    private static VlcSetConfigCreateDelegate? s_setConfigCreate;
    private static VlcSetInt64Delegate? s_setInt64;

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

    // Video filter submodule strings
    private static readonly byte[] s_filterModuleNameBytes = "dotnet_overlay\0"u8.ToArray();
    private static readonly byte[] s_filterShortNameBytes = ".NET Overlay\0"u8.ToArray();
    private static readonly byte[] s_filterDescriptionBytes = ".NET Native AOT Video Filter Overlay\0"u8.ToArray();
    private static readonly byte[] s_filterCapabilityBytes = "video filter\0"u8.ToArray();
    private static readonly byte[] s_filterOpenNameBytes = "FilterOpen\0"u8.ToArray();
    private static readonly byte[] s_filterShortcut1Bytes = "dotnet_overlay\0"u8.ToArray();
    private static readonly byte[] s_filterShortcut2Bytes = "dotnet\0"u8.ToArray();
    private static readonly byte[] s_filterShortcut3Bytes = "netoverlay\0"u8.ToArray();

    // GCHandles to keep strings pinned
    private static GCHandle s_apiVersionHandle;
    private static GCHandle s_copyrightHandle;
    private static GCHandle s_moduleNameHandle;
    private static GCHandle s_shortNameHandle;
    private static GCHandle s_descriptionHandle;
    private static GCHandle s_capabilityHandle;
    private static GCHandle s_openNameHandle;
    private static GCHandle s_closeNameHandle;

    // Video filter handles
    private static GCHandle s_filterModuleNameHandle;
    private static GCHandle s_filterShortNameHandle;
    private static GCHandle s_filterDescriptionHandle;
    private static GCHandle s_filterCapabilityHandle;
    private static GCHandle s_filterOpenNameHandle;
    private static GCHandle s_filterShortcut1Handle;
    private static GCHandle s_filterShortcut2Handle;
    private static GCHandle s_filterShortcut3Handle;

    // Pointers to pinned strings
    private static nint s_apiVersionPtr;
    private static nint s_copyrightPtr;
    private static nint s_moduleNamePtr;
    private static nint s_shortNamePtr;
    private static nint s_descriptionPtr;
    private static nint s_capabilityPtr;
    private static nint s_openNamePtr;
    private static nint s_closeNamePtr;

    // Video filter pointers
    private static nint s_filterModuleNamePtr;
    private static nint s_filterShortNamePtr;
    private static nint s_filterDescriptionPtr;
    private static nint s_filterCapabilityPtr;
    private static nint s_filterOpenNamePtr;
    private static nint s_filterShortcut1Ptr;
    private static nint s_filterShortcut2Ptr;
    private static nint s_filterShortcut3Ptr;

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

        // Video filter strings
        s_filterModuleNameHandle = GCHandle.Alloc(s_filterModuleNameBytes, GCHandleType.Pinned);
        s_filterModuleNamePtr = s_filterModuleNameHandle.AddrOfPinnedObject();

        s_filterShortNameHandle = GCHandle.Alloc(s_filterShortNameBytes, GCHandleType.Pinned);
        s_filterShortNamePtr = s_filterShortNameHandle.AddrOfPinnedObject();

        s_filterDescriptionHandle = GCHandle.Alloc(s_filterDescriptionBytes, GCHandleType.Pinned);
        s_filterDescriptionPtr = s_filterDescriptionHandle.AddrOfPinnedObject();

        s_filterCapabilityHandle = GCHandle.Alloc(s_filterCapabilityBytes, GCHandleType.Pinned);
        s_filterCapabilityPtr = s_filterCapabilityHandle.AddrOfPinnedObject();

        s_filterOpenNameHandle = GCHandle.Alloc(s_filterOpenNameBytes, GCHandleType.Pinned);
        s_filterOpenNamePtr = s_filterOpenNameHandle.AddrOfPinnedObject();

        s_filterShortcut1Handle = GCHandle.Alloc(s_filterShortcut1Bytes, GCHandleType.Pinned);
        s_filterShortcut1Ptr = s_filterShortcut1Handle.AddrOfPinnedObject();

        s_filterShortcut2Handle = GCHandle.Alloc(s_filterShortcut2Bytes, GCHandleType.Pinned);
        s_filterShortcut2Ptr = s_filterShortcut2Handle.AddrOfPinnedObject();

        s_filterShortcut3Handle = GCHandle.Alloc(s_filterShortcut3Bytes, GCHandleType.Pinned);
        s_filterShortcut3Ptr = s_filterShortcut3Handle.AddrOfPinnedObject();
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

            // ==========================================
            // Register video filter submodule
            // ==========================================
            s_setShortcuts = VlcSetHelpers.GetDelegate<VlcSetShortcuts3Delegate>(vlcSetPtr);
            s_setConfigCreate = VlcSetHelpers.GetDelegate<VlcSetConfigCreateDelegate>(vlcSetPtr);
            s_setInt64 = VlcSetHelpers.GetDelegate<VlcSetInt64Delegate>(vlcSetPtr);

            nint filterModule;
            if (s_setCreate(opaque, 0, VLC_MODULE_CREATE, &filterModule) != 0)
                return -1;

            // Set filter module name
            if (s_setString(opaque, filterModule, VLC_MODULE_NAME, s_filterModuleNamePtr) != 0)
                return -1;

            // Set display names
            if (s_setString(opaque, filterModule, VLC_MODULE_SHORTNAME, s_filterShortNamePtr) != 0)
                return -1;

            if (s_setString(opaque, filterModule, VLC_MODULE_DESCRIPTION, s_filterDescriptionPtr) != 0)
                return -1;

            // Set capability: video filter module with score 0
            if (s_setString(opaque, filterModule, VLC_MODULE_CAPABILITY, s_filterCapabilityPtr) != 0)
                return -1;

            if (s_setInt(opaque, filterModule, VLC_MODULE_SCORE, 0) != 0)
                return -1;

            // Register filter open callback (close is done via ops->close)
            if (s_setCallback(opaque, filterModule, VLC_MODULE_CB_OPEN, s_filterOpenNamePtr, s_filterOpenCallback) != 0)
                return -1;

            // Note: VLC_MODULE_NAME automatically adds the name as the first shortcut
            // Additional shortcuts can be added via VLC_MODULE_SHORTCUT, but only BEFORE VLC_MODULE_NAME
            // Since we already set the name, the module is accessible via "dotnet_overlay"

            // Set subcategory for video filter
            nint config;
            if (s_setConfigCreate(opaque, 0, VLC_CONFIG_CREATE, VlcConfigTypes.CONFIG_SUBCATEGORY, &config) != 0)
                return -1;

            if (s_setInt64(opaque, config, VLC_CONFIG_VALUE, VlcConfigSubcategory.SUBCAT_VIDEO_VFILTER) != 0)
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

    // Static filter operations structure - must be kept alive for VLC
    private static VlcFilterOperations s_filterOps;
    private static GCHandle s_filterOpsHandle;
    private static nint s_filterOpsPtr;
    private static bool s_filterOpsInitialized;

    // Filter callback function pointers
    private static nint s_filterVideoCallback;
    private static nint s_filterCloseOpsCallback;

    /// <summary>
    /// Video filter frame callback - called by VLC for each video frame.
    /// Signature: picture_t* (*filter_video)(filter_t*, picture_t*)
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static nint FilterVideoCallback(nint filterPtr, nint picturePtr)
    {
        return FilterVideoImpl(filterPtr, picturePtr);
    }

    /// <summary>
    /// Video filter close callback via ops->close.
    /// Signature: void (*close)(filter_t*)
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void FilterCloseOpsCallback(nint filterPtr)
    {
        FilterCloseImpl(filterPtr);
    }

    private static void InitializeFilterOps()
    {
        if (s_filterOpsInitialized)
            return;

        // Get callback function pointers
        s_filterVideoCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&FilterVideoCallback;
        s_filterCloseOpsCallback = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FilterCloseOpsCallback;

        // Initialize the operations structure
        s_filterOps = new VlcFilterOperations
        {
            FilterVideo = s_filterVideoCallback,
            Drain = nint.Zero,
            Flush = nint.Zero,
            ChangeViewpoint = nint.Zero,
            VideoMouse = nint.Zero,
            Close = s_filterCloseOpsCallback
        };

        // Pin the structure so VLC can access it
        s_filterOpsHandle = GCHandle.Alloc(s_filterOps, GCHandleType.Pinned);
        s_filterOpsPtr = s_filterOpsHandle.AddrOfPinnedObject();
        s_filterOpsInitialized = true;
    }

    private static int FilterOpenImpl(nint filterPtr)
    {
        try
        {
            s_currentFilterPtr = filterPtr;

            // Initialize the filter operations structure if not already done
            InitializeFilterOps();

            // Read filter structure to get video format info
            // filter_t layout (simplified for the fields we need):
            // - VlcObjectHeader obj
            // - nint p_module
            // - nint p_sys
            // - VlcEsFormat fmt_in
            // - nint vctx_in
            // - VlcEsFormat fmt_out
            // - nint vctx_out
            // - byte b_allow_fmt_out_change
            // - padding
            // - nint psz_name
            // - nint p_cfg
            // - nint ops <-- this is what we need to set

            // Calculate offset to fmt_in.video (VlcVideoFormat within VlcEsFormat)
            // VlcObjectHeader size + 2 pointers (p_module, p_sys) = header_size
            // Then VlcEsFormat starts, and video_format_t is at offset after the first few fields

            // Read the filter structure
            ref VlcFilter filter = ref System.Runtime.CompilerServices.Unsafe.AsRef<VlcFilter>((void*)filterPtr);

            // Get video format info from fmt_in.video
            uint chroma = filter.FormatIn.Video.Chroma;
            uint width = filter.FormatIn.Video.Width;
            uint height = filter.FormatIn.Video.Height;

            Console.Error.WriteLine($"[VlcPlugin] FilterOpen: {width}x{height} chroma=0x{chroma:X8}");

            // Log fourcc as characters
            char c1 = (char)(chroma & 0xFF);
            char c2 = (char)((chroma >> 8) & 0xFF);
            char c3 = (char)((chroma >> 16) & 0xFF);
            char c4 = (char)((chroma >> 24) & 0xFF);
            Console.Error.WriteLine($"[VlcPlugin] Chroma fourcc: {c1}{c2}{c3}{c4}");

            // Initialize filter state
            FilterState.Initialize(filterPtr, (int)width, (int)height, chroma);

            // Set filter->ops to our operations structure
            // The ops field is at a specific offset in filter_t
            // We need to write the pointer to our operations structure
            filter.Operations = s_filterOpsPtr;

            // Copy format to output (we don't change the format)
            filter.FormatOut = filter.FormatIn;

            Console.Error.WriteLine("[VlcPlugin] FilterOpen completed successfully");
            return 0; // VLC_SUCCESS
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VlcPlugin] FilterOpen failed: {ex.Message}");
            Console.Error.WriteLine($"[VlcPlugin] Stack trace: {ex.StackTrace}");
            return -1; // VLC_EGENERIC
        }
    }

    private static nint FilterVideoImpl(nint filterPtr, nint picturePtr)
    {
        try
        {
            if (picturePtr == nint.Zero)
                return nint.Zero;

            // Read the picture structure
            ref VlcPicture inputPic = ref System.Runtime.CompilerServices.Unsafe.AsRef<VlcPicture>((void*)picturePtr);

            // Check if we have accessible planes
            if (inputPic.PlaneCount == 0)
            {
                // Opaque format (GPU) - cannot modify, pass through
                return picturePtr;
            }

            // Get format info
            uint chroma = inputPic.Format.Chroma;

            // Read the filter for format info
            ref VlcFilter filter = ref System.Runtime.CompilerServices.Unsafe.AsRef<VlcFilter>((void*)filterPtr);

            // For video filters, we need to:
            // 1. Get a new output picture from VLC
            // 2. Copy input to output
            // 3. Process the output (apply overlay)
            // 4. Release input
            // 5. Return output

            // However, filter_NewPicture is an inline function that calls through owner.video->buffer_new
            // For simplicity in this first implementation, we'll modify the input picture directly
            // (This is acceptable for passthrough filters, though not ideal)

            // Get plane 0 info for processing
            ref VlcPlane plane0 = ref inputPic.Plane0;

            if (plane0.Pixels != nint.Zero)
            {
                // Process the frame - apply overlay
                FilterState.ProcessFrame(
                    plane0.Pixels,
                    plane0.Pitch,
                    plane0.VisiblePitch,
                    plane0.VisibleLines,
                    chroma);
            }

            // Return the (modified) input picture
            return picturePtr;
        }
        catch (Exception ex)
        {
            // Log errors only occasionally to avoid spam
            if (FilterState.FrameCount % 300 == 0)
            {
                Console.Error.WriteLine($"[VlcPlugin] FilterVideo error: {ex.Message}");
            }
            // Return input picture unchanged on error
            return picturePtr;
        }
    }

    private static void FilterCloseImpl(nint filterPtr)
    {
        try
        {
            Console.Error.WriteLine("[VlcPlugin] FilterClose called");
            FilterState.Cleanup();
            s_currentFilterPtr = nint.Zero;
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }
}
