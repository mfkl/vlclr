# VLCLR ImageSharp benchmark: optimized-38b06db

- Commit: `38b06db`
- Captured: `2026-07-22T12:11:47.8038448+00:00`
- Runtime: `.NET 10.0.10`
- OS: `Microsoft Windows 10.0.19045`
- Architecture: `X64`
- Logical processors available: `8`
- Canvas: `1920x1080` RGBA
- Compact region: `1189x123` RGBA
- Warmups / measurements: `5 / 30`

| Scenario | Median elapsed | P95 elapsed | Median allocated | P95 allocated | GC (0/1/2) |
| --- | ---: | ---: | ---: | ---: | ---: |
| RenderFullFrame | 133.04 ms | 263.90 ms | 13.32 MiB | 13.32 MiB | 66/38/0 |
| StageFullFramePixels | 5.35 ms | 11.36 ms | 7.91 MiB | 7.91 MiB | 11/11/11 |
| RenderAndStageFullFrame | 51.36 ms | 74.13 ms | 21.23 MiB | 21.24 MiB | 94/66/28 |
| CopyFullFrameDirectToPlane | 0.89 ms | 1.46 ms | 112 B | 112 B | 0/0/0 |
| RenderCompactRegion | 35.22 ms | 47.14 ms | 12.90 MiB | 12.90 MiB | 64/35/0 |
| CopyCompactRegionToPlane | 0.02 ms | 0.03 ms | 112 B | 112 B | 0/0/0 |
| RenderAndCopyCompactRegion | 31.68 ms | 39.92 ms | 12.90 MiB | 12.90 MiB | 64/36/0 |

The staging scenarios deliberately reproduce the original `PictureConverter` full-frame temporary RGBA allocation.

Reference render: [imagesharp-optimized-38b06db.png](imagesharp-optimized-38b06db.png)
