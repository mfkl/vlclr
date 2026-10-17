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
/// Layout and size come from <see cref="Interop.Subpicture"/>; every offset is identical on all ABIs.
/// The embedded regions list and updater are exposed as their pointer-sized members.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Interop.Subpicture.MaxSizeOf)]
public struct VLCSubpicture
{
    /// <summary>Subpicture channel ID (i_channel)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Channel)]
    public long Channel;

    /// <summary>Increasing unique number for ordering (i_order)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Order)]
    public long Order;

    /// <summary>Next subpicture to be displayed (p_next)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Next)]
    public nint Next;

    /// <summary>Region list prev pointer (regions.prev)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Regions + Interop.List.Offsets.Prev)]
    public nint RegionsPrev;

    /// <summary>Region list next pointer (regions.next)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Regions + Interop.List.Offsets.Next)]
    public nint RegionsNext;

    /// <summary>Beginning of display date in microseconds (i_start)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Start)]
    public long Start;

    /// <summary>End of display date in microseconds (i_stop)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Stop)]
    public long Stop;

    /// <summary>Display until next subtitle appears (b_ephemer)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Ephemer)]
    public byte IsEphemer;

    /// <summary>Enable fading (b_fade)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Fade)]
    public byte IsFade;

    /// <summary>Subtitle with timestamps relative to video (b_subtitle)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Subtitle)]
    public byte IsSubtitle;

    /// <summary>Original width of the movie (i_original_picture_width)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.OriginalPictureWidth)]
    public uint OriginalPictureWidth;

    /// <summary>Original height of the movie (i_original_picture_height)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.OriginalPictureHeight)]
    public uint OriginalPictureHeight;

    /// <summary>Global transparency (i_alpha)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Alpha)]
    public int Alpha;

    /// <summary>Updater private data pointer (updater.sys)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Updater + Interop.SubpictureUpdater.Offsets.Sys)]
    public nint UpdaterSys;

    /// <summary>Updater operations pointer (updater.ops)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Updater + Interop.SubpictureUpdater.Offsets.Ops)]
    public nint UpdaterOps;

    /// <summary>Reserved to the core (p_private)</summary>
    [FieldOffset(Interop.Subpicture.Offsets.Private)]
    public nint Private;
}

/// <summary>
/// Subpicture updater structure (subpicture_updater_t from vlc_subpicture.h).
/// Contains private data and operations for dynamic subpictures.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.SubpictureUpdater.MaxSizeOf)]
public struct VLCSubpictureUpdater
{
    /// <summary>Private data for the updater</summary>
    [FieldOffset(Interop.SubpictureUpdater.Offsets.Sys)]
    public nint Sys;

    /// <summary>Pointer to updater operations table</summary>
    [FieldOffset(Interop.SubpictureUpdater.Offsets.Ops)]
    public nint Ops;
}

/// <summary>
/// Subpicture updater operations (vlc_spu_updater_ops from vlc_subpicture.h).
/// Virtual table for subpicture updater callbacks.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Interop.SpuUpdaterOps.MaxSizeOf)]
public struct VLCSubpictureUpdaterOps
{
    /// <summary>Update callback - creates regions for current video format</summary>
    [FieldOffset(Interop.SpuUpdaterOps.Offsets.Update)]
    public nint Update;

    /// <summary>Destroy callback - cleanup private data</summary>
    [FieldOffset(Interop.SpuUpdaterOps.Offsets.Destroy)]
    public nint Destroy;
}

/// <summary>
/// VLC tick type - represents time in microseconds
/// </summary>
public static class VLCTick
{
    /// <summary>Invalid tick value (VLC_TICK_INVALID, 0). Not a negative sentinel.</summary>
    public const long Invalid = Interop.VlcConfig.TickInvalid;

    /// <summary>First valid tick (VLC_TICK_0, 1).</summary>
    public const long Zero = Interop.VlcConfig.Tick0;

    /// <summary>One second in ticks (CLOCK_FREQ, microseconds).</summary>
    public const long Second = Interop.VlcConfig.ClockFreq;

    /// <summary>One millisecond in ticks (CLOCK_FREQ / 1000).</summary>
    public const long Millisecond = Interop.VlcConfig.ClockFreq / 1000;
}
