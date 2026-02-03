# VLCLR

**VLC + CLR = VLCLR** - A framework for building VLC 4.x plugins in C# using Native AOT compilation.

## Overview

VLCLR enables writing VLC plugins entirely in C# without any C code by leveraging .NET Native AOT to compile directly to native DLLs that VLC can load.

Key features:
- **Pure C# implementation** - No C code or interop bridge required
- **Native AOT compilation** - Compiles to native code for direct VLC plugin loading
- **VLC 4.x support** - Built for VLC 4.0.6 plugin API
- **Base classes** - `VLCVideoFilterBase` and `VLCTextRendererBase` handle boilerplate
- **Source generator** - Automatically generates entry points from attributes
- **Configuration system** - Declarative VLC config options via `[VLCConfig]` attribute

## Project Structure

| Component | Description |
|-----------|-------------|
| `src/VLCLR/` | Framework library with VLC bindings, base classes, and helpers |
| `src/VLCLR.Generators/` | Source generator for automatic entry point generation |
| `src/VLCLR.Tests/` | Unit tests for struct layouts and API contracts |
| `samples/VideoOverlay/` | Sample video filter plugin with text overlay |
| `samples/SubtitleRenderer/` | Sample text renderer plugin for subtitles |

### VLCLR Framework

The framework provides:
- **Base classes** (`VLCLR.Plugin`) - `VLCVideoFilterBase`, `VLCTextRendererBase` for easy plugin development
- **Native types** (`VLCLR.Native`) - C# struct definitions matching VLC 4.x (filter_t, picture_t, etc.)
- **Module registration** (`VLCLR.Module`) - Fluent API for plugin entry points
- **Wrapper classes** (`VLCLR`) - High-level C# wrappers (VLCPlayer, VLCPlaylist, VLCLogger, etc.)
- **Text rendering** (`VLCLR.Text`, `VLCLR.Rendering`) - Text parsing and canvas rendering
- **Source generator** - Generates `vlc_entry`, callbacks, and config registration from attributes

## Building

Requirements:
- .NET 10 SDK
- VLC 4.0 import library (`libvlccore.lib` in `lib/`)

```bash
# Build the sample plugins
dotnet publish samples/VideoOverlay -c Release -r win-x64
dotnet publish samples/SubtitleRenderer -c Release -r win-x64

# Run tests
dotnet test src/VLCLR.Tests
```

## Creating a Plugin

### 1. Video Filter (Simplest Example)

```csharp
using VLCLR.Plugin;

[VLCModule("my_filter")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription("My Video Filter")]
public partial class MyFilter : VLCVideoFilterBase
{
    protected override void ProcessFrame(VLCFrame frame)
    {
        // Modify frame.Pixels here
        // frame.Width, frame.Height, frame.Pitch available
    }
}
```

That's it! The source generator creates all entry points automatically.

### 2. With Configuration Options

```csharp
[VLCModule("my_filter")]
[VLCCapability("video filter")]
[VLCConfig("my-filter-opacity", VLCConfigType.Float, Default = 1.0f, Min = 0.0f, Max = 1.0f,
    Description = "Filter opacity")]
[VLCConfig("my-filter-enabled", VLCConfigType.Bool, Default = true,
    Description = "Enable filter")]
public partial class MyFilter : VLCVideoFilterBase
{
    protected override void ProcessFrame(VLCFrame frame)
    {
        // Access config via VLC's var_GetFloat/var_GetBool
    }
}
```

### 3. Text Renderer

```csharp
[VLCModule("my_renderer")]
[VLCCapability("text renderer", Score = 100)]
[VLCDescription("My Text Renderer")]
public partial class MyRenderer : VLCTextRendererBase
{
    protected override nint RenderText(VLCTextRequest request)
    {
        // request.Text, request.Style, request.Alignment available
        // Return pointer to rendered subpicture_region_t
    }
}
```

### 4. Project File Settings

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <PublishAot>true</PublishAot>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <AssemblyName>libmy_plugin_plugin</AssemblyName>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="path/to/VLCLR.csproj" />
  <DirectPInvoke Include="libvlccore" />
  <NativeLibrary Include="path/to/libvlccore.lib" />
</ItemGroup>
```

## Generated Code

The source generator produces:
- `vlc_entry` - Module registration with all attributes
- `vlc_entry_api_version` - VLC API version pointer
- `vlc_entry_copyright` - Copyright string pointer
- Filter callbacks (`FilterVideo`, `Close`, `Flush` or `Render`, `Close`)
- GCHandle management for multi-instance support
- Configuration option registration

## Usage

### Integration Tests (LibVLCSharp)

```bash
# Run VideoOverlay integration test
cd tests/IntegrationTest && dotnet run -- ../../vlc-sdk path/to/video.mp4

# Run SubtitleRenderer integration test
cd tests/SubtitleRendererTest && dotnet run -- ../../vlc-sdk path/to/video.mp4 path/to/subtitles.srt
```

### Manual Testing with VLC

```bash
# Build the plugin
dotnet publish samples/VideoOverlay -c Release -r win-x64

# Copy to VLC plugin directory
cp samples/VideoOverlay/bin/Release/net10.0/win-x64/native/libdotnet_overlay_plugin.dll <vlc-path>/plugins/video_filter/

# Regenerate plugin cache
<vlc-path>/vlc-cache-gen.exe <vlc-path>/plugins

# Run with VLC
vlc.exe --video-filter dotnet_overlay --no-hw-dec video.mp4
```

## Samples

### VideoOverlay

A video filter that overlays debug information (frame count, GC stats) on video frames.

See [`samples/VideoOverlay/README.md`](samples/VideoOverlay/README.md)

### SubtitleRenderer

A text renderer that renders styled subtitles using ImageSharp.

See [`samples/SubtitleRenderer/README.md`](samples/SubtitleRenderer/README.md)

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Source Generator (VLCLR.Generators)                        │
│  - Generates entry points from [VLCModule] attribute        │
│  - Generates callbacks from base class                      │
│  - Zero boilerplate for plugin author                       │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ uses
┌─────────────────────────────────────────────────────────────┐
│  Base Classes (VLCLR.Plugin)                                │
│  - VLCVideoFilterBase, VLCTextRendererBase                  │
│  - Handles state management, lifecycle, error handling      │
│  - Plugin author overrides virtual methods                  │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ uses
┌─────────────────────────────────────────────────────────────┐
│  Core Framework (VLCLR)                                     │
│  - ModuleBuilder, native types, wrappers                    │
│  - Direct control for advanced users                        │
└─────────────────────────────────────────────────────────────┘
```

## License

See individual source files for licensing information.
