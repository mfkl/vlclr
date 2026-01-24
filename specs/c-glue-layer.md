# C Glue Layer Specification

## Overview

The C glue layer is a native C library that:
1. Exposes VLC plugin entry points (satisfies VLC's plugin ABI)
2. Forwards calls to the C# Native AOT library
3. Translates between VLC types and simpler C types where needed

## Architecture

```
VLC Plugin System
       │
       ▼
┌─────────────────────────┐
│   C Glue Library        │  ◄── libvlc_csharp_glue.dll
│   (VLC Plugin Entry)    │
│   - vlc_module_begin    │
│   - Open/Close callbacks│
└──────────┬──────────────┘
           │ Calls exported functions
           ▼
┌─────────────────────────┐
│   C# Native AOT DLL     │  ◄── VlcPlugin.dll (native)
│   [UnmanagedCallersOnly]│
│   - CSharpPluginOpen    │
│   - CSharpPluginClose   │
└─────────────────────────┘
```

## Build Configuration

### Compiler
- Clang/LLVM (cross-platform compatible)

### Windows Build
```bash
clang -shared -o libvlc_csharp_glue.dll \
    src/glue/*.c \
    -I./vlc/include \
    -L./vlc/lib -lvlccore \
    -Wl,--export-all-symbols
```

### Output
- `libvlc_csharp_glue.dll` (or `.so` on Linux)
- Must be placed in VLC plugin directory

## Source Structure

```
src/glue/
├── plugin_entry.c      # VLC module descriptor and entry points
├── csharp_bridge.h     # Function pointer declarations for C# exports
└── csharp_bridge.c     # Dynamic loading of C# DLL, function resolution
```

## Plugin Entry Implementation

### plugin_entry.c

```c
#include <vlc_common.h>
#include <vlc_plugin.h>
#include <vlc_interface.h>
#include "csharp_bridge.h"

static int Open(vlc_object_t *obj)
{
    // Initialize bridge to C# DLL
    if (csharp_bridge_init() != 0)
        return VLC_EGENERIC;

    // Forward to C# implementation
    return csharp_plugin_open((void*)obj);
}

static void Close(vlc_object_t *obj)
{
    // Forward to C# implementation
    csharp_plugin_close((void*)obj);

    // Cleanup bridge
    csharp_bridge_cleanup();
}

vlc_module_begin()
    set_shortname(N_("C# Plugin"))
    set_description(N_("C# Native AOT Plugin"))
    set_capability("interface", 0)
    set_callbacks(Open, Close)
    set_category(CAT_INTERFACE)
vlc_module_end()
```

## C# Bridge Implementation

### csharp_bridge.h

```c
#ifndef CSHARP_BRIDGE_H
#define CSHARP_BRIDGE_H

// Initialize the bridge (load C# DLL, resolve functions)
int csharp_bridge_init(void);

// Cleanup (unload C# DLL)
void csharp_bridge_cleanup(void);

// Function pointers to C# exports
typedef int (*csharp_open_fn)(void* vlc_object);
typedef void (*csharp_close_fn)(void* vlc_object);

extern csharp_open_fn csharp_plugin_open;
extern csharp_close_fn csharp_plugin_close;

#endif
```

### csharp_bridge.c

```c
#include "csharp_bridge.h"
#include <windows.h>  // Or dlfcn.h on Unix

static HMODULE csharp_dll = NULL;

csharp_open_fn csharp_plugin_open = NULL;
csharp_close_fn csharp_plugin_close = NULL;

int csharp_bridge_init(void)
{
    // Load C# Native AOT DLL (same directory as glue)
    csharp_dll = LoadLibraryA("VlcPlugin.dll");
    if (!csharp_dll)
        return -1;

    // Resolve exported functions
    csharp_plugin_open = (csharp_open_fn)GetProcAddress(csharp_dll, "CSharpPluginOpen");
    csharp_plugin_close = (csharp_close_fn)GetProcAddress(csharp_dll, "CSharpPluginClose");

    if (!csharp_plugin_open || !csharp_plugin_close)
    {
        FreeLibrary(csharp_dll);
        csharp_dll = NULL;
        return -1;
    }

    return 0;
}

void csharp_bridge_cleanup(void)
{
    if (csharp_dll)
    {
        FreeLibrary(csharp_dll);
        csharp_dll = NULL;
    }
    csharp_plugin_open = NULL;
    csharp_plugin_close = NULL;
}
```

## Type Translation

### Opaque Pointers
- `vlc_object_t*` → `void*` (IntPtr in C#)
- All VLC object types passed as opaque pointers

### Simple Types
- `int`, `unsigned int` → direct mapping
- `bool` → `int` (0/1)
- `vlc_tick_t` (int64_t) → `long`

### Strings
- VLC uses UTF-8 encoded `char*`
- C# marshals as `IntPtr`, manually convert with UTF-8 encoding

### Callbacks
- VLC function pointers → C function pointers
- C# provides `[UnmanagedCallersOnly]` static methods
- Bridge resolves at init time

## Error Handling

### Return Codes
- `VLC_SUCCESS` (0) - Operation successful
- `VLC_EGENERIC` (-1) - Generic error
- `VLC_ENOMEM` (-2) - Out of memory

### Bridge Failures
- If C# DLL load fails, return `VLC_EGENERIC` from Open
- Log errors via VLC's logging system when available

## Acceptance Criteria

1. C glue compiles cleanly with Clang
2. Exports correct VLC plugin symbols
3. Successfully loads C# Native AOT DLL at runtime
4. Resolves C# exported functions without errors
5. Correctly forwards Open/Close calls to C#
6. Handles missing C# DLL gracefully (logs error, returns VLC_EGENERIC)
7. No memory leaks in bridge code
8. Works on Windows (primary), portable to Linux/macOS
