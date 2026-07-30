# VLCLR Intelligent Media Platform Plan

**Status:** Active; YOLO live GPU feasibility implemented, overlay and release
qualification next

**Created:** 2026-07-29
**Scope:** Existing subtitle translation, semantic transcript search, YOLO
object search, offline and real-time upscaling, and one shared .NET runtime for
independently packaged managed VLC plugins.

## Goal

Evolve VLCLR into an offline, local-first intelligent media platform for VLC 4.
Preserve the existing subtitle translator, add searchable speech and visual
content, support file and playback upscaling, and allow multiple managed
plugins to reuse one CoreCLR instance inside each `vlc.exe` process.

The plan separates three concerns:

1. VLC callbacks remain small, bounded, and failure-safe.
2. Managed plugin code shares one runtime while isolating dependencies.
3. Heavy AI inference uses reusable services and stays out of process when that
   gives a stronger crash or native-library boundary.

## Assumptions

- "One runtime" means one CoreCLR inside each VLC process. A runtime cannot be
  shared across operating-system processes.
- Existing Native AOT plugins remain supported. The managed host is a second,
  opt-in execution model rather than an immediate replacement.
- Worker processes are permitted for GPU/native dependency isolation and long
  jobs. They have their own runtime and are outside the in-process runtime goal.
- Media, transcripts, embeddings, detections, and enhanced outputs stay local
  by default.
- The first UX is CLI/sidecar based. VLC or desktop UI can consume the same
  contracts after the workflows stabilize.
- Models are selected by measured quality, latency, licensing, and packaging
  compatibility, not by popularity alone.

## Non-goals

- Treating `AssemblyLoadContext` as a security sandbox.
- Loading untrusted third-party code into VLC.
- Blocking VLC audio/video/render callbacks on inference, disk, IPC, or locks.
- Guaranteeing neural upscaling in real time on every GPU and resolution.
- Migrating every Native AOT plugin before the shared host proves worthwhile.
- Requiring a cloud account or remote inference service.

## Current baseline

- `SubtitleTranslator` already provides manifest-verified Marian/OPUS-MT
  translation, bounded inference, decoder caching, deadlines, and original-text
  fallback.
- `LiveAudioTranslator` captures decoded PCM without blocking VLC, uses an
  out-of-process Whisper/ONNX worker, and schedules translated cues through a
  companion sub-source module.
- `VLCLR.LiveTranslation` provides a versioned named-pipe protocol, model
  profiles, inference contracts, and latency metrics.
- `ModuleBuilder` can register multiple modules and distinct callback names in
  one plugin descriptor.
- The framework already covers video, subtitle, audio-format, and `block_t`
  interop, Native AOT exports, and VLC lifecycle tests.

Important gaps:

- Current VLC plugins are separate Native AOT binaries; arbitrary managed
  assemblies cannot be discovered and loaded dynamically.
- The live worker protocol is specialized, not a general multi-client AI job
  service.
- There is no durable media index shared by transcripts and detections.
- The video-filter abstraction assumes in-place processing and cannot yet
  negotiate a larger output picture or GPU surface.
- Embedding, detection, and super-resolution models lack a common catalog.

## Guiding principles

1. **Never block VLC hot paths.** Callbacks may publish into a bounded slot or
   return a prepared result, but may not wait for heavy work.
2. **Bound every queue.** Define capacity, replacement/drop policy,
   cancellation, and metrics for every pipeline.
3. **Use media time.** Persist source PTS, media identity, track, generation,
   and model provenance with every result.
4. **Reject stale work.** Seek, stop, media replacement, model change, and index
   invalidation advance a generation.
5. **Manifest every model.** Record revision, license, hashes, tensor contract,
   preprocessing, postprocessing, providers, and validation evidence.
6. **CPU correctness first.** Enable accelerators only after reference parity
   and device-specific qualification.
7. **Keep failures local.** A plugin, model, or worker failure should not disable
   unrelated features.
8. **Do not force native ML libraries into one process.** Shared CoreCLR is a
   goal; sharing incompatible provider DLLs is not.

## Target architecture

