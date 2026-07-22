# Subtitle frame capture

This small integration utility loads the Native AOT subtitle renderer through
LibVLC, waits for an active subtitle cue, and asks VLC to save the fully
composited video frame. It verifies both that the renderer opened and that the
plugin logged creation of a compact subpicture region.

Publish and deploy `SubtitleRenderer`, regenerate VLC's plugin cache, then run:

```powershell
dotnet run --project tests/SubtitleFrameCapture -c Release -- `
  vlc-binaries/vlc-4.0.0-dev `
  https://archive.org/download/BigBuckBunny_328/BigBuckBunny.avi `
  tests/SubtitleRendererTest/fixtures/test.srt `
  subtitle-frame.png `
  2000
```

On Windows, run the command from Git Bash if the hosting terminal blocks while
VLC creates its video output.
