// VLC object header structure
// Source: vlc/include/vlc_objects.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// VLC object header - first member of all VLC objects
/// This is struct vlc_object_t from vlc_objects.h
/// Size on 64-bit: 24 bytes
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct VLCObjectHeader
{
    /// <summary>Logger pointer (struct vlc_logger*)</summary>
    [FieldOffset(0)]
    public nint Logger;

    /// <summary>Private/Object union (struct vlc_object_internals* or struct vlc_object_marker*)</summary>
    [FieldOffset(8)]
    public nint PrivOrObj;

    /// <summary>No interact flag</summary>
    [FieldOffset(16)]
    public byte NoInteract;

    /// <summary>Module probe force flag</summary>
    [FieldOffset(17)]
    public byte Force;

    // Implicit padding from offset 18 to 24 (6 bytes) due to Size = 24
}
