# SubtitleRenderer Sample

A VLC 4.x text renderer plugin written in C# using Native AOT compilation. This sample demonstrates how to build a subtitle renderer that receives parsed subtitle text with styling from VLC and renders it to pixels using ImageSharp, leveraging the VLCLR framework's base classes and source generator.

## What It Does

This plugin:
- Registers as a VLC text renderer (`dotnet_subtitle`)
- Receives `subpicture_region_t` containing `text_segment_t` chains from VLC
- Parses text styling information from `text_style_t` (fonts, colors, outlines, shadows)
- Renders styled text to pixels using ImageSharp
- Returns a rendered `picture_t` to VLC for compositing onto video
- Demonstrates the `VLCTextRendererBase` pattern with automatic entry point generation

## Features

- **Zero Boilerplate**: Uses `[VLCModule]` attribute and source generator - no manual entry points
- **Native AOT Compilation**: Compiles to a native DLL with no .NET runtime dependency
- **VLC 4.x Plugin**: Uses the VLC 4.0.6 plugin API
- **Base Class Pattern**: Extends `VLCTextRendererBase` for automatic lifecycle management
- **Configuration Options**: Demonstrates `[VLCConfig]` attribute for VLC settings
- **Styled Subtitles**: Supports fonts, colors, bold, italic, outline, shadow, background box
- **ImageSharp Integration**: Uses SixLabors.ImageSharp for text rendering
- **Embedded Fonts**: Includes JetBrains Mono font as an embedded resource
- **Multi-instance Support**: Each renderer instance maintains separate state

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
samples/SubtitleRenderer/bin/Release/net10.0/win-x64/native/libdotnet_subtitle_plugin.dll
```

### Build from Repository Root

```bash
# Build from solution root
dotnet publish samples/SubtitleRenderer -c Release -r win-x64
```

## Deploying to VLC

1. Copy the built DLL to VLC's plugins folder:
```bash
# Copy to spu subfolder (for text renderer capability)
cp samples/SubtitleRenderer/bin/Release/net10.0/win-x64/native/libdotnet_subtitle_plugin.dll vlc-binaries/vlc-4.0.0-dev/plugins/spu/
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
dotnet_subtitle     .NET Subtitle
```

## Running

### Basic Usage with Subtitle File

```bash
vlc-binaries/vlc-4.0.0-dev/vlc.exe --text-renderer dotnet_subtitle --sub-file subtitles.srt test.mp4
```

### Command Line Options

- `--text-renderer dotnet_subtitle` - Activates the subtitle renderer
- `--sub-file <path>` - Load subtitle file (SRT, ASS, etc.)
- `--no-hw-dec` - Disables hardware decoding (optional, for debugging)
- `--vvv` - Verbose logging (optional, for debugging)

### Configuration Options

The plugin registers these VLC configuration options:

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `dotnet-subtitle-force-outline` | Bool | true | Always render text outline |
| `dotnet-subtitle-outline-width` | Integer | 3 | Outline width in pixels (1-10) |
| `dotnet-subtitle-force-white` | Bool | true | Force white text when VLC sends black |

Example with custom settings:
```bash
vlc.exe --text-renderer dotnet_subtitle --dotnet-subtitle-outline-width 5 --sub-file test.srt video.mp4
```

## Implementation Details

### Plugin Declaration

The entire plugin is declared using attributes:

```csharp
[VLCModule("dotnet_subtitle")]
[VLCCapability("text renderer", Score = 100)]
[VLCDescription(".NET Native AOT Text Renderer for Subtitles")]
[VLCConfig("dotnet-subtitle-force-outline", VLCConfigType.Bool, Default = true,
    Description = "Force text outline")]
[VLCConfig("dotnet-subtitle-outline-width", VLCConfigType.Integer, Default = 3, Min = 1, Max = 10,
    Description = "Outline width")]
