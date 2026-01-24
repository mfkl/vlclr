/**
 * C# Bridge Header
 * Function pointer declarations for dynamically loaded C# Native AOT exports.
 */

#ifndef CSHARP_BRIDGE_H
#define CSHARP_BRIDGE_H

/* Export macro for cross-platform visibility */
#ifdef _WIN32
#define BRIDGE_API __declspec(dllexport)
#else
#define BRIDGE_API __attribute__((visibility("default")))
#endif

/**
 * Initialize the bridge (load C# DLL, resolve functions).
 * @return 0 on success, -1 on failure.
 */
int csharp_bridge_init(void);

/**
 * Cleanup (unload C# DLL).
 */
void csharp_bridge_cleanup(void);

/* Function pointer types matching C# exports */
typedef int (*csharp_open_fn)(void* vlc_object);
typedef void (*csharp_close_fn)(void* vlc_object);

/* Function pointers resolved at runtime */
extern csharp_open_fn csharp_plugin_open;
extern csharp_close_fn csharp_plugin_close;

/**
 * VLC logging wrapper for C# to call.
 * Wraps VLC's variadic logging into a simple function C# can P/Invoke.
 * @param vlc_object Pointer to vlc_object_t
 * @param type Log type: 0=INFO, 1=ERR, 2=WARN, 3=DBG
 * @param message UTF-8 message string
 */
BRIDGE_API void csharp_bridge_log(void* vlc_object, int type, const char* message);

/**
 * VLC variable wrappers for C# to call.
 * These wrap VLC's variable system functions.
 */

/**
 * Create a VLC variable.
 * @param vlc_object Pointer to vlc_object_t
 * @param name Variable name
 * @param type Variable type (VLC_VAR_*)
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_var_create(void* vlc_object, const char* name, int type);

/**
 * Destroy a VLC variable.
 * @param vlc_object Pointer to vlc_object_t
 * @param name Variable name
 */
BRIDGE_API void csharp_bridge_var_destroy(void* vlc_object, const char* name);

/**
 * Set an integer variable value.
 * @param vlc_object Pointer to vlc_object_t
 * @param name Variable name
 * @param value Value to set
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_var_set_integer(void* vlc_object, const char* name, long long value);

/**
 * Get an integer variable value.
 * @param vlc_object Pointer to vlc_object_t
 * @param name Variable name
 * @return The variable value, or 0 if not found
 */
BRIDGE_API long long csharp_bridge_var_get_integer(void* vlc_object, const char* name);

/**
 * Set a string variable value.
 * @param vlc_object Pointer to vlc_object_t
 * @param name Variable name
 * @param value Value to set (UTF-8)
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_var_set_string(void* vlc_object, const char* name, const char* value);

/**
 * Get a string variable value.
 * @param vlc_object Pointer to vlc_object_t
 * @param name Variable name
 * @param buffer Buffer to receive the string (caller must free with csharp_bridge_free_string)
 * @return Newly allocated string (UTF-8), or NULL if not found. Caller must free.
 */
BRIDGE_API char* csharp_bridge_var_get_string(void* vlc_object, const char* name);

/**
 * Free a string returned by csharp_bridge_var_get_string.
 * @param str String to free
 */
BRIDGE_API void csharp_bridge_free_string(char* str);

/*
 * Player Events API
 * These functions allow C# to subscribe to VLC player state changes.
 */

/**
 * Player state enumeration matching vlc_player_state.
 */
typedef enum {
    CSHARP_PLAYER_STATE_STOPPED = 0,
    CSHARP_PLAYER_STATE_STARTED,
    CSHARP_PLAYER_STATE_PLAYING,
    CSHARP_PLAYER_STATE_PAUSED,
    CSHARP_PLAYER_STATE_STOPPING,
} csharp_player_state_t;

/**
 * Player event callback types for C#.
 * These are called from the C glue layer when VLC events occur.
 */
typedef void (*csharp_on_state_changed_fn)(int new_state, void* user_data);
typedef void (*csharp_on_position_changed_fn)(long long new_time, double new_pos, void* user_data);
typedef void (*csharp_on_media_changed_fn)(void* new_media, void* user_data);

/**
 * Player listener callbacks structure for C#.
 */
