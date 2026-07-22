# VLCLR ImageSharp subtitle optimization

The production-shaped benchmark went from rendering and staging an entire
`1920x1080` RGBA frame to rendering a measured `1189x123` subtitle region and
copying its rows directly into the VLC plane.

Both results below were captured on the same laptop with .NET 10 using 5 warmup
iterations and 30 measurements. The before run uses commit `38afb0b`; the after
run uses commit `38b06db`.

| Metric | Before | After | Change |
| --- | ---: | ---: | ---: |
| Median elapsed | 76.49 ms | 31.68 ms | 58.6% lower / 2.41x faster |
| P95 elapsed | 123.51 ms | 39.92 ms | 67.7% lower / 3.09x faster |
| Managed allocation per render | 43.99 MiB | 12.90 MiB | 70.7% lower / 3.41x less |
| RGBA surface pixels | 2,073,600 | 146,247 | 92.9% fewer |
| VLC copy allocation | 7.91 MiB | 112 B | Full-frame temporary removed |

The remaining managed allocation is primarily ImageSharp text shaping and
rasterization for the shadow, outline, and foreground. The optimized production
path no longer creates a large-object-heap RGBA staging array, and the compact
surface reduces the native VLC picture and memory bandwidth by roughly 14x for
this subtitle.

## Reproduction

```powershell
dotnet run --project benchmarks/VLCLR.ImageSharp.Benchmarks -c Release -- `
  --label local-run `
  --output benchmarks/results `
  --warmups 5 `
  --iterations 30
```

- [Matched baseline report](imagesharp-baseline-repeat-38afb0b.md)
- [Optimized report](imagesharp-optimized-38b06db.md)
- [Before reference frame](imagesharp-baseline-repeat-38afb0b.png)
- [After reference frame](imagesharp-optimized-38b06db.png)

## Native AOT + VLC visual verification

The optimized plugin was published for `win-x64`, deployed into the VLC 4
`spu` plugin directory, and exercised against a local Big Buck Bunny copy. VLC
reported both renderer startup and compact-region creation before saving these
fully composited snapshots:

- [Bottom-center subtitle](subtitle-vlc-optimized.png)
- [Bottom-left aligned subtitle](subtitle-vlc-bottom-left.png)
