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

#endif /* CSHARP_BRIDGE_H */
