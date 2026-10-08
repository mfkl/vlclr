using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>VLC 3.x vout_display_info_t.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVoutDisplayInfo
{
    public byte IsSlow;
    public byte HasDoubleClick;
    public byte NeedsHideMouse;
    public byte HasPicturesInvalid;
    public nint SubpictureChromas;
}

/// <summary>VLC 3.x vout_display_owner_t.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVoutDisplayOwner
{
    public nint Sys;
    public nint Event;
    public nint WindowNew;
    public nint WindowDel;
}

/// <summary>ABI-accurate VLC 3.x vout_display_t for 64-bit callbacks.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCVoutDisplay
{
    public VLCObjectHeader Object;
    public nint Module;
    public nint Configuration;
    public VLCVideoFormat Source;
    public VLCVideoFormat Format;
    public VLCVoutDisplayInfo Info;
    public nint Pool;
    public nint Prepare;
    public nint Display;
    public nint Control;
    public nint Manage;
    public nint Sys;
    public VLCVoutDisplayOwner Owner;
}

/// <summary>VLC 3.x vout display control queries on Windows.</summary>
public enum VLCVoutDisplayControl
{
    HideMouse = 0,
    ResetPictures = 1,
    ChangeFullscreen = 2,
    ChangeWindowState = 3,
    ChangeDisplaySize = 4,
    ChangeDisplayFilled = 5,
    ChangeZoom = 6,
    ChangeSourceAspect = 7,
    ChangeSourceCrop = 8,
    ChangeViewpoint = 9,
}
