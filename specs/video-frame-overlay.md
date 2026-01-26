# Video Frame Overlay Specification

## Overview

This spec defines a video filter plugin that overlays .NET runtime debug information on each video frame. The overlay demonstrates .NET's ability to intercept and modify VLC video frames in real-time.

## Visual Design

```
┌─────────────────────────────┐
│  Hello from .NET 8.0 🟣     │
│  Frame: 1247                │
│  GC0: 12  Heap: 4.2 MB      │
└─────────────────────────────┘
```

**Overlay characteristics:**
- Semi-transparent black background (RGBA: 0, 0, 0, ~170)
- White text
- Fixed position: top-left corner with padding (10px, 10px)
- Approximate size: 280x80 pixels
- Monospace font (embedded TTF, e.g., JetBrains Mono)

**Content displayed:**
1. .NET runtime version from `RuntimeInformation.FrameworkDescription`
2. Frame counter (incremented each frame)
3. GC Generation 0 collection count + managed heap size in MB

## VLC Video Filter API

### Plugin Capability

```c
set_capability("video filter", 0)  // Priority 0 (low)
```

### Filter Structure

```c
struct vlc_filter_operations {
    picture_t *(*filter_video)(filter_t *, picture_t *);
    void (*flush)(filter_t *);
    void (*close)(filter_t *);
};
```

### Filter Lifecycle

1. **Open**: VLC calls `Open(filter_t *)` when filter is activated
   - Validate input format (check chroma)
   - Initialize filter state (frame counter, .NET runtime info)
   - Set output format = input format (no format change)
   - Assign `filter->ops` to our operations structure

2. **Filter**: VLC calls `filter_video(filter_t *, picture_t *)` per frame
   - Get or create output picture
   - Copy input pixels to output
   - Composite overlay onto output
   - Release input picture
   - Return output picture

3. **Close**: VLC calls `close(filter_t *)` on shutdown
   - Free resources
   - Cleanup .NET state

## Architecture

```
VLC Video Pipeline
        │
        ▼
┌──────────────────────────────┐
│  libdotnet_filter_plugin.dll │  C glue (VLC video filter)
│  - vlc_module_begin/end      │
│  - capability: "video filter"│
│  - Open/Close/Filter cbs     │──────┐
└──────────────────────────────┘      │
                                      │ Function pointers
                                      ▼
┌──────────────────────────────┐
│  VlcPlugin.dll (native AOT)  │  C# frame processing
│  - DotNetFilterOpen()        │
│  - DotNetFilterClose()       │
│  - DotNetFilterFrame()       │
│  - Overlay rendering         │
└──────────────────────────────┘
        │
        ▼
┌──────────────────────────────┐
│  SixLabors.ImageSharp.Fonts  │  Text rendering
│  - Font loading (embedded)   │
│  - Text measurement          │
│  - Glyph rendering           │
└──────────────────────────────┘
```

## Pixel Format Handling

### Supported Formats (Initial)

For simplicity, start with RGB-based formats where overlay compositing is straightforward:

| VLC Chroma | Description | Handling |
|------------|-------------|----------|
| `RV32` | 32-bit RGBA | Direct RGBA overlay |
| `RV24` | 24-bit RGB | RGB overlay (no alpha) |
| `BGRA` | 32-bit BGRA | Swap channels then overlay |

### YUV Format Strategy (Deferred)

VLC commonly uses I420 (YUV 4:2:0). Two approaches for future work:

1. **Request format conversion**: Set `filter->fmt_out` to RGB format, let VLC handle conversion
2. **Direct YUV overlay**: Convert overlay to YUV, composite in YUV space

For initial implementation, request RGB format from VLC.

## ImageSharp.Fonts Integration

### Font Embedding

Embed a TTF font (e.g., JetBrains Mono) as a managed resource:

```csharp
// Load from embedded resource stream
var collection = new FontCollection();
var family = collection.Add(GetEmbeddedFontStream("JetBrainsMono-Regular.ttf"));
var font = family.CreateFont(16, FontStyle.Regular);
```

### Overlay Rendering

