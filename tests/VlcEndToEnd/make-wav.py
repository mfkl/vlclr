#!/usr/bin/env python3
"""Write a short deterministic 32-bit float WAV for VLC end-to-end runs.

Each channel holds its own tone plus pseudo-random noise, at most 0.9 in
magnitude, so a filter that scales, mixes or moves channels changes the
samples, and a mix-up of channels shows. Channel masks follow WAVE's order
(the same speakers as VLC's, in WAVE order): "stereo" is FL FR, "5.1" is
FL FR FC LFE BL BR. No third-party packages are needed.

Usage: make-wav.py OUTPUT [LAYOUT [RATE SECONDS]]
       LAYOUT: stereo (default) or 5.1
"""

import math
import struct
import sys

LAYOUTS = {
    "stereo": (2, 0x3),
    "5.1": (6, 0x3F),
}


def main():
    if len(sys.argv) < 2:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    output = sys.argv[1]
    layout = sys.argv[2] if len(sys.argv) > 2 else "stereo"
    rate = int(sys.argv[3]) if len(sys.argv) > 3 else 48000
    seconds = float(sys.argv[4]) if len(sys.argv) > 4 else 2.0
    channels, mask = LAYOUTS[layout]
    frames = int(rate * seconds)

    state = 1
    samples = []
    for i in range(frames):
        for c in range(channels):
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF
            noise = ((state >> 8) & 0xFFFF) / 65535.0 - 0.5
            tone = math.sin(2 * math.pi * (220.0 * (c + 1)) * i / rate)
            samples.append(0.6 * tone + 0.3 * noise)
    data = struct.pack("<%df" % len(samples), *samples)

    # WAVE_FORMAT_EXTENSIBLE with the IEEE float sub-format and a channel mask.
    float_guid = struct.pack("<IHH8s", 0x00000003, 0x0000, 0x0010, bytes.fromhex("800000aa00389b71"))
    block_align = 4 * channels
    fmt = struct.pack("<HHIIHH", 0xFFFE, channels, rate, rate * block_align, block_align, 32)
    fmt += struct.pack("<HHI", 22, 32, mask) + float_guid
    body = b"WAVE" + b"fmt " + struct.pack("<I", len(fmt)) + fmt + b"data" + struct.pack("<I", len(data)) + data
    with open(output, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", len(body)) + body)
    return 0


if __name__ == "__main__":
    sys.exit(main())