```text
vlc.exe
|-- existing Native AOT plugins (compatibility path)
`-- VLCLR native host shim
    |-- starts CoreCLR once through hostfxr
    `-- VLCLR.ManagedHost
        |-- shared managed contracts
        |-- PluginLoadContext: search controller
        |-- PluginLoadContext: object-search controller
        `-- PluginLoadContext: upscaling controller
                 |
                 | versioned local IPC
                 v
        VLCLR.AI service/broker
        |-- speech + translation
        |-- embeddings
        |-- YOLO detection
        |-- super-resolution
        `-- provider/session scheduler
                 |
                 +--> media index
                 `--> prepared/upscaled output
```

The in-process host owns VLC descriptors, configuration, callbacks, and light
orchestration. Heavy inference remains behind IPC unless an explicit benchmark
and native-dependency review approves an in-process implementation.

## Shared platform foundation

### Media identity and timeline

Define a versioned `MediaIdentity` from canonical URI/path, local file size and
mtime, partial content hash, duration, and selected stream signatures. Changing
identity invalidates dependent indexes.

All records use VLC microsecond ticks and include source time range, track ID,
generation, confidence, model/profile revision, provenance, and schema version.

### General AI job protocol

Generalize the existing framing code with capability discovery, client
registration, submit/progress/complete/cancel/fail messages, streaming audio or
frame batches with backpressure, provider selection, artifact references,
heartbeat, restart, quotas, and priority. Keep live-translation messages
compatible while adding an offline job command family.

### Model catalog

Extend manifests for `text-embedding`, `object-detection`, and
`super-resolution`, alongside current speech and translation profiles. Include
shapes, color space, scale, normalization, labels/tokenizer, postprocessing
version, memory needs, providers, hashes, source, and license.

### Media index

```text
index/<media-id>/
|-- index.json
|-- metadata.db
|-- transcript.jsonl
|-- embeddings.bin
|-- detections.jsonl
`-- thumbnails/
```

Use SQLite for metadata and joins, an append-only memory-mappable embedding
file, and exact cosine search first. Add HNSW only after corpus scale proves it
necessary.

## Workstream A: one shared .NET runtime

### Recommended architecture

Add a small native VLC host that starts CoreCLR through `nethost`/`hostfxr` and
loads `VLCLR.ManagedHost`. The first experiment should use **one native host DLL
that discovers managed manifests and registers them as VLC submodules**.

Independent plugins are installed in separate directories and have separate
assemblies/dependencies, but share the host descriptor and CoreCLR. If VLC cache
or callback identity makes this impractical, fall back to generated per-plugin
proxy DLLs that all call one process-wide host library.

```text
vlc-root/
|-- dotnet/VLCLR.ManagedHost.runtimeconfig.json
|-- dotnet/VLCLR.ManagedHost.dll
|-- dotnet/VLCLR.Managed.Abstractions.dll
|-- dotnet/plugins/
|   |-- semantic-search/plugin.json + assemblies
|   `-- object-search/plugin.json + assemblies
`-- plugins/misc/libvlclr_managed_host_plugin.dll
```

A plugin manifest declares stable ID/version, entry assembly/type, required
host API, VLC modules/capabilities/configuration, dependency declarations,
platform requirements, and restart policy. Descriptor discovery must not load
models, start workers, or touch media so cache generation remains deterministic.

### Dependency isolation

- Load the host and shared contracts in the default `AssemblyLoadContext`.
- Use one custom `AssemblyLoadContext` plus `AssemblyDependencyResolver` per
  plugin package.
- Always resolve shared contracts from the default context to avoid type
  identity splits.
- Resolve private managed dependencies from each plugin directory.
- Treat native dependencies as process-wide hazards despite managed isolation.
- Do not promise hot unload initially. Callback pointers and state may keep a
  context rooted, so upgrades may require restarting VLC.

### A1. Hosting feasibility spike

- [ ] Build a minimal native host plugin using `nethost` and `hostfxr`.
- [ ] Start one framework-dependent .NET 10 runtime from a host-owned config.
- [ ] Invoke managed code during `vlc_entry`.
- [ ] Register two trivial managed VLC modules.
- [ ] Validate plugin-cache generation and normal playback loading.
- [ ] Confirm `coreclr.dll` is loaded once.
- [ ] Compare discovery/startup time, callback overhead, working set, DLL size,
      and failure behavior with equivalent Native AOT modules.

