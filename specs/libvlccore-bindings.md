# libvlccore C# Bindings Specification

## Overview

This spec covers the C# bindings for `libvlccore`, enabling the C# plugin to call back into VLC for services like logging, variables, events, player state, and playlist control.

## Architecture: C Bridge Layer

### Why Not Direct P/Invoke to libvlccore?

Direct P/Invoke to libvlccore is not possible due to:

1. **Variadic Functions**: Core VLC functions like `msg_Info`, `msg_Warn`, `msg_Err` use variadic arguments (`...`). C# P/Invoke cannot call variadic C functions directly - there is no standard way to marshal variable argument lists.

2. **Internal Type Complexity**: VLC's internal types (`vlc_object_t`, opaque structures) use complex inheritance and memory layouts that require C-level understanding.

### Solution: C Bridge Layer (libdotnet_bridge_plugin)

Instead of direct P/Invoke to libvlccore, this project uses a **C bridge layer**:

```
┌─────────────────┐     P/Invoke      ┌────────────────────────┐     Direct C calls     ┌─────────────┐
│   C# Plugin     │ ───────────────▶ │    C Bridge Layer       │ ───────────────────▶  │  libvlccore │
│  (VlcPlugin.dll)│                   │(libdotnet_bridge_plugin)│                        │             │
└─────────────────┘                   └────────────────────────┘                        └─────────────┘
```

The C bridge:
- Wraps VLC's variadic functions with fixed-argument equivalents
- Handles VLC threading (Lock/Unlock) correctly
- Manages memory allocation between C and C# runtimes
- Provides a stable, simple ABI for P/Invoke

### Bridge Implementation Files

| File | Purpose |
|------|---------|
| `src/glue/dotnet_bridge.c` | Bridge function implementations |
| `src/glue/dotnet_bridge.h` | Bridge function declarations |
| `src/VlcPlugin/Native/VlcBridge.cs` | C# P/Invoke declarations to the bridge |

### Bridge Function Naming Convention

Bridge functions use the `dotnet_bridge_` prefix:

| C Bridge Function | Purpose |
|------------------|---------|
| `dotnet_bridge_log` | Log to VLC (wraps variadic vlc_object_Log) |
| `dotnet_bridge_var_create` | Create VLC variable |
| `dotnet_bridge_var_set_integer` | Set integer variable |
| `dotnet_bridge_var_get_string` | Get string variable |
| `dotnet_bridge_get_player` | Get player from interface |
| `dotnet_bridge_player_*` | Player control (state, seek, pause, volume, mute) |
| `dotnet_bridge_playlist_*` | Playlist control functions |

## Generated/ Directory (Manually Maintained)

The `src/VlcPlugin/Generated/` directory contains manually maintained type definitions:

```
src/VlcPlugin/Generated/
├── VlcTypes.cs         # Enums and structs (VlcLogType, VlcPlayerState, VlcValue, etc.)
└── VlcConstants.cs     # VLC constant values (VlcVarType, VlcVarAction, etc.)
```

### Why Manual Maintenance?

These files are maintained manually because:

1. Only a subset of VLC types are needed for the C# plugin
2. Manual maintenance allows for proper C# documentation and naming conventions
3. Types can be kept in sync with the C bridge layer's needs

### Updating Generated Files

When VLC headers change:

1. Compare `vlc/include/vlc_*.h` with current Generated/ files
2. Update enums, structs, and constants as needed
3. Update the "VLC Version" comment at the top of each file
4. Ensure C bridge layer is also updated if function signatures change

### Current File Structure

**VlcTypes.cs** contains:
- `VlcLogType` enum (from vlc_messages.h)
- `VlcPlayerState` enum (from vlc_player.h)
- `VlcVarAtomicOp` enum (from vlc_variables.h)
- `VlcValue` struct (from vlc_variables.h)
- `VlcLog` struct (from vlc_messages.h)

**VlcConstants.cs** contains:
- `VlcVarType` class with variable type constants
- `VlcVarAction` class with var_Change action constants
- `VlcPlaylistOrder` class with playlist order constants
- `VlcPlaylistRepeat` class with repeat mode constants

## P/Invoke Declarations (VlcBridge.cs)

The actual P/Invoke declarations are in `src/VlcPlugin/Native/VlcBridge.cs`:

```csharp
internal static partial class VlcBridge
{
    private const string LibraryName = "libdotnet_bridge_plugin";

    [LibraryImport(LibraryName, EntryPoint = "dotnet_bridge_log",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Log(nint vlcObject, int type, string message);

    [LibraryImport(LibraryName, EntryPoint = "dotnet_bridge_var_set_integer",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int VarSetInteger(nint vlcObject, string name, long value);

    // ... more bridge function declarations
}
```

Key points:
- Uses `LibraryImport` (source-generated P/Invoke) for AOT compatibility
- All calls go to `libdotnet_bridge_plugin`, NOT `libvlccore`
- Uses `StringMarshalling.Utf8` for string parameters
- VLC object pointers are passed as `nint`

