# VideoOverlay Sample

A VLC 4.x video filter plugin written in C# using Native AOT compilation. This sample demonstrates how to build a video filter that overlays text and graphics onto video frames using the VLCLR framework's base classes and source generator.

## What It Does

This plugin:
- Registers as a VLC video filter (`dotnet_overlay`)
- Processes video frames in various formats (RV32, I420, etc.)
- Renders a text overlay with frame counter and GC statistics
- Demonstrates the `VLCVideoFilterBase` pattern with automatic entry point generation

## Features

- **Zero Boilerplate**: Uses `[VLCModule]` attribute and source generator - no manual entry points
- **Native AOT Compilation**: Compiles to a native DLL with no .NET runtime dependency
- **VLC 4.x Plugin**: Uses the VLC 4.0.6 plugin API
- **Base Class Pattern**: Extends `VLCVideoFilterBase` for automatic lifecycle management
- **Configuration Options**: Demonstrates `[VLCConfig]` attribute for VLC settings
- **ImageSharp Integration**: Uses SixLabors.ImageSharp for text rendering
- **Embedded Fonts**: Includes JetBrains Mono font as an embedded resource

## Building

### Prerequisites

- .NET 10.0 SDK or later
- Visual Studio 2022 Build Tools (for Native AOT)
- Windows x64 (current configuration)

### Build Commands

```bash
# Build the plugin (creates native DLL)
dotnet publish -c Release -r win-x64

# Output location
samples/VideoOverlay/bin/Release/net10.0/win-x64/native/libdotnet_overlay_plugin.dll
```

### Build from Repository Root

```bash
# Build from solution root
dotnet publish samples/VideoOverlay -c Release -r win-x64
```

## Deploying to VLC

1. Copy the built DLL to VLC's plugins folder:
```bash
# Copy to video_filter subfolder (for video filter capability)
cp samples/VideoOverlay/bin/Release/net10.0/win-x64/native/libdotnet_overlay_plugin.dll vlc-binaries/vlc-4.0.0-dev/plugins/video_filter/
```

2. Regenerate VLC's plugin cache:
```bash
vlc-binaries/vlc-4.0.0-dev/vlc-cache-gen.exe vlc-binaries/vlc-4.0.0-dev/plugins
```

3. Verify the plugin is recognized:
```bash
vlc-binaries/vlc-4.0.0-dev/vlc.exe --list | grep dotnet
```

You should see:
```
dotnet_overlay      .NET Overlay
```

## Running

### Basic Usage

```bash
vlc-binaries/vlc-4.0.0-dev/vlc.exe --video-filter dotnet_overlay --no-hw-dec test.mp4
```

### Command Line Options

- `--video-filter dotnet_overlay` - Activates the overlay filter
- `--no-hw-dec` - Disables hardware decoding (required for CPU-accessible frames)
- `--vvv` - Verbose logging (optional, for debugging)

### Configuration Options

The plugin registers these VLC configuration options:

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `dotnet-overlay-opacity` | Float | 1.0 | Overlay opacity (0.0-1.0) |
| `dotnet-overlay-x` | Integer | 10 | X position in pixels |
| `dotnet-overlay-y` | Integer | 10 | Y position in pixels |
| `dotnet-overlay-enabled` | Bool | true | Enable/disable overlay |

Example with custom settings:
```bash
vlc.exe --video-filter dotnet_overlay --dotnet-overlay-opacity 0.5 --no-hw-dec video.mp4
```

## Implementation Details

### Plugin Declaration

The entire plugin is declared using attributes:

```csharp
[VLCModule("dotnet_overlay")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription(".NET Native AOT Video Filter Overlay")]
[VLCConfig("dotnet-overlay-opacity", VLCConfigType.Float, Default = 1.0f, Min = 0.0f, Max = 1.0f,
    Description = "Overlay opacity")]
public partial class VideoOverlayFilter : VLCVideoFilterBase
{
    protected override void ProcessFrame(VLCFrame frame)
    {
        // Frame processing logic
    }
}
```

The source generator automatically creates:
- `vlc_entry` - Module registration
- `vlc_entry_api_version` - API version string
- `vlc_entry_copyright` - Copyright string
- Filter callbacks (FilterVideo, Close, Flush)
- GCHandle management for multi-instance support

### Filter Lifecycle

1. **OnOpen** (optional override):
   - Called when VLC activates the filter
   - Initialize resources (renderer, fonts, etc.)
   - Return `false` to fail initialization

2. **OnFirstFrame** (optional override):
   - Called on the first frame
   - Good for one-time setup that needs frame info

3. **ProcessFrame** (required override):
   - Called for each video frame
   - Access frame data via `VLCFrame` wrapper
   - Modify pixels in-place

4. **OnClose** (optional override):
   - Called when VLC deactivates the filter
   - Clean up resources

### VLCFrame API

The `VLCFrame` wrapper provides safe access to frame data:

```csharp
protected override void ProcessFrame(VLCFrame frame)
{
    // Frame dimensions
    int width = frame.Width;
    int height = frame.Height;

    // Pixel access
    nint pixels = frame.Pixels;      // Pointer to plane 0
    int pitch = frame.Pitch;         // Bytes per line

    // Format info
    uint chroma = frame.Chroma;      // FourCC code
    int planeCount = frame.PlaneCount;

    // Span access for safe manipulation
    Span<byte> data = frame.GetPlaneSpan(0);
}
```

## Project Structure

```
VideoOverlay/
├── VideoOverlay.csproj          # Project file with Native AOT settings
├── VideoOverlayFilter.cs        # Main filter class (single file!)
├── OverlayRenderer.cs           # Text overlay rendering logic
└── Resources/
    └── JetBrainsMono-Regular.ttf # Embedded font
```

## Dependencies

- **VLCLR**: The VLC .NET framework (project reference)
- **SixLabors.ImageSharp**: Image processing (v3.1.12)
- **SixLabors.ImageSharp.Drawing**: Drawing primitives (v2.1.7)
- **SixLabors.Fonts**: Font rendering (v2.1.3)

## Troubleshooting

### Plugin Not Found

```bash
# Check VLC plugin path
vlc.exe --list

# Regenerate plugin cache
vlc-cache-gen.exe ./plugins
```

### Video Filter Not Applied

- Ensure `--no-hw-dec` is used (hardware decoded frames are not CPU-accessible)
- Check video format support (use `--vvv` for format info)
- Verify the plugin is in the `plugins/video_filter/` subfolder

### Build Errors

- Ensure Visual Studio 2022 Build Tools are installed
- Check that .NET 10.0 SDK is installed: `dotnet --version`
- Verify `vswhere.exe` is in PATH (required for Native AOT)

## Notes

- **Multi-instance Support**: Each filter instance has its own state (via GCHandle in filter->p_sys)
- **Debug Output**: The filter writes debug info to VLC's logger with `[VideoOverlay]` prefix
- **Performance**: Native AOT provides excellent performance for frame processing
- **First Frame Capture**: Saves first rendered overlay to `overlay_test.png` for verification

## Learn More

- See `../../README.md` for framework documentation
- See `../SubtitleRenderer/README.md` for text renderer example
- VLC 4.x plugin guide: https://www.videolan.org/developers/vlc.html
