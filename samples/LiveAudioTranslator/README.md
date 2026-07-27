# Live audio translation

This sample translates speech to timestamped French subtitles on stock VLC
master without forcing software video decoding. The default opens normal VLC
Qt playback immediately, warms the speech and translation models in a separate
process, discards startup audio, and begins rendering current speech after the
worker is ready.

The default live path is:

```text
runner -> configure .NET worker ----- warm models ----- READY
     \-> VLC Qt immediately -> discard PCM -> bounded PCM transport
                                      |
                  sub-source <- timestamped translated cues
```

Whisper and ONNX inference never run in the Native AOT plugin. Audio callbacks
only observe the source PTS, copy a bounded PCM block, enqueue it, and return
the original VLC block. Dedicated transport and receive tasks perform all
named-pipe I/O.

## Modes

- `live-immediate` is the default. It starts without playback delay, accepts
  audio only after model readiness, keeps one replaceable pending utterance,
  and drops results older than the configured relevance window. It does not
  promise exact synchronization.
- `prepared` is the explicit synchronized local-file mode. It prepares a safe
  cue lead (or the complete timeline when RTF is at least 1.0) before launching
  VLC, then selects cues from the current media PTS.
- `live-sync` is reserved for the delayed live path. Measurements on the
  validated stock VLC build show that `file-caching` and `network-caching`
  buffer compressed input but do not deliver decoded PCM ahead of
  presentation. The request fails clearly instead of silently falling back to
  prepared playback or displaying stale captions.

The measured live-sync limitation needs a small generic VLC decoder-audio
probe before that mode can be enabled for streams. The normal
presentation-side audio filter cannot create recognition headroom by itself.

## Build and deploy

Download and verify the existing default model bundles:

```powershell
pwsh samples/LiveAudioTranslator/download-models.ps1
```

Build the lightweight Native AOT plugin, self-contained worker, runner, and
prepared-mode helper, then deploy them and regenerate the VLC plugin cache:

```powershell
pwsh samples/LiveAudioTranslator/deploy.ps1 `
  -VlcDirectory vlc-binaries/vlc-4.0.0-dev
```

The defaults remain:

```text
speech-model         = whisper-tiny-multilingual
translation-model    = opus-mt-en-fr
speech-provider      = auto
translation-provider = auto
```

Model IDs resolve through
`samples/LiveAudioTranslator.Worker/models/model-profiles.json`. The higher
level profile references the existing version-1 speech or translation
manifest; it does not merge or overwrite those distinct schemas.

## Run

Run visible playback from Git Bash:

```bash
samples/LiveAudioTranslator/run.sh \
  "/c/Users/Martin/Videos/BigBuckBunny.mp4"

samples/LiveAudioTranslator/run.sh --prepared \
  "/c/Users/Martin/Videos/BigBuckBunny.mp4"

# live-sync fails until the decoder-audio probe exists.
samples/LiveAudioTranslator/run.sh --live-sync \
  "/c/Users/Martin/Videos/BigBuckBunny.mp4"
```

Extra VLC options follow the media argument:

```bash
samples/LiveAudioTranslator/run.sh video.mp4 -vvv
```

Hardware video decoding remains enabled. Do not add `--no-hw-dec` to a live
acceptance run.

For `live-immediate`, the runner creates a unique session and pipe, sends the
worker configuration, and starts VLC without waiting for model `READY`.
Audio observed while the worker is disconnected or warming is counted and
discarded without copying a transport payload. The active playback generation
is flushed before the readiness gate opens, so startup or reconnect cannot
replay old PCM. A worker failure does not stop video playback; the runner
reports the missing captions and returns a failure after VLC closes. Closing
VLC closes or terminates the owned worker.

The disabled live-sync implementation retains an 8–60-second delay policy and
can select `p99 cue latency + safety margin` from a valid benchmark profile.
It is not launched while the required decoded-audio lead is absent.

## Provider variants

The worker deliberately packages one Whisper runtime and one ONNX Runtime
provider so native libraries cannot win through undocumented probing order:

```powershell
# CPU baseline
dotnet publish samples/LiveAudioTranslator.Worker -c Release -r win-x64 `
  --self-contained true

# Separate Whisper OpenVINO package
dotnet publish samples/LiveAudioTranslator.Worker -c Release -r win-x64 `
  --self-contained true -p:WhisperRuntimeFlavor=OpenVino `
  -o artifacts/workers/openvino

# Separate Whisper Vulkan package
dotnet publish samples/LiveAudioTranslator.Worker -c Release -r win-x64 `
  --self-contained true -p:WhisperRuntimeFlavor=Vulkan `
  -o artifacts/workers/vulkan

# Separate OPUS-MT DirectML package
dotnet publish samples/LiveAudioTranslator.Worker -c Release -r win-x64 `
  --self-contained true -p:TranslationRuntimeFlavor=DirectML `
  -o artifacts/workers/directml
```

DirectML sessions are sequential and disable memory-pattern optimization.
The current DirectML package line is 1.24.4; CPU/OpenVINO use ONNX Runtime
1.27.1, and the provider/version is part of the benchmark profile key.
Accelerated packages are not selected merely because they load. A cached
profile must show at least a 20% end-to-end improvement over CPU, total RTF at
or below 0.75, accepted quality, stable initialization, acceptable memory, and
no VLC frame-drop regression. Otherwise `auto` uses CPU.

Create the machine/model/runtime fingerprint and an unqualified qualification
template with:

```powershell
dotnet run --project samples/LiveAudioTranslator.Worker -c Release -- `
  --benchmark `
  --catalog samples/LiveAudioTranslator.Worker/bin/Release/net10.0/win-x64/models/model-profiles.json `
  --output artifacts/live-immediate/provider-benchmark.json