**Exit gate:** stop or redesign if CoreCLR hosting destabilizes cache generation,
module callbacks, or VLC shutdown.

### A2. Managed host SDK

- [ ] Add `VLCLR.Managed.Abstractions` with versioned plugin, module, lifecycle,
      logging, configuration, and callback contracts.
- [ ] Add deterministic manifest discovery and one load context per plugin.
- [ ] Reuse existing attributes where practical; extend the generator to emit a
      managed descriptor rather than native exports.
- [ ] Catch every managed exception before it crosses a VLC boundary.
- [ ] Track state per native object pointer and disable repeatedly failing
      modules without affecting other plugins.
- [ ] Log runtime, host, plugin, dependency, and load-context versions.

### A3. Independence tests

- [ ] Load at least three managed plugins simultaneously.
- [ ] Test two plugins with conflicting versions of the same private library.
- [ ] Verify shared contract type identity.
- [ ] Verify one plugin failing in open/filter/close does not break another.
- [ ] Verify install, remove, and upgrade after cache regeneration/restart.
- [ ] Document unsupported native dependency combinations.

### A4. Migration proof

- [ ] Port `SubtitleRenderer` while retaining its Native AOT version.
- [ ] Port a second module with no native ML dependency, preferably a search
      controller.
- [ ] Compare total disk and memory cost with one, two, and three plugins.
- [ ] Migrate `SubtitleTranslator` only if ONNX native dependency and memory
      measurements justify it; otherwise keep translation behind a worker.

### Shared-runtime definition of done

- One CoreCLR serves at least three active managed plugins in one VLC process.
- Plugins are independently installable and versioned.
- Private managed dependency versions do not conflict.
- Managed exceptions never escape into native VLC code.
- Cache/restart behavior for installation and upgrades is documented.
- Measured startup, callback, memory, and disk results are published against
  Native AOT equivalents.

## Workstream B: common AI service and index

### B1. Reusable worker services

- [ ] Extract model catalog, provider selection, session caching, telemetry,
      and manifest validation from `LiveAudioTranslator.Worker`.
- [ ] Preserve current live-caption behavior and protocol compatibility.
- [ ] Add a broker for multiple local clients and bounded task-specific jobs.
- [ ] Define priorities for live audio, interactive query, background indexing,
      and bulk upscaling.
- [ ] Prevent background work from starving playback or live captions.

### B2. Provider policy

- [ ] Keep pinned CPU ONNX Runtime as the reproducible baseline.
- [ ] Evaluate Windows ML as an optional system-shared runtime/provider path on
      supported Windows versions.
- [ ] Evaluate isolated CPU, OpenVINO, Vulkan, DirectML, and vendor variants.
- [ ] Use ONNX Runtime I/O binding or provider-native buffers when copies are a
      material part of latency.
- [ ] Record model, runtime, provider, device, driver, memory, timing, quality,
      and stability in every qualification report.
- [ ] Never choose an accelerator only because it initializes successfully.

### B3. Index library

- [ ] Implement media fingerprinting and invalidation.
- [ ] Add schema migrations and atomic index publication.
- [ ] Add checkpoints, cancellation, resume, and corruption recovery.
- [ ] Add exact normalized-vector search.
- [ ] Add temporal joins across transcripts and detections.
- [ ] Add retention controls for text, thumbnails, and intermediate artifacts.

## Workstream C: existing subtitle translation

Treat the current translator as the regression baseline, not disposable code.

- [ ] Preserve original-text fallback for missing model, initialization failure,
      deadline, and inference failure.
- [ ] Add golden cases for punctuation, multiple regions, long cues, malformed
      input, and cached/uncached parity.
- [ ] Add terminology glossary and proper-noun overrides.
- [ ] Add optional dual-language rendering.
- [ ] Persist translations by source text, language pair, model/tokenizer
      revision, and glossary revision.
- [ ] Emit original and translated timeline records into the media index only
      when explicitly enabled.
- [ ] Expose translation through the common service for search-result views.
- [ ] Add a worker-backed mode and compare it with the current in-process path.
- [ ] Keep Native AOT available until a managed-host path matches correctness,
      fallback behavior, and latency.

