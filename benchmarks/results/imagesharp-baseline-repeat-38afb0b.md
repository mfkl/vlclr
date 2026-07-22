# VLCLR ImageSharp benchmark: baseline-repeat-38afb0b

- Commit: `38afb0b`
- Captured: `2026-07-22T12:14:02.1222819+00:00`
- Runtime: `.NET 10.0.10`
- OS: `Microsoft Windows 10.0.19045`
- Architecture: `X64`
- Logical processors available: `8`
- Canvas: `1920x1080` RGBA
- Warmups / measurements: `5 / 30`

| Scenario | Median elapsed | P95 elapsed | Median allocated | P95 allocated | GC (0/1/2) |
| --- | ---: | ---: | ---: | ---: | ---: |
| RenderFullFrame | 81.22 ms | 179.48 ms | 36.08 MiB | 36.09 MiB | 181/83/0 |
| StageFullFramePixels | 0.68 ms | 4.10 ms | 7.91 MiB | 7.91 MiB | 5/5/5 |
| RenderAndStageFullFrame | 76.49 ms | 123.51 ms | 43.99 MiB | 44.00 MiB | 191/93/10 |

The staging scenarios deliberately reproduce the original `PictureConverter` full-frame temporary RGBA allocation.

Reference render: [imagesharp-baseline-repeat-38afb0b.png](imagesharp-baseline-repeat-38afb0b.png)
