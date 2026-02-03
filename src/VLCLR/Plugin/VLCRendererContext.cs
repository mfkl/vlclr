// VLC Renderer Context wrapper
// Provides safe access to text renderer information
// VLC Version: 4.0.6

namespace VLCLR.Plugin;

/// <summary>
/// Provides safe access to VLC text renderer context information.
/// Wraps the native filter_t pointer and exposes commonly needed properties.
/// </summary>
public readonly struct VLCRendererContext
{
    private readonly nint _filterPtr;

    /// <summary>
    /// Creates a renderer context from a native filter pointer.
    /// </summary>
    /// <param name="filterPtr">Pointer to filter_t</param>
    public VLCRendererContext(nint filterPtr)
    {
        _filterPtr = filterPtr;
    }

    /// <summary>
    /// Gets a logger bound to this filter's VLC object.
    /// </summary>
    public VLCLogger Logger => new(_filterPtr);

    /// <summary>
    /// Gets the native filter pointer for advanced use.
    /// </summary>
    public nint NativePtr => _filterPtr;

    /// <summary>
    /// Gets whether this context is valid (has a non-null filter pointer).
    /// </summary>
    public bool IsValid => _filterPtr != 0;
}