```csharp
using var overlay = new Image<Rgba32>(280, 80);

overlay.Mutate(ctx =>
{
    // Semi-transparent background
    ctx.Fill(Color.FromRgba(0, 0, 0, 170));

    // White text
    var textOptions = new RichTextOptions(font)
    {
        Origin = new PointF(8, 8),
        TabWidth = 4
    };

    ctx.DrawText(textOptions, $"Hello from {RuntimeInformation.FrameworkDescription} 🟣", Color.White);
    // ... additional lines
});

// Blit overlay onto frame at (10, 10)
```

### Memory Optimization

- **Reuse overlay Image<Rgba32>** between frames (clear and redraw)
- **Pre-render static elements** (background, "Hello from" prefix) once
- **Update only dynamic content** (frame count, GC stats) per frame

## C Bridge Functions

### New Exports Required

```c
// Filter lifecycle
int dotnet_filter_open(void *filter);
void dotnet_filter_close(void *filter);

// Frame processing
// Returns pointer to output picture (may be same as input if modified in-place)
void *dotnet_filter_frame(void *filter, void *input_picture);

// Picture access helpers
void *dotnet_picture_get_plane(void *picture, int plane_index);
int dotnet_picture_get_pitch(void *picture, int plane_index);
int dotnet_picture_get_lines(void *picture, int plane_index);
int dotnet_picture_get_visible_pitch(void *picture, int plane_index);
int dotnet_picture_get_visible_lines(void *picture, int plane_index);

// Picture format info
uint32_t dotnet_picture_get_chroma(void *picture);
int dotnet_picture_get_width(void *picture);
int dotnet_picture_get_height(void *picture);

// Picture lifecycle
void *dotnet_filter_new_picture(void *filter);
void dotnet_picture_release(void *picture);
void dotnet_picture_copy_properties(void *dst, void *src);
```

## C# Native AOT Exports

```csharp
[UnmanagedCallersOnly(EntryPoint = "DotNetFilterOpen")]
public static int Open(nint filterPtr)
{
    // Initialize filter state, load font, etc.
}

[UnmanagedCallersOnly(EntryPoint = "DotNetFilterClose")]
public static void Close(nint filterPtr)
{
    // Cleanup
}

[UnmanagedCallersOnly(EntryPoint = "DotNetFilterFrame")]
public static nint ProcessFrame(nint filterPtr, nint inputPicture)
{
    // Get output picture
    // Copy pixels
    // Render overlay
    // Composite
    // Return output picture
}
```

## Deployment

```
VLC Installation/
├── vlc.exe
├── libvlccore.dll
└── plugins/
    ├── control/
    │   ├── libdotnet_bridge_plugin.dll   ← Existing interface plugin
    │   └── VlcPlugin.dll
    └── video_filter/
        └── libdotnet_filter_plugin.dll   ← New video filter plugin
```

**Note**: The video filter C glue (`libdotnet_filter_plugin.dll`) can share the same `VlcPlugin.dll` since it's already loaded in the process.

## Activation

```bash
# Enable the filter via command line
vlc --video-filter=dotnet_overlay video.mp4

# Or via VLC preferences: Video > Filters > .NET Overlay
```

## Acceptance Criteria

1. **Filter loads**: VLC lists `dotnet_overlay` in `vlc -vvv --list`
2. **Filter activates**: `--video-filter=dotnet_overlay` doesn't error
3. **Overlay visible**: Debug box appears at top-left corner of video
4. **Frame counter works**: Number increments each frame
5. **GC stats update**: Values change as .NET runtime operates
6. **Performance**: No visible stutter on 1080p video
7. **Format handling**: Works with common video formats (via RGB conversion)

## Dependencies

- **SixLabors.ImageSharp** (NuGet) - Image manipulation
- **SixLabors.Fonts** (NuGet) - Font handling
- **JetBrains Mono** or similar (embedded TTF) - Monospace font

## Future Enhancements (Out of Scope)

- Direct YUV overlay rendering (performance)
- GPU-accelerated rendering (DXVA, VAAPI integration)
- Configurable overlay position/size
- Custom text content via VLC variables
