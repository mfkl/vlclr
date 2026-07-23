# Live audio translator timing baseline

Measured 2026-07-22 on the repository's bundled VLC 4.0.0-dev build and the
supplied `fullchapter_maintaining.mp4` input.

## Audio extraction

- Input audio: AAC, 44.1 kHz, stereo; media duration about 709.85 seconds.
- Command: bundled VLC dummy interface, `--no-video --no-sout-video`, WAV sout
  transcoding to `s16l`, one channel, 16 kHz.
- Output: PCM16, 16 kHz, mono, about 709.93 seconds and 22.7 MB.
- A 20-second extraction produced 19.9705 seconds of validated PCM. The
  duration difference is within two 20-ms VAD frames.
- VLC returns zero even when sout setup fails, so the orchestrator additionally
  requires a non-empty file and the preparation helper validates the RIFF/WAVE
  structure, PCM format, sample rate, channel count, alignment, and duration.

## Preparation

20-second model smoke, Whisper threads 2 and ONNX threads 1:

| Metric | Result |
|---|---:|
| Model initialization and warm-up | 3.75 s |
| Audio duration | 19.9705 s |
| Audio processing wall time | 32.87 s |
| Total RTF | 1.646 |
| Whisper inference | 31.25 s |
| Translation inference | 1.48 s |
| Ordered cues | 9 |

With this measured RTF, the launch policy correctly requires complete
preparation rather than attempting streaming-ahead playback.

## VLC clock mapping and scheduling

The first observed decoded-audio block had PTS 23,220 microseconds and duration
23,219 microseconds. Audio observations and sub-source callback dates were both
in VLC's monotonic system-clock domain after mapping through `vlc_tick_now`.

The first cue was already in progress when the sub-source opened, so it was
rendered only for its remaining 1.176 seconds. Subsequent observed scheduling
errors were +13.5 ms, +5.7 ms, and -75.9 ms. Nine cues were prepared; the clean
filtered smoke selected its first four in sequence from their media intervals.
No completed-order fallback exists. The headless smoke closed both modules
without a plugin error or assertion.

This report is a timing/module smoke, not visual acceptance. Upright video,
visible cue relevance, seek behavior, smoothness, and Qt close behavior still
must be accepted in the normal visible window as described in the README.
