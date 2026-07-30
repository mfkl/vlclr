# YOLO Object Search Plan

**Status:** Pure-C# live GPU boxes/labels validated; indexing pending
**Selected feature:** Real-time object detection with rolling object search
**Parent plan:** [INTELLIGENT_MEDIA_PLATFORM_PLAN.md](INTELLIGENT_MEDIA_PLATFORM_PLAN.md)
**Initial platform:** Windows x64, VLC 4.x, .NET 10, D3D11, OpenVINO GPU
**Reference hardware:** Dell XPS 13 9310, i7-1195G7, Iris Xe, 32 GB RAM
**Last updated:** 2026-07-30

## Implementation status

| Milestone | Status | Current result |
|---|---|---|
| Pure-C# VLC D3D11 surface access | Complete | `VLCFrame` exposes VLC's borrowed decoder texture and array slice without a C++ shim. |
| Detection/query core | Complete | COCO-80 labels and aliases, natural query parsing, YOLOX decoding, confidence filtering, and NMS have model-free tests. |
| Live GPU feasibility | Complete | Native AOT plugin preserves D3D11VA playback, performs GPU scale/letterbox and OpenVINO GPU inference, and sustains a scheduled 15-Hz Nano path in the short reference run. |
| On-screen GPU overlay | Complete | Fresh boxes and class/confidence labels are BGRA-rasterized from metadata and GPU-composed into NV12 through the D3D11 video processor. |
| Golden and sustained release gates | Pending | Compare against reference detections and run the 30-minute 1080p/30 thermal/drop/memory qualification. |
| Rolling search and seek | Pending | Add PTS-tagged persistence, presence intervals, query UI, and click-to-seek after the overlay path is stable. |

## Decision summary

Real-time YOLO object detection is feasible on the reference hardware at a
15-Hz detector cadence. The Native AOT `dotnet_yolo_search` plugin now runs live
inside VLC with D3D11VA decoding still active, reads VLC's decoder texture array
and slice through unsafe C#, GPU-scales/letterboxes frames to NV12 416 x 416,
binds both planes as OpenVINO Remote Tensors, runs YOLOX-Nano on Iris Xe,
decodes labeled boxes in C#, and GPU-composes fresh boxes with class/confidence
labels into the NV12 output. Rolling indexing, golden-image agreement, and the
sustained thermal release gate remain.

All authored feature and interop source is C#. There is no C++ bridge, C++
worker, or provider shim. VLC, D3D11, the Intel graphics driver, and OpenVINO
remain native runtime dependencies.

The recommended first version is:

- analyze the currently playing local video;
- recognize a fixed, documented class vocabulary;
- run entirely on-device;
- sample live frames at a target detector cadence below the rendered frame rate;
- send only the latest eligible frame to a background C# inference thread;
- overlay the freshest valid boxes while VLC continues rendering every frame;
- store watched detections in SQLite for rolling class/time search;
- use YOLOX-Nano 416 as the provisional performance-safe profile;
- keep YOLOX-Tiny 416 as a viable quality profile and choose between them after
  golden-image and visual-video comparison;
- require VLC D3D11 hardware decode and OpenVINO GPU inference;
- use an owned NV12 inference texture with no CPU pixel readback;
- fail clearly if the GPU path is unavailable; do not silently fall back to CPU;
- add unplayed-file indexing later.

The main risks are now drawing the overlay without forcing a software chroma
conversion, matching the official YOLOX output on golden images, packaging the
OpenVINO runtime, and sustaining the result under a 30-minute thermal run. The
VLC D3D11 picture ABI, OpenVINO Remote Tensor integration, C# YOLOX decoding,
and short-duration Iris Xe contention are no longer feasibility unknowns.

## Reference-hardware feasibility target

The development machine was inspected on 2026-07-29:

| Component | Observed |
|---|---|
| Machine | Dell XPS 13 9310 |
| CPU | Intel Core i7-1195G7, 4 cores / 8 threads |
| GPU | Intel Iris Xe integrated graphics, driver 32.0.101.7077 |
| Memory | 31.7 GB |
| OS | Windows 10 Pro 22H2, build 19045 |

The repository's packaged OpenVINO GPU worker successfully initialized and
warmed a model on `GPU` on this machine. Intel also lists Iris Xe and Windows 10
as supported by current OpenVINO releases.

The pure-C# probe in
[`YoloObjectSearch.CSharpProbe`](benchmarks/YoloObjectSearch.CSharpProbe/)
proved all of the following on this exact OS, driver, and GPU:

- D3D feature level 11.0;
- extended resource sharing enabled;
- extended shared NV12 textures enabled;
- a decoder-style 1920 x 1080 NV12 texture array can be created;
- an arbitrary decoder array slice can be selected from C#;
- the D3D11 video processor accepts NV12 input and output;
- a 1920 x 1080 NV12 surface can be GPU-scaled/letterboxed into an owned
   416 x 416 NV12 surface;
- OpenVINO can consume the completed surface without CPU readback.

The same C# probe and the live
[`YoloObjectSearch`](samples/YoloObjectSearch/) plugin validated the rest of the
GPU path:

- OpenVINO 2026.2.1 imports both NV12 planes as D3D11 Remote Tensors;
- NV12-to-BGR preprocessing and YOLOX inference execute on Iris Xe;
- no staging texture, `Map`, CPU pixel conversion, or CPU inference is used;
- a 15-Hz deadline scheduler alternates one- and two-frame gaps on 24 fps input
  without queueing old frames;
- live Nano inference is typically 6-10 ms after warm-up;
- a 25-second live run submitted/inferred 120/120 frames after model warm-up,
  with zero busy skips, 1.115/10.422 ms average/max GPU blit, and
  7.223/11.440 ms average/max inference;
- VLC simultaneously keeps D3D11VA decode and Direct3D11 rendering active;
- `show me the ball confidence 0.03` resolves to class 32 and returns only
  `sports ball` detections.
- a 48-second 1920 x 1080 person-overlay run rendered 454 frames from 225 fresh
  result uploads containing 313 selected boxes, with D3D11VA and NV12 retained.
