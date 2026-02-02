// VLC text renderer operations structure
// Source: vlc/include/vlc_filter.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Text renderer filter operations structure (vlc_filter_operations from vlc_filter.h).
/// This is the same struct as VLCFilterOperations but with text renderer-specific documentation.
/// Text renderers use the first union member for the render callback.
/// </summary>
/// <remarks>
/// The struct is identical to VLCFilterOperations because VLC uses a single vlc_filter_operations
/// struct for all filter types, with different callbacks populated based on capability.
///
/// Text renderer signature:
/// <code>
/// subpicture_region_t* (*render)(filter_t*, const subpicture_region_t*, const vlc_fourcc_t*)
/// </code>
///
/// The render callback receives:
/// - filter: The filter instance
/// - region: Input region containing text_segment_t chain with text and styling
/// - chroma_list: NULL-terminated array of supported output chromas (RGBA, etc.)
///
/// Returns:
/// - A new subpicture_region_t with rendered picture, or NULL on failure
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct VLCTextRendererOperations
{
    /// <summary>
    /// Render text to picture (render).
    /// Signature: subpicture_region_t* (*render)(filter_t*, const subpicture_region_t*, const vlc_fourcc_t*)
    /// </summary>
    /// <remarks>
    /// The text renderer receives a region containing:
    /// - p_text: linked list of text_segment_t with text and optional styles
    /// - fmt: format hints (may be used for sizing)
    /// - i_align: alignment flags
    /// - i_max_width/i_max_height: maximum render dimensions
    ///
    /// The renderer should:
    /// 1. Parse text segments from region->p_text
    /// 2. Apply text styling from each segment's style pointer
    /// 3. Render styled text to a picture_t (typically RGBA)
    /// 4. Create and return a new subpicture_region_t containing the rendered picture
    /// </remarks>
    public nint Render;

    /// <summary>
    /// Drain callback (unused for text renderers).
    /// </summary>
    public nint Drain;

    /// <summary>
    /// Flush callback (optional, unused for text renderers).
    /// </summary>
    public nint Flush;

    /// <summary>
    /// Change viewpoint callback (unused for text renderers).
    /// </summary>
    public nint ChangeViewpoint;

    /// <summary>
    /// Video mouse callback (unused for text renderers).
    /// </summary>
    public nint VideoMouse;

    /// <summary>
    /// Close callback - release filter resources.
    /// Signature: void (*close)(filter_t*)
    /// </summary>
    public nint Close;
}
