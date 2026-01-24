# VLC 4.x Plugin ABI Specification

## Overview

This spec defines the requirements for a native plugin to be discovered, loaded, and executed by VLC 4.x's plugin system.

## VLC Version

- Target: VLC 4.x (development branch)
- Source location: `./vlc`

## Required Exported Symbols

VLC plugins must export specific symbols for the plugin loader to recognize them:

### Module Descriptor

```c
// The plugin must define these via VLC's plugin macros
vlc_module_begin()
    set_shortname("C# Plugin")
    set_description("C# Native AOT Plugin via glue layer")
    set_capability("interface", 0)
    set_callbacks(Open, Close)
vlc_module_end()
```

### Key Symbols

1. `vlc_entry_api_version` - ABI version marker
2. `vlc_entry_copyright` - License/copyright string
3. `vlc_entry` - Main entry point function

## Plugin Discovery

### Location
- VLC searches `lib/vlc/plugins/` subdirectories
- Subdirectory name indicates capability (e.g., `interface/`, `video_filter/`)
- For development: use `--plugin-path` to specify custom location

### Naming Convention
- Windows: `libpluginname_plugin.dll`
- Linux: `libpluginname_plugin.so`
- macOS: `libpluginname_plugin.dylib`

### File Extension
- Must match platform conventions
- VLC filters by extension during scan

## Plugin Lifecycle

### 1. Discovery Phase
- VLC scans plugin directories
- Loads each potential plugin temporarily
- Reads module descriptor (capability, priority, callbacks)
- Caches metadata in `plugins.dat`

### 2. Load Phase
- When capability is needed, VLC loads the plugin
- Calls `Open` callback with `vlc_object_t*` parameter
- Plugin initializes its state

### 3. Runtime Phase
- Plugin responds to VLC callbacks
- May call back into libvlccore for VLC services
- Must handle threading correctly (VLC is heavily multithreaded)

### 4. Unload Phase
- VLC calls `Close` callback
- Plugin must release all resources
- Must not leave dangling references

## Required Headers

From `vlc/include/vlc/`:
- `vlc_common.h` - Core types and macros
- `vlc_plugin.h` - Plugin registration macros
- `vlc_interface.h` - Interface plugin specifics
- `vlc_threads.h` - Threading primitives
- `vlc_variables.h` - VLC variable system

## ABI Considerations

### Calling Convention
- Standard C calling convention (`cdecl` on Windows)
- No C++ name mangling (extern "C" if using C++)

### Data Types
- Use VLC's exact type definitions
- `vlc_object_t*` is the base object type
- `vlc_tick_t` for time values
- `block_t*` for data blocks

### Memory Management
- VLC provides allocators: `vlc_alloc`, `vlc_free`
- Plugins must use VLC's allocators for VLC-owned memory
- Plugin-private memory can use standard allocators

### Thread Safety
- Assume callbacks may come from any thread
- Use VLC's synchronization primitives
- Never block the main thread

## Interface Plugin Specifics

For the initial simple plugin targeting "interface" capability:

### Capability String
```c
set_capability("interface", 0)  // Priority 0 (low)
```

### Callbacks
```c
static int Open(vlc_object_t *obj);   // Returns VLC_SUCCESS or VLC_EGENERIC
static void Close(vlc_object_t *obj);
```

### Optional Features
- Can register variables for configuration
- Can listen to VLC events
- Can create UI elements (if GUI available)
- Can be invoked via libvlc API

## Acceptance Criteria

1. Plugin DLL is discovered by VLC on startup (`vlc -vvv --list` shows it)
2. Plugin loads without unresolved symbol errors
3. `Open` callback is invoked when plugin is activated
4. `Close` callback is invoked on shutdown
5. No memory leaks or crashes during load/unload cycle
6. Plugin can be activated via `--intf pluginname` CLI flag
7. Plugin can be activated via libvlc API