## Bound Functionality

### Currently Implemented

| Category | Functions |
|----------|-----------|
| **Logging** | Log messages at Info/Warn/Error/Debug levels |
| **Variables** | Create, destroy, get/set integer and string variables |
| **Player** | Get state, add/remove event listeners, seeking (time/position), pause/resume |
| **Audio** | Volume control (get/set), mute control (get/set/toggle) |
| **Playlist** | Start, stop, pause, resume, next, prev, goto, count |
| **Objects** | Get parent, get typename |

### Player Control Functions

The bridge provides comprehensive player control:

```c
// State and position
dotnet_bridge_player_get_state(player)     // Returns VlcPlayerState enum value
dotnet_bridge_player_get_time(player)      // Returns current time in microseconds
dotnet_bridge_player_get_length(player)    // Returns total length in microseconds
dotnet_bridge_player_get_position(player)  // Returns position as ratio [0.0, 1.0]

// Seeking
dotnet_bridge_player_seek_by_time(player, time, speed, whence)  // Seek by microseconds
dotnet_bridge_player_seek_by_pos(player, pos, speed, whence)    // Seek by ratio
dotnet_bridge_player_can_seek(player)      // Returns 1 if seekable

// Playback control
dotnet_bridge_player_pause(player)         // Pause playback
dotnet_bridge_player_resume(player)        // Resume playback
dotnet_bridge_player_can_pause(player)     // Returns 1 if pausable

// Audio output control
dotnet_bridge_player_get_volume(player)    // Returns volume [0.0, 2.0], -1.0 if no audio
dotnet_bridge_player_set_volume(player, v) // Set volume, returns 0 on success
dotnet_bridge_player_is_muted(player)      // Returns 0/1 for mute state, -1 if no audio
dotnet_bridge_player_set_mute(player, m)   // Set mute (0/1), returns 0 on success
dotnet_bridge_player_toggle_mute(player)   // Toggle mute, returns 0 on success
```

The C# `VlcPlayer` class exposes these through:
- `Volume` property (get/set, range 0.0-2.0)
- `IsMuted` property (get/set, nullable bool for no-audio case)
- `ToggleMute()` method (returns success bool)

### Player Event Callbacks

The bridge supports callbacks from VLC to C# for player events:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct PlayerCallbacksNative
{
    public nint OnStateChanged;
    public nint OnPositionChanged;
    public nint OnMediaChanged;
    public nint UserData;
}
```

## AOT Compatibility

The implementation is fully AOT-compatible:

1. Uses `LibraryImport` instead of `DllImport` for source-generated marshalling
2. No runtime code generation
3. Explicit struct layouts for callback structures
4. Direct P/Invoke with no dynamic library loading from C#

```xml
<!-- In VlcPlugin.csproj -->
<PropertyGroup>
    <PublishAot>true</PublishAot>
</PropertyGroup>
```

## Adding New Bindings

To expose additional VLC functionality:

1. **Add C bridge function** in `src/glue/dotnet_bridge.c`:
   ```c
   BRIDGE_API int dotnet_bridge_new_function(void* obj, const char* param)
   {
       return vlc_actual_function((vlc_object_t*)obj, param);
   }
   ```

2. **Declare in header** `src/glue/dotnet_bridge.h`:
   ```c
   BRIDGE_API int dotnet_bridge_new_function(void* obj, const char* param);
   ```

3. **Add P/Invoke declaration** in `src/VlcPlugin/Native/VlcBridge.cs`:
   ```csharp
   [LibraryImport(LibraryName, EntryPoint = "dotnet_bridge_new_function",
       StringMarshalling = StringMarshalling.Utf8)]
   internal static partial int NewFunction(nint obj, string param);
   ```

4. **Add types** to `src/VlcPlugin/Generated/` if needed

5. **Create high-level wrapper** if appropriate (e.g., in `VlcLogger.cs`, `VlcVariable.cs`)

## Testing

### Unit Tests

Located in `tests/VlcPlugin.Tests/`:
- Test high-level wrapper functionality
- Mock the bridge layer for isolated testing
- Verify type definitions match expected values

### Integration Tests

Require VLC to be loaded:
- Test actual VLC function calls through the bridge
- Verify callback behavior
- Test with real media playback

## Version Compatibility

### VLC Version

Current bindings target VLC 4.0.6. Type definitions in Generated/ files include version comments.

### Updating for New VLC Versions

1. Update VLC submodule or headers
2. Check for API changes in relevant headers
3. Update C bridge functions if signatures changed
4. Update Generated/ type definitions
5. Update version comments in files
6. Run tests to verify compatibility

## Acceptance Criteria

1. C bridge functions wrap all needed VLC APIs
2. P/Invoke declarations match bridge function signatures
3. Type definitions in Generated/ match VLC headers
4. AOT-compatible (no runtime code generation)
5. Trim-safe (no reflection issues)
6. High-level wrappers provide usable API
7. Works when loaded alongside libvlccore
8. Documented update process for new VLC versions
