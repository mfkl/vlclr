/**
 * C# Bridge Header
 * Function pointer declarations for dynamically loaded C# Native AOT exports.
 */

#ifndef CSHARP_BRIDGE_H
#define CSHARP_BRIDGE_H

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

#endif /* CSHARP_BRIDGE_H */