**Definition of done:** current behavior does not regress; search can index
original, translated, or both text fields without mixing languages/models; and
translation remains usable when the index service is unavailable.

## Workstream D: semantic transcript search

### MVP experience

Index one media file and answer queries such as "where they discuss deployment
failures" or "the CPU versus GPU comparison." Results include timestamp,
source text, optional translation, score, model provenance, and an action to
seek VLC to that position.

### Indexing pipeline

1. Discover embedded or external subtitle tracks.
2. If no suitable transcript exists, reuse Whisper preparation.
3. Preserve display text while creating normalized search text.
4. Group cues into sentence/paragraph windows with temporal overlap.
5. Generate embeddings in bounded batches.
6. Persist vectors, source timestamps, language, track, and model revision.
7. Publish atomically after validation.

### Query pipeline

1. Normalize/embed the query with the same model profile.
2. Run exact cosine search.
3. Merge overlapping hits and optionally rerank neighboring segments.
4. Apply language, speaker/track, and time filters.
5. Return top results through CLI and versioned JSON/IPC.
6. Seek only after explicit user selection.

### Delivery phases

#### D1. Subtitle-only vertical slice

- [ ] Select and license-review a compact multilingual embedding model.
- [ ] Add manifest, tokenizer assets, golden vectors, and downloader.
- [ ] Index SRT/ASS and embedded subtitle cues without speech recognition.
- [ ] Add `vlclr-search index`, `query`, and `seek` commands.
- [ ] Create labeled relevance fixtures and regression tests.

#### D2. Generated transcripts

- [ ] Reuse Whisper segmentation and timestamp handling.
- [ ] Store recognized text separately from translated captions.
- [ ] Record speech-model/language provenance.
- [ ] Re-index incrementally when only translation or embeddings change.

#### D3. VLC integration

- [ ] Add a lightweight managed controller or sidecar endpoint.
- [ ] Display results outside the video hot path.
- [ ] Seek to a selected result and show a temporary context overlay.
- [ ] Keep search usable when playback is stopped.

### Semantic-search acceptance

- Fully offline indexing and querying.
- Identical inputs produce stable segment IDs.
- An agreed top-5 relevance target passes on a labeled corpus.
- Query p95 is below 200 ms for a two-hour single-media index after model load.
- Every result identifies source transcript and model revision.
- Cancellation or corruption cannot damage source media.

## Workstream E: YOLO object search

**Selected for detailed feasibility and implementation planning.** See
[YOLO_OBJECT_SEARCH_PLAN.md](YOLO_OBJECT_SEARCH_PLAN.md). The detailed plan
selects real-time, fixed-vocabulary detection while one video plays, with live
boxes and a searchable watched timeline. It targets the i7-1195G7/Iris Xe test
machine and requires an end-to-end 1080p/30 benchmark. The first live plugin is
now implemented entirely in C#: unsafe VLC D3D11 ABI access, GPU
scale/letterbox, OpenVINO Remote Tensors, YOLOX decoding/NMS, COCO-80 aliases,
and query parsing. Nano runs at a scheduled 15 Hz alongside VLC
D3D11VA/Direct3D11 playback with typical warm inference of 6-10 ms. Live GPU
overlay, golden-image correctness, indexing, packaging, and sustained thermal
qualification remain.

### MVP experience

Display fresh boxes and labels while VLC continues rendering at the source frame
rate. Persist detections from the watched portion and support searches such as
"person," "car above 70% confidence," and "phone between 00:20:00 and
00:30:00." Offline scanning of unplayed content and combined transcript/object
queries are later extensions.

### Detection pipeline

1. Rate-limit PTS-tagged D3D11 opaque frames in a GPU-aware VLC video filter.
2. GPU-scale/letterbox the decoder surface into an owned NV12 texture without
   CPU readback.
3. Let the background C# worker thread consume only the newest eligible texture.
4. Wrap it as an OpenVINO D3D11 Remote Tensor and run GPU-only inference.
5. Apply versioned confidence filtering and non-maximum suppression.
6. Return PTS-tagged boxes; render only results inside a short staleness window.
7. Store watched detection metadata outside the callback; defer thumbnails
   because the GPU-only v1 forbids frame readback.

### Delivery phases

#### E1. Model correctness