typedef struct {
    csharp_on_state_changed_fn on_state_changed;
    csharp_on_position_changed_fn on_position_changed;
    csharp_on_media_changed_fn on_media_changed;
    void* user_data;
} csharp_player_callbacks_t;

/**
 * Get the player from an interface object.
 * @param intf Pointer to intf_thread_t (passed to Open callback)
 * @return Pointer to vlc_player_t, or NULL on failure
 */
BRIDGE_API void* csharp_bridge_get_player(void* intf);

/**
 * Get the current player state.
 * @param player Pointer to vlc_player_t
 * @return Current player state (csharp_player_state_t values)
 */
BRIDGE_API int csharp_bridge_player_get_state(void* player);

/**
 * Add a player listener.
 * @param player Pointer to vlc_player_t
 * @param callbacks Pointer to csharp_player_callbacks_t structure
 * @return Listener ID (opaque pointer), or NULL on failure
 */
BRIDGE_API void* csharp_bridge_player_add_listener(void* player, csharp_player_callbacks_t* callbacks);

/**
 * Remove a player listener.
 * @param player Pointer to vlc_player_t
 * @param listener_id Listener ID returned by csharp_bridge_player_add_listener
 */
BRIDGE_API void csharp_bridge_player_remove_listener(void* player, void* listener_id);

/**
 * Get the current playback time.
 * @param player Pointer to vlc_player_t
 * @return Current time in VLC ticks (microseconds), or VLC_TICK_INVALID if not playing
 */
BRIDGE_API long long csharp_bridge_player_get_time(void* player);

/**
 * Get the total length of the current media.
 * @param player Pointer to vlc_player_t
 * @return Total length in VLC ticks (microseconds), or VLC_TICK_INVALID if unknown
 */
BRIDGE_API long long csharp_bridge_player_get_length(void* player);

/**
 * Get the current playback position as a ratio.
 * @param player Pointer to vlc_player_t
 * @return Position as a ratio [0.0, 1.0], or -1.0 if not playing
 */
BRIDGE_API double csharp_bridge_player_get_position(void* player);

/**
 * Seek speed enumeration.
 */
typedef enum {
    CSHARP_SEEK_PRECISE = 0,  /* Seek to exact time (may be slower) */
    CSHARP_SEEK_FAST = 1,     /* Seek to nearest keyframe (faster) */
} csharp_seek_speed_t;

/**
 * Seek whence enumeration.
 */
typedef enum {
    CSHARP_SEEK_ABSOLUTE = 0, /* Seek from beginning of media */
    CSHARP_SEEK_RELATIVE = 1, /* Seek relative to current position */
} csharp_seek_whence_t;

/**
 * Seek to a specific time.
 * @param player Pointer to vlc_player_t
 * @param time Time in VLC ticks (microseconds)
 * @param speed Seek precision (0=precise, 1=fast)
 * @param whence Seek reference (0=absolute, 1=relative)
 */
BRIDGE_API void csharp_bridge_player_seek_by_time(void* player, long long time, int speed, int whence);

/**
 * Seek to a specific position.
 * @param player Pointer to vlc_player_t
 * @param position Position as a ratio [0.0, 1.0]
 * @param speed Seek precision (0=precise, 1=fast)
 * @param whence Seek reference (0=absolute, 1=relative)
 */
BRIDGE_API void csharp_bridge_player_seek_by_pos(void* player, double position, int speed, int whence);

/**
 * Check if seeking is supported.
 * @param player Pointer to vlc_player_t
 * @return 1 if seeking is supported, 0 otherwise
 */
BRIDGE_API int csharp_bridge_player_can_seek(void* player);

/**
 * Check if pausing is supported.
 * @param player Pointer to vlc_player_t
 * @return 1 if pausing is supported, 0 otherwise
 */
BRIDGE_API int csharp_bridge_player_can_pause(void* player);

/**
 * Pause the player.
 * @param player Pointer to vlc_player_t
 */
BRIDGE_API void csharp_bridge_player_pause(void* player);

/**
 * Resume the player.
 * @param player Pointer to vlc_player_t
 */
BRIDGE_API void csharp_bridge_player_resume(void* player);

/*
 * Playlist Control API
 * These functions allow C# to control VLC playlist playback.
 */

