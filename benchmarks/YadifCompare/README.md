# Yadif: VLC's C and assembly against C#

VLC's yadif deinterlacer (modules/video_filter/deinterlace) against the C#
ports in `VLCLR.Filters` (`VLCLR.Filters.Video.Yadif`, and `XDeinterlace` for
the first picture), outside VLC, on the same frames and the same driver loop.
For using yadif in a plugin, see `samples/Yadif` instead.

## Harness

Builds VLC's code from a VLC checkout next to this repository (`../vlc`), as
is: `yadif.h` with clang and VLC's release flags, `yadif_x86.asm` with NASM,
and `algo_x.c` for the first picture. Then, for each instruction set, checks
every C# implementation against VLC's C sample for sample (random pictures of
odd sizes, both fields, every parity, 8 and 10 bits; X on random, combed and
real pictures) and times one 1080i frame.

Needs clang (`winget install LLVM.LLVM`), NASM (`winget install NASM.NASM`),
ffmpeg, and a 1080i clip to take three frames from (`-Clip`, `-At`).

```powershell
Harness/build.ps1 [-Isa x86-64-v3] [-Clip <1080i clip>] [-At <seconds>]
Harness/bin/x86-64-v3/Harness.exe [rounds] [framesPerRound] [cpu]
```

The harness pins itself to one core (`cpu`, 2 by default) at high priority
and rotates the order of the variants every round. It exits with 1 on any
mismatch.

| Variant | What |
|---|---|
| VLC C (x86-64) | `yadif_filter_line_c`, as VLC's release builds compile it |
| VLC C (isa) | the same C for the harness's instruction set |
| VLC SSE2 asm, VLC SSSE3 asm | `yadif_x86.asm`; VLC runs SSSE3 for 8 bits |
| VLC C 16-bit | `yadif_filter_line_c_16bit`, what VLC runs for 9 to 16 bits |
| C# Reference | line-for-line port of the C |
| C# Scalar | the same arithmetic written for RyuJIT |
| C# Vector128/256/512 | the structure of VLC's SSSE3 assembly, 8, 16 and 32 pixels at a time |

## Memory

`Memory/run.ps1` runs VLC's yadif (the harness's `cyadif.dll`) and the C#
filter as a Native AOT library with the plugins' publish settings
(`YadifNative.dll`) in the same small host, and prints the process's private
bytes, working set and threads at each step. Build the harness first.

```powershell
Memory/run.ps1 [-Frames 300] [-Runs 3]
```

## Demo clip

`make-demo.ps1` renders a synthetic 1080i pattern made to show combing (still
text, scrolling text, a moving fence, a swinging ball) with ffmpeg.
