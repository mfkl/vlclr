// VLC Module Descriptor for Video Overlay Sample
// Exports vlc_entry, vlc_entry_api_version, vlc_entry_copyright
// Uses VLCLR.Module.ModuleBuilder for fluent module registration

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR;
using VLCLR.Module;
using VLCLR.Native;

namespace VideoOverlay;

/// <summary>
/// VLC module descriptor for the .NET video overlay filter.
/// Exports the vlc_entry function that VLC calls to register the plugin.
/// </summary>
public static unsafe class ModuleDescriptor
{
    // Static strings for VLC API version and copyright (must remain pinned)
    private static readonly PinnedString s_apiVersion = new("4.0.6");
    private static readonly PinnedString s_copyright = new("Copyright (C) VideoLabs");

    // Static filter operations structure - must be kept alive for VLC
    private static VLCFilterOperations s_filterOps;
    private static GCHandle s_filterOpsHandle;
    private static nint s_filterOpsPtr;
    private static bool s_filterOpsInitialized;

    // Filter callback function pointers for ops struct
    private static nint s_filterVideoCallback;
    private static nint s_filterCloseOpsCallback;

    /// <summary>
    /// VLC plugin entry point. Called by VLC to register the module.
    /// Signature: int vlc_entry(vlc_set_cb vlc_set, void* opaque)
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "vlc_entry")]
    public static int VlcEntry(nint vlcSetPtr, nint opaque)
    {
        return ModuleBuilder.Create(vlcSetPtr, opaque)
            .WithName("dotnet_overlay")
            .WithShortName(".NET Overlay")
            .WithDescription(".NET Native AOT Video Filter Overlay")
            .WithCapability("video filter")
            .WithScore(0)
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
    /// Video filter module open callback - called by VLC when activating the filter.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FilterOpen(nint filterPtr)
    {
        try
        {
            // Initialize the filter operations structure if not already done
            InitializeFilterOps();

            // Read the filter structure to get video format info
            ref VLCFilter filter = ref Unsafe.AsRef<VLCFilter>((void*)filterPtr);

            // Get video format info from fmt_in.video
            uint chroma = filter.FormatIn.Video.Chroma;
            uint width = filter.FormatIn.Video.Width;
            uint height = filter.FormatIn.Video.Height;

            Console.Error.WriteLine($"[VideoOverlay] FilterOpen: {width}x{height} chroma=0x{chroma:X8}");

            // Log fourcc as characters
            char c1 = (char)(chroma & 0xFF);
            char c2 = (char)((chroma >> 8) & 0xFF);
            char c3 = (char)((chroma >> 16) & 0xFF);
            char c4 = (char)((chroma >> 24) & 0xFF);
            Console.Error.WriteLine($"[VideoOverlay] Chroma fourcc: {c1}{c2}{c3}{c4}");

            // Initialize filter state
            FilterState.Initialize(filterPtr, (int)width, (int)height, chroma);

            // Set filter->ops to our operations structure
            filter.Operations = s_filterOpsPtr;

            Console.Error.WriteLine("[VideoOverlay] FilterOpen completed successfully");
            return 0; // VLC_SUCCESS
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VideoOverlay] FilterOpen failed: {ex.Message}");
            Console.Error.WriteLine($"[VideoOverlay] Stack trace: {ex.StackTrace}");
            return -1; // VLC_EGENERIC
        }
    }

    /// <summary>
    /// Video filter frame callback - called by VLC for each video frame.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint FilterVideoCallback(nint filterPtr, nint picturePtr)
    {
        try
        {
            if (picturePtr == nint.Zero)
                return nint.Zero;

            // Read the picture structure
            ref VLCPicture inputPic = ref Unsafe.AsRef<VLCPicture>((void*)picturePtr);

            // Check if we have accessible planes
            if (inputPic.PlaneCount == 0)
            {
                // Input has no plane data - try to allocate output via buffer_new
                ref VLCFilter filterRef = ref Unsafe.AsRef<VLCFilter>((void*)filterPtr);

                if (filterRef.Owner.Callbacks != nint.Zero)
                {
                    ref VLCFilterVideoCallbacks videoCallbacks = ref Unsafe.AsRef<VLCFilterVideoCallbacks>((void*)filterRef.Owner.Callbacks);

                    if (videoCallbacks.BufferNew != nint.Zero)
                    {
                        // Call buffer_new(filter) to allocate output picture
                        var bufferNewFn = (delegate* unmanaged<nint, nint>)videoCallbacks.BufferNew;
                        nint outPicPtr = bufferNewFn(filterPtr);

                        if (outPicPtr != nint.Zero)
                        {
                            ref VLCPicture outPic = ref Unsafe.AsRef<VLCPicture>((void*)outPicPtr);

                            if (outPic.PlaneCount > 0 && outPic.Plane0.Pixels != nint.Zero)
                            {
                                // We have an output picture with plane data - process it
                                VLCCore.PictureCopyProperties(outPicPtr, picturePtr);

                                uint outChroma = outPic.Format.Chroma;
                                ref VLCPlane outPlane0 = ref outPic.Plane0;

                                FilterState.ProcessFrame(
                                    outPlane0.Pixels,
                                    outPlane0.Pitch,
                                    outPlane0.VisiblePitch,
                                    outPlane0.VisibleLines,
                                    outChroma);

                                // Release input, return output
                                return outPicPtr;
                            }
                        }
                    }
                }

                // Still no plane data - pass through
                return picturePtr;
            }

            // Get format info
            uint chroma = inputPic.Format.Chroma;

            // Get plane 0 info for processing
            ref VLCPlane plane0 = ref inputPic.Plane0;

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
                Console.Error.WriteLine($"[VideoOverlay] FilterVideo error: {ex.Message}");
            }
            // Return input picture unchanged on error
            return picturePtr;
        }
    }

    /// <summary>
    /// Video filter close callback via ops->close.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FilterCloseOpsCallback(nint filterPtr)
    {
        try
        {
            Console.Error.WriteLine("[VideoOverlay] FilterClose called");
            FilterState.Cleanup();
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }

    private static void InitializeFilterOps()
    {
        if (s_filterOpsInitialized)
            return;

        // Get callback function pointers
        s_filterVideoCallback = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&FilterVideoCallback;
        s_filterCloseOpsCallback = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FilterCloseOpsCallback;

        // Initialize the operations structure
        s_filterOps = new VLCFilterOperations
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
}
