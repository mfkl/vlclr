# SubtitleRenderer Sample

A VLC 4.x text renderer plugin written in C# using Native AOT compilation. This sample demonstrates how to build a subtitle renderer that receives parsed subtitle text with styling from VLC and renders it to pixels using ImageSharp.

## What It Does

This plugin:
- Registers as a VLC text renderer (`dotnet_subtitle`)
- Receives `subpicture_region_t` containing `text_segment_t` chains from VLC
- Parses text styling information from `text_style_t` (fonts, colors, outlines, shadows)
- Renders styled text to pixels using ImageSharp
- Returns a rendered `picture_t` to VLC for compositing onto video

## Features

- **Native AOT Compilation**: Compiles to a native DLL with no .NET runtime dependency
- **VLC 4.x Plugin**: Uses the VLC 4.0.6 plugin API
- **Text Renderer**: Implements VLC's text renderer interface
- **Styled Subtitles**: Supports fonts, colors, bold, italic, outline, shadow, background box
- **ImageSharp Integration**: Uses SixLabors.ImageSharp for text rendering
- **Embedded Fonts**: Includes JetBrains Mono font as an embedded resource
- **Font Caching**: Efficient font loading and caching by name/size/style
- **Canvas Reuse**: Reuses rendering canvas for performance

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

### Example with URL

```bash
vlc.exe --text-renderer dotnet_subtitle --sub-file "C:/path/to/subtitles.srt" "file:///C:/path/to/video.mp4"
```

## Implementation Details

### Module Registration

The plugin exports three entry points:
- `vlc_entry` - Main entry point for module registration
- `vlc_entry_api_version` - VLC API version (4.0.6)
- `vlc_entry_copyright` - Copyright information

Module registration uses VLCLR's fluent API:

```csharp
ModuleBuilder.Create(vlcSetPtr, opaque)
    .WithName("dotnet_subtitle")
    .WithShortName(".NET Subtitle")
    .WithDescription(".NET Native AOT Text Renderer for Subtitles")
    .WithCapability("text renderer")
    .WithScore(100)  // Higher score to be preferred
    .WithOpenCallback(&FilterOpen)
    .Register();
```

### Renderer Lifecycle

1. **Open** (`FilterOpen`): 
   - Initializes renderer state
   - Sets up the renderer operations structure
   - Returns 0 for success

2. **Render** (`RenderCallback`):
   - Called for each subtitle that needs rendering
   - Receives text segments with styling from VLC
   - Renders text to an ImageSharp canvas
   - Converts canvas to VLC picture format
   - Returns subpicture region for VLC to composite

3. **Close** (`CloseCallback`):
   - Cleans up renderer resources
   - Called when the renderer is deactivated

### Text Rendering Pipeline

1. **Parse**: Extract text and style from VLC's text segment linked list
2. **Style**: Convert VLC `text_style_t` to `SubtitleStyle` with C# properties
3. **Layout**: Calculate text position based on alignment (left/center/right)
4. **Render** (in order):
   - Background box (if enabled)
   - Drop shadow (if enabled)
   - Text outline (if enabled)
   - Foreground text
5. **Convert**: Copy RGBA pixels to VLC picture in appropriate chroma format
6. **Return**: Subpicture region for VLC compositing

### Styling Support

The renderer supports these styling features from VLC:
- **Font**: Family name, size, bold, italic
- **Color**: Foreground RGB, alpha transparency
- **Outline**: Color, alpha, width in pixels
- **Shadow**: Color, alpha, offset in pixels
- **Background**: Color, alpha (semi-transparent box behind text)

**Note**: If VLC passes black text color (0x000000), the renderer forces white text
with outline for visibility on dark videos.

### Output Formats

The renderer outputs RGBA or BGRA format depending on VLC's supported chromas:
- Prefers RGBA (native ImageSharp format)
- Falls back to BGRA with pixel swizzling if needed

### Debug Output

Set the `DOTNET_SUBTITLE_DEBUG_PATH` environment variable to save the first
rendered subtitle as a PNG file:

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
├── ModuleDescriptor.cs          # VLC entry points and renderer callbacks
├── RendererState.cs             # Renderer state management and render loop
├── SubtitleStyle.cs             # VLC text_style_t wrapper with C# properties
├── TextSegmentParser.cs         # Parses VLC text segment linked lists
├── FontManager.cs               # Font loading and caching
├── SubtitleCanvas.cs            # ImageSharp canvas for rendering text
├── PictureConverter.cs          # Converts ImageSharp to VLC picture
└── Resources/
    └── JetBrainsMono-Regular.ttf # Embedded default font
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

## Testing

### Integration Test

```bash
# Run the integration test with a test subtitle file
dotnet run --project tests/SubtitleRendererTest -- \
  vlc-binaries/vlc-4.0.0-dev \
  "C:/path/to/video.mp4" \
  "tests/SubtitleRendererTest/fixtures/test.srt" \
  10
```

### Test Subtitle Files

The test fixtures include:
- `test.srt` - Basic SRT subtitles (5 entries, 1-15 seconds)
- `styled.ass` - ASS subtitles with multiple styles (bold, italic, colors)
- `positioned.srt` - SRT with alignment tags

## Performance Notes

- **Font Caching**: Fonts are cached by name/size/style combination (max 32 entries)
- **Canvas Reuse**: The rendering canvas is reused across render calls
- **Memory**: All allocations happen during first render; subsequent renders reuse buffers
- **Native AOT**: No JIT compilation overhead; excellent frame-to-frame consistency

## Notes

- **Use Git Bash**: Windows Terminal/PowerShell may hang when running VLC interactively
- **Debug Output**: The renderer writes debug info to stderr with `[.NET Subtitle]` prefix
- **Thread Safety**: All rendering is done on VLC's video output thread
- **Fallback**: If rendering fails, returns null (VLC will use fallback renderer or skip subtitle)

## Learn More

- See `../../src/VLCLR/README.md` for framework documentation
- See `../VideoOverlay/README.md` for video filter example
- VLC headers: `../../vlc/include/vlc/vlc_text_style.h`, `vlc_subpicture.h`
- VLC 4.x plugin guide: https://www.videolan.org/developers/vlc.html
