# VLCLR ImageSharp benchmark: baseline-447b296

- Commit: `447b296`
- Captured: `2026-07-22T11:49:10.0109364+00:00`
- Runtime: `.NET 10.0.10`
- OS: `Microsoft Windows 10.0.19045`
- Architecture: `X64`
- Logical processors available: `8`
- Canvas: `1920x1080` RGBA
- Warmups / measurements: `3 / 15`

| Scenario | Median elapsed | P95 elapsed | Median allocated | P95 allocated | GC (0/1/2) |
| --- | ---: | ---: | ---: | ---: | ---: |
| RenderFullFrame | 140.99 ms | 352.86 ms | 36.09 MiB | 36.10 MiB | 90/37/0 |
| StageFullFramePixels | 3.08 ms | 5.17 ms | 7.91 MiB | 7.91 MiB | 2/2/2 |
| RenderAndStageFullFrame | 114.38 ms | 327.50 ms | 43.99 MiB | 44.00 MiB | 94/45/4 |

The staging scenarios deliberately reproduce the original `PictureConverter` full-frame temporary RGBA allocation.

Reference render: [imagesharp-baseline-447b296.png](imagesharp-baseline-447b296.png)
