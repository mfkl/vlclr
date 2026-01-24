using System.Runtime.InteropServices;

namespace VlcPlugin.Native;

/// <summary>
/// P/Invoke declarations for the C glue layer bridge functions.
/// These are exported from libhello_csharp_plugin.dll.
/// </summary>
internal static partial class VlcBridge
{
    private const string LibraryName = "libhello_csharp_plugin";

    /// <summary>
    /// Log a message through VLC's logging system.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="type">Log type: 0=INFO, 1=ERR, 2=WARN, 3=DBG</param>
    /// <param name="message">UTF-8 message string</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Log(nint vlcObject, int type, string message);

    /// <summary>
    /// Create a VLC variable.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="type">Variable type (VLC_VAR_*)</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_var_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarCreate(nint vlcObject, string name, int type);

    /// <summary>
    /// Destroy a VLC variable.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_var_destroy", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void VarDestroy(nint vlcObject, string name);

    /// <summary>
    /// Set an integer variable value.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_var_set_integer", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarSetInteger(nint vlcObject, string name, long value);

    /// <summary>
    /// Get an integer variable value.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <returns>The variable value, or 0 if not found</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_var_get_integer", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial long VarGetInteger(nint vlcObject, string name);

    /// <summary>
    /// Set a string variable value.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <param name="value">Value to set (UTF-8)</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_var_set_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarSetString(nint vlcObject, string name, string? value);

    /// <summary>
    /// Get a string variable value.
    /// Returns a newly allocated string that must be freed with VarFreeString.
    /// </summary>
    /// <param name="vlcObject">Pointer to vlc_object_t</param>
    /// <param name="name">Variable name</param>
    /// <returns>Pointer to newly allocated string, or IntPtr.Zero if not found</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_var_get_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint VarGetStringPtr(nint vlcObject, string name);

    /// <summary>
    /// Free a string returned by VarGetStringPtr.
    /// </summary>
    /// <param name="str">String pointer to free</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_free_string")]
    internal static partial void VarFreeString(nint str);

    #region Player Events

    /// <summary>
    /// Get the player from an interface object.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <returns>Pointer to vlc_player_t, or IntPtr.Zero on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_get_player")]
    internal static partial nint GetPlayer(nint intf);

    /// <summary>
    /// Get the current player state.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <returns>Current player state</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_player_get_state")]
    internal static partial int PlayerGetState(nint player);

    /// <summary>
    /// Add a player listener.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="callbacks">Pointer to callbacks structure</param>
    /// <returns>Listener handle, or IntPtr.Zero on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_player_add_listener")]
    internal static partial nint PlayerAddListener(nint player, ref PlayerCallbacksNative callbacks);

    /// <summary>
    /// Remove a player listener.
    /// </summary>
    /// <param name="player">Pointer to vlc_player_t</param>
    /// <param name="listenerHandle">Listener handle from PlayerAddListener</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_player_remove_listener")]
    internal static partial void PlayerRemoveListener(nint player, nint listenerHandle);

    #endregion

    #region Playlist Control

    /// <summary>
    /// Get the playlist from an interface object.
    /// </summary>
    /// <param name="intf">Pointer to intf_thread_t</param>
    /// <returns>Pointer to vlc_playlist_t, or IntPtr.Zero on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_get_playlist")]
    internal static partial nint GetPlaylist(nint intf);

    /// <summary>
    /// Start playback.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_start")]
    internal static partial int PlaylistStart(nint playlist);

    /// <summary>
    /// Stop playback.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_stop")]
    internal static partial void PlaylistStop(nint playlist);

    /// <summary>
    /// Pause playback.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_pause")]
    internal static partial void PlaylistPause(nint playlist);

    /// <summary>
    /// Resume playback.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_resume")]
    internal static partial void PlaylistResume(nint playlist);

    /// <summary>
    /// Go to the next item.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_next")]
    internal static partial int PlaylistNext(nint playlist);

    /// <summary>
    /// Go to the previous item.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_prev")]
    internal static partial int PlaylistPrev(nint playlist);

    /// <summary>
    /// Check if there is a next item.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>1 if true, 0 if false</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_has_next")]
    internal static partial int PlaylistHasNext(nint playlist);

    /// <summary>
    /// Check if there is a previous item.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>1 if true, 0 if false</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_has_prev")]
    internal static partial int PlaylistHasPrev(nint playlist);

    /// <summary>
    /// Get the number of items in the playlist.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Number of items</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_count")]
    internal static partial long PlaylistCount(nint playlist);

    /// <summary>
    /// Get the current item index.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <returns>Current index, or -1 if none</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_get_current_index")]
    internal static partial long PlaylistGetCurrentIndex(nint playlist);

    /// <summary>
    /// Go to a specific index.
    /// </summary>
    /// <param name="playlist">Pointer to vlc_playlist_t</param>
    /// <param name="index">Index to go to (-1 for none)</param>
    /// <returns>0 on success, error code on failure</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_playlist_goto")]
    internal static partial int PlaylistGoTo(nint playlist, long index);

    #endregion

    #region Object Management

    /// <summary>
    /// Get the parent of a VLC object.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <returns>Pointer to parent vlc_object_t, or IntPtr.Zero if none</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_object_parent")]
    internal static partial nint ObjectParent(nint obj);

    /// <summary>
    /// Get the type name of a VLC object.
    /// </summary>
    /// <param name="obj">Pointer to vlc_object_t</param>
    /// <returns>Pointer to type name string (VLC-owned, do not free), or IntPtr.Zero on error</returns>
    [LibraryImport(LibraryName, EntryPoint = "csharp_bridge_object_typename")]
    internal static partial nint ObjectTypename(nint obj);

    #endregion
}

/// <summary>
/// Native callback structure for player events.
/// Must match csharp_player_callbacks_t in C.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PlayerCallbacksNative
{
    public nint OnStateChanged;
    public nint OnPositionChanged;
    public nint OnMediaChanged;
    public nint UserData;
}

// Note: VlcLogType, VlcPlayerState, and VlcVarType are defined in VlcPlugin.Generated namespace.
// Use 'using VlcPlugin.Generated;' or fully qualify types as VlcPlugin.Generated.VlcLogType, etc.
