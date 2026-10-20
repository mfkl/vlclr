<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo-dark.svg">
    <img src="assets/logo.svg" alt="VLCLR" width="360">
  </picture>
</h1>

> Early development: APIs may change between releases.

**VLC + CLR = VLCLR** is a framework for writing VLC 4.x plugins in C# and
publishing them as Native AOT libraries that VLC loads directly, with no C shim.

Targets VLC 4.0.6 and .NET 10 on:

- Windows x64
- Linux x64
- macOS arm64 and x64

CI builds the plugins and runs them inside VLC 4 nightlies on every platform.

## Minimal plugin

```csharp
using VLCLR.Plugin;

[VLCModule("my_filter")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription("My C# video filter")]
public partial class MyFilter : VLCVideoFilterBase
{
    protected override void ProcessFrame(VLCFrame frame)
    {
        // Inspect or modify the negotiated VLC picture.
    }
}
```

The source generator supplies `vlc_entry`, module registration, native
callbacks, exception handling, and per-instance state. Advanced plugins can use
`VLCLR.Module.ModuleBuilder` directly.

## Samples

| Sample | Capability | Platforms |
|---|---|---|
| [VideoOverlay](samples/VideoOverlay/README.md) | CPU video filter with ImageSharp text and graphics | Windows, Linux, macOS |
| [SubtitleRenderer](samples/SubtitleRenderer/README.md) | ImageSharp text renderer | Windows, Linux, macOS |
| [GaussianBlur](samples/GaussianBlur/README.md) | Port of VLC's Gaussian blur video filter with the same output, SIMD included | Windows, Linux, macOS |
| [Yadif](samples/Yadif/README.md) | Port of VLC's yadif deinterlacer with the same output, SIMD included | Windows, Linux, macOS |

### Advanced samples

These are closed source and available under the commercial license.

| Sample | Capability |
|---|---|
| Privacy Shield | Real-time redaction of people, faces, and license plates during playback, with solid, mosaic, or blur effects. GPU-accelerated end to end |
| Semantic Video Navigator | Describe a moment in plain words and jump straight to it. Indexing runs in the background while playback stays smooth |
| Shader Studio | GPU-accelerated live effects (CRT, Comic, Neon, VHS, Noir, Halftone, Temporal Echo), switched instantly from a panel inside VLC |
| Live Audio Translator | Live speech translation: listens to the audio track, transcribes and translates it, and shows the result as subtitles |
| Subtitle Translator | Offline English-to-French subtitle translation with a local ONNX model, inside the text renderer |
| Object Search | YOLOX object detection on the GPU (D3D11 and OpenVINO), with boxes and labels drawn over the video |

## Requirements

- .NET SDK 10.0.102
- A Native AOT toolchain: MSVC with `vswhere.exe` in `PATH` on Windows, clang
  on Linux, Xcode command line tools on macOS
- A VLC 4 development build compatible with plugin API 4.0.6
- On Windows, `lib/libvlccore.lib` when publishing plugins

## Build and test

```sh
dotnet build vlclr.sln -c Release
dotnet test src/VLCLR.Tests -c Release
dotnet publish samples/VideoOverlay -c Release
```

`dotnet publish` targets the current OS and architecture.

## Repository map

| Path | Purpose |
|---|---|
| `src/VLCLR/` | Core bindings, wrappers, module registration, and plugin base classes |
| `src/VLCLR/Native/Interop/` | The whole libvlccore C API, generated from VLC's headers by `tools/VLCLR.CImport`: raw, and changing when the headers do |
| `src/VLCLR.Generators/` | Attribute-based VLC module source generator |
| `src/VLCLR.ImageSharp/` | Frame and text-rendering helpers |
| `src/VLCLR.Filters/` | Ports of VLC's video filters |
| `samples/` | Native AOT plugin examples, each with its own guide |
| `tests/` | Unit, ABI, integration, and visual tests |
| `benchmarks/` | Rendering and filter performance measurements |

## License

VLCLR is dual-licensed:

- **Open source**: [GNU General Public License v2.0 or later](LICENSE).
- **Commercial**: for use in proprietary software without GPL obligations,
  contact [@mfkl](https://github.com/mfkl).

Third-party components keep their own licenses.