- [ ] Select a redistributable YOLO model only after license review.
- [ ] Pin ONNX graph, labels, preprocessing, and postprocessing.
- [ ] Match reference detections on a golden image corpus within tolerance.
- [x] Implement fixed COCO-80 labels/aliases, YOLOX grid decoding, confidence,
      letterbox reversal, and NMS in C# with model-free tests.
- [x] Benchmark YOLOX-Tiny and Nano through the pinned OpenVINO GPU path; do not
      add a CPU execution fallback.

#### E2. Real-time feasibility and overlay

- [x] Prove shared NV12 handles, keyed mutexes, and GPU video-processor scaling
      on the reference Iris Xe hardware.
- [x] Prove D3D11/OpenCL NV12-plane interop and OpenVINO GPU preprocessing.
- [x] Sustain Nano and Tiny at 15 Hz for 20 seconds while VLC confirms D3D11VA
      decode and Direct3D11 rendering.
- [x] Add a logical-capacity-one in-process D3D11 texture transport.
- [x] Keep D3D11VA active in a live VLC plugin with no CPU pixel readback.
- [x] Schedule Nano at 15 Hz on 24 fps input using one/two-frame deadline gaps.
- [ ] Meet the 1080p/30, 10-detection-updates/s sustained reference-machine gate.
- [ ] Render fresh boxes while rejecting stale/old-generation results.

#### E3. Rolling search and later backfill

- [ ] Query by class, synonym, confidence, and time range.
- [ ] Merge adjacent detections into presence intervals.
- [ ] Make watched/analyzed timeline coverage explicit.
- [ ] Add offline backfill only after thumbnail versus sequential decode
      benchmarks.
- [ ] Join detection intervals with semantic transcript hits.
- [ ] Seek VLC and display thumbnail/bounding-box context.

### YOLO-search acceptance

- Golden detections match the reference export within documented tolerance.
- Every result maps to exact media time and source frame dimensions.
- 1080p/30 playback sustains at least 10 fresh Tiny 416 detection updates/s on
  the i7-1195G7/Iris Xe reference hardware after thermal steady state.
- Capture-to-result p95 is at most 150 ms and stale boxes disappear.
- Query p95 is below 200 ms for a two-hour watched timeline.
- Frame transport and index writing are bounded and cancellation-safe.
- VLC's video callback never waits for detection.
- Manifest fully reproduces labels, license, preprocessing, and postprocessing.

## Workstream F: offline upscaling

Offline upscaling establishes model correctness and the reusable frame pipeline
before any real-time claim.

### Scope

- File-to-file local processing with initial 2x and 4x profiles.
- Quality-oriented and speed-oriented models.
- Tiled inference with overlap to bound memory and prevent seams.
- Audio, subtitles, chapters, variable frame timing, and essential metadata
  preservation.
- Cancellation, checkpoints/resume, progress, and disk-space estimation.

Real-ESRGAN is a candidate quality profile, not an automatic choice. Its export,
model license, output behavior, and performance must pass the same gate as any
other model.

```text
decode -> color conversion -> tile/overlap -> inference -> blend
       -> color conversion -> encode -> remux audio/subtitles/metadata
```

The first implementation may use a standalone decoder/encoder to reduce risk.
VLC filter integration follows only after VLCLR can return a new picture with a
different size and format safely.

### F1. Image correctness

- [ ] Define `ISuperResolutionAdapter` and manifest-driven preprocessing.
- [ ] Match full-frame and tiled output with a reference implementation.
- [ ] Test odd sizes, alpha, color range/matrix, edge padding, and overlap.
- [ ] Add seam detection and deterministic hashes where applicable.

### F2. Video pipeline

- [ ] Add decode, ordering, timestamp, encode, and remux adapters.
- [ ] Preserve A/V/subtitle sync and variable frame rate.
- [ ] Add bounded frame pools and backpressure.
- [ ] Add resumable checkpoints at safe segment/container boundaries.
- [ ] Produce machine-readable quality and performance reports.

### F3. Provider qualification

- [ ] Establish CPU correctness and memory baseline.
- [ ] Evaluate Windows ML and isolated ONNX Runtime variants.
- [ ] Use reusable device buffers/I/O binding where supported.
- [ ] Record throughput by resolution, scale, tile, precision, and model.
- [ ] Reject accelerators with output drift, instability, or excessive copies.

