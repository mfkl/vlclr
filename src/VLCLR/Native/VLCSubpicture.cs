// VLC subpicture structure
// Source: vlc/include/vlc_subpicture.h
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Subpicture structure (subpicture_t from vlc_subpicture.h).
/// A subpicture represents an overlay to be displayed on top of video,
/// such as subtitles or OSD elements.
/// </summary>
/// <remarks>
/// Layout on 64-bit:
/// - i_channel (ssize_t): 8 bytes at offset 0
/// - i_order (int64_t): 8 bytes at offset 8
/// - p_next (pointer): 8 bytes at offset 16
/// - regions (vlc_list): 16 bytes at offset 24
/// - i_start (vlc_tick_t/int64_t): 8 bytes at offset 40
/// - i_stop (vlc_tick_t/int64_t): 8 bytes at offset 48
/// - b_ephemer, b_fade, b_subtitle: 3 bytes at offset 56, then 1 byte padding
/// - i_original_picture_width: 4 bytes at offset 60
/// - i_original_picture_height: 4 bytes at offset 64
/// - i_alpha: 4 bytes at offset 68
/// - 4 bytes padding at offset 72
/// - updater (subpicture_updater_t): 16 bytes at offset 76 (needs 8-byte alignment, so offset 80)
/// - p_private: 8 bytes at offset 96
/// Total: 104 bytes
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 104)]
public struct VLCSubpicture
{
    /// <summary>Subpicture channel ID (i_channel)</summary>
    [FieldOffset(0)]
    public long Channel;

    /// <summary>Increasing unique number for ordering (i_order)</summary>
    [FieldOffset(8)]
    public long Order;

    /// <summary>Next subpicture to be displayed (p_next)</summary>
    [FieldOffset(16)]
    public nint Next;

    /// <summary>Region list prev pointer (regions.prev)</summary>
    [FieldOffset(24)]
    public nint RegionsPrev;

    /// <summary>Region list next pointer (regions.next)</summary>
    [FieldOffset(32)]
    public nint RegionsNext;

    /// <summary>Beginning of display date in microseconds (i_start)</summary>
    [FieldOffset(40)]
    public long Start;

    /// <summary>End of display date in microseconds (i_stop)</summary>
    [FieldOffset(48)]
    public long Stop;

    /// <summary>Display until next subtitle appears (b_ephemer)</summary>
    [FieldOffset(56)]
    public byte IsEphemer;

    /// <summary>Enable fading (b_fade)</summary>
    [FieldOffset(57)]
    public byte IsFade;

    /// <summary>Subtitle with timestamps relative to video (b_subtitle)</summary>
    [FieldOffset(58)]
    public byte IsSubtitle;

    // 1 byte padding at 59

    /// <summary>Original width of the movie (i_original_picture_width)</summary>
    [FieldOffset(60)]
    public uint OriginalPictureWidth;

    /// <summary>Original height of the movie (i_original_picture_height)</summary>
    [FieldOffset(64)]
    public uint OriginalPictureHeight;

    /// <summary>Global transparency (i_alpha)</summary>
    [FieldOffset(68)]
    public int Alpha;

    // 4 bytes padding at 72-75 for 8-byte alignment

    /// <summary>Updater private data pointer (updater.sys)</summary>
    [FieldOffset(80)]
    public nint UpdaterSys;

    /// <summary>Updater operations pointer (updater.ops)</summary>
    [FieldOffset(88)]
    public nint UpdaterOps;

    /// <summary>Reserved to the core (p_private)</summary>
    [FieldOffset(96)]
    public nint Private;
}

/// <summary>
/// Subpicture updater structure (subpicture_updater_t from vlc_subpicture.h).
/// Contains private data and operations for dynamic subpictures.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCSubpictureUpdater
{
    /// <summary>Private data for the updater</summary>
    public nint Sys;

    /// <summary>Pointer to updater operations table</summary>
    public nint Ops;
}

/// <summary>
/// Subpicture updater operations (vlc_spu_updater_ops from vlc_subpicture.h).
/// Virtual table for subpicture updater callbacks.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VLCSubpictureUpdaterOps
{
    /// <summary>Update callback - creates regions for current video format</summary>
    public nint Update;

    /// <summary>Destroy callback - cleanup private data</summary>
    public nint Destroy;
}

/// <summary>
/// VLC tick type - represents time in microseconds
/// </summary>
public static class VLCTick
{
    /// <summary>Invalid tick value</summary>
    public const long Invalid = long.MinValue;

    /// <summary>One second in microseconds</summary>
    public const long Second = 1_000_000;

    /// <summary>One millisecond in microseconds</summary>
    public const long Millisecond = 1_000;
}
