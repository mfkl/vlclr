# Gain Sample

VLC's gain filter as a C# audio filter plugin, built with VLCLR and Native AOT.
It produces exactly the same samples as VLC's own `gain` filter: every sample
times `--dotnet-gain-value`.

It is the smallest audio filter there is, to show `VLCAudioFilterBase`. The
multiplication lives in the framework, in `VLCLR.Filters`
(`VLCLR.Filters.Audio.Gain`); [`GainPlugin.cs`](GainPlugin.cs) is the VLC audio
filter around it:

- **Open**: read `--dotnet-gain-value`. The base class has already asked VLC
  for 32-bit float samples, at the input's rate and channels; VLC converts
  what comes before and after the filter.
- **Each block**: multiply the samples in place.

```csharp
protected override void ProcessAudio(in VLCAudioBlock block)
    => Gain.Amplify(block.Output, _gain);
```

The filter runs on the decoder's channels, before VLC mixes them down for the
device: on a 5.1 track it gets all six (L R RL RR C LFE, in that order).

## Building

```powershell
dotnet publish samples/Gain -c Release -r win-x64
```

Copy `bin/Release/net10.0/win-x64/native/libdotnet_gain_plugin.dll` into
VLC's `plugins/audio_filter/` folder, then refresh the plugin cache
(`vlc-cache-gen.exe plugins`).

## Usage

```
vlc --audio-filter=dotnet_gain --dotnet-gain-value=0.5 video.mp4
```

Or when transcoding:

```
vlc in.wav --sout "#transcode{acodec=f32l,afilter=dotnet_gain}:std{access=file,mux=wav,dst=out.wav}" --dotnet-gain-value=0.5 vlc://quit
```
