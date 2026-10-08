using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>A single plane in VLC 3's <c>picture_resource_t</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCPictureResourcePlane
{
    /// <summary>
    /// Borrowed pixel storage. The creator retains ownership and must keep it
    /// valid until the picture destroy callback runs.
    /// </summary>
    public nint Pixels;

    public int Lines;
    public int Pitch;
}

/// <summary>
/// Win64 layout of VLC 3's <c>picture_resource_t</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VLCPictureResource
{
    /// <summary>
    /// System allocation transferred to the returned picture on successful
    /// PictureNewFromResource. On failure it remains owned by the caller.
    /// </summary>
    public nint Sys;

    /// <summary>
    /// Callback retained by the picture and invoked exactly once when its last
    /// reference is released. The callback owns cleanup of Sys and the picture
    /// allocation. If null, VLC frees Sys and the picture with its allocator.
    /// </summary>
    public delegate* unmanaged[Cdecl]<VLCPicture*, void> Destroy;

    public VLCPictureResourcePlane Plane0;
    public VLCPictureResourcePlane Plane1;
    public VLCPictureResourcePlane Plane2;
    public VLCPictureResourcePlane Plane3;
    public VLCPictureResourcePlane Plane4;
}

/// <summary>
/// Win64 layout of VLC 3's <c>picture_pool_configuration_t</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct VLCPicturePoolConfiguration
{
    public uint PictureCount;

    private uint _padding;

    /// <summary>
    /// Borrowed pointer to an array of PictureCount picture pointers. VLC copies
    /// the pointers during PicturePoolNewExtended; on success the pool consumes
    /// the picture references, while on failure none are released.
    /// </summary>
    public VLCPicture** Pictures;

    /// <summary>
    /// Optional callback retained by the pool. It borrows the picture for the
    /// duration of the call and returns a VLC status code.
    /// </summary>
    public delegate* unmanaged[Cdecl]<VLCPicture*, int> Lock;

    /// <summary>
    /// Optional callback retained by the pool. It borrows the picture for the
    /// duration of the call and must not release the pool-owned reference.
    /// </summary>
    public delegate* unmanaged[Cdecl]<VLCPicture*, void> Unlock;
}
