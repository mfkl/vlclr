using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Native;

/// <summary>
/// Direct P/Invoke declarations for libvlccore functions.
/// These call VLC's native library directly without the C glue layer.
/// </summary>
public static partial class VLCCore
{
    private const string LibraryName = "libvlccore";

    /// <summary>Returns VLC's monotonic system clock in microseconds.</summary>
    public static long TickNow() => Gen.VlcTick.Now();

    #region Logging

    /// <summary>
    /// Log a message through VLC's logging system.
    /// vlc_object_Log is variadic; it is called with no variable arguments,
    /// which every ABI passes the same way, so <paramref name="format"/> must
    /// hold no conversions: escape '%' as "%%" (see <see cref="VLCLogger.ToLiteralFormat"/>).
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="type">Log type: 0=INFO, 1=ERR, 2=WARN, 3=DBG</param>
    /// <param name="module">Module name (e.g., "dotnet")</param>
    /// <param name="file">Source file name (can be empty)</param>
    /// <param name="line">Source line number (can be 0)</param>
    /// <param name="func">Function name (can be empty)</param>
    /// <param name="format">The message, with '%' escaped as "%%"</param>
    // Hand-written: vlc_object_Log is variadic, so the generator skipped it
    // (see Native/Interop/cimport-report.md).
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_Log", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void Log(
        nint obj,
        int type,
        string module,
        string file,
        uint line,
        string func,
        string format);

    #endregion

    #region Variables

    /// <summary>
    /// Create a VLC variable.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int VarCreate(nint obj, string name, int type)
    {
        Span<byte> buffer = stackalloc byte[Utf8StringMarshaller.ManagedToUnmanagedIn.BufferSize];
        scoped Utf8StringMarshaller.ManagedToUnmanagedIn utf8 = new();
        try
        {
            utf8.FromManaged(name, buffer);
            return Gen.Var.Create((Gen.ObjectHeader*)obj, utf8.ToUnmanaged(), type);
        }
        finally { utf8.Free(); }
    }

    /// <summary>
    /// Destroy a VLC variable.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    public static unsafe void VarDestroy(nint obj, string name)
    {
        Span<byte> buffer = stackalloc byte[Utf8StringMarshaller.ManagedToUnmanagedIn.BufferSize];
        scoped Utf8StringMarshaller.ManagedToUnmanagedIn utf8 = new();
        try
        {
            utf8.FromManaged(name, buffer);
            Gen.Var.Destroy((Gen.ObjectHeader*)obj, utf8.ToUnmanaged());
        }
        finally { utf8.Free(); }
    }

    /// <summary>
    /// Set a variable value with type checking.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <param name="value">Value to set (passed as vlc_value_t)</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int VarSetChecked(nint obj, string name, int type, VLCValueNative value)
    {
        Span<byte> buffer = stackalloc byte[Utf8StringMarshaller.ManagedToUnmanagedIn.BufferSize];
        scoped Utf8StringMarshaller.ManagedToUnmanagedIn utf8 = new();
        try
        {
            utf8.FromManaged(name, buffer);
            return Gen.Var.SetChecked((Gen.ObjectHeader*)obj, utf8.ToUnmanaged(), type,
                Unsafe.As<VLCValueNative, Gen.Value>(ref value));
        }
        finally { utf8.Free(); }
    }

    /// <summary>
    /// Get a variable value with type checking.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <param name="value">Pointer to receive value</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int VarGetChecked(nint obj, string name, int type, out VLCValueNative value)
    {
        Gen.Value native = default;
        Span<byte> buffer = stackalloc byte[Utf8StringMarshaller.ManagedToUnmanagedIn.BufferSize];
        scoped Utf8StringMarshaller.ManagedToUnmanagedIn utf8 = new();
        try
        {
            utf8.FromManaged(name, buffer);
            int result = Gen.Var.GetChecked((Gen.ObjectHeader*)obj, utf8.ToUnmanaged(), type, &native);
            value = Unsafe.As<Gen.Value, VLCValueNative>(ref native);
            return result;
        }
        finally { utf8.Free(); }
    }

    /// <summary>
    /// Resolves a variable from the object, its parents, or the module configuration.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t.</param>
    /// <param name="name">Configuration/variable name.</param>
    /// <param name="type">Expected VLC_VAR_* type.</param>
    /// <param name="value">Receives the inherited value.</param>
    /// <returns>0 on success, a VLC error code otherwise.</returns>
    public static unsafe int VarInherit(nint obj, string name, int type, out VLCValueNative value)
    {
        Gen.Value native = default;
        Span<byte> buffer = stackalloc byte[Utf8StringMarshaller.ManagedToUnmanagedIn.BufferSize];
        scoped Utf8StringMarshaller.ManagedToUnmanagedIn utf8 = new();
        try
        {
            utf8.FromManaged(name, buffer);
            int result = Gen.Var.Inherit((Gen.ObjectHeader*)obj, utf8.ToUnmanaged(), type, &native);
            value = Unsafe.As<Gen.Value, VLCValueNative>(ref native);
            return result;
        }
        finally { utf8.Free(); }
    }

    #endregion

    #region Interface/Playlist Access

    /// <summary>
    /// Get the main playlist from an interface.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <returns>Pointer to vlc_playlist_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint IntfGetMainPlaylist(nint intf)
        => (nint)Gen.VlcIntf.GetMainPlaylist((Gen.IntfThread*)intf);

    /// <summary>
    /// Get the player from a playlist.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Pointer to vlc_player_t</returns>
    public static unsafe nint PlaylistGetPlayer(nint playlist)
        => (nint)Gen.VlcPlaylist.GetPlayer((Gen.Playlist*)playlist);

    #endregion

    #region Player Lock/Unlock

    /// <summary>
    /// Lock the player. Must be called before most player operations.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    public static unsafe void PlayerLock(nint player)
        => Gen.VlcPlayer.Lock((Gen.Player*)player);

    /// <summary>
    /// Unlock the player. Must be called after player operations.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    public static unsafe void PlayerUnlock(nint player)
        => Gen.VlcPlayer.Unlock((Gen.Player*)player);

    #endregion

    #region Player State Queries

    /// <summary>
    /// Get the current player state.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Current player state enum value</returns>
    public static unsafe int PlayerGetState(nint player)
        => (int)Gen.VlcPlayer.GetState((Gen.Player*)player);

    /// <summary>
    /// Get the current playback time.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Current time in VLC ticks (microseconds), or VLC_TICK_INVALID if not playing</returns>
    public static unsafe long PlayerGetTime(nint player)
        => Gen.VlcPlayer.GetTime((Gen.Player*)player);

    /// <summary>
    /// Get the total length of the current media.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Total length in VLC ticks (microseconds), or VLC_TICK_INVALID if unknown</returns>
    public static unsafe long PlayerGetLength(nint player)
        => Gen.VlcPlayer.GetLength((Gen.Player*)player);

    /// <summary>
    /// Get the current playback position as a ratio.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Position as a ratio [0.0, 1.0], or -1.0 if not playing</returns>
    public static unsafe double PlayerGetPosition(nint player)
        => Gen.VlcPlayer.GetPosition((Gen.Player*)player);

    /// <summary>
    /// Get the current player capabilities.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Bitfield of capabilities (VLC_PLAYER_CAP_*)</returns>
    public static unsafe int PlayerGetCapabilities(nint player)
        => Gen.VlcPlayer.GetCapabilities((Gen.Player*)player);

    #endregion

    #region Player Playback Control

    /// <summary>
    /// Pause the player.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    public static unsafe void PlayerPause(nint player)
        => Gen.VlcPlayer.Pause((Gen.Player*)player);

    /// <summary>
    /// Resume the player.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    public static unsafe void PlayerResume(nint player)
        => Gen.VlcPlayer.Resume((Gen.Player*)player);

    /// <summary>
    /// Seek to a specific time.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="time">Time in VLC ticks (microseconds)</param>
    /// <param name="speed">Seek precision (0=precise, 1=fast)</param>
    /// <param name="whence">Seek reference (0=absolute, 1=relative)</param>
    public static unsafe void PlayerSeekByTime(nint player, long time, int speed, int whence)
        => Gen.VlcPlayer.SeekByTime((Gen.Player*)player, time,
            (Gen.PlayerSeekSpeed)speed, (Gen.PlayerWhence)whence);

    /// <summary>
    /// Seek to a specific position.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="position">Position as a ratio [0.0, 1.0]</param>
    /// <param name="speed">Seek precision (0=precise, 1=fast)</param>
    /// <param name="whence">Seek reference (0=absolute, 1=relative)</param>
    public static unsafe void PlayerSeekByPos(nint player, double position, int speed, int whence)
        => Gen.VlcPlayer.SeekByPos((Gen.Player*)player, position,
            (Gen.PlayerSeekSpeed)speed, (Gen.PlayerWhence)whence);

    #endregion

    #region Player Listener Management

    /// <summary>
    /// Add a player listener.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="cbs">Pointer to vlc_player_cbs structure</param>
    /// <param name="cbsData">User data passed to callbacks</param>
    /// <returns>Listener ID, or IntPtr.Zero on failure</returns>
    public static unsafe nint PlayerAddListener(nint player, nint cbs, nint cbsData)
        => (nint)Gen.VlcPlayer.AddListener((Gen.Player*)player, (Gen.PlayerCbs*)cbs, (void*)cbsData);

    /// <summary>
    /// Remove a player listener.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="listenerId">Listener ID from PlayerAddListener</param>
    public static unsafe void PlayerRemoveListener(nint player, nint listenerId)
        => Gen.VlcPlayer.RemoveListener((Gen.Player*)player, (Gen.PlayerListenerId*)listenerId);

    #endregion

    #region Playlist Lock/Unlock

    /// <summary>
    /// Lock the playlist. Must be called before most playlist operations.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    public static unsafe void PlaylistLock(nint playlist)
        => Gen.VlcPlaylist.Lock((Gen.Playlist*)playlist);

    /// <summary>
    /// Unlock the playlist. Must be called after playlist operations.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    public static unsafe void PlaylistUnlock(nint playlist)
        => Gen.VlcPlaylist.Unlock((Gen.Playlist*)playlist);

    #endregion

    #region Playlist Playback Control

    /// <summary>
    /// Start playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int PlaylistStart(nint playlist)
        => Gen.VlcPlaylist.Start((Gen.Playlist*)playlist);

    /// <summary>
    /// Stop playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    public static unsafe void PlaylistStop(nint playlist)
        => Gen.VlcPlaylist.Stop((Gen.Playlist*)playlist);

    /// <summary>
    /// Pause playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    public static unsafe void PlaylistPause(nint playlist)
        => Gen.VlcPlaylist.Pause((Gen.Playlist*)playlist);

    /// <summary>
    /// Resume playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    public static unsafe void PlaylistResume(nint playlist)
        => Gen.VlcPlaylist.Resume((Gen.Playlist*)playlist);

    #endregion

    #region Playlist Navigation

    /// <summary>
    /// Go to the next item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int PlaylistNext(nint playlist)
        => Gen.VlcPlaylist.Next((Gen.Playlist*)playlist);

    /// <summary>
    /// Go to the previous item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int PlaylistPrev(nint playlist)
        => Gen.VlcPlaylist.Prev((Gen.Playlist*)playlist);

    /// <summary>
    /// Check if there is a next item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>True if there is a next item</returns>
    public static unsafe bool PlaylistHasNext(nint playlist)
        => Gen.VlcPlaylist.HasNext((Gen.Playlist*)playlist) != 0;

    /// <summary>
    /// Check if there is a previous item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>True if there is a previous item</returns>
    public static unsafe bool PlaylistHasPrev(nint playlist)
        => Gen.VlcPlaylist.HasPrev((Gen.Playlist*)playlist) != 0;

    #endregion

    #region Playlist Queries

    /// <summary>
    /// Get the number of items in the playlist.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Number of items</returns>
    public static unsafe nuint PlaylistCount(nint playlist)
        => Gen.VlcPlaylist.Count((Gen.Playlist*)playlist);

    /// <summary>
    /// Get the current item index.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Current index, or -1 if none</returns>
    public static unsafe nint PlaylistGetCurrentIndex(nint playlist)
        => Gen.VlcPlaylist.GetCurrentIndex((Gen.Playlist*)playlist);

    /// <summary>
    /// Go to a specific index.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <param name="index">Index to go to (-1 for none)</param>
    /// <returns>0 on success, error code on failure</returns>
    public static unsafe int PlaylistGoTo(nint playlist, nint index)
        => Gen.VlcPlaylist.GoTo((Gen.Playlist*)playlist, index);

    #endregion

    #region Audio Output (No lock required)

    /// <summary>
    /// Get the audio volume.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Volume in the range [0.0, 2.0], or -1.0 if no audio outputs</returns>
    public static unsafe float PlayerAoutGetVolume(nint player)
        => Gen.VlcPlayerAout.GetVolume((Gen.Player*)player);

    /// <summary>
    /// Set the audio volume.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="volume">Volume in the range [0.0, 2.0]</param>
    /// <returns>0 on success, -1 on failure</returns>
    public static unsafe int PlayerAoutSetVolume(nint player, float volume)
        => Gen.VlcPlayerAout.SetVolume((Gen.Player*)player, volume);

    /// <summary>
    /// Check if the audio output is muted.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>0 if not muted, 1 if muted, -1 if no audio outputs</returns>
    public static unsafe int PlayerAoutIsMuted(nint player)
        => Gen.VlcPlayerAout.IsMuted((Gen.Player*)player);

    /// <summary>
    /// Mute or unmute the audio output.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="mute">True to mute, false to unmute</param>
    /// <returns>0 on success, -1 on failure</returns>
    public static unsafe int PlayerAoutMute(nint player, bool mute)
        => Gen.VlcPlayerAout.Mute((Gen.Player*)player, mute ? (byte)1 : (byte)0);

    #endregion

    #region Object Management

    /// <summary>
    /// Get the parent of a VLC object.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <returns>Pointer to parent vlc_object_t, or IntPtr.Zero if none</returns>
    public static unsafe nint ObjectParent(nint obj)
        => (nint)Gen.VlcObject.Parent((Gen.ObjectHeader*)obj);

    /// <summary>
    /// Get the type name of a VLC object.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <returns>Pointer to type name string (VLC-owned, do not free), or IntPtr.Zero on error</returns>
    public static unsafe nint ObjectTypename(nint obj)
        => (nint)Gen.VlcObject.Typename((Gen.ObjectHeader*)obj);

    #endregion

    #region Memory Management

    /// <summary>
    /// Free memory allocated by VLC.
    /// Used to free strings returned by var_GetString and similar functions.
    /// Calls libvlc_free, which frees with the C runtime of the VLC build
    /// (msvcrt or the UCRT on Windows, libc elsewhere).
    /// </summary>
    /// <param name="ptr">Pointer to free</param>
    public static unsafe void Free(nint ptr)
    {
        if (GlobalLibvlcFree.Pointer != null)
            GlobalLibvlcFree.Pointer(ptr);
        else
            LibvlcFree(ptr);
    }

    /// <summary>
    /// libvlc_free among the process's global symbols: on Linux and macOS, the
    /// libvlc the VLC process runs on. Looked up before loading "libvlc" by
    /// name, which could open a different libvlc (a distro VLC 3, say) and pull
    /// a second libvlc and libvlccore into the process. Null on Windows, where
    /// loading by name finds the libvlc.dll already loaded.
    /// </summary>
    private static unsafe class GlobalLibvlcFree
    {
        public static readonly delegate* unmanaged[Cdecl]<nint, void> Pointer =
            NativeLibrary.TryGetExport(NativeLibrary.GetMainProgramHandle(), "libvlc_free", out var free)
                ? (delegate* unmanaged[Cdecl]<nint, void>)free
                : null;
    }

    /// <summary>
    /// libvlc_free (vlc/libvlc.h) is exported by libvlc, not libvlccore, so plugins
    /// don't link it: it binds on first use to the libvlc of that name.
    /// </summary>
    [LibraryImport("libvlc", EntryPoint = "libvlc_free")]
    private static partial void LibvlcFree(nint ptr);

    #endregion

    #region Picture Management

    /// <summary>
    /// Retain a VLC video context.
    /// </summary>
    public static unsafe nint VideoContextHold(nint videoContext)
        => (nint)Gen.VlcVideoContext.Hold((Gen.VideoContext*)videoContext);

    /// <summary>
    /// Release a retained VLC video context.
    /// </summary>
    public static unsafe void VideoContextRelease(nint videoContext)
        => Gen.VlcVideoContext.Release((Gen.VideoContext*)videoContext);

    /// <summary>
    /// Create a new picture from a video format.
    /// The picture must be released with PictureRelease.
    /// </summary>
    /// <param name="format">Pointer to video_format_t structure</param>
    /// <returns>Pointer to picture_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint PictureNewFromFormat(nint format)
        => (nint)Gen.Picture.NewFromFormat((Gen.VideoFormat*)format);

    /// <summary>
    /// Copy both picture dynamic properties and pixels.
    /// </summary>
    /// <param name="dst">Destination picture pointer</param>
    /// <param name="src">Source picture pointer</param>
    public static unsafe void PictureCopy(nint dst, nint src)
        => Gen.Picture.Copy((Gen.Picture*)dst, (Gen.Picture*)src);

    /// <summary>
    /// Copy picture dynamic properties (date, flags, etc) without copying pixels.
    /// </summary>
    /// <param name="dst">Destination picture pointer</param>
    /// <param name="src">Source picture pointer</param>
    public static unsafe void PictureCopyProperties(nint dst, nint src)
        => Gen.Picture.CopyProperties((Gen.Picture*)dst, (Gen.Picture*)src);

    /// <summary>
    /// Destroy a picture with zero references.
    /// Note: In C, picture_Release decrements refs and calls this when refs reaches 0.
    /// For filters, VLC manages the refcount, so we typically use filter_NewPicture instead.
    /// </summary>
    /// <param name="picture">Picture pointer</param>
    public static unsafe void PictureDestroy(nint picture)
        => Gen.Picture.Destroy((Gen.Picture*)picture);

    /// <summary>
    /// Release a picture reference, mirroring VLC's inline picture_Release.
    /// </summary>
    public static unsafe void PictureRelease(nint picture)
    {
        if (picture == 0)
        {
            return;
        }

        VLCPicture* picturePointer = (VLCPicture*)picture;
        ref int references = ref Unsafe.As<uint, int>(
            ref picturePointer->Refs);
        if (Interlocked.Decrement(ref references) == 0)
        {
            PictureDestroy(picture);
        }
    }

    /// <summary>
    /// Get the chroma description for a fourcc code.
    /// </summary>
    /// <param name="fourcc">Fourcc chroma code (e.g., VLC_CODEC_I420)</param>
    /// <returns>Pointer to vlc_chroma_description_t, or IntPtr.Zero if unknown</returns>
    public static unsafe nint FourccGetChromaDescription(uint fourcc)
        => (nint)Gen.VlcFourcc.GetChromaDescription(fourcc);

    #endregion

    #region Subpicture Region Management

    /// <summary>
    /// Create a new subpicture region.
    /// You must use subpicture_region_Delete to destroy it.
    /// </summary>
    /// <param name="format">Pointer to video_format_t structure</param>
    /// <returns>Pointer to subpicture_region_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint SubpictureRegionNew(nint format)
        => (nint)Gen.SubpictureRegion.New((Gen.VideoFormat*)format);

    /// <summary>
    /// Create a new text subpicture region.
    /// You must use subpicture_region_Delete to destroy it.
    /// </summary>
    /// <returns>Pointer to subpicture_region_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint SubpictureRegionNewText()
        => (nint)Gen.SubpictureRegion.NewText();

    /// <summary>
    /// Create a subpicture region containing the picture.
    /// A reference will be added to the picture on success.
    /// You must use subpicture_region_Delete to destroy it.
    /// </summary>
    /// <param name="picture">Pointer to picture_t</param>
    /// <returns>Pointer to subpicture_region_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint SubpictureRegionForPicture(nint picture)
        => (nint)Gen.SubpictureRegion.ForPicture((Gen.Picture*)picture);

    /// <summary>
    /// Delete a subpicture region.
    /// You may give it NULL.
    /// </summary>
    /// <param name="region">Pointer to subpicture_region_t</param>
    public static unsafe void SubpictureRegionDelete(nint region)
        => Gen.SubpictureRegion.Delete((Gen.SubpictureRegion*)region);

    /// <summary>Delete a subpicture created by VLC.</summary>
    public static unsafe void SubpictureDelete(nint subpicture)
        => Gen.Subpicture.Delete((Gen.Subpicture*)subpicture);

    #endregion

    #region Text Style Management

    /// <summary>
    /// Create a default text style with sensible defaults.
    /// </summary>
    /// <returns>Pointer to text_style_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint TextStyleNew()
        => (nint)Gen.TextStyle.New();

    /// <summary>
    /// Create a text style.
    /// Pass STYLE_NO_DEFAULTS (0) for zero-filled object,
    /// or any other value for sensible defaults.
    /// </summary>
    /// <param name="features">Feature flags (STYLE_NO_DEFAULTS or other)</param>
    /// <returns>Pointer to text_style_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint TextStyleCreate(int features)
        => (nint)Gen.TextStyle.Create(features);

    /// <summary>
    /// Copy a text style into another.
    /// </summary>
    /// <param name="dst">Destination text_style_t pointer</param>
    /// <param name="src">Source text_style_t pointer</param>
    /// <returns>Pointer to destination text_style_t</returns>
    public static unsafe nint TextStyleCopy(nint dst, nint src)
        => (nint)Gen.TextStyle.Copy((Gen.TextStyle*)dst, (Gen.TextStyle*)src);

    /// <summary>
    /// Duplicate a text style.
    /// </summary>
    /// <param name="style">Pointer to text_style_t to duplicate</param>
    /// <returns>Pointer to new text_style_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint TextStyleDuplicate(nint style)
        => (nint)Gen.TextStyle.Duplicate((Gen.TextStyle*)style);

    /// <summary>
    /// Merge two styles using non-default values.
    /// </summary>
    /// <param name="dst">Destination text_style_t pointer</param>
    /// <param name="src">Source text_style_t pointer</param>
    /// <param name="override_">True to also overwrite non-defaults</param>
    public static unsafe void TextStyleMerge(nint dst, nint src, bool override_)
        => Gen.TextStyle.Merge((Gen.TextStyle*)dst, (Gen.TextStyle*)src, override_ ? (byte)1 : (byte)0);

    /// <summary>
    /// Delete a text style.
    /// </summary>
    /// <param name="style">Pointer to text_style_t</param>
    public static unsafe void TextStyleDelete(nint style)
        => Gen.TextStyle.Delete((Gen.TextStyle*)style);

    #endregion

    #region Text Segment Management

    /// <summary>
    /// Create a new text segment.
    /// This duplicates the string passed as argument.
    /// Use text_segment_ChainDelete to destroy the chain, or text_segment_Delete for a single segment.
    /// </summary>
    /// <param name="text">Text string for the segment (will be duplicated)</param>
    /// <returns>Pointer to text_segment_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint TextSegmentNew(string? text)
    {
        Span<byte> buffer = stackalloc byte[Utf8StringMarshaller.ManagedToUnmanagedIn.BufferSize];
        scoped Utf8StringMarshaller.ManagedToUnmanagedIn utf8 = new();
        try
        {
            utf8.FromManaged(text, buffer);
            return (nint)Gen.TextSegment.New(utf8.ToUnmanaged());
        }
        finally { utf8.Free(); }
    }

    /// <summary>
    /// Create a new text segment with style inherited from the given style.
    /// This doesn't initialize the text.
    /// </summary>
    /// <param name="style">Pointer to text_style_t to inherit from</param>
    /// <returns>Pointer to text_segment_t, or IntPtr.Zero on failure</returns>
    public static unsafe nint TextSegmentNewInheritStyle(nint style)
        => (nint)Gen.TextSegment.NewInheritStyle((Gen.TextStyle*)style);

    /// <summary>
    /// Delete a single text segment and its content.
    /// This assumes the segment is not part of a chain.
    /// </summary>
    /// <param name="segment">Pointer to text_segment_t</param>
    public static unsafe void TextSegmentDelete(nint segment)
        => Gen.TextSegment.Delete((Gen.TextSegment*)segment);

    /// <summary>
    /// Delete a list of text segments (chain).
    /// You may pass it NULL.
    /// </summary>
    /// <param name="segment">Pointer to first text_segment_t in chain</param>
    public static unsafe void TextSegmentChainDelete(nint segment)
        => Gen.TextSegment.ChainDelete((Gen.TextSegment*)segment);

    /// <summary>
    /// Copy a text_segment and its chain into a new one.
    /// You may give it NULL, but it will return NULL.
    /// </summary>
    /// <param name="segment">Pointer to text_segment_t to copy</param>
    /// <returns>Pointer to new text_segment_t chain, or IntPtr.Zero if input was NULL</returns>
    public static unsafe nint TextSegmentCopy(nint segment)
        => (nint)Gen.TextSegment.Copy((Gen.TextSegment*)segment);

    #endregion
}

