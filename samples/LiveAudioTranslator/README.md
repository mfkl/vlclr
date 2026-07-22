# Live offline audio translator

This VLC 4 sample generates French subtitles from a video's decoded audio. It
does not require an SRT file or an embedded subtitle track.

The pipeline runs locally:

```text
video audio -> Whisper multilingual -> English text -> OPUS-MT ONNX -> French subtitles
```

Whisper.net provides the offline speech-to-English stage. ONNX Runtime is still
used for the English-to-French stage because Whisper only translates speech to
English. The sample exports two VLC modules from one Native AOT DLL: an audio
filter that observes decoded PCM and a subpicture source that displays results.

## Set up once

Close VLC, then run these commands from PowerShell at the repository root:

```powershell
pwsh samples/LiveAudioTranslator/download-models.ps1
pwsh samples/LiveAudioTranslator/deploy.ps1 -VlcDirectory vlc-binaries/vlc-4.0.0-dev
```

The download is about 306 MiB. Both model bundles are verified by exact size
and SHA-256 before deployment. The deploy script publishes the Native AOT DLL,
copies the Whisper and ONNX native runtimes, installs both models, and
regenerates VLC's plugin cache.

## Run any video

Use Git Bash, as required by this repository's VLC development build:

```bash
samples/LiveAudioTranslator/run.sh "/c/Users/Martin/Videos/BigBuckBunny.mp4"
```

There is deliberately no `--sub-file` argument. To use a VLC build in another
directory:

```bash
VLC_DIR="/c/path/to/vlc-4.0.0-dev" \
  samples/LiveAudioTranslator/run.sh "/c/path/to/video.mp4"
```

The equivalent direct command is:

```bash
vlc-binaries/vlc-4.0.0-dev/vlc.exe \
  --audio-filter=dotnet_audio_translator \
  --sub-source=dotnet_live_subtitles \
  --no-hw-dec \
  "file:///C:/path/to/video.mp4"
```

The default source language is automatic and the target is French. Speech is
split after roughly 650 ms of silence or at six seconds, so subtitles appear a
few seconds after speech. The bundled `tiny` multilingual Whisper model favors
speed; a larger multilingual GGML model can be selected with
`--live-translator-whisper-model=C:/path/to/model.bin`.

The native Whisper and ONNX sessions remain loaded until VLC exits. This avoids
tearing down an in-flight native inference when playback stops and also makes a
second video in the same VLC process start faster.

## Validate

The fast tests do not load either model:

```powershell
dotnet test src/VLCLR.Tests -c Release
dotnet test tests/SubtitleTranslator.UnitTests -c Release
```

To smoke-test Whisper independently with a 16-kHz mono WAV:

```powershell
dotnet run --project tests/WhisperSmoke -c Release -- `
  samples/LiveAudioTranslator/models/whisper/ggml-tiny.bin `
  C:/path/to/speech-16k.wav
```

To exercise both VLC modules with a video that has speech but no subtitles:

```powershell
dotnet run --project tests/LiveAudioTranslatorIntegrationTest -c Release -- `
  vlc-binaries/vlc-4.0.0-dev C:/path/to/video-with-speech.mp4 45
```
