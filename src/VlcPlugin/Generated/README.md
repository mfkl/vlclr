# VLC Generated Bindings

This directory contains C# type definitions for VLC structures, enums, and constants.

## Why Manual Instead of Auto-Generated?

These bindings are maintained manually due to practical limitations:

1. **Variadic Functions**: VLC's logging and other core functions are variadic (`printf`-style) which cannot be called via P/Invoke directly
2. **C Bridge Requirement**: Due to (1), function calls go through a C bridge layer (`libdotnet_bridge_plugin`), not direct P/Invoke to `libvlccore`
3. **Minimal Surface Area**: Only types, enums, and constants are needed - not function bindings

## Files

| File | Purpose | VLC Header Source |
|------|---------|-------------------|
| `VlcTypes.cs` | Enums and structs | `vlc_messages.h`, `vlc_variables.h`, `vlc_player.h` |
| `VlcConstants.cs` | Constant values | `vlc_variables.h`, `vlc_playlist.h` |

## Updating Bindings

When VLC headers change:

1. Compare the source headers in `vlc/include/` with the C# files
2. Update types/constants as needed, preserving XML documentation
3. Update the "VLC Version" comment at the top of each file
4. Run tests: `dotnet test src/VlcPlugin.Tests`

## Architecture

```
VLC Process
    |
libdotnet_bridge_plugin.dll (C glue) <-- Wraps variadic VLC functions
    |
VlcPlugin.dll (C# Native AOT)
    |
+-- Native/VlcBridge.cs      <-- P/Invoke to C glue (NOT libvlccore)
+-- Generated/VlcTypes.cs    <-- Type definitions (this directory)
+-- Generated/VlcConstants.cs
```

The C bridge approach is the correct architecture for VLC plugins using Native AOT.
