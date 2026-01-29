# VLCLR

**VLC + CLR = VLCLR** - A framework for building VLC 4.x plugins in C# using Native AOT compilation.

## Overview

VLCLR enables writing VLC plugins entirely in C# without any C code by leveraging .NET Native AOT to compile directly to native DLLs that VLC can load.

Key features:
- **Pure C# implementation** - No C code or interop bridge required
- **Native AOT compilation** - Compiles to native code for direct VLC plugin loading
- **VLC 4.x support** - Built for VLC 4.0.6 plugin API
- **Fluent API** - Clean module registration with `ModuleBuilder`

## Project Structure

| Component | Description |
|-----------|-------------|
| `src/VLCLR/` | Framework library with VLC bindings and helpers |
| `src/VLCLR.Tests/` | Unit tests for struct layouts and API contracts |
| `samples/VideoOverlay/` | Sample video filter plugin with text overlay |

### VLCLR Framework

The framework provides:
- **Native types** (`VLCLR.Native`) - C# struct definitions matching VLC 4.x (filter_t, picture_t, etc.)
- **Module registration** (`VLCLR.Module`) - Fluent API for plugin entry points
- **Wrapper classes** (`VLCLR`) - High-level C# wrappers (VLCPlayer, VLCPlaylist, VLCLogger, etc.)
- **P/Invoke bindings** - Direct calls to libvlccore functions

### VideoOverlay Sample

A working video filter that renders a text overlay showing:
- .NET runtime version
- Frame counter
- GC statistics

## Building

Requirements:
- .NET 10 SDK
- VLC 4.0 import library (`libvlccore.lib` in `lib/`)

```bash
# Build the sample plugin
dotnet publish samples/VideoOverlay -c Release -r win-x64

# Run tests
dotnet test src/VLCLR.Tests
```

## Usage

1. Copy the plugin DLL to VLC's plugin directory:
```bash
cp samples/VideoOverlay/bin/Release/net10.0/win-x64/native/libdotnet_plugin.dll <vlc-path>/plugins/video_filter/
```

2. Regenerate the plugin cache:
```bash
vlc-cache-gen.exe <vlc-path>/plugins
```

3. Run VLC with the video filter:
```bash
vlc.exe --video-filter=dotnet_overlay --no-hw-dec video.mp4
```

## Creating Your Own Plugin

1. Create a new .NET project referencing VLCLR
2. Implement module entry point using `ModuleBuilder`:

```csharp
[UnmanagedCallersOnly(EntryPoint = "vlc_entry")]
public static int VlcEntry(nint vlcSetPtr, nint opaque)
{
    return ModuleBuilder.Create(vlcSetPtr, opaque)
        .WithName("my_plugin")
        .WithCapability("video filter")
        .WithOpenCallback(&MyFilterOpen)
        .Register();
}
```

3. Add project file settings for Native AOT:
```xml
<PublishAot>true</PublishAot>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<DirectPInvoke Include="libvlccore" />
<NativeLibrary Include="path/to/libvlccore.lib" />
```

## License

See individual source files for licensing information.