- a follow-up 42-second box/label run rendered 996 frames from 496 uploads
  containing 692 selected boxes without an overlay or inference error.

### Proposed release gate on this machine

- 1920 x 1080, 30 fps H.264 playback remains smooth;
- YOLOX-Tiny, fixed 416 x 416 input;
- at least 10 fresh detection results per second after warm-up;
- capture-to-result p95 at or below 150 ms;
- no synchronous wait for inference in `ProcessFrame`;
- D3D11 hardware decode remains active;
- no texture is mapped or copied into CPU-visible memory;
- video frame-drop regression at or below 1 percentage point;
- stale boxes disappear rather than remaining after 250 ms;
- working set stays bounded during a 30-minute run;
- the target holds for 15 minutes after the laptop reaches steady-state
  temperature, on AC power.

Ten detector updates per second is a provisional engineering floor, not the
final UX choice. The Phase 2 build must expose 5/10/15/30-Hz modes for visual
evaluation. The detector may skip eligible frames; VLC must not.

### Measured short-duration feasibility

Each contention run precompiled and warmed the model before VLC started, then
performed 300 cross-device shared-NV12 inferences at 15 Hz for 20 seconds while
VLC played the local 1080p H.264 Big Buck Bunny fixture. VLC logs confirmed
D3D11VA decode and Direct3D11 rendering.

| Profile | Median | p95 | Maximum | VLC late/drop messages |
|---|---:|---:|---:|---:|
| YOLOX-Nano 416 | 7.46 ms | 9.38 ms | 11.80 ms | 0 / 0 |
| YOLOX-Tiny 416 | 10.50 ms | 19.04 ms | 55.30 ms | 1 / 0 |

At uncapped maximum throughput through the same video-processor, cross-device
shared texture, and Remote Tensor path, Nano averaged 11.45 ms (87.35
round-trips/s) and Tiny averaged 20.96 ms (47.72 round-trips/s).

These are transport/inference feasibility measurements, not detection-quality
or release claims. The source texture is synthetic, postprocessing/NMS is not
included, the VLC plugin bridge is not yet present, and the runs are too short
to establish thermal stability. Model compilation took about 15-19 seconds in
the clean contention runs, so production must compile/cache and warm the model
before enabling live detection.

## Locked product decisions

- **GPU-only media path:** VLC hardware decode, GPU resize/preprocessing, and GPU
  inference are mandatory. There is no CPU pixel path or CPU inference fallback.
- **Scope:** v1 supports local file playback. Webcam and network-stream support
  are deferred.
- **Cadence:** choose the final detector update rate after watching and measuring
  the working GPU prototype.
- **Failure:** unsupported hardware/driver/model combinations disable the feature
  with a diagnostic instead of degrading to CPU.

Lightweight CPU orchestration is unavoidable: VLC/plugin control flow, IPC,
SQLite, result parsing, and potentially NMS operate on metadata. This plan
interprets "no CPU" as no CPU video decode, pixel conversion/readback,
preprocessing, or model inference. See the remaining design question.

## Assumptions to confirm

This plan still assumes:

1. **Vocabulary:** fixed object classes are acceptable for the first release.
   This is normal object detection, not arbitrary text/open-vocabulary search.
2. **Search scope:** the first release analyzes the currently playing local video. It
   indexes what has been watched; it does not scan unseen future content.
3. **Result UX:** live bounding boxes and labels are required. A rolling
   list/timeline provides click-to-seek for prior detections.
4. **UI:** boxes render in VLC; search/status may initially use a small companion
   application or CLI.
5. **Licensing:** the default model and its weights must be redistributable
   under a permissive license.
6. **Execution:** inference is asynchronous, latest-frame-wins, and may reduce
   its cadence rather than degrading VLC playback.

See **Design questions** at the end for the choices that need product input.

## Product definition

### User story

A user enables **Live object detection** while a video plays. Boxes and labels
appear with bounded latency. The user can then search the watched timeline for:

- `person`
- `car confidence:0.70`
- `cell phone from:00:20:00 to:00:30:00`
- `bicycle`

Each result shows:

- detected class;
- confidence;
- media timestamp;
- a button or action that seeks the current VLC player to that timestamp.

Adjacent hits for the same class are grouped into an object-presence interval so
that a person visible for 20 seconds does not produce 20 nearly identical search
results.

The live overlay is based on the newest non-stale result. It is not allowed to
hold the video frame while waiting for a matching inference result.

### MVP acceptance

The MVP is complete when:

- live detection can be enabled and disabled without restarting VLC;
- 1080p/30 playback meets the reference-hardware release gate above;
- the worker receives bounded, timestamped frames with latest-frame-wins
  behavior;
- boxes are mapped to source coordinates and rendered only while fresh;
- supported labels and synonyms are discoverable;
- detections match a pinned reference implementation on golden images;
- each detection and stored result uses the source frame PTS;
- selecting a result seeks VLC to the right moment;
- query p95 is below 200 ms for a two-hour watched timeline;
- memory and queue sizes are bounded;
- model, labels, preprocessing, thresholds, and postprocessing are reproducible
  from the stored index metadata;
- the VLC playback/video callback never waits for model inference.

### Explicit non-goals for the MVP

- arbitrary natural-language visual search;
- custom user-trained classes;
- facial recognition or person identity;
- object tracking across every frame;
- inference on every rendered source frame;
- object search over portions of a video that have not been played or scanned;
- library-wide background crawling;
- cloud inference or upload;
- transcript/object combined queries;
- frame-perfect discovery of very short appearances.

## Feasibility assessment