/// <summary>
/// VLC value union for P/Invoke. Matches vlc_value_t in C.
/// Uses explicit field layout to match C union behavior: 8 bytes, the same
/// size and offsets as the generated <c>Interop.Value</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct VLCValueNative
{
    [FieldOffset(0)]
    public long Integer;

    [FieldOffset(0)]
    public byte Bool;

    [FieldOffset(0)]
    public float Float;

    [FieldOffset(0)]
    public nint String;

    [FieldOffset(0)]
    public nint Address;

    [FieldOffset(0)]
    public int CoordX;

    [FieldOffset(4)]
    public int CoordY;
}

/// <summary>
/// Player capability flags.
/// </summary>
public static class VLCPlayerCapabilities
{
    public const int Seek = 1 << 0;
    public const int Pause = 1 << 1;
    public const int ChangeRate = 1 << 2;
    public const int Rewind = 1 << 3;
}

/// <summary>
/// VLC player callbacks structure (vlc_player_cbs), laid out from the generated
/// <c>Interop.PlayerCbs</c>: 37 callback slots, all of which must exist even if
/// unused. Each field holds a C function pointer, or zero for no callback.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = Gen.PlayerCbs.MaxSizeOf)]
public struct VLCPlayerCbs
{
    // Slot 1: on_current_media_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnCurrentMediaChanged)] public nint OnCurrentMediaChanged;
    // Slot 2: on_state_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnStateChanged)] public nint OnStateChanged;
    // Slot 3: on_error_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnErrorChanged)] public nint OnErrorChanged;
    // Slot 4: on_buffering_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnBufferingChanged)] public nint OnBufferingChanged;
    // Slot 5: on_rate_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnRateChanged)] public nint OnRateChanged;
    // Slot 6: on_capabilities_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnCapabilitiesChanged)] public nint OnCapabilitiesChanged;
    // Slot 7: on_position_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnPositionChanged)] public nint OnPositionChanged;
    // Slot 8: on_length_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnLengthChanged)] public nint OnLengthChanged;
    // Slot 9: on_track_list_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTrackListChanged)] public nint OnTrackListChanged;
    // Slot 10: on_track_selection_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTrackSelectionChanged)] public nint OnTrackSelectionChanged;
    // Slot 11: on_track_delay_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTrackDelayChanged)] public nint OnTrackDelayChanged;
    // Slot 12: on_program_list_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnProgramListChanged)] public nint OnProgramListChanged;
    // Slot 13: on_program_selection_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnProgramSelectionChanged)] public nint OnProgramSelectionChanged;
    // Slot 14: on_titles_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTitlesChanged)] public nint OnTitlesChanged;
    // Slot 15: on_title_selection_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTitleSelectionChanged)] public nint OnTitleSelectionChanged;
    // Slot 16: on_chapter_selection_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnChapterSelectionChanged)] public nint OnChapterSelectionChanged;
    // Slot 17: on_teletext_menu_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTeletextMenuChanged)] public nint OnTeletextMenuChanged;
    // Slot 18: on_teletext_enabled_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTeletextEnabledChanged)] public nint OnTeletextEnabledChanged;
    // Slot 19: on_teletext_page_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTeletextPageChanged)] public nint OnTeletextPageChanged;
    // Slot 20: on_teletext_transparency_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnTeletextTransparencyChanged)] public nint OnTeletextTransparencyChanged;
    // Slot 21: on_category_delay_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnCategoryDelayChanged)] public nint OnCategoryDelayChanged;
    // Slot 22: on_associated_subs_fps_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnAssociatedSubsFpsChanged)] public nint OnAssociatedSubsFpsChanged;
    // Slot 23: on_renderer_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnRendererChanged)] public nint OnRendererChanged;
    // Slot 24: on_recording_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnRecordingChanged)] public nint OnRecordChanged;
    // Slot 25: on_signal_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnSignalChanged)] public nint OnSignalChanged;
    // Slot 26: on_statistics_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnStatisticsChanged)] public nint OnStatisticsChanged;
    // Slot 27: on_atobloop_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnAtobloopChanged)] public nint OnAtobloopChanged;
    // Slot 28: on_media_meta_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnMediaMetaChanged)] public nint OnMediaMetaChanged;
    // Slot 29: on_media_epg_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnMediaEpgChanged)] public nint OnMediaEpgChanged;
    // Slot 30: on_media_subitems_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnMediaSubitemsChanged)] public nint OnSubitemsChanged;
    // Slot 31: on_media_attachments_added
    [FieldOffset(Gen.PlayerCbs.Offsets.OnMediaAttachmentsAdded)] public nint OnMediaAttachmentsAdded;
    // Slot 32: on_vout_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnVoutChanged)] public nint OnVoutChanged;
    // Slot 33: on_cork_changed
    [FieldOffset(Gen.PlayerCbs.Offsets.OnCorkChanged)] public nint OnCorksChanged;
    // Slot 34: on_playback_restore_queried
    [FieldOffset(Gen.PlayerCbs.Offsets.OnPlaybackRestoreQueried)] public nint OnPlaybackRestoreQueried;
    // Slot 35: on_stopping_current_media
    [FieldOffset(Gen.PlayerCbs.Offsets.OnStoppingCurrentMedia)] public nint OnStoppingCurrentMedia;
    // Slot 36: on_next_frame_status
    [FieldOffset(Gen.PlayerCbs.Offsets.OnNextFrameStatus)] public nint OnNextFrameStatus;
    // Slot 37: on_prev_frame_status
    [FieldOffset(Gen.PlayerCbs.Offsets.OnPrevFrameStatus)] public nint OnPrevFrameStatus;
}
