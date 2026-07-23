# Offline audio translator

This VLC 4 sample generates local French subtitles from a video's audio. The
default path prepares timestamped cues ahead of playback, then selects them
from the current media PTS. It does not force software video decoding and does
not display an old translation merely because inference is behind.

```text
bundled VLC -> 16-kHz mono WAV -> Whisper -> English -> OPUS-MT -> timed JSONL
normal VLC Qt player -> audio PTS clock -> media-time cue scheduler -> subtitle
```

Whisper and ONNX run in `LiveAudioTranslator.Prepare`, outside the visible VLC
process. The Native AOT plugin remains lightweight during synchronized
playback: its audio-filter module observes audio timestamps and its sub-source
module renders already prepared cues.

## Set up once

Close VLC, then run from PowerShell at the repository root:

```powershell
pwsh samples/LiveAudioTranslator/download-models.ps1
pwsh samples/LiveAudioTranslator/deploy.ps1 `
  -VlcDirectory vlc-binaries/vlc-4.0.0-dev
```

The download is about 306 MiB. Deployment publishes the Native AOT plugin and
the framework-dependent `win-x64` preparation helper, verifies both model
bundles by exact size and SHA-256, keeps the Whisper.net managed/native
versions together, and regenerates VLC's plugin cache.

## Synchronized playback (default)

Run from Git Bash, as required by this repository's VLC development build:

```bash
samples/LiveAudioTranslator/run.sh \
  "/c/Users/Martin/Videos/BigBuckBunny.mp4"
```

The runner performs a correctness-first WAV extraction using the bundled VLC,
validates that it is non-empty 16-kHz mono PCM16, initializes and warms both
models, and writes a private versioned cue timeline. It launches the normal Qt
player only when preparation is complete or has a safe measured lead:

- RTF at or above 1.0: prepare the whole timeline first.
- RTF below 0.67: launch with at least 15 seconds prepared.
- RTF from 0.67 to 1.0: calculate a larger conservative lead; if it exceeds
  two minutes, prepare the whole timeline.

If playback reaches unprepared audio, the plugin emits no subtitle and logs a
rate-limited `lead_underrun`; it never substitutes an older cue. Audio clock
anchors older than the configured limit are also rejected rather than guessed
from wall time.

Extra VLC arguments are preserved:

```bash
samples/LiveAudioTranslator/run.sh video.mp4 --start-time=30 -vvv
```

Hardware decoding remains available. There is intentionally no `--no-hw-dec`
option in this sample because it does not inspect video pixels.

## Immediate live captions (optional)

For a stream, or when starting playback immediately matters more than exact
synchronization:

```bash
samples/LiveAudioTranslator/run.sh --live video.mp4
```

Live mode is bounded-latency, not frame-accurate synchronization. It warms both
models before accepting PCM, discards warm-up audio, uses 2.5-second adaptive
utterances, keeps at most one pending utterance, replaces old pending work with
current speech, attaches PTS and a seek generation, and drops results older
than the configured maximum age. Zero-delay final translation is impossible
without delaying playback.

The conservative defaults reserve CPU for VLC:

- Whisper threads: 2
- ONNX threads: 1
- silence boundary: 400 ms
- maximum utterance: 2,500 ms
- maximum caption age: 3,500 ms

These can be changed with VLC options such as
`--live-translator-whisper-threads=4`,
`--live-translator-translation-threads=2`, and
`--live-translator-maximum-age-ms=3000`.

## Timeline and privacy

Each run creates a unique directory under the current user's temporary
directory. The JSONL file begins with a versioned manifest containing the
normalized media identity, languages, model identity, audio duration, timeline
offset, and generation ID. Every following cue has a monotonic sequence and
media-time interval. A companion progress file is atomically replaced, and a
reader exposes only complete newline-terminated JSONL records.

Cue text exists only in that private temporary timeline. Logs contain hashes,
timings, queue/drop counts, RTF, lead, and scheduling errors—not recognized or
translated speech. A successful run removes its temporary directory. A failed
run retains the directory and prints its path so the WAV, timeline prefix, and
extraction log remain available for diagnosis.

## Validation

Fast tests do not load VLC, Whisper, ONNX, or model files:

```powershell
dotnet test tests/SubtitleTranslator.UnitTests -c Release
```

They cover resampling timestamps, silence and forced splits, transcript overlap
removal, partial/malformed cue writes, atomic progress, clock drift and seeks,
stale anchors, duplicate suppression, generation changes, and RTF lead policy.

Build and publish both processes with:

```powershell
dotnet build vlclr.sln -c Release
dotnet publish samples/LiveAudioTranslator -c Release -r win-x64
dotnet publish samples/LiveAudioTranslator.Prepare -c Release -r win-x64 `
  --self-contained false
```

The dummy-interface integration harness is useful for live-mode discovery and
PTS/drop metrics, but it is not visual acceptance:

```powershell
dotnet run --project tests/LiveAudioTranslatorIntegrationTest -c Release -- `
  vlc-binaries/vlc-4.0.0-dev C:/path/to/video-with-speech.mp4 45
```

Final acceptance must use `run.sh` and the visible Qt window for at least 60
seconds. Verify upright/smooth video, five relevant French cues, a backward
seek, a forward seek into prepared audio, a clean close, no assertion/crash,
and no remaining preparation or workspace VLC process.