### Offline-upscaling acceptance

- No visible tile seams on the golden corpus.
- A/V/subtitle synchronization stays within an agreed long-file tolerance.
- Peak memory stays within the configured budget.
- Cancellation leaves a valid checkpoint or clearly incomplete artifact.
- Output records exact source hash, model, provider, device, and parameters.
- Quality metrics and visual comparisons accompany benchmarks.

## Workstream G: real-time upscaling

Real-time upscaling is a separate engineering track, not offline upscaling
called synchronously from `ProcessFrame`.

### Required framework work

- Add a video-filter/converter abstraction that negotiates a larger output
  format and returns a different VLC picture.
- Define picture ownership and pass-through fallback precisely.
- Reuse output pictures through a bounded frame pool.
- Discover Windows video devices/surfaces and measure D3D11/D3D12 upload,
  inference, download, and presentation separately.
- Prove whether zero-copy or low-copy integration is feasible with the target
  VLC 4 build before choosing the production path.

### Runtime behavior

- Queue depth is one latest frame; stale work is replaced, not accumulated.
- Audio is never delayed to wait for enhancement.
- Missed budgets, model failure, or device change cause pass-through or a
  conventional scaler fallback.
- Warm the model before activation or switch at a clean frame boundary.
- Support explicit quality levels and automatic downgrade.
- Reset on seek, discontinuity, format change, device loss, and stop.

### G1. Output-size filter spike

- [ ] Implement nearest/bilinear scaling that returns a larger VLC picture.
- [ ] Validate ownership, seek, resize, stop, close, and cache behavior.
- [ ] Test 720p-to-1080p and 1080p-to-4K negotiation.

### G2. Lightweight neural model

- [ ] Select a small 2x model that can plausibly meet a 30/60 fps budget.
- [ ] Implement CPU correctness and fixed-shape buffer reuse.
- [ ] Add GPU provider variants with per-stage timing.
- [ ] Establish named reference hardware and driver profiles.

### G3. GPU transport

- [ ] Compare CPU readback/upload, shared texture, and provider-native paths.
- [ ] Use I/O binding or native provider APIs only where they remove measured
      copies.
- [ ] Test coexistence with VLC hardware decoding on the same GPU.
- [ ] Handle device removal/provider failure without crashing VLC.

### G4. Adaptive path

- [ ] Add automatic bypass and quality downgrade thresholds.
- [ ] Report inference, copy, queue, drop, and active-mode metrics.
- [ ] Run long playback, seek, fullscreen, display-change, and multi-instance
      tests.
- [ ] Publish supported model/resolution/device combinations instead of making a
      universal real-time claim.

### Real-time acceptance

- Named reference hardware sustains the declared input/output resolution and
  frame rate with p95 processing below the frame budget.
- Queue depth stays bounded and A/V sync does not drift.
- Missed budgets cause bypass/downgrade instead of increasing latency.
- Seek, stop, format change, and device loss recover without VLC crashing.
- Results are compared with VLC's normal scaler and offline reference output.

## Delivery order and gates

```text
Phase 0: baseline + shared contracts
    |
    +--> Phase 1: shared CoreCLR host spike --------+
    |                                               |
    `--> Phase 2: AI service + media index ----------+
                         |                           |
                         +--> Phase 3: semantic search
                         +--> Phase 4: YOLO search
                         `--> Phase 5: offline upscaling
                                              |
                                              `--> Phase 6: real-time spike

