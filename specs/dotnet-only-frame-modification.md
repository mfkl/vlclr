# .NET-Only Frame Modification Specification

## Overview

This spec defines the requirement for video frame modification to occur **exclusively** in the .NET layer (VlcPlugin.dll), with zero pixel manipulation in the C glue layer. The C glue layer should only handle VLC API interactions and pass pixel pointers to .NET.

## Rationale

- Clear separation of concerns: C handles VLC ABI, C# handles business logic
- Easier debugging and testing of overlay rendering
- Full access to ImageSharp and .NET runtime introspection
- Enables future features (dynamic text, animations) without C recompilation

## Architecture

```
VLC Video Pipeline
        │
        ▼
┌──────────────────────────────┐
│  libdotnet_filter_plugin.dll │  C glue
│  - VLC module registration   │
│  - Format validation         │
│  - Picture allocation        │
│  - NO pixel modification     │──────┐
│  - Calls DotNetFilterFrame() │      │
└──────────────────────────────┘      │
                                      │ Raw pixel pointer
                                      ▼
┌──────────────────────────────┐
│  VlcPlugin.dll (native AOT)  │  C# frame processing
│  - DotNetFilterFrame()       │
│  - ALL pixel modification    │
│  - ImageSharp overlay render │
│  - Alpha blending            │
└──────────────────────────────┘
```

## C Glue Responsibilities

The C layer (`video_filter_entry.c`) MUST:

1. Register with VLC as a video filter module
2. Validate chroma format compatibility
3. Load VlcPlugin.dll and resolve exports
4. Allocate output pictures via `filter_NewPicture()`
5. Call `DotNetFilterOpen()` during filter initialization
6. Call `DotNetFilterFrame()` for each frame, passing:
   - Pixel buffer pointer
   - Pitch (bytes per row including padding)
   - Visible pitch (bytes per row of actual pixels)
   - Visible lines (height in pixels)
   - Chroma format (fourcc)
7. Call `DotNetFilterClose()` during cleanup

The C layer MUST NOT:

- Modify pixel data directly
- Draw rectangles, text, or any visual elements
- Perform any image processing

## C# Responsibilities

The .NET layer (`FilterState.cs`, `OverlayRenderer.cs`) MUST:

1. Initialize overlay renderer with font and dimensions
2. Track frame count
3. Render overlay text using ImageSharp
4. Composite overlay onto video frame pixels
5. Handle different chroma formats (I420, BGRA, RGBA, etc.)
6. Log to stderr for debugging

## DotNetFilterFrame Signature

```c
// C declaration
typedef void (*dotnet_filter_frame_fn)(
    void* filter,           // VLC filter_t pointer (for identification)
    uint8_t* pixels,        // Raw pixel buffer (writable)
    int pitch,              // Bytes per row (with padding)
    int visible_pitch,      // Bytes per row (actual pixels)
    int visible_lines,      // Height in pixels
    uint32_t chroma         // VLC fourcc format code
);
```

```csharp
// C# export
[UnmanagedCallersOnly(EntryPoint = "DotNetFilterFrame")]
public static void ProcessFrame(
    nint filter,
    nint pixels,
    int pitch,
    int visiblePitch,
    int visibleLines,
    uint chroma)
```

## Pixel Format Handling in C#

The C# layer must handle these common formats:

| Chroma | FourCC | BPP | Channel Order | Notes |
|--------|--------|-----|---------------|-------|
| I420 | 0x30323449 | 1 | Y plane only | Grayscale overlay on Y |
| YV12 | 0x32315659 | 1 | Y plane only | Same as I420 |
| RV32 | 0x32335652 | 4 | BGRX | Common Windows format |
| BGRA | 0x41524742 | 4 | BGRA | With alpha |
| RGBA | 0x41424752 | 4 | RGBA | Standard |

## Verification

### Debug Output

On first frame, C# should:
1. Write `[VlcPlugin] FilterState initialized: {width}x{height}` to stderr
2. Save overlay to `overlay_test.png` (current working directory)

### Visual Verification

The overlay should display:
```
┌─────────────────────────────┐
│  Hello from .NET 8.0 🟣     │
│  Frame: 1247                │
│  GC0: 12  Heap: 4.2 MB      │
└─────────────────────────────┘
```

## Acceptance Criteria

- [ ] C layer contains zero `pixel[x] = value` statements
- [ ] C layer contains no `draw_*` or `fill_*` functions
- [ ] `DotNetFilterFrame` is called for every frame
- [ ] Overlay is visible on video
- [ ] Frame counter increments correctly
- [ ] Works with I420 format (software decoding)