| Area | Feasibility | Evidence and main risk |
|---|---|---|
| VLC D3D11 surfaces | Proven live | `VLCFrame.TryGetD3D11Surface` reads the texture, array slice, and processor view through tested C# ABI layouts. |
| In-process GPU transport | Proven live | A bounded C# callback blits the selected decoder slice into an owned 416 x 416 NV12 texture with no CPU pixel path. |
| OpenVINO Remote Tensor | Proven live | Nano consumes both NV12 planes and completes GPU preprocessing/inference while VLC keeps hardware decoding active. |
| Native AOT plugin | Proven live | The 2.64 MB plugin exports VLC entry points and requires no installed .NET runtime or authored native shim. |
| Search index | High | Detection rows and time intervals map naturally to SQLite indexes. |
| Live box overlay | Proven live | A C# D3D11 video-processor path blends a small BGRA metadata layer into an owned NV12 target, then GPU-copies the result to VLC's current decoder slice without mapping video pixels. |
| VLC seek integration | High | `VLCPlayer` already supplies the control boundary for prior results. |
| Rich in-VLC UI | Medium | There is no existing object-search UI surface in VLCLR. A CLI/sidecar is the shortest path. |
| Detection quality | Medium to high | Common COCO-style objects are mature; small, occluded, unusual, or domain-specific objects will still be missed. |
| Licensing | Manageable | Use a permissively licensed model candidate and pin the exact graph, weights, labels, and license. Do not silently bundle an AGPL model. |

### Important product limitation

At 10 detector updates per second, an object visible between sampled frames can
still be missed. Results also arrive after their source frame has been rendered,
so rapidly moving objects can make boxes lag. The UI must expose the current
detector cadence/latency and never imply exhaustive frame-by-frame coverage.

## Recommended architecture

```text
VLC D3D11 hardware decoder
  - decoder texture array (NV12/P010)
            |
            v
Native AOT C# controller
  - current media identity
  - PTS-based frame rate limiter
  - access D3D11 opaque picture + array slice
  - D3D11 VideoProcessor scale/letterbox
  - update owned 416 x 416 NV12 inference texture
  - read newest completed result
  - GPU-composite fresh boxes/labels
  - append/search/seek controller
            |
            |
            v
Background C# OpenVINO worker thread on the same Iris Xe adapter
  - D3D11 RemoteContext/RemoteTensor
  - GPU NV12 -> BGR/tensor preprocessing
  - YOLOX GPU inference
  - confidence filter + NMS
  - PTS-tagged detection results
  - latency/GPU telemetry
            |
            +----> rolling SQLite detections/intervals

CLI or companion UI
  - live status/cadence/latency
  - label/synonym search
  - timeline/results
  - click-to-seek
```

### Why inference runs on a background thread in v1

- Model compilation and inference never block VLC's playback callback.
- Logical queue capacity is one, so old detector frames cannot accumulate.
- The 2.61 MB Native AOT plugin needs no installed .NET runtime.
- The implementation stays entirely in C# while OpenVINO remains an external,
  version-pinned native runtime.
- A later isolated process can reuse the same C# contracts if crash isolation
  becomes worth the shared-handle and IPC complexity.

## Live frame transport decision

Use a **zero-CPU-readback D3D11 inference surface**. This is not literally
zero-copy:
one GPU video-processor operation is required to resize/letterbox the decoder
surface into a shareable 416 x 416 NV12 texture. Pixels never enter system RAM.

The current `VLCVideoFilterBase` is a CPU-picture abstraction. Add a separate
GPU filter contract; do not make `VLCFrame.GetPlaneSpan()` pretend that a D3D11
opaque picture has CPU planes.

The GPU callback should:

1. verify D3D11 opaque NV12/P010 input and the expected VLC video-context type;
2. draw the newest result only if its age/PTS policy says it is fresh;
3. check a PTS-based detector rate limiter;
4. atomically claim the single latest-frame inference slot;
5. if inference is busy, increment a skipped counter and
   return the frame unchanged;
6. use `ID3D11VideoProcessor` to preserve aspect ratio, letterbox, and scale the
   decoder texture array slice into the owned NV12 input;
7. signal the C# worker thread with PTS/generation metadata and return.

The worker consumes only the newest ready slot. If inference is slower than the
target cadence, intermediate detector frames are intentionally replaced. A
queue of old video frames would increase latency indefinitely and is forbidden.

### Future process-isolation option

If process isolation is later required, add:

- VLC `VLC_CODEC_D3D11_OPAQUE` / NV12 input;
- two or three fixed 416 x 416 NV12 D3D11 textures;
- `D3D11_RESOURCE_MISC_SHARED_NTHANDLE` and keyed-mutex synchronization;
- `IDXGIResource1.CreateSharedHandle` plus handles duplicated into the worker;
- one VLC producer and one OpenVINO worker consumer;
- both D3D11 devices pinned to the same adapter LUID;
- producer key 0, ready key 1, with generation counters preventing ABA/stale
  slot reuse;
- no `Map`, staging texture, `GetData`, CPU memcpy, or RGBA named-pipe payload;
- a small named-pipe control/result protocol;
- result records containing generation, source PTS, dimensions, boxes,
  confidence, inference time, and provider.

The process worker would open each shared texture on its own D3D11 device, wrap
it through
OpenVINO's D3D11 Remote Context/Tensor API, adds GPU NV12-to-BGR preprocessing to
the compiled model, waits for inference completion, then releases the keyed
mutex slot back to VLC.

### VLC framework boundary

VLC's public `filter_t` carries `vctx_in/vctx_out`, and its D3D11 modules show how
to consume `VLC_CODEC_D3D11_OPAQUE`, obtain the decoder device, preserve the
video context, and access `picture_sys_d3d11_t`. The D3D11 picture layout and
helpers live in VLC module-private headers, however.

Use the tested unsafe-C# `VLCPictureContext`, `VLCD3D11PictureSystem`, and
`VLCD3D11PictureContext` layouts pinned to VLC 4.0.6 to:

- validates the D3D11 video context and picture context;
- hides `picture_sys_d3d11_t` and COM vtables from plugin writers;
- owns video-processor, shared-texture, handle, and keyed-mutex lifetimes;
- exposes a narrow C ABI to the Native AOT controller;
- fails closed on unknown VLC layout/version.

Add a separate `VLCD3D11VideoFilterBase`/`VLCD3D11Frame` managed API. Preserve
the current CPU `VLCVideoFilterBase` API for existing plugins.

### Offline/backfill frame extraction

The following adapter decision applies only to indexing unplayed portions of a
file after the real-time overlay is working.

