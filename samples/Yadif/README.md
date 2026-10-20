# Yadif Sample

VLC's yadif deinterlacer as a C# video filter plugin, built with VLCLR and
Native AOT. It produces exactly the same pictures as VLC's own `deinterlace`
filter in `yadif` mode, with the same timestamps.

The deinterlacing lives in the framework, in `VLCLR.Filters`:
`VLCLR.Filters.Video.Yadif` (a line-for-line port of VLC's C kernel plus
scalar, `Vector128`, `Vector256` and `Vector512` versions, all bit-exact) and
`VLCLR.Filters.Video.XDeinterlace` (VLC's "X" algorithm, which yadif uses for
the first picture). [`YadifPlugin.cs`](YadifPlugin.cs) is the VLC video filter
around them, and keeps the picture history the way VLC's deinterlace filter
does:

- **Open**: check the chroma (planar YUV, 8 to 16 bits, same in and out) and
  create the deinterlacer for its bit depth.
- **Each frame**: hold the input in a history of three pictures
  (`VLCCore.PictureHold`), get a new picture from VLC's output pool and
  render into it:
  - first picture: X, on that picture alone;
  - second picture: yadif does not have the three pictures it needs yet, and
    the picture is dropped, as VLC does (VLC's builds with `NDEBUG` return
    the input instead, still interlaced);
  - then: yadif of the previous picture, from the pictures before and after
    it. The output is one picture behind the input.
- **Output properties**: `SetOutputProperties` gives the output the date of
  the picture it was made from and marks it progressive.
- **Flush and close**: release the history; the next picture is a first one.

## Building

```powershell
dotnet publish samples/Yadif -c Release -r win-x64
```

Copy `bin/Release/net10.0/win-x64/native/libdotnet_yadif_plugin.dll` into
VLC's `plugins/video_filter/` folder, then refresh the plugin cache
(`vlc-cache-gen.exe plugins`).

The vector implementations need a build for the matching instruction set,
for example AVX2 (`Vector256`):

```powershell
dotnet publish samples/Yadif -c Release -r win-x64 -p:IlcInstructionSet=x86-64-v3
```

## Usage

```
vlc --video-filter=dotnet_yadif interlaced.ts
```

As a video filter, it deinterlaces every picture, progressive or not; VLC's
own deinterlacer is inserted by the video output only for interlaced
pictures. Supported input: planar YUV with one or two bytes per sample
(I420, I422, I444, their 10 and 16-bit variants, ...). For packed chromas,
where VLC falls back to its blend mode, the filter does not open.

The yadif kernel is FFmpeg's, under the GPL-2.0-or-later.

## Comparing with VLC

- `benchmarks/YadifCompare`: VLC's C and SSE2/SSSE3 assembly against every
  C# version, sample for sample, and timed.
- `tests/VlcEndToEnd/run.sh`: VLC transcodes an interlaced clip through its
  deinterlace filter and through `dotnet_yadif`, and every output picture
  must be the same.
