# Subtitle translator benchmark

- Timestamp (UTC): 2026-07-22T13:23:18.8425736+00:00
- Processor: Intel64 Family 6 Model 140 Stepping 2, GenuineIntel
- Logical processors: 8
- OS: Microsoft Windows NT 10.0.19045.0
- Framework: .NET 10.0.10
- ONNX Runtime: 1.27.1.0
- Iterations per cue: 1
- Rendering baseline (separate): `benchmarks/results/imagesharp-optimized-38b06db.json`

| Decoder | Threads | Init ms | p50 ms | p90 ms | p95 ms | p99 ms | Max ms | Median alloc | Cache hit | Working set |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| cached | 4 | 1470.9 | 200.0 | 925.3 | 2852.2 | 2852.2 | 2852.2 | 43.2 KiB | 1.55 µs | 608.5 MiB |
| hybrid | 4 | 2358.4 | 71.2 | 734.3 | 3027.5 | 3027.5 | 3027.5 | 28.8 KiB | 0.45 µs | 510.2 MiB |
| uncached | 4 | 2193.9 | 97.5 | 1372.4 | 9039.2 | 9039.2 | 9039.2 | 28.8 KiB | 0.46 µs | 454.4 MiB |

## Corpus groups

| Decoder | Threads | Group | Samples | Average ms | p50 ms | p95 ms | Max ms |
|---|---:|---|---:|---:|---:|---:|---:|
| cached | 4 | adversarial | 1 | 2852.2 | 2852.2 | 2852.2 | 2852.2 |
| cached | 4 | long | 2 | 901.5 | 877.7 | 925.3 | 925.3 |
| cached | 4 | multiline | 1 | 154.6 | 154.6 | 154.6 | 154.6 |
| cached | 4 | punctuation | 1 | 382.0 | 382.0 | 382.0 | 382.0 |
| cached | 4 | typical | 3 | 129.3 | 127.8 | 200.0 | 200.0 |
| cached | 4 | unicode | 2 | 271.4 | 207.1 | 335.7 | 335.7 |
| cached | 4 | very-short | 2 | 37.2 | 37.1 | 37.2 | 37.2 |
| hybrid | 4 | adversarial | 1 | 3027.5 | 3027.5 | 3027.5 | 3027.5 |
| hybrid | 4 | long | 2 | 692.1 | 649.9 | 734.3 | 734.3 |
| hybrid | 4 | multiline | 1 | 63.8 | 63.8 | 63.8 | 63.8 |
| hybrid | 4 | punctuation | 1 | 174.7 | 174.7 | 174.7 | 174.7 |
| hybrid | 4 | typical | 3 | 65.5 | 58.5 | 106.0 | 106.0 |
| hybrid | 4 | unicode | 2 | 188.4 | 71.2 | 305.7 | 305.7 |
| hybrid | 4 | very-short | 2 | 22.7 | 21.1 | 24.3 | 24.3 |
| uncached | 4 | adversarial | 1 | 9039.2 | 9039.2 | 9039.2 | 9039.2 |
| uncached | 4 | long | 2 | 1248.3 | 1124.2 | 1372.4 | 1372.4 |
| uncached | 4 | multiline | 1 | 73.5 | 73.5 | 73.5 | 73.5 |
| uncached | 4 | punctuation | 1 | 203.5 | 203.5 | 203.5 | 203.5 |
| uncached | 4 | typical | 3 | 75.1 | 88.7 | 97.5 | 97.5 |
| uncached | 4 | unicode | 2 | 185.7 | 128.9 | 242.6 | 242.6 |
| uncached | 4 | very-short | 2 | 57.8 | 54.7 | 60.9 | 60.9 |
