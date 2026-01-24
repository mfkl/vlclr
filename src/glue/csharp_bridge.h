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

#endif /* CSHARP_BRIDGE_H */
