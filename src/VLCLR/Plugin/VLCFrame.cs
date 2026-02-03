// VLC Frame wrapper
// Provides safe access to video frame data
// VLC Version: 4.0.6

using System.Runtime.CompilerServices;
using VLCLR.Native;

namespace VLCLR.Plugin;

/// <summary>
/// Provides safe access to video frame data.
/// This is a ref struct to ensure stack allocation and zero GC pressure in the hot path.
/// </summary>
public readonly ref struct VLCFrame
{
    private readonly nint _picturePtr;
    private readonly VLCFilterContext _context;

    /// <summary>
    /// Creates a frame wrapper from a native picture pointer.
    /// </summary>
    /// <param name="picturePtr">Pointer to picture_t</param>
    /// <param name="context">The filter context</param>
    public VLCFrame(nint picturePtr, VLCFilterContext context)
    {
        _picturePtr = picturePtr;
        _context = context;
    }

    /// <summary>
    /// Gets the native picture pointer for advanced use.
    /// </summary>
    public nint NativePtr => _picturePtr;

    /// <summary>
    /// Gets whether this frame is valid (has a non-null picture pointer).
    /// </summary>
    public bool IsValid => _picturePtr != 0;

    /// <summary>
    /// Gets the filter context associated with this frame.
    /// </summary>
    public VLCFilterContext Context => _context;

    /// <summary>
    /// Gets the pointer to plane 0 pixels (the main/Y plane).
    /// </summary>
    public nint Pixels => GetPlane(0).Pixels;

    /// <summary>
    /// Gets the pitch (bytes per line) for plane 0.
    /// </summary>
    public int Pitch => GetPlane(0).Pitch;

    /// <summary>
    /// Gets the visible pitch (bytes per visible line) for plane 0.
    /// </summary>
    public int VisiblePitch => GetPlane(0).VisiblePitch;

    /// <summary>
    /// Gets the number of visible lines for plane 0.
    /// </summary>
    public int VisibleLines => GetPlane(0).VisibleLines;

    /// <summary>
    /// Gets the chroma (pixel format) as a FourCC value.
    /// </summary>
    public uint Chroma => GetPicture().Format.Chroma;

    /// <summary>
    /// Gets the frame width.
    /// </summary>
    public int Width => (int)GetPicture().Format.VisibleWidth;

    /// <summary>
    /// Gets the frame height.
    /// </summary>
    public int Height => (int)GetPicture().Format.VisibleHeight;

    /// <summary>
    /// Gets the number of planes in this frame.
    /// </summary>
    public int PlaneCount => GetPicture().PlaneCount;

    /// <summary>
    /// Gets the display timestamp (PTS) for this frame.
    /// </summary>
    public long Date => GetPicture().Date;

    /// <summary>
    /// Gets a span over the pixel data for a specific plane.
    /// </summary>
    /// <param name="planeIndex">The plane index (0-4)</param>
    /// <returns>A span of bytes covering the plane's pixel data</returns>
    public unsafe Span<byte> GetPlaneSpan(int planeIndex)
    {
        var plane = GetPlane(planeIndex);
        if (plane.Pixels == 0) return Span<byte>.Empty;

        // Calculate total size: pitch * lines
        int size = plane.Pitch * plane.Lines;
        if (size <= 0) return Span<byte>.Empty;

        return new Span<byte>((void*)plane.Pixels, size);
    }

    /// <summary>
    /// Gets a span over the visible pixel data for a specific plane.
    /// This excludes any padding at the end of each line.
    /// </summary>
    /// <param name="planeIndex">The plane index (0-4)</param>
    /// <returns>A span of bytes covering the plane's visible pixel data</returns>
    public unsafe Span<byte> GetVisiblePlaneSpan(int planeIndex)
    {
        var plane = GetPlane(planeIndex);
        if (plane.Pixels == 0) return Span<byte>.Empty;

        // Calculate visible size: visiblePitch * visibleLines
        int size = plane.VisiblePitch * plane.VisibleLines;
        if (size <= 0) return Span<byte>.Empty;

        return new Span<byte>((void*)plane.Pixels, size);
    }

    /// <summary>
    /// Gets a span over the entire plane 0 pixel data.
    /// </summary>
    public Span<byte> PixelSpan => GetPlaneSpan(0);

    /// <summary>
    /// Gets the plane data for a specific plane index.
    /// </summary>
    /// <param name="planeIndex">The plane index (0-4)</param>
    /// <returns>The plane structure</returns>
    public VLCPlane GetPlane(int planeIndex)
    {
        return GetPicture().GetPlane(planeIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe VLCPicture GetPicture()
    {
        return *(VLCPicture*)_picturePtr;
    }
}
