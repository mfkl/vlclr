# Gaussian blur benchmark plugin

The Gaussian blur from `VLCLR.Filters` (`VLCLR.Filters.Video.GaussianBlur`),
packaged for comparing C and C# performance inside the same VLC. For using the
blur in a plugin, see `samples/GaussianBlur` instead.

Compared to the sample, this plugin:

- is built once per runtime and instruction set, with its own module name, so
  the builds install side by side: `dotnet_gaussianblur_net10` (options
  `--dotnet-gaussianblur-net10-*`), `dotnet_gaussianblur_net10_avx2`, and so on;
- lets you pick the implementation with `--dotnet-gaussianblur-<runtime>-impl`:
  `scalar` (line-for-line port of VLC's C, the default), `tuned`, `simd`,
  `simdmax`. All produce the same output as VLC's filter;
- times the blur on every frame.

## Build

```powershell
dotnet publish DotnetGaussianBlur -c Release -f net10.0                    # x86-64-v2
dotnet publish DotnetGaussianBlur -c Release -f net10.0 -p:BlurIsa=avx2    # x86-64-v3, for simd/simdmax
```

`net8.0` and `net10.0` build with the repository's SDK; `net11.0` needs a .NET 11
SDK, run from outside the repository (its `global.json` pins SDK 10) with
`-p:BlurTfms=net11.0`.

## Measuring

Each run logs one line to VLC's log when the filter closes (`-v` or more):

```
[bench] impl=CS-SIMDMax-AVX2 sigma=4 frames=24 first_ms=... mean_ms=... median_ms=... p95_ms=... min_ms=...
```

Per-frame time of the blur only, first frame reported separately.
`VLCLR_BLUR_DUMP=<file>` appends the visible planes of the first
`VLCLR_BLUR_DUMP_FRAMES` (default 30) output pictures to a file, for
byte-by-byte comparison with VLC's own filter.
