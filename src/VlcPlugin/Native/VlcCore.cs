using System.Runtime.InteropServices;

namespace VlcPlugin.Native;

/// <summary>
/// Direct P/Invoke declarations for libvlccore functions.
/// These call VLC's native library directly without the C glue layer.
/// </summary>
internal static partial class VlcCore
{
    private const string LibraryName = "libvlccore";

    #region Logging

    /// <summary>
    /// Log a message through VLC's logging system.
    /// Note: This is a variadic function. We use "%s" format and pass a single pre-formatted string.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="type">Log type: 0=INFO, 1=ERR, 2=WARN, 3=DBG</param>
    /// <param name="module">Module name (e.g., "dotnet")</param>
    /// <param name="file">Source file name (can be empty)</param>
    /// <param name="line">Source line number (can be 0)</param>
    /// <param name="func">Function name (can be empty)</param>
    /// <param name="format">Format string - use "%s" for pre-formatted message</param>
    /// <param name="message">The pre-formatted message (when format is "%s")</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_Log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Log(
        nint obj,
        int type,
        string module,
        string file,
        uint line,
        string func,
        string format,
        string message);

    #endregion

    #region Variables

    /// <summary>
    /// Create a VLC variable.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "var_Create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarCreate(nint obj, string name, int type);

    /// <summary>
    /// Destroy a VLC variable.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    [LibraryImport(LibraryName, EntryPoint = "var_Destroy", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void VarDestroy(nint obj, string name);

    /// <summary>
    /// Set a variable value with type checking.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <param name="value">Value to set (passed as vlc_value_t)</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "var_SetChecked", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarSetChecked(nint obj, string name, int type, VlcValueNative value);

    /// <summary>
    /// Get a variable value with type checking.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <param name="value">Pointer to receive value</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "var_GetChecked", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarGetChecked(nint obj, string name, int type, out VlcValueNative value);

    #endregion

    #region Interface/Playlist Access

    /// <summary>
    /// Get the main playlist from an interface.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <returns>Pointer to vlc_playlist_t, or IntPtr.Zero on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_intf_GetMainPlaylist")]
    internal static partial nint IntfGetMainPlaylist(nint intf);

    /// <summary>
    /// Get the player from a playlist.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Pointer to vlc_player_t</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_GetPlayer")]
    internal static partial nint PlaylistGetPlayer(nint playlist);

    #endregion

    #region Player Lock/Unlock

    /// <summary>
    /// Lock the player. Must be called before most player operations.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_Lock")]
    internal static partial void PlayerLock(nint player);

    /// <summary>
    /// Unlock the player. Must be called after player operations.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_Unlock")]
    internal static partial void PlayerUnlock(nint player);

    #endregion

    #region Player State Queries

    /// <summary>
    /// Get the current player state.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Current player state enum value</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_GetState")]
    internal static partial int PlayerGetState(nint player);

    /// <summary>
    /// Get the current playback time.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Current time in VLC ticks (microseconds), or VLC_TICK_INVALID if not playing</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_GetTime")]
    internal static partial long PlayerGetTime(nint player);

    /// <summary>
    /// Get the total length of the current media.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Total length in VLC ticks (microseconds), or VLC_TICK_INVALID if unknown</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_GetLength")]
    internal static partial long PlayerGetLength(nint player);

    /// <summary>
    /// Get the current playback position as a ratio.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Position as a ratio [0.0, 1.0], or -1.0 if not playing</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_GetPosition")]
    internal static partial double PlayerGetPosition(nint player);

    /// <summary>
    /// Get the current player capabilities.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Bitfield of capabilities (VLC_PLAYER_CAP_*)</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_GetCapabilities")]
    internal static partial int PlayerGetCapabilities(nint player);

    #endregion

    #region Player Playback Control

    /// <summary>
    /// Pause the player.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_Pause")]
    internal static partial void PlayerPause(nint player);

    /// <summary>
    /// Resume the player.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_Resume")]
    internal static partial void PlayerResume(nint player);

    /// <summary>
    /// Seek to a specific time.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="time">Time in VLC ticks (microseconds)</param>
    /// <param name="speed">Seek precision (0=precise, 1=fast)</param>
    /// <param name="whence">Seek reference (0=absolute, 1=relative)</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_SeekByTime")]
    internal static partial void PlayerSeekByTime(nint player, long time, int speed, int whence);

    /// <summary>
    /// Seek to a specific position.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="position">Position as a ratio [0.0, 1.0]</param>
    /// <param name="speed">Seek precision (0=precise, 1=fast)</param>
    /// <param name="whence">Seek reference (0=absolute, 1=relative)</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_SeekByPos")]
    internal static partial void PlayerSeekByPos(nint player, double position, int speed, int whence);

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
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_AddListener")]
    internal static partial nint PlayerAddListener(nint player, nint cbs, nint cbsData);

    /// <summary>
    /// Remove a player listener.
    /// Requires player lock to be held.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="listenerId">Listener ID from PlayerAddListener</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_RemoveListener")]
    internal static partial void PlayerRemoveListener(nint player, nint listenerId);

    #endregion

    #region Playlist Lock/Unlock

    /// <summary>
    /// Lock the playlist. Must be called before most playlist operations.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Lock")]
    internal static partial void PlaylistLock(nint playlist);

    /// <summary>
    /// Unlock the playlist. Must be called after playlist operations.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Unlock")]
    internal static partial void PlaylistUnlock(nint playlist);

    #endregion

    #region Playlist Playback Control

    /// <summary>
    /// Start playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Start")]
    internal static partial int PlaylistStart(nint playlist);

    /// <summary>
    /// Stop playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Stop")]
    internal static partial void PlaylistStop(nint playlist);

    /// <summary>
    /// Pause playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Pause")]
    internal static partial void PlaylistPause(nint playlist);

    /// <summary>
    /// Resume playback.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Resume")]
    internal static partial void PlaylistResume(nint playlist);

    #endregion

    #region Playlist Navigation

    /// <summary>
    /// Go to the next item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Next")]
    internal static partial int PlaylistNext(nint playlist);

    /// <summary>
    /// Go to the previous item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Prev")]
    internal static partial int PlaylistPrev(nint playlist);

    /// <summary>
    /// Check if there is a next item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>True if there is a next item</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_HasNext")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool PlaylistHasNext(nint playlist);

    /// <summary>
    /// Check if there is a previous item.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>True if there is a previous item</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_HasPrev")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool PlaylistHasPrev(nint playlist);

    #endregion

    #region Playlist Queries

    /// <summary>
    /// Get the number of items in the playlist.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Number of items</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_Count")]
    internal static partial nuint PlaylistCount(nint playlist);

    /// <summary>
    /// Get the current item index.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Current index, or -1 if none</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_GetCurrentIndex")]
    internal static partial nint PlaylistGetCurrentIndex(nint playlist);

    /// <summary>
    /// Go to a specific index.
    /// Requires playlist lock to be held.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <param name="index">Index to go to (-1 for none)</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_playlist_GoTo")]
    internal static partial int PlaylistGoTo(nint playlist, nint index);

    #endregion

    #region Audio Output (No lock required)

    /// <summary>
    /// Get the audio volume.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Volume in the range [0.0, 2.0], or -1.0 if no audio outputs</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_aout_GetVolume")]
    internal static partial float PlayerAoutGetVolume(nint player);

    /// <summary>
    /// Set the audio volume.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="volume">Volume in the range [0.0, 2.0]</param>
    /// <returns>0 on success, -1 on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_aout_SetVolume")]
    internal static partial int PlayerAoutSetVolume(nint player, float volume);

    /// <summary>
    /// Check if the audio output is muted.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>0 if not muted, 1 if muted, -1 if no audio outputs</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_aout_IsMuted")]
    internal static partial int PlayerAoutIsMuted(nint player);

    /// <summary>
    /// Mute or unmute the audio output.
    /// Does NOT require player lock.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="mute">True to mute, false to unmute</param>
    /// <returns>0 on success, -1 on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_player_aout_Mute")]
    internal static partial int PlayerAoutMute(nint player, [MarshalAs(UnmanagedType.U1)] bool mute);

    #endregion

    #region Object Management

    /// <summary>
    /// Get the parent of a VLC object.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <returns>Pointer to parent vlc_object_t, or IntPtr.Zero if none</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_parent")]
    internal static partial nint ObjectParent(nint obj);

    /// <summary>
    /// Get the type name of a VLC object.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <returns>Pointer to type name string (VLC-owned, do not free), or IntPtr.Zero on error</returns>
    [LibraryImport(LibraryName, EntryPoint = "vlc_object_typename")]
    internal static partial nint ObjectTypename(nint obj);

    #endregion

    #region Memory Management

    /// <summary>
    /// Free memory allocated by VLC.
    /// Used to free strings returned by var_GetString and similar functions.
    /// </summary>
    /// <param name="ptr">Pointer to free</param>
    [LibraryImport(LibraryName, EntryPoint = "free")]
    internal static partial void Free(nint ptr);

    #endregion
}

