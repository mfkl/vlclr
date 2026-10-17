// VLC object header structure
// Source: vlc/include/vlc_objects.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// VLC object header - first member of all VLC objects
/// This is struct vlc_object_t from vlc_objects.h.
/// Layout and size come from <see cref="Interop.ObjectHeader"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.ObjectHeader.MaxSizeOf)]
public struct VLCObjectHeader
{
    /// <summary>Logger pointer (struct vlc_logger*)</summary>
    [FieldOffset(Interop.ObjectHeader.Offsets.Logger)]
    public nint Logger;

    /// <summary>Private/Object union (struct vlc_object_internals* or struct vlc_object_marker*)</summary>
    [FieldOffset(Interop.ObjectHeader.Offsets.Priv)]
    public nint PrivOrObj;

    /// <summary>No interact flag</summary>
    [FieldOffset(Interop.ObjectHeader.Offsets.NoInteract)]
    public byte NoInteract;

    /// <summary>Module probe force flag</summary>
    [FieldOffset(Interop.ObjectHeader.Offsets.Force)]
    public byte Force;
}
