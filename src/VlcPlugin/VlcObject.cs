using VlcPlugin.Native;

namespace VlcPlugin;

/// <summary>
/// Provides object management functions for navigating the VLC object hierarchy.
/// VLC uses a tree structure of objects where each object has a parent.
/// </summary>
public sealed class VlcObject
{
    private readonly nint _handle;

    /// <summary>
    /// Creates a VlcObject wrapper around a native VLC object pointer.
    /// </summary>
    /// <param name="handle">Native pointer to vlc_object_t</param>
    public VlcObject(nint handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Gets the native handle to the VLC object.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether this object handle is valid (non-null).
    /// </summary>
    public bool IsValid => _handle != nint.Zero;

    /// <summary>
    /// Gets the parent of this VLC object.
    /// </summary>
    /// <returns>The parent object, or null if this is the root object</returns>
    public VlcObject? GetParent()
    {
        if (_handle == nint.Zero)
            return null;

        nint parent = VlcBridge.ObjectParent(_handle);
        return parent != nint.Zero ? new VlcObject(parent) : null;
    }

    /// <summary>
    /// Gets the type name of this VLC object.
    /// Common type names include: "libvlc", "interface", "audio output", "video output", etc.
    /// </summary>
    /// <returns>The type name string, or null on error</returns>
    public string? GetTypeName()
    {
        if (_handle == nint.Zero)
            return null;

        nint namePtr = VlcBridge.ObjectTypename(_handle);
        if (namePtr == nint.Zero)
            return null;

        // The string is owned by VLC, so we just need to marshal it
        return VlcInterop.PtrToStringUtf8(namePtr);
    }

    /// <summary>
    /// Gets the root object (libvlc instance) by traversing up the parent chain.
    /// </summary>
    /// <returns>The root object, or this object if it has no parent</returns>
    public VlcObject GetRoot()
    {
        var current = this;
        VlcObject? parent;

        while ((parent = current.GetParent()) != null)
        {
            current = parent;
        }

        return current;
    }

    /// <summary>
    /// Static helper to get the parent of a VLC object.
    /// </summary>
    /// <param name="obj">Native pointer to vlc_object_t</param>
    /// <returns>Native pointer to parent vlc_object_t, or IntPtr.Zero if none</returns>
    public static nint GetParent(nint obj)
    {
        if (obj == nint.Zero)
            return nint.Zero;

        return VlcBridge.ObjectParent(obj);
    }

    /// <summary>
    /// Static helper to get the type name of a VLC object.
    /// </summary>
    /// <param name="obj">Native pointer to vlc_object_t</param>
    /// <returns>Type name string, or null on error</returns>
    public static string? GetTypeName(nint obj)
    {
        if (obj == nint.Zero)
            return null;

        nint namePtr = VlcBridge.ObjectTypename(obj);
        if (namePtr == nint.Zero)
            return null;

        return VlcInterop.PtrToStringUtf8(namePtr);
    }
}
