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

A working video filter that renders a text overlay showing frame counter and timestamp.

See [`samples/VideoOverlay/README.md`](samples/VideoOverlay/README.md) for detailed documentation, build instructions, and usage.

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

### Testing with LibVLCSharp

The easiest way to test the plugin is using the IntegrationTest sample:

```bash
# Build the plugin
dotnet publish samples/VideoOverlay -c Release -r win-x64

# Copy to VLC SDK plugin directory
cp samples/VideoOverlay/bin/Release/net10.0/win-x64/native/libdotnet_overlay_plugin.dll <vlc-sdk-path>/plugins/video_filter/

# Regenerate plugin cache
<vlc-sdk-path>/vlc-cache-gen.exe <vlc-sdk-path>/plugins

# Run integration test
cd tests/IntegrationTest
dotnet run <vlc-sdk-path> <video-url>
```

Example:
```bash
dotnet run ./vlc-sdk file:///C:/path/to/video.mp4
```

The integration test uses LibVLCSharp to programmatically load and test the plugin, verifying that the video filter loads and processes frames successfully.

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