public partial class SubtitleTextRenderer : VLCTextRendererBase
{
    protected override nint RenderText(VLCTextRequest request)
    {
        // Text rendering logic
    }
}
```

The source generator automatically creates:
- `vlc_entry` - Module registration
- `vlc_entry_api_version` - API version string
- `vlc_entry_copyright` - Copyright string
- Renderer callbacks (Render, Close)
- GCHandle management for multi-instance support

### Renderer Lifecycle

1. **OnOpen** (optional override):
   - Called when VLC activates the renderer
   - Initialize fonts, canvas, resources
   - Return `false` to fail initialization

2. **RenderText** (required override):
   - Called for each subtitle that needs rendering
   - Receives `VLCTextRequest` with text and style
   - Access `RegionPtr` for raw segment parsing
   - Access `ChromaListPtr` for output format selection
   - Return pointer to rendered `subpicture_region_t`

3. **OnClose** (optional override):
   - Called when VLC deactivates the renderer
   - Clean up resources

### VLCTextRequest API

The `VLCTextRequest` wrapper provides access to text data:

```csharp
protected override nint RenderText(VLCTextRequest request)
{
    // Text content
    string text = request.Text;
    bool hasText = request.HasText;

    // Style information
    int fontSize = request.FontSize;
    uint color = request.FontColorArgb;
    bool bold = request.IsBold;
    bool italic = request.IsItalic;

    // Alignment
    TextAlignment alignment = request.HorizontalAlignment;
    TextVerticalPosition vpos = request.VerticalPosition;

    // For advanced segment parsing:
    var segments = TextSegmentParser.ParseWithVisibility(RegionPtr);
}
```

### Text Rendering Pipeline

1. **Parse**: Extract text and style from `RegionPtr` using `TextSegmentParser`
2. **Style**: Apply visibility optimizations (force white text, force outline)
3. **Layout**: Calculate text position based on alignment
4. **Render**: Draw background → shadow → outline → foreground text
5. **Convert**: Convert ImageSharp image to VLC picture using `PictureConverter`
6. **Return**: Subpicture region pointer for VLC compositing

### Debug Output

Set the `DOTNET_SUBTITLE_DEBUG_PATH` environment variable to save the first rendered subtitle as a PNG file:

```bash
# Bash/Git Bash
export DOTNET_SUBTITLE_DEBUG_PATH=./subtitle_debug.png
vlc.exe --text-renderer dotnet_subtitle --sub-file test.srt video.mp4

# PowerShell
$env:DOTNET_SUBTITLE_DEBUG_PATH = "./subtitle_debug.png"
vlc.exe --text-renderer dotnet_subtitle --sub-file test.srt video.mp4
```

## Project Structure

```
SubtitleRenderer/
├── SubtitleRenderer.csproj      # Project file with Native AOT settings
├── SubtitleTextRenderer.cs      # Main renderer class (single file!)
└── Resources/
    └── JetBrainsMono-Regular.ttf # Embedded default font
```

Note: The implementation is now a single file thanks to the base class and source generator!

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

# Verify in spu folder (not video_filter)
ls vlc-binaries/vlc-4.0.0-dev/plugins/spu/ | grep dotnet
```

### Subtitles Not Appearing

- Ensure subtitle file path is correct and absolute
- Check that the subtitle timing overlaps with video playback time
- Use `--vvv` to see verbose logs from the renderer
- Enable debug output with `DOTNET_SUBTITLE_DEBUG_PATH` to verify rendering

### Text Not Visible

- Text color defaults to white (forced if VLC sends black)
- Outline is always enabled for visibility
- Check that `libdotnet_subtitle_plugin.dll` is in `plugins/spu/` folder

### Build Errors

- Ensure Visual Studio 2022 Build Tools are installed
- Check that .NET 10.0 SDK is installed: `dotnet --version`
- Verify `vswhere.exe` is in PATH (required for Native AOT)

## Performance Notes

- **Canvas Reuse**: The rendering canvas is reused across render calls
- **Memory**: All allocations happen during first render; subsequent renders reuse buffers
- **Native AOT**: No JIT compilation overhead; excellent frame-to-frame consistency
- **Multi-instance**: Each renderer instance maintains its own canvas and state

## Notes

- **Multi-instance Support**: Each renderer instance has its own state (via GCHandle in filter->p_sys)
- **Debug Output**: The renderer writes debug info to VLC's logger with `[SubtitleTextRenderer]` prefix
- **Thread Safety**: All rendering is done on VLC's video output thread
- **Fallback**: If rendering fails, returns null (VLC will use fallback renderer or skip subtitle)

## Learn More

- See `../../README.md` for framework documentation
- See `../VideoOverlay/README.md` for video filter example
- VLC headers: `../../vlc/include/vlc/vlc_text_style.h`, `vlc_subpicture.h`
- VLC 4.x plugin guide: https://www.videolan.org/developers/vlc.html