```

The template remains `qualified: false` until the representative quality and
playback benchmark fills the timing, quality, GPU, memory, and frame-drop
fields.

Run the 2-, 10-, 30-minute, and continuous-speech playback suite with:

```powershell
dotnet run --project benchmarks/LiveAudioTranslator.Benchmarks -c Release -- `
  --vlc-root vlc-binaries/vlc-4.0.0-dev `
  --startup-media tests/fixtures/live-sync-speech.mp4 `
  --sustained-media C:/fixtures/live-sync-10m.mp4 `
  --stability-media C:/fixtures/live-sync-30m.mp4 `
  --stress-media C:/fixtures/continuous-speech.mp4 `
  --output artifacts/live-immediate/performance.json
```

The playback benchmark uses `live-immediate`, the supported worker-backed mode.
It must not use `live-sync` because that request fails until the decoder-audio
probe exists. The command runs normal Qt/hardware-decoded playback and records
worker startup, RTF and latency metrics, decode lead,
drops, stale completions, restarts, process CPU/private memory, VLC
late/dropped-frame log counts, and a metrics hash. GPU counters and quality
scores are explicitly `null`/unaccepted until a machine-specific collector and
fixed quality-corpus result are provided; such a report cannot qualify an
accelerated provider.

For a lifecycle-only smoke, override the four durations with
`--startup-seconds`, `--sustained-seconds`, `--stability-seconds`, and
`--stress-seconds`. Short samples validate startup and cleanup, but are not
provider qualification evidence.

## Validation and visual proof

Generate the documented normal-orientation speech fixture if it is not already
available:

```powershell
pwsh tests/fixtures/create-live-sync-speech.ps1
```

Then run:

```powershell
dotnet build vlclr.sln -c Release
dotnet test src/VLCLR.Tests -c Release
dotnet test tests/SubtitleTranslator.UnitTests -c Release
dotnet test tests/LiveAudioTranslator.ProtocolTests -c Release
dotnet publish samples/LiveAudioTranslator -c Release -r win-x64
dotnet publish samples/LiveAudioTranslator.Prepare -c Release -r win-x64
dotnet publish samples/LiveAudioTranslator.Worker -c Release -r win-x64 `
  --self-contained true

dotnet run --project tests/LiveAudioTranslatorIntegrationTest -c Release -- `
  vlc-binaries/vlc-4.0.0-dev `
  tests/fixtures/live-sync-speech.mp4 `
  20

dotnet run --project tests/LiveAudioTranslator.VisualTest -c Release -- `
  --prepared-acceptance `
  --vlc-root vlc-binaries/vlc-4.0.0-dev `
  --media C:/Users/Martin/Videos/seo-course/fullchapter_maintaining.mp4 `
  --cue-file "$env:TEMP/vlclr-live-audio-<run-id>/timeline.jsonl" `
  --output artifacts/live-audio-translator/prepared-acceptance
```

The prepared acceptance harness launches normal Qt playback through Git Bash,
keeps hardware decoding enabled, runs for at least 60 seconds, requires at
least five rendered cues, seeks backward and forward into prepared regions,
checks generation monotonicity and scheduler p95, rejects underruns, native
failures, dropped frames, forced termination, and non-zero exit, and requires
both plugin modules to close. The cue timeline contains private translated
text and remains in the per-run user temp directory.

On the validated D3D11 output, the subtitle plane is visible in Qt but omitted
from BitBlt, `PrintWindow`, DWM thumbnails, Windows Graphics Capture, and VLC
snapshots. A native VLC marquee reproduces the same capture limitation.
Consequently the saved Qt PNG proves window visibility and orientation but
not subtitle pixels; render events, seek generations, and direct inspection of
the visible Qt window provide the subtitle evidence. Do not treat a blank
subtitle plane in those capture APIs as proof that Qt did not render it.

The retained file/HTTP visual-test modes are timing probes. Both prove that
stock VLC input caching buffers compressed media without delivering decoded
PCM ahead of presentation, so they are not acceptance modes for `live-sync`.

The 2026-07-24 exact-video acceptance ran for 98.3 seconds, rendered 45 cues,
advanced generations 1 → 3 → 5 across the two seeks, measured steady-state
scheduler p95 at 78.5 ms, used D3D11VA/Direct3D11, recorded no underrun,
dropped frame, assertion, crash, or forced termination, and closed both plugin
modules with VLC exit code 0.

The 2026-07-27 immediate-live acceptance used the same exact input and normal
Qt playback for 65 seconds. VLC started in 127 ms, CPU models reached `READY`
at 7.53 seconds, 301 startup blocks/6.99 seconds of PCM were discarded, and
the first accepted PTS was 7.01 seconds. Eleven real captions rendered, the
bounded pending queue replaced obsolete utterances, and both modules and the
worker closed cleanly with VLC/worker exit code 0. CPU semantic p95 was
6.42 seconds, above the 3.5-second performance target; the 7-second relevance
window keeps this baseline usable without claiming the target is met.

## Timing, failure, and privacy

The presentation clock maps decoded PTS to current playback time and shows no
cue while that mapping is uncertain. Flush, discontinuity, seek, stop, and
media replacement advance the playback generation, clear queued audio/cues,
and flush the worker. Results are rejected before translation and again before
delivery when their generation is obsolete.

The first cue after startup or a seek is an already-active resume sample. Its
age is reported as `scheduler_sample=resume-age`; it is not mixed into the
steady-state transport percentile. Production logs contain model/provider
IDs, timing, RTF, queue depth, drops, lead, generation, sequence, and
scheduling error. They do not contain recognized or translated text. Cue text
only crosses the private per-session named pipe and VLC's subtitle renderer.
