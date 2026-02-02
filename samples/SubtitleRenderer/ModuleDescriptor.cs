// VLC Module Descriptor for Subtitle Renderer Sample
// Exports vlc_entry, vlc_entry_api_version, vlc_entry_copyright
// Uses VLCLR.Module.ModuleBuilder for fluent module registration

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR;
using VLCLR.Module;
using VLCLR.Native;

namespace SubtitleRenderer;

/// <summary>
/// VLC module descriptor for the .NET subtitle text renderer.
/// Exports the vlc_entry function that VLC calls to register the plugin.
/// </summary>
public static unsafe class ModuleDescriptor
{
    // Static strings for VLC API version and copyright (must remain pinned)
    private static readonly PinnedString s_apiVersion = new("4.0.6");
    private static readonly PinnedString s_copyright = new("Copyright (C) VideoLabs");

    // Static text renderer operations structure - must be kept alive for VLC
    private static VLCTextRendererOperations s_rendererOps;
    private static GCHandle s_rendererOpsHandle;
    private static nint s_rendererOpsPtr;
    private static bool s_rendererOpsInitialized;

    // Renderer callback function pointers for ops struct
    private static nint s_renderCallback;
    private static nint s_closeCallback;

    /// <summary>
    /// VLC plugin entry point. Called by VLC to register the module.
    /// Signature: int vlc_entry(vlc_set_cb vlc_set, void* opaque)
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry")]
    public static int VlcEntry(nint vlcSetPtr, nint opaque)
    {
        return ModuleBuilder.Create(vlcSetPtr, opaque)
            .WithName("dotnet_subtitle")
            .WithShortName(".NET Subtitle")
            .WithDescription(".NET Native AOT Text Renderer for Subtitles")
            .WithCapability("text renderer")
            .WithScore(100)  // Higher score to be preferred over default renderer
            .WithOpenCallback(&FilterOpen)
            .Register();
    }

    /// <summary>
    /// Returns the VLC API version this plugin was built for.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_api_version")]
    public static nint VlcEntryApiVersion() => s_apiVersion.Pointer;

    /// <summary>
    /// Returns the copyright string for this plugin.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright")]
    public static nint VlcEntryCopyright() => s_copyright.Pointer;

    /// <summary>
    /// Text renderer module open callback - called by VLC when activating the renderer.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FilterOpen(nint filterPtr)
    {
        try
        {
            // Initialize the renderer operations structure if not already done
            InitializeRendererOps();

            // Read the filter structure
            ref VLCFilter filter = ref Unsafe.AsRef<VLCFilter>((void*)filterPtr);

            Console.Error.WriteLine("[.NET Subtitle] FilterOpen called");

            // Initialize renderer state
            RendererState.Initialize(filterPtr);

            // Set filter->ops to our operations structure
            filter.Operations = s_rendererOpsPtr;

            Console.Error.WriteLine("[.NET Subtitle] FilterOpen completed successfully");
            return 0; // VLC_SUCCESS
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[.NET Subtitle] FilterOpen failed: {ex.Message}");
            Console.Error.WriteLine($"[.NET Subtitle] Stack trace: {ex.StackTrace}");
            return -1; // VLC_EGENERIC
        }
    }

    /// <summary>
    /// Text renderer render callback - called by VLC to render a subtitle region.
    /// Signature: subpicture_region_t* (*render)(filter_t*, const subpicture_region_t*, const vlc_fourcc_t*)
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint RenderCallback(nint filterPtr, nint regionPtr, nint chromaListPtr)
    {
        try
        {
            return RendererState.Render(filterPtr, regionPtr, chromaListPtr);
        }
        catch (Exception ex)
        {
            // Log errors only occasionally to avoid spam
            if (RendererState.RenderCount % 100 == 0)
            {
                Console.Error.WriteLine($"[.NET Subtitle] Render error: {ex.Message}");
            }
            return nint.Zero; // Return null on error
        }
    }

    /// <summary>
    /// Text renderer close callback via ops->close.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CloseCallback(nint filterPtr)
    {
        try
        {
            Console.Error.WriteLine("[.NET Subtitle] Close called");
            RendererState.Cleanup();
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }

    private static void InitializeRendererOps()
    {
        if (s_rendererOpsInitialized)
            return;

        // Get callback function pointers
        s_renderCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&RenderCallback;
        s_closeCallback = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CloseCallback;

        // Initialize the operations structure
        // Text renderers use the first field (Render) instead of FilterVideo
        s_rendererOps = new VLCTextRendererOperations
        {
            Render = s_renderCallback,
            Drain = nint.Zero,
            Flush = nint.Zero,
            ChangeViewpoint = nint.Zero,
            VideoMouse = nint.Zero,
            Close = s_closeCallback
        };

        // Pin the structure so VLC can access it
        s_rendererOpsHandle = GCHandle.Alloc(s_rendererOps, GCHandleType.Pinned);
        s_rendererOpsPtr = s_rendererOpsHandle.AddrOfPinnedObject();
        s_rendererOpsInitialized = true;
    }
}