Phase 7: managed migrations, combined search, packaging, hardening
```

### Phase 0: freeze the current baseline

- [ ] Record current subtitle/live translation correctness, performance,
      memory, and packaging evidence.
- [ ] Define media identity, timeline, model, and job schemas.
- [ ] Add schema compatibility tests.

### Phase 1: shared runtime feasibility

- [ ] Complete A1 and decide single host versus generated proxies.
- [ ] Stop or redesign if the host destabilizes VLC or cache generation.

### Phase 2: common AI/index foundation

- [ ] Complete B1-B3 without changing current live-caption behavior.
- [ ] Establish CPU and provider qualification baselines.

### Phase 3: semantic search

- [ ] Ship subtitle-only CLI indexing/query first.
- [ ] Add Whisper and VLC seek integration second.

### Phase 4: object search

- [ ] Ship bounded real-time detection and live boxes on the reference machine.
- [ ] Add structured query over the watched timeline.
- [ ] Add offline backfill only after the live path is stable.
- [ ] Add transcript/object temporal fusion after both indexes are stable.

### Phase 5: offline upscaling

- [ ] Ship deterministic image and file-to-file processing.
- [ ] Measure tiling, color, timing, and providers before real-time work.

### Phase 6: real-time feasibility gate

- [ ] Prove output-size negotiation and GPU transport.
- [ ] If the target misses its frame budget, keep real-time experimental rather
      than weakening the acceptance criteria.

### Phase 7: productization

- [ ] Port selected controllers to the managed host.
- [ ] Add combined semantic + object queries.
- [ ] Add installer/package layout, cache regeneration, diagnostics, model
      management, and deletion/retention UX.
- [ ] Publish a support matrix and reproducible benchmark artifacts.

## Proposed repository additions

Names are provisional until the architecture spikes complete.

```text
native/VLCLR.ManagedHost.Native/

src/
|-- VLCLR.Managed.Abstractions/
|-- VLCLR.ManagedHost/
|-- VLCLR.AI.Contracts/
|-- VLCLR.AI.Runtime/
|-- VLCLR.MediaIndex/
`-- VLCLR.VideoProcessing/

samples/
|-- SemanticTranscriptSearch/
|-- ObjectSearch/
|-- OfflineUpscaler/
`-- RealtimeUpscaler/

tools/
|-- VLCLR.MediaIndexer/
|-- VLCLR.Search.Cli/
`-- VLCLR.ModelManager/

tests/
|-- ManagedHost.Tests/
|-- ManagedHost.IntegrationTests/
|-- MediaIndex.Tests/
|-- SemanticSearch.Tests/
|-- ObjectSearch.Tests/
|-- OfflineUpscaler.Tests/
`-- RealtimeUpscaler.IntegrationTests/

benchmarks/
|-- ManagedHost.Benchmarks/
|-- SemanticSearch.Benchmarks/
|-- ObjectSearch.Benchmarks/
`-- Upscaling.Benchmarks/
```

## Validation strategy

### Always-on model-free CI

- Schema, manifest, protocol, index, and package validation.
- Managed-host discovery and dependency-isolation tests.
- Golden pre/postprocessing tests with tiny synthetic models.
- Native AOT and managed-host callback/export smoke tests.
- Queue, cancellation, generation, and failure-injection tests.

### Opt-in model correctness CI

- Hash-verified model downloads cached by manifest hash.
- Golden translations and embeddings.
- YOLO reference detections.
- Super-resolution parity and tile-seam tests.
- CPU versus accelerated-provider output comparisons.

### VLC integration CI

- Cache generation and simultaneous managed plugin loading.
- Verification that one CoreCLR serves all managed modules.
- Headless frame/audio callback lifecycle.
- Seek, flush, stop, media replacement, and close.
- Worker crash/restart and stale-result rejection.
- Search-result seek accuracy.
- Output-size negotiation and upscaler fallback.

### Machine-specific performance lab

- Record CPU, GPU/NPU, driver, OS, VLC build, and power mode.
- Test long media, continuous speech, rapid seeks, multiple instances, and
  device loss.
- Measure per-stage latency, throughput, allocations, working set, GPU memory,
  copies, queue depth, drops, and A/V drift.
- Use fixed visual and semantic corpora with human review where automated
  metrics are insufficient.

## Privacy, security, and licensing

- Indexes can contain private transcripts and thumbnails. Store them under a
  user-controlled local root with explicit deletion and retention commands.
- Production logs contain IDs, timing, model/provider data, and error codes,
  not speech, translation, transcript, or query text by default.
- Named pipes are scoped to the user/session and reject unrelated clients.
- Managed plugins are trusted code; dependency isolation is not a security
  boundary.
- Every model/tokenizer requires source, revision, license, redistribution
  decision, and checksum before packaging.
- Enhanced outputs and thumbnails inherit the sensitivity of their source.

## Major risks