/// <summary>
/// VLC value union for P/Invoke. Matches vlc_value_t in C.
/// Uses explicit field layout to match C union behavior.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct VlcValueNative
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
internal static class VlcPlayerCapabilities
{
    public const int Seek = 1 << 0;
    public const int Pause = 1 << 1;
    public const int ChangeRate = 1 << 2;
    public const int Rewind = 1 << 3;
}

/// <summary>
/// VLC player callbacks structure.
/// Has 35 callback function pointers - all must be defined even if unused.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VlcPlayerCbs
{
    // Callback 1: void (*on_current_media_changed)(player, new_media, data)
    public nint OnCurrentMediaChanged;
    // Callback 2: void (*on_state_changed)(player, new_state, data)
    public nint OnStateChanged;
    // Callback 3: void (*on_error_changed)(player, error, data)
    public nint OnErrorChanged;
    // Callback 4: void (*on_buffering_changed)(player, new_buffering, data)
    public nint OnBufferingChanged;
    // Callback 5: void (*on_rate_changed)(player, new_rate, data)
    public nint OnRateChanged;
    // Callback 6: void (*on_capabilities_changed)(player, old_caps, new_caps, data)
    public nint OnCapabilitiesChanged;
    // Callback 7: void (*on_position_changed)(player, new_time, new_pos, data)
    public nint OnPositionChanged;
    // Callback 8: void (*on_length_changed)(player, new_length, data)
    public nint OnLengthChanged;
    // Callback 9: void (*on_track_list_changed)(player, type, action, track, data)
    public nint OnTrackListChanged;
    // Callback 10: void (*on_track_selection_changed)(player, unselected_id, selected_id, data)
    public nint OnTrackSelectionChanged;
    // Callback 11: void (*on_track_delay_changed)(player, es_id, delay, data)
    public nint OnTrackDelayChanged;
    // Callback 12: void (*on_program_list_changed)(player, action, prgm, data)
    public nint OnProgramListChanged;
    // Callback 13: void (*on_program_selection_changed)(player, unselected_id, selected_id, data)
    public nint OnProgramSelectionChanged;
    // Callback 14: void (*on_titles_changed)(player, titles, data)
    public nint OnTitlesChanged;
    // Callback 15: void (*on_title_selection_changed)(player, new_title, new_idx, data)
    public nint OnTitleSelectionChanged;
    // Callback 16: void (*on_chapter_selection_changed)(player, title, new_chapter_idx, data)
    public nint OnChapterSelectionChanged;
    // Callback 17: void (*on_teletext_menu_changed)(player, has_teletext_menu, data)
    public nint OnTeletextMenuChanged;
    // Callback 18: void (*on_teletext_enabled_changed)(player, enabled, data)
    public nint OnTeletextEnabledChanged;
    // Callback 19: void (*on_teletext_page_changed)(player, new_page, data)
    public nint OnTeletextPageChanged;
    // Callback 20: void (*on_teletext_transparency_changed)(player, enabled, data)
    public nint OnTeletextTransparencyChanged;
    // Callback 21: void (*on_category_delay_changed)(player, cat, new_delay, data)
    public nint OnCategoryDelayChanged;
    // Callback 22: void (*on_associated_subs_fps_changed)(player, fps, data)
    public nint OnAssociatedSubsFpsChanged;
    // Callback 23: void (*on_renderer_changed)(player, new_renderer, data)
    public nint OnRendererChanged;
    // Callback 24: void (*on_record_changed)(player, recording, data)
    public nint OnRecordChanged;
    // Callback 25: void (*on_signal_changed)(player, quality, strength, data)
    public nint OnSignalChanged;
    // Callback 26: void (*on_statistics_changed)(player, stats, data)
    public nint OnStatisticsChanged;
    // Callback 27: void (*on_atobloop_changed)(player, state, time_a, time_b, data)
    public nint OnAtobloopChanged;
    // Callback 28: void (*on_media_meta_changed)(player, media, data)
    public nint OnMediaMetaChanged;
    // Callback 29: void (*on_media_epg_changed)(player, media, data)
    public nint OnMediaEpgChanged;
    // Callback 30: void (*on_subitems_changed)(player, media, new_subitems, data)
    public nint OnSubitemsChanged;
    // Callback 31: void (*on_vout_changed)(player, action, vout, order, es_id, data)
    public nint OnVoutChanged;
    // Callback 32: void (*on_corks_changed)(player, corks, data)
    public nint OnCorksChanged;
    // Callback 33: void (*on_playback_restore_queried)(player, data)
    public nint OnPlaybackRestoreQueried;
    // Callback 34: void (*on_stopping_current_media)(player, current_media, data)
    public nint OnStoppingCurrentMedia;
    // Callback 35: void (*on_media_attachments_added)(player, media, attachments, count, data)
    public nint OnMediaAttachmentsAdded;
}
