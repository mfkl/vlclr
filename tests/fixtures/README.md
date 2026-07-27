# Live-sync visual fixture

`live-sync-speech.mp4` is a generated, normal-orientation 1280×720 H.264/AAC
fixture containing repeated English speech. English is the known source
language; the default OPUS-MT target is French.

Generate it with:

```powershell
pwsh tests/fixtures/create-live-sync-speech.ps1
```

The generated MP4 is intentionally not stored as source. The script fixes the
duration, orientation metadata, frame rate, audio text, and codecs so local
visual runs can reproduce it. `ffmpeg` and the Windows `System.Speech`
assembly are required.
