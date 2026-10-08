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

/// <summary>
/// VLC 3.x vout display control queries, numbered as on Windows. Other targets
/// lack the two fullscreen queries, so convert what VLC passes with
/// <see cref="VLCVoutDisplayControls.FromNative"/>.
/// </summary>
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

/// <summary>Reads VLC 3's platform-dependent display control ABI.</summary>
public static unsafe class VLCVoutDisplayControls
{
    // VOUT_DISPLAY_CHANGE_FULLSCREEN and VOUT_DISPLAY_CHANGE_WINDOW_STATE exist
    // only in Windows (and OS/2) builds, after VOUT_DISPLAY_RESET_PICTURES.
    private const int FirstShiftedQuery = 2;
    private const int WindowsOnlyQueries = 2;

    // vout_display_cfg_t starts with a bool is_fullscreen on Windows only.
    private static int ConfigurationShift => OperatingSystem.IsWindows() ? 8 : 0;

    /// <summary>The query VLC passed to a display's control callback.</summary>
    public static VLCVoutDisplayControl FromNative(int query) =>
        (VLCVoutDisplayControl)(!OperatingSystem.IsWindows() && query >= FirstShiftedQuery ? query + WindowsOnlyQueries : query);

    /// <summary><c>cfg->is_display_filled</c> of a <c>const vout_display_cfg_t *</c>.</summary>
    public static bool IsDisplayFilled(nint configuration) =>
        *((byte*)configuration + 32 + ConfigurationShift) != 0;

    /// <summary><c>cfg->zoom.num</c> and <c>cfg->zoom.den</c> of a <c>const vout_display_cfg_t *</c>.</summary>
    public static (int Numerator, int Denominator) Zoom(nint configuration) =>
        (*(int*)((byte*)configuration + 36 + ConfigurationShift), *(int*)((byte*)configuration + 40 + ConfigurationShift));
}