Define an `IMediaFrameSource` boundary and implement the first two adapters as a
short benchmark spike.

### Option A: LibVLC thumbnail-by-time requests

**Recommended MVP candidate.**

VLC 4 provides `libvlc_media_thumbnail_request_by_time`, with:

- precise or fast seek;
- requested output dimensions;
- RGBA output;
- asynchronous completion;
- cancellation and timeout;
- the actual decoded picture timestamp.

Advantages:

- smallest implementation;
- uses VLC's own demuxers and decoders;
- no extra decoder dependency;
- easy bounded request lifecycle;
- produces a compact frame near the detector's input size.

Risks:

- one seek per sample can be expensive for long GOPs;
- precise seeking may repeatedly decode from a prior keyframe;
- opening multiple requests concurrently may increase decoder and memory cost
  without increasing useful throughput;
- color, rotation, crop, and anamorphic-video behavior require fixtures.

### Option B: sequential LibVLC video callbacks

**Benchmark fallback for dense scans.**

Play/decode the file as fast as practical through a headless LibVLC player,
discard frames outside the sampling schedule, and copy selected frames into a
bounded queue.

Advantages:

- decodes the stream once in order;
- likely better for high sample rates and long-GOP content;
- naturally reports frame PTS in decode order.

Risks:

- LibVLC video callbacks introduce memory copies and restrict hardware decoding;
- output buffer lifecycle, pitch, chroma, orientation, and end-of-stream
  handling are more complex;
- a headless player needs explicit fast-as-possible and clock behavior;
- callbacks must never block on inference.

### Option C: dedicated decoder such as FFmpeg

**Deferred fallback.**

Use only if both VLC paths fail timestamp, color, codec, or throughput gates.
This can be the fastest and most controllable indexing path, but it creates a
second media stack plus additional packaging and licensing work.

### Extractor selection gate

Benchmark Options A and B on:

- short H.264 MP4;
- two-hour H.264 or HEVC file with long GOPs;
- variable-frame-rate media;
- rotated phone video;
- 4:2:0 limited-range and full-range samples;
- a file with non-zero start timestamps.

For each profile, record:

- wall-clock indexing time;
- extracted frames per second;
- requested versus actual PTS error;
- duplicate frame rate;
- peak working set;
- CPU use;
- cancellation latency;
- decoded color/orientation correctness.

Choose the simplest adapter that meets the balanced-profile target. Keep the
other behind the interface until codec coverage proves whether it is useful.

## Model decision

### Recommended candidate

Keep both **YOLOX-Nano at 416 x 416** and **YOLOX-Tiny at 416 x 416** through
the correctness/visual-quality spike. Nano is the provisional default because
it had the cleanest 15-Hz contention result and the most GPU headroom. Tiny is
still performance-viable and may become the quality default if its recall is
visibly better.

The official YOLOX repository:

- is Apache-2.0 licensed;
- documents ONNX export and an ONNX Runtime example;
- reports approximately 5.06 million parameters for Tiny and 0.91 million for
  Nano;
- provides a stable reference for letterboxing, grid decoding, confidence
  calculation, and NMS.

Performance alone does not select the final model: both passed the short
reference-hardware gate. Before redistribution, review and store the exact
checkpoint/export license and dataset attribution; the repository license alone
is not a substitute for an artifact review.

### Why not default to Ultralytics weights

Ultralytics currently offers its code and models under AGPL-3.0 by default, with
an enterprise license for proprietary commercial use. Do not make an
Ultralytics export the bundled default unless VLCLR intentionally adopts the
AGPL obligations or obtains the appropriate commercial rights.

Users may later configure external compatible models, but the product must not
download or bundle one without making its license and compatibility explicit.

### Detector abstraction

```csharp
public interface IObjectDetector : IAsyncDisposable
{
    DetectorDescriptor Descriptor { get; }

    ValueTask<DetectionBatch> DetectAsync(
        VideoFrame frame,
        DetectionOptions options,
        CancellationToken cancellationToken);
}
```

The implementation should initially be `YoloXObjectDetector`. Do not build a
generic graph interpreter. Compatibility is defined by a strict, versioned model
manifest and a tested adapter.

### Required model manifest

Store and validate:

- model ID and revision;
- source URL/repository revision;
- model and label-file SHA-256;
- model and weights license;
- training label set and label order;
- input tensor name, type, layout, and fixed shape;
- channel order;
- resize and interpolation rule;
- letterbox placement and pad value;
- pixel scaling/normalization;
- output tensor names and shapes;
- YOLO strides/grid-decoding version;
- confidence formula;
- default objectness/class thresholds;
- class-aware or class-agnostic NMS;
- NMS IoU threshold;
- maximum detections;
- postprocessor version;
- known provider constraints.

An index run references the full model-manifest hash. Any behavior-affecting
change invalidates or versions the old detection index.

## Detection pipeline

For every scheduled sample:

1. Read the D3D11 decoder texture, array slice, source PTS, dimensions, color
   metadata, and orientation through the unsafe-C# VLC ABI view.
2. Use the D3D11 video processor to preserve aspect ratio and letterbox into a
   owned 416 x 416 NV12 texture.
3. Signal the background C# worker with metadata only.
4. Wrap the owned texture as OpenVINO D3D11 NV12 Remote Tensors.
5. Run GPU NV12-to-BGR conversion, tensor layout/scaling/normalization, and
   YOLOX inference through one cached compiled model.
6. Decode grids and strides.
7. Calculate confidence using the pinned objectness/class formula.
8. Apply the pinned confidence threshold and NMS.
9. Transform boxes back through the saved letterbox geometry into source-frame
   coordinates.
10. Clamp and return normalized plus source-pixel box coordinates with source
    PTS and generation.

Steps 6-8 may operate on small output metadata on CPU unless the remaining CPU
boundary decision requires a GPU postprocessing graph. No video pixels or input
tensors may be read back.

### Correctness fixtures

Before video indexing, compare C# output with the official YOLOX Python path on
a small golden-image corpus:

- no detections;
- one large centered object;
- multiple classes;
- overlapping same-class boxes;
- overlapping different-class boxes;
- portrait and landscape images;
- non-square source;
- objects at image edges;
- low-confidence candidates around the threshold.

Compare decoded class, confidence, and box coordinates within documented
tolerances. Keep preprocessing and NMS unit-testable without loading ONNX
Runtime.

## Sampling and result semantics

### Initial profiles

| Profile | Target detector cadence | Intended use |
|---|---:|---|
| Low power | 5 updates/s | Nano GPU or reduced power |
| Balanced | 10 updates/s | default release gate |
| Responsive | 15 updates/s | Iris Xe when latency remains bounded |

These are rate-limit targets, not promises. If the worker cannot keep up, the
plugin skips detector submissions instead of queueing stale frames. VLC still
renders at the source frame rate.

The first implementation schedules by source PTS. Scene-change and motion-aware
sampling are later optimizations and must never cause unbounded bursts.

### Presence intervals

A frame detection is evidence at one instant, not evidence for the entire gap
until the next frame. Build display intervals using an explicit algorithm:

- group by class;
- sort by actual frame time;
- merge detections when the gap is no more than a profile-derived tolerance;
- retain peak confidence, first/last evidence time, evidence count, and the best
  representative evidence PTS/box;
- label the interval as sampled/estimated;
- do not claim that boxes belong to the same physical object.

True object tracks and object counts across time are a later, separate feature.

## Index design

Use SQLite with WAL mode during a run and publish only a valid completed or
resumable index state.

### Core entities

```text
media
  media_id, canonical_uri, size, modified_utc, quick_hash, duration_ms

index_runs
  run_id, media_id, status, started_utc, completed_utc
  worker_version, extractor_id, sampling_profile
  model_manifest_hash, provider, failure

frames
  frame_id, run_id, requested_ms, actual_ms
  source_width, source_height, orientation

detections
  detection_id, frame_id, class_id, class_name, confidence
  x, y, width, height
  source_x, source_y, source_width, source_height

presence_intervals
  interval_id, run_id, class_id, start_ms, end_ms
  peak_confidence, evidence_count, representative_detection_id

```

Add indexes at minimum for:

- `(run_id, class_id, actual_ms)`;
- `(run_id, class_id, confidence)`;
- `(media_id, status)`;
- interval class/time lookup.

### Media identity and invalidation

The default identity is canonical URI/path, file size, modified time, and a
bounded quick hash. Offer a full hash for portable or high-assurance indexes.

Re-index when:

- media identity changes;
- model-manifest hash changes;
- sampling profile changes;
- detector/postprocessor version changes;
- an incompatible extractor timestamp/color fix is introduced.

Keep the prior completed index until the replacement run is atomically
published. A canceled or failed run must not hide a valid older index.

### Thumbnail policy

Thumbnails are disabled in the GPU-only v1. Producing image files requires a
readback or a separate hardware-encoding path and is not needed for live boxes,
label/time search, or click-to-seek.

If added later, generate at most one representative thumbnail per presence
interval through an explicitly measured asynchronous GPU/hardware-encode path.

Provide a **Delete index** action. Index data stays local unless a future feature
explicitly changes that contract.

## Search API

The first query API should be structured, even if the UI presents a search box:

```csharp
public sealed record ObjectSearchQuery(
    string Label,
    float MinimumConfidence = 0.50f,
    TimeSpan? From = null,
    TimeSpan? To = null,
    int Limit = 100);
```

Resolve labels through a versioned alias table:

```text
person -> person
people -> person
bike -> bicycle
phone -> cell phone
tv -> tv
```

Return both intervals and the underlying evidence:

```csharp
public sealed record ObjectSearchResult(
    string Label,
    TimeSpan Start,
    TimeSpan End,
    float PeakConfidence,
    int EvidenceCount,
    TimeSpan SeekTime,
    DetectionBox RepresentativeBox);
```

Defer free-form Boolean grammar until exact label, confidence, and time-range
queries are stable. Transcript/object fusion can later join on media identity and
overlapping time intervals.

## Worker protocol and lifecycle

Reuse the architectural patterns from `LiveAudioTranslator`, but define a live
video protocol plus shared D3D11 texture ring instead of sending images through
the live-audio message types.

Required operations:

- protocol negotiation;
- capabilities/model list;
- configure model/provider/cadence;
- start/stop live session;
- frame-slot-ready notification;
- PTS-tagged detection result;
- health and performance telemetry;
- pause/resume;
- worker/provider failure and optional Tiny-to-Nano GPU profile fallback;
- graceful shutdown.

Telemetry should include:

- rendered/eligible/submitted/skipped/inferred frame counts;
- detections written;
- capture/resize/transport/inference/postprocess/result latency;
- result age when rendered;
- current cadence, GPU model/profile, and failure/fallback reason;
- queue/ring occupancy;
- video frame-drop baseline and active value where VLC exposes them.

The live inference queue has logical capacity one: latest frame wins. SQLite
writing uses a separate bounded queue so a slow disk cannot delay detections or
the video callback.

## Runtime and packaging

The first release can use:

- **VLC plugin:** 2.61 MB Native AOT C# controller and D3D11 interop;
- **worker:** background thread compiled into the same Native AOT plugin;
- **inference:** pinned external OpenVINO GPU runtime called through C# P/Invoke;
- **model:** separate versioned asset, installed/downloaded explicitly;
- **index:** user-local data outside the VLC plugin directory.

This avoids requiring a machine-wide .NET installation. OpenVINO is loaded into
`vlc.exe` for v1; it remains separately versioned and is not folded into the
plugin DLL.

Keep model assets separate from application binaries so users can:

- install only the detector profile they need;
- update or remove a model independently;
- inspect its source, hash, and license;
- install the validated Intel GPU worker package.

There is no CPU inference worker in v1. If D3D11 opaque input, shared NV12,
OpenVINO GPU Remote Tensors, or the expected adapter cannot be initialized, the
feature reports the failed gate and stays off.

Do not block this feature on the shared CoreCLR host. Later, the controller can
move from Native AOT to the shared host without changing the detector worker or
index format.

