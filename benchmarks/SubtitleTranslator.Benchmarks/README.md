# Subtitle translator inference benchmark

This executable measures ONNX inference separately from ImageSharp rendering.
It writes raw per-cue JSON and a Markdown summary containing runtime/model
provenance, hardware metadata, token counts, phase timings, percentiles,
managed allocation, cache-hit cost, and process memory.

Quick comparison:

```powershell
dotnet run --project benchmarks/SubtitleTranslator.Benchmarks -c Release -- `
  samples/SubtitleTranslator/models/opus-mt-en-fr benchmarks/results `
  --iterations 1 --threads 4 --decoders cached,hybrid,uncached
```

Full CPU matrix:

```powershell
dotnet run --project benchmarks/SubtitleTranslator.Benchmarks -c Release -- `
  samples/SubtitleTranslator/models/opus-mt-en-fr benchmarks/results `
  --iterations 3 --threads 1,2,4,6,8 --decoders cached,hybrid,uncached
```

Native AOT smoke publish:

```powershell
dotnet publish benchmarks/SubtitleTranslator.Benchmarks -c Release -r win-x64 -p:PublishAot=true
```

Record the machine's power mode and video-decode workload alongside results;
the executable records `powerMode` as `not-recorded` because Windows does not
provide a stable cross-version managed API for it.