/**
 * Get the playlist from an interface object.
 * @param intf Pointer to intf_thread_t (passed to Open callback)
 * @return Pointer to vlc_playlist_t, or NULL on failure
 */
BRIDGE_API void* csharp_bridge_get_playlist(void* intf);

/**
 * Start playback.
 * @param playlist Pointer to vlc_playlist_t
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_playlist_start(void* playlist);

/**
 * Stop playback.
 * @param playlist Pointer to vlc_playlist_t
 */
BRIDGE_API void csharp_bridge_playlist_stop(void* playlist);

/**
 * Pause playback.
 * @param playlist Pointer to vlc_playlist_t
 */
BRIDGE_API void csharp_bridge_playlist_pause(void* playlist);

/**
 * Resume playback.
 * @param playlist Pointer to vlc_playlist_t
 */
BRIDGE_API void csharp_bridge_playlist_resume(void* playlist);

/**
 * Go to the next item.
 * @param playlist Pointer to vlc_playlist_t
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_playlist_next(void* playlist);

/**
 * Go to the previous item.
 * @param playlist Pointer to vlc_playlist_t
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_playlist_prev(void* playlist);

/**
 * Check if there is a next item.
 * @param playlist Pointer to vlc_playlist_t
 * @return 1 if true, 0 if false
 */
BRIDGE_API int csharp_bridge_playlist_has_next(void* playlist);

/**
 * Check if there is a previous item.
 * @param playlist Pointer to vlc_playlist_t
 * @return 1 if true, 0 if false
 */
BRIDGE_API int csharp_bridge_playlist_has_prev(void* playlist);

/**
 * Get the number of items in the playlist.
 * @param playlist Pointer to vlc_playlist_t
 * @return Number of items
 */
BRIDGE_API long long csharp_bridge_playlist_count(void* playlist);

/**
 * Get the current item index.
 * @param playlist Pointer to vlc_playlist_t
 * @return Current index, or -1 if none
 */
BRIDGE_API long long csharp_bridge_playlist_get_current_index(void* playlist);

/**
 * Go to a specific index.
 * @param playlist Pointer to vlc_playlist_t
 * @param index Index to go to (-1 for none)
 * @return 0 on success, error code on failure
 */
BRIDGE_API int csharp_bridge_playlist_goto(void* playlist, long long index);

/*
 * Object Management API
 * These functions allow C# to navigate the VLC object hierarchy.
 */

/**
 * Get the parent of a VLC object.
 * @param obj Pointer to vlc_object_t
 * @return Pointer to parent vlc_object_t, or NULL if none
 */
BRIDGE_API void* csharp_bridge_object_parent(void* obj);

/**
 * Get the type name of a VLC object.
 * @param obj Pointer to vlc_object_t
 * @return Type name string (do not free - owned by VLC), or NULL on error
 */
BRIDGE_API const char* csharp_bridge_object_typename(void* obj);

/*
 * Audio Output Control API
 * These functions allow C# to control volume and mute.
 * Note: These functions do NOT require the player lock.
 */

/**
 * Get the audio volume.
 * @param player Pointer to vlc_player_t
 * @return Volume in the range [0.0, 2.0], or -1.0 if no audio outputs
 */
BRIDGE_API float csharp_bridge_player_get_volume(void* player);

/**
 * Set the audio volume.
 * @param player Pointer to vlc_player_t
 * @param volume Volume in the range [0.0, 2.0]
 * @return 0 on success, -1 on failure
 */
BRIDGE_API int csharp_bridge_player_set_volume(void* player, float volume);

/**
 * Check if the audio output is muted.
 * @param player Pointer to vlc_player_t
 * @return 0 if not muted, 1 if muted, -1 if no audio outputs
 */
BRIDGE_API int csharp_bridge_player_is_muted(void* player);

/**
 * Mute or unmute the audio output.
 * @param player Pointer to vlc_player_t
 * @param mute 1 to mute, 0 to unmute
 * @return 0 on success, -1 on failure
 */
BRIDGE_API int csharp_bridge_player_set_mute(void* player, int mute);

/**
 * Toggle the mute state.
 * @param player Pointer to vlc_player_t
 * @return 0 on success, -1 on failure
 */
BRIDGE_API int csharp_bridge_player_toggle_mute(void* player);

#endif /* CSHARP_BRIDGE_H */
