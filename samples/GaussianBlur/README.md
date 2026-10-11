# GaussianBlur Sample

VLC's Gaussian blur as a C# video filter plugin, built with VLCLR and Native AOT.
It produces exactly the same output as VLC's own `gaussianblur` filter.

The blur engine lives in the framework, in `VLCLR.Filters`
(`VLCLR.Filters.Video.GaussianBlur`: a line-for-line port of VLC's C code plus
tuned and AVX2 versions, all bit-exact). [`GaussianBlurPlugin.cs`](GaussianBlurPlugin.cs)
is the VLC video filter around it:

- **Open**: check the chroma (planar YUV, same in and out), read the
  `--dotnet-gaussianblur-sigma` option and create the blur.
- **Each frame**: get a new picture from VLC's output pool
  (`Context.NewPicture()`) and blur the input into it. The blur can't work in
  place; `VLCVideoFilterBase` copies the input's timestamps onto the output and
  releases the input.
- **Close**: free the blur's buffers.

```csharp
protected override nint ProcessFrameToOutput(VLCFrame frame)
{
    nint outputPicture = Context.NewPicture();
    if (outputPicture == 0)
    {
        return 0;
    }
    _blur!.Apply(frame, new VLCFrame(outputPicture, Context));
    return outputPicture;
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
