// Exact legacy playlist layouts from the shipped 3.0.24-beta1 SDK headers:
// vlc_common.h:428-469, vlc_arrays.h:166-172, vlc_playlist.h:125-164.
// These are internal ABI readers, never a public raw-structure API.
using System.Runtime.InteropServices;

namespace VLCLR.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct VLCPlaylistItemArrayNative
{
    public int Capacity;
    public int Count;
    public nint Elements;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VLCPlaylistItemNative
{
    public nint Input;
    public nint Children;
    public nint Parent;
    public int ChildCount;
    public uint PlayCount;
    public int Id;
    public byte Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VLCPlaylistNative
{
    public VLCObjectHeader Object;
    public VLCPlaylistItemArrayNative Items;
    public VLCPlaylistItemArrayNative Current;
    public int CurrentIndex;
    public VLCPlaylistItemNative Root;
    public nint PlayingRoot;
    public nint MediaLibraryRoot;
}
