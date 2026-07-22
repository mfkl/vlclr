# VLCLR ImageSharp benchmarks

This dependency-free harness measures the subtitle rendering hot path without
requiring VLC to be running. It reports median and p95 elapsed time, managed
allocations, and GC collections for a representative outlined 1080p subtitle.

Run it from the repository root with a Release build:

```powershell
dotnet run --project benchmarks/VLCLR.ImageSharp.Benchmarks -c Release -- `
  --label my-run `
  --output benchmarks/results `
  --warmups 3 `
  --iterations 15
```

The harness writes Markdown and JSON reports plus a reference PNG. The staging
scenarios intentionally reproduce the original `PictureConverter` temporary
full-frame RGBA allocation, making the pre-optimization cost explicit even
though VLC itself is not loaded by the benchmark process.

Close CPU-intensive applications and use the same power profile when comparing
runs. Treat the median as the main comparison and p95 as a useful stutter signal.