## Performance design

### Baseline rules

- Never run inference or wait for IPC in VLC's playback callback.
- Allow only a bounded D3D11 video-processor blit and non-blocking slot claim in
  the callback.
- Use a PTS rate limiter and logical queue capacity one.
- Drop/replace detector frames when busy; never queue stale frames.
- Cache one OpenVINO compiled model and a bounded infer-request set.
- Reuse one owned NV12 texture and its OpenVINO Remote Tensors.
- Pass metadata between C# threads, not pixel payloads.
- Keep tensor shape fixed for the baseline model.
- Never use a staging texture or map video pixels into CPU memory.
- Tag every frame/result with session generation and source PTS.
- Reject results from old sessions, seeks, or media generations.
- Hide a result when its source PTS is too old for the current playback PTS.
- Separate capture, resize, transport, inference, result, overlay, and SQLite
  timing in telemetry.
- Commit SQLite rows in bounded batches.
- Keep overlay rendering allocation-free in steady state.

### Reference-machine gate

OpenVINO GPU with D3D11 Remote Tensors is the only v1 execution provider.
Correctness is compared against pinned reference outputs, not a product CPU
fallback.

Benchmark the complete VLC path, not an isolated tensor:

- 1080p/30 H.264 playback;
- hardware decoding confirmed active in VLC diagnostics;
- Tiny 416 at at least 10 fresh detection updates/s;
- capture-to-result p95 at or below 150 ms;
- video frame-drop regression at or below 1 percentage point;
- zero CPU pixel readback confirmed by code path and GPU/ETW diagnostics;
- stale result age capped at 250 ms;
- steady memory and cadence for 30 minutes;
- sustained thermal result reported separately from the first minute;
- stop/cancellation observed in under two seconds;
- search p95 below 200 ms.

Also record 720p/30, 1080p/60, and 4K/30 as characterization; they are not MVP
release gates unless explicitly promoted.

### Accelerators

Evaluate:

1. OpenVINO GPU D3D11 Remote Tensor with Tiny and Nano.
2. FP16 and, only if quality is acceptable and the Iris Xe path supports it,
   INT8 model variants.
3. Other GPU providers only if they can consume the shared D3D11 path without
   CPU tensor upload and have explicit packaging/CI coverage.

ONNX Runtime DirectML is not a v1 fallback because the current C# path does not
provide the required direct NV12 D3D11 Remote Tensor integration. The selected
OpenVINO API explicitly supports a D3D11 device, decoder NV12 surfaces, GPU
color conversion, resize, and inference.

### What to measure before optimizing

- time spent in the VLC filter callback;
- D3D11 video-processor wait and blit time;
- keyed-mutex miss/wait counts by ring slot;
- shared-handle notification/open time;
- OpenVINO GPU preprocessing time;
- inference time;
- postprocessing/NMS time;
- result age at overlay;
- rendered-video dropped frames and frame-time percentiles;
- VLC hardware-decoder and GPU-engine utilization;
- GPU memory, shared-memory bandwidth, and adapter contention;
- SQLite time;
- worker startup/model-load time;
- peak working set;
- provider-specific first-run warm-up.

Use PresentMon/ETW or equivalent GPU-engine evidence to distinguish video decode,
video processing, rendering, and compute contention. Model-only FPS is not an
acceptance result.

## Repository layout

Use focused reusable projects rather than placing the implementation directly
in the sample plugin:

```text
src/
|-- VLCLR.D3D11/
|   `-- Managed wrappers for hardware-frame metadata and bridge calls
|-- VLCLR.ObjectDetection/
|   |-- Models/
|   |-- Preprocessing/
|   `-- Postprocessing/
|-- VLCLR.MediaIndex/
`-- VLCLR.AI.Contracts/

samples/
|-- YoloObjectSearch/
`-- YoloObjectSearch.Cli/

tests/
|-- YoloObjectSearch.UnitTests/
`-- YoloObjectSearch.IntegrationTests/

benchmarks/
`-- YoloObjectSearch.CSharpProbe/
    `-- Pure-C# D3D11, Remote Tensor, inference, and decoder probe
