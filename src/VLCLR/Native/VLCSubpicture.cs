// VLC subpicture structure
// Source: vlc-3.0.24-beta1/sdk/include/vlc/plugins/vlc_subpicture.h
// VLC Version: 3.0.24-beta1

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Subpicture structure (subpicture_t from vlc_subpicture.h).
/// Size on 64-bit: 104 bytes.
///
/// VLC 3 has p_region (single pointer), b_absolute, and the updater struct.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 104)]
public struct VLCSubpicture
{
    /// <summary>Subpicture channel ID (i_channel)</summary>
    [FieldOffset(0)]
    public int Channel;

    // 4 bytes padding

    /// <summary>Increasing unique number for ordering (i_order)</summary>
    [FieldOffset(8)]
    public long Order;

    /// <summary>Next subpicture to be displayed (p_next)</summary>
    [FieldOffset(16)]
    public nint Next;

    /// <summary>Region list composing this subtitle (p_region)</summary>
    [FieldOffset(24)]
    public nint Region;

    /// <summary>Beginning of display date (i_start)</summary>
    [FieldOffset(32)]
    public long Start;

    /// <summary>End of display date (i_stop)</summary>
    [FieldOffset(40)]
    public long Stop;

    /// <summary>Display until next subtitle appears (b_ephemer)</summary>
    [FieldOffset(48)]
    public byte IsEphemer;

    /// <summary>Enable fading (b_fade)</summary>
    [FieldOffset(49)]
    public byte IsFade;

    /// <summary>Subtitle with timestamps relative to video (b_subtitle)</summary>
    [FieldOffset(50)]
    public byte IsSubtitle;

    /// <summary>Position is absolute (b_absolute)</summary>
    [FieldOffset(51)]
    public byte IsAbsolute;

    /// <summary>Original width of the movie (i_original_picture_width)</summary>
    [FieldOffset(52)]
    public int OriginalPictureWidth;

    /// <summary>Original height of the movie (i_original_picture_height)</summary>
    [FieldOffset(56)]
    public int OriginalPictureHeight;

    /// <summary>Global transparency (i_alpha)</summary>
    [FieldOffset(60)]
    public int Alpha;

    /// <summary>Updater structure start (subpicture_updater_t)</summary>
    [FieldOffset(64)]
    public VLCSubpictureUpdater Updater;

    /// <summary>Reserved to the core (p_private)</summary>
    [FieldOffset(96)]
    public nint Private;
}

/// <summary>
/// Subpicture updater structure (subpicture_updater_t from vlc_subpicture.h).
/// VLC 3 has: pf_validate, pf_update, pf_destroy, p_sys (4 function pointers + 1 pointer).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCSubpictureUpdater
{
    public nint Validate;
    public nint Update;
    public nint Destroy;
    public nint Sys;
}

/// <summary>
/// VLC tick type - represents time in microseconds
/// </summary>
public static class VLCTick
{
    public const long Invalid = long.MinValue;
    public const long Second = 1_000_000;
    public const long Millisecond = 1_000;
}
