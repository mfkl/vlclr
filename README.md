# VLCLR

A VLC 4.x plugin written in C# using Native AOT compilation.

## Overview

This project demonstrates how to write VLC plugins in C# by combining:

- **C glue layer** - Implements VLC plugin entry points and forwards calls to C#
- **C# Native AOT library** - Plugin logic compiled to native code via .NET Native AOT
- **libvlccore bindings** - Generated C# bindings for calling back into VLC

## Components

| Component | Description |
|-----------|-------------|
| `src/VlcPlugin/` | C# Native AOT plugin library |
| `src/glue/` | C layer bridging VLC and .NET |
| `src/VlcPlugin.Tests/` | Unit tests |

## Plugins

- **Interface plugin** (`libdotnet_bridge_plugin.dll`) - Player control, playlist management, event handling
- **Video filter plugin** (`libdotnet_filter_plugin.dll`) - Real-time video frame overlay with text rendering

## Building

Requirements:
- .NET 10 SDK
- LLVM/Clang
- VLC 4.0 SDK (headers + import libraries)

```bash
# Build C# Native AOT
dotnet publish src/VlcPlugin -c Release -r win-x64

# Build C glue (example for video filter)
clang -c -o video_filter_entry.o src/glue/video_filter_entry.c -I<vlc-sdk>/include/vlc/plugins
clang -shared -o libdotnet_filter_plugin.dll video_filter_entry.o <vlc-sdk>/lib/libvlccore.lib
```

## Usage

Copy the plugin DLLs to VLC's plugin directory and regenerate the plugin cache:

```bash
vlc-cache-gen.exe <vlc-path>/plugins
```

Run VLC with the video filter:

```bash
vlc.exe --video-filter=dotnet_overlay --no-hw-dec video.mp4
```

## License

See individual source files for licensing information.
