// VLC object header structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_common.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// VLC object header - vlc_common_members from vlc_common.h
/// Size on 64-bit: 40 bytes
/// Layout: object_type(8) + header(8) + flags(4) + force(1) + pad(3) + libvlc(8) + parent(8)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCObjectHeader
{
    /// <summary>Object type name (const char*)</summary>
    public nint ObjectType;

    /// <summary>Log messages header (char*)</summary>
    public nint Header;

    /// <summary>Object flags (int)</summary>
    public int Flags;

    /// <summary>Module probe force flag (bool)</summary>
    public byte Force;

    /// <summary>LibVLC instance pointer (libvlc_int_t*)</summary>
    public nint LibVLC;

    /// <summary>Parent object pointer (vlc_object_t*)</summary>
    public nint Parent;
}