| Risk | Mitigation |
|---|---|
| CoreCLR startup breaks VLC cache generation | Prove A1 first, separate descriptor discovery from feature startup, retain Native AOT fallback |
| Managed dependency conflict | Per-plugin load contexts, shared contracts only in default context, conflicting-version tests |
| Native ML DLL conflict inside VLC | Default heavy inference to isolated workers and allowlist in-process native dependencies |
| Managed plugin cannot unload | Make restart-required upgrade the MVP; add cooperative unload only after callback roots are removable |
| Background indexing affects playback | Priority classes, bounded queues, resource budgets, pause/throttle controls |
| Weak semantic relevance | Labeled corpora, versioned relevance metrics, deterministic regression cases |
| YOLO sampling misses short events | Interval plus scene-change sampling, accuracy controls, optional tracking |
| Upscaling changes color or creates seams | Golden color/tile corpus, overlap blending, reference comparison |
| Real-time model misses budget | Latest-frame replacement, automatic bypass/downgrade, hardware support matrix |
| GPU inference competes with VLC decode | Measure coexistence, choose device/provider explicitly, retain CPU/offline modes |
| Model downloads become irreproducible | Commit hashes/manifests, cache by manifest, never silently use `latest` |

## Decisions required before implementation

1. **Runtime interpretation:** confirm one CoreCLR per VLC process, rather than
   only one shared external worker process.
2. **Host packaging:** one host DLL with manifest-discovered submodules
   (recommended) or generated proxy DLL per plugin.
3. **Search UX:** CLI/sidecar first (recommended), VLC interface module, or
   separate desktop app.
4. **Index scope:** one media file first (recommended) or an entire library.
5. **Provider baseline:** pinned self-contained ONNX Runtime for reproducibility,
   with Windows ML evaluated as an optional shared provider deployment.
6. **YOLO model:** choose only after license, export, labels, accuracy, and
   target-device benchmark review.
7. **Offline media adapter:** VLC pipeline or dedicated decoder/encoder, decided
   from timestamp, color, and throughput evidence.
8. **Real-time target:** name the first required resolution, frame rate, and
   reference GPU before selecting a model.

## Overall completion criteria

- Existing subtitle translation remains correct, recoverable, and independently
  usable.
- At least three independent managed VLC plugins share one CoreCLR with measured
  isolation and failure behavior.
- A media file can be indexed for transcript semantics and YOLO detections,
  searched locally, and opened/seeking at the selected timestamp.
- Transcript and object evidence can be joined by media time.
- Offline upscaling produces synchronized output with bounded memory, verified
  quality, and complete provenance.
- Real-time upscaling either meets a declared hardware/resolution/frame-rate
  matrix with graceful fallback or remains explicitly experimental.
- Models, providers, indexes, and outputs are versioned and reproducible.
- VLC callbacks never synchronously perform heavy inference or unbounded I/O.

## Recommended first implementation slice

The product preference is now to implement YOLO object search first. The
transport/runtime portion of the live-path spike is complete; the next bounded
work is:

1. **YOLO model correctness:** pin a legally redistributable ONNX model,
   compare the implemented C# preprocessing/postprocessing against golden-image
   detections.
2. **GPU overlay and sustained live gate:** render the already-live detections
   through a D3D11 output/shader or GPU-composited VLC subpicture, then measure
   callback time, capture-to-result latency, video drops, memory, and sustained
   thermals at 1080p/30.

These validate the two largest YOLO-specific assumptions without committing to
the search UI. If both pass, productize the live overlay and then add rolling
search/seek over watched detections. Process isolation remains an optional
all-C# follow-on if its crash boundary justifies shared-handle/IPC complexity.
Shared runtime and semantic search remain independent follow-on work. Offline
backfill and real-time upscaling remain later work.

## Technical references

- [.NET native hosting with `nethost` and `hostfxr`](https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting)
- [Assembly loading and dependency isolation](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext)
- [Assembly unloadability](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability)
- [ONNX Runtime execution providers](https://onnxruntime.ai/docs/execution-providers/)
- [ONNX Runtime I/O binding](https://onnxruntime.ai/docs/performance/tune-performance/iobinding.html)
- [Windows ML overview](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/overview)
- [Real-ESRGAN reference implementation](https://github.com/xinntao/Real-ESRGAN)
