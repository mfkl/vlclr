# GaussianBlur Sample

VLC's Gaussian blur as a C# video filter plugin, built with VLCLR and Native AOT.
It produces exactly the same output as VLC's own `gaussianblur` filter.

The blur itself lives in the framework, in `VLCLR.Filters`
(`VLCLR.Filters.Video.GaussianBlur` and `GaussianBlurFilterBase`), so this
plugin is only its module declaration:

```csharp
[VLCModule("dotnet_gaussianblur")]
[VLCCapability("video filter", Score = 0)]
[VLCConfig("dotnet-gaussianblur-sigma", VLCConfigType.Float, Default = 2.0f, Min = 0.01f, Max = 4096.0f)]
public partial class GaussianBlurPlugin : GaussianBlurFilterBase
{
    protected override string SigmaOption => "dotnet-gaussianblur-sigma";
}
```

## Building

```powershell
dotnet publish samples/GaussianBlur -c Release -r win-x64
```

Copy `bin/Release/net10.0/win-x64/native/libdotnet_gaussianblur_plugin.dll`
into VLC's `plugins/video_filter/` folder, then refresh the plugin cache
(`vlc-cache-gen.exe plugins`).

The fastest implementation (AVX2) needs an AVX2 build:

```powershell
dotnet publish samples/GaussianBlur -c Release -r win-x64 -p:IlcInstructionSet=x86-64-v3
```

## Usage

```
vlc --video-filter=dotnet_gaussianblur --dotnet-gaussianblur-sigma=4 video.mp4
```

Supported input: planar YUV 4:2:0 (I420, YV12) and 4:2:2 (I422), like VLC's
filter. sigma is the width of the bell curve: pixels up to 3 x sigma away are
blended, so sigma 4 is a 25-pixel window.