```

Avoid moving existing translation code during the first spike. Reuse its proven
patterns, then extract common contracts only where the YOLO implementation
demonstrates a real shared abstraction.

## Delivery plan

### Phase 0: lock product and legal decisions

- [ ] Answer the design questions at the end of this plan.
- [x] Name the first Windows reference hardware.
- [x] Require D3D11 hardware decode and GPU inference with no CPU pixel path.
- [x] Scope v1 to local file playback.
- [x] Make detector cadence a measured/visual decision from the prototype.
- [ ] Confirm that lightweight CPU metadata orchestration/NMS is acceptable.
- [ ] Choose permissive-only versus separately licensed model policy.
- [x] Define the initial COCO-80 supported label list and display aliases.
- [ ] Confirm boxes/labels in VLC and choose the first search/status surface.
- [ ] Choose the default index retention location.

**Exit:** live UX, GPU-only boundary, hardware/cadence target, and model-license
policy are written down.

### Phase 1: model-correctness spike

- [x] Pin candidate YOLOX-Tiny and Nano ONNX artifacts, sizes, URLs, and hashes
      in the evaluation manifest.
- [ ] Complete the weight/artifact and label/dataset attribution review.
- [ ] Implement strict model-manifest loading.
- [x] Port exact YOLOX preprocessing, output decoding, confidence, and NMS.
- [x] Build model-free decoding, letterbox, query, vocabulary, and NMS tests.
- [ ] Build a golden-image comparator against the official Python
      implementation.
- [x] Benchmark Tiny versus Nano through OpenVINO GPU for isolated host-input
      and D3D11 Remote Tensor latency/warm-up.
- [ ] Compare Tiny versus Nano memory and detection quality.
- [ ] Export/evaluate FP16 and, if supported, INT8 variants.

**Exit:** one model is selected and C# detections match the reference within
documented tolerance; isolated inference is fast enough to justify the live
pipeline spike.

### Phase 2: end-to-end live feasibility spike

- [x] Probe shared NV12/BGRA resources and keyed mutexes on the reference GPU.
- [x] Probe 1080p NV12 to shared 416p NV12 D3D11 video-processor scaling.
- [x] Probe direct D3D11/OpenCL NV12-plane import, acquire, and release.
- [x] Run YOLOX-Nano and Tiny through cross-device shared NV12 OpenVINO Remote
      Tensors with GPU color conversion and no CPU pixel path.
- [x] Run short prewarmed 15-Hz Nano/Tiny contention tests while VLC uses
      D3D11VA decode and Direct3D11 rendering.
- [x] Add the version-pinned unsafe-C# VLC D3D11 picture/context ABI view.
- [x] Add a GPU-safe borrowed-surface abstraction to `VLCFrame`.
- [x] Verify VLC keeps D3D11 hardware decoding active with the plugin enabled.
- [x] Add a deadline-accumulating 15-Hz rate limiter.
- [x] Use a single latest-frame owned NV12 input for in-process v1.
- [x] Add pure-C# OpenVINO D3D11 Remote Tensor interop.
- [x] Compile NV12-to-BGR preprocessing into the GPU benchmark model path.
- [x] Transfer the validated preprocessing path into the live C# plugin worker.
- [x] Enforce latest-frame-wins and session/media/seek generations.
- [x] Draw boxes through a D3D11 filter/shader or GPU-composited VLC subpicture
      path without forcing a software chroma conversion.
- [x] Assert that no staging texture, `Map`, or CPU pixel copy occurs.
- [ ] Measure callback time, detector cadence, capture-to-result latency, result
      age, VLC dropped frames, GPU-engine use, memory bandwidth, and temperature.
- [ ] Expose 5/10/15/30-Hz modes and select the UX cadence after visual testing.
- [ ] Cache/compile and warm the selected model before enabling detection;
      never compile the model after time-critical playback work begins.
- [ ] Test 720p/30, 1080p/30, 1080p/60, and 4K/30 for characterization.
- [ ] Run the 1080p/30 release gate for 30 minutes.
- [ ] Record the model/provider/cadence decision and observed limitations.

**Exit:** 1080p/30 hardware-decoded playback with GPU-only Tiny or Nano meets the
reference-machine gate. Failure stops the feature; it does not authorize a CPU
fallback.

### Phase 3: real-time overlay MVP

- [x] Turn the spike into a Native AOT VLC video-filter plugin.
- [ ] Package model, worker, provider, hashes, and license metadata.
- [ ] Add enable/disable, threshold, cadence, and profile options.
- [x] Render class, confidence, and full-color boxes on the supported NV12
      output path.
- [ ] Define a visible fallback for unsupported output chroma.
- [x] Add staleness TTL and hide boxes across seeks/media changes.
- [x] Pause inference submissions and overlay composition while media time is
      not advancing.
- [ ] Add worker restart and optional Tiny-to-Nano GPU degraded mode.
- [ ] Surface active model/provider/cadence/latency in logs and status.
- [ ] Preserve smooth playback when the worker is unavailable or overloaded.
- [ ] Refuse startup with a precise diagnostic when the GPU-only capability
      matrix is not met.

**Exit:** a user can enable live object boxes during playback on the reference
machine, and failure never stalls or terminates VLC.

### Phase 4: rolling object search

- [ ] Define SQLite schema, migrations, media identity, and session generation.
- [ ] Persist PTS-tagged detections outside the callback through a bounded writer.
- [ ] Build presence intervals without frame thumbnails.
- [ ] Implement exact label, alias, confidence, and time-range queries.
- [ ] Add the chosen CLI/companion search and status UI.
- [ ] Seek the current VLC player to a selected result.
- [ ] Explain that results cover watched/analyzed portions only.
- [ ] Handle loop, seek, media change, and duplicate timeline segments.
- [ ] Add clear-history and delete-index controls.

**Exit:** watched detections are searchable and reliably seek to the correct
moment without affecting live overlay latency.

### Phase 5: quality and hardening

- [ ] Add variable-frame-rate, rotated, HDR, interlaced, and unsupported chroma
      tests.
- [ ] Add rapid seek, pause/resume, rate change, loop, and media-switch tests.
- [ ] Add stale-result, ring-overwrite, worker-crash, and Tiny-to-Nano GPU
      profile-fallback tests.
- [ ] Add atomic index replacement and crash-recovery tests.
- [ ] Add path/URI validation and malicious-worker-message tests.
- [ ] Add model hash and manifest-tampering tests.
- [ ] Add index deletion controls.
- [ ] Add performance baselines to CI or a repeatable benchmark job.
- [ ] Document cadence, latency, thermal, recall, and hardware-decode tradeoffs.

**Exit:** supported inputs, failure behavior, privacy, and performance are
documented and repeatable.

### Phase 6: optional improvements

- [ ] Motion-aware cadence and lightweight tracking/interpolation.
- [ ] P010/HDR Remote Tensor support with validated color metadata.
- [ ] Same-process direct decoder-surface inference if measurements prove the
      cross-process GPU blit is a material bottleneck and the isolation tradeoff
      is acceptable.
- [ ] Offline/backfill indexing for unplayed portions using VLC thumbnails versus
      sequential decode benchmarks.
- [ ] Scene-change sampling.
- [ ] Background library indexing.
- [ ] Object tracking and better interval grouping.
- [ ] User-provided compatible models.
- [ ] Hardware-provider auto-benchmark and selection.
- [ ] Search/transcript interval fusion.

## Testing strategy

### Unit tests

- letterbox dimensions and padding;
- NV12/BGR conversion reference values;
- D3D11 slot/key/generation state transitions;
- tensor layout and values;
- grid/stride decoding;
- confidence calculation;
- class-aware and class-agnostic NMS;
- box inverse transforms and clamping;
- label aliases;
- interval merging;
- media identity and invalidation;
- SQLite migrations and query boundaries;
- protocol framing and malformed messages.

### Golden model tests

Keep model downloads opt-in where repository size or redistribution is
undesirable. Verify:

- C# versus official Python classes;
- confidence tolerance;
- IoU/coordinate tolerance;
- OpenVINO GPU versus pinned reference agreement;
- repeatability across clean worker starts.

### Integration tests

- generate or use a legally distributable short video with known object
  appearances;
- exercise capability-probe failures for unsupported shared NV12, adapter
  mismatch, and OpenVINO GPU initialization;
- repeatedly create, share, open, synchronize, seek, restart, and tear down the
  D3D11 texture ring without leaked handles or stale generations;
- verify source PTS, result freshness, overlay coordinates, and click-to-seek;
- force inference slower than playback and verify latest-frame-wins;
- terminate/restart the worker while VLC continues playing;
- seek and change media while a result is in flight;
- verify no old-generation box is ever rendered;
- run 30-minute 1080p/30 thermal and bounded-memory tests;
- compare playback dropped frames with the filter off and on;
- modify/replace the media and verify invalidation;
- modify the model manifest and verify invalidation;
- run a no-detections file;
- run Tiny and Nano GPU profile paths;
- prove no CPU pixel readback with instrumentation.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Boxes arrive too late and visibly lag | Latest-frame-wins, p95 latency gate, short staleness TTL, optional tracker later. |
| VLC private D3D11 picture ABI changes | Native version-pinned bridge, runtime layout/version validation, fail closed. |
| GPU blit or synchronization causes VLC frame drops | Zero-timeout keyed-mutex acquisition, bounded texture ring, callback/GPU-timeline gate. |
| Shared NV12 support varies by hardware | Startup capability probe; GPU-only feature remains unavailable when a mandatory gate fails. |
| OpenVINO preprocessing changes break a valid shared surface | Preserve the passing u8 NV12 surface contract, test both plane handles directly, and keep a synthetic Remote Tensor smoke test. |
| Iris Xe contention with decode/render | Measure GPU engines end-to-end; lower cadence or select Nano, never CPU fallback. |
| Laptop thermal throttling breaks initial results | Require a sustained 15/30-minute benchmark on AC power. |
| Short object appearances are missed | Expose detector cadence and sampled semantics; add motion-aware scheduling later. |
| Incorrect color/layout silently hurts quality | Golden image/video fixtures and strict manifest fields. |
| Model licensing blocks redistribution | Permissive-first policy, exact artifact review, explicit install metadata. |
| GPU packaging becomes larger or fragile | One pinned Intel GPU worker package with verified hashes; no provider matrix in v1. |
| Inference harms VLC playback | Separate process, logical queue capacity one, adaptive cadence, no callback inference. |
| Duplicate results overwhelm users | Presence-interval grouping and representative evidence PTS/boxes. |
| Index becomes stale | Media fingerprint plus model/extractor/sampling version invalidation. |
| A general abstraction delays the feature | Implement YOLOX adapter first; extract shared code only after demonstrated reuse. |

## Rough effort

For one experienced engineer familiar with VLCLR:

| Deliverable | Rough elapsed effort |
|---|---:|
| C# D3D11/OpenVINO feasibility and model spikes | completed |
| Stable GPU-only VLC live-box MVP | additional 3-6 weeks |
| Rolling search and recoverable first release | additional 2-4 weeks |

The highest-variance remaining items are sustained Iris Xe contention,
golden-image agreement, OpenVINO packaging, and model licensing. Library
indexing and tracking are not included.

## Design questions

Locked:

- v1 is local file playback;
- VLC hardware decode and GPU vision inference are mandatory;
- there is no CPU frame/inference fallback;
- detector cadence and Tiny-versus-Nano choice follow measured visual testing.

Still to decide:

1. **CPU boundary:** Is lightweight CPU work on metadata—plugin control, IPC,
   SQLite, box parsing, and possibly NMS—acceptable? Literal zero CPU use is not
   possible for VLC or the plugin.
2. **Vocabulary:** Is a fixed COCO-like list acceptable, or is searching for
   arbitrary concepts such as a specific logo/product required?
3. **Search coverage:** Is history of the watched portion sufficient, or must v1
   also GPU-scan unplayed portions?
4. **Model policy:** Must every bundled model be permissively licensed?
5. **Privacy/storage:** May detection indexes persist until manually deleted?

Recommended remaining answers are: metadata-only CPU work is acceptable, fixed
classes, watched history only, permissive-only model, and persistent local
indexes with explicit deletion.

## References

- [VLC 4 media thumbnail API](vlc/include/vlc/libvlc_media.h)
- [VLC 4 picture API](vlc/include/vlc/libvlc_picture.h)
- [VLC RGBA picture implementation](vlc/lib/picture.c)
- [Official YOLOX repository and model zoo](https://github.com/Megvii-BaseDetection/YOLOX)
- [Official YOLOX Apache-2.0 license](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/LICENSE)
- [Official YOLOX ONNX Runtime demo](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/demo/ONNXRuntime/onnx_inference.py)
- [Official YOLOX preprocessing](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/yolox/data/data_augment.py)
- [Official YOLOX postprocessing](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/yolox/utils/demo_utils.py)
- [Ultralytics licensing](https://www.ultralytics.com/license)
- [ONNX Runtime C# guidance](https://onnxruntime.ai/docs/get-started/with-csharp.html)
- [ONNX Runtime I/O binding](https://onnxruntime.ai/docs/performance/tune-performance/iobinding.html)
- [ONNX Runtime DirectML provider](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)
- [OpenVINO system requirements and supported Iris Xe GPU](https://docs.openvino.ai/2026/about-openvino/release-notes-openvino/system-requirements.html)
- [OpenVINO GPU device guidance](https://docs.openvino.ai/2026/openvino-workflow/running-inference/inference-devices-and-modes/gpu-device.html)
- [OpenVINO GPU Remote Tensor and direct D3D11 NV12 input](https://docs.openvino.ai/nightly/openvino-workflow/running-inference/inference-devices-and-modes/gpu-device/remote-tensor-api-gpu-plugin.html)
- [D3D11 shared NT handles](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiresource-getsharedhandle)
- [D3D11 shared resource and keyed-mutex flags](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_resource_misc_flag)
