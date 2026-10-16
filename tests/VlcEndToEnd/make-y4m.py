#!/usr/bin/env python3
"""Write a small deterministic I420 YUV4MPEG2 clip for VLC end-to-end runs.

The picture mixes gradients, hard edges and pseudo-random noise so a blur
visibly changes every plane. Every frame is the same picture.
No third-party packages are needed.

Usage: make-y4m.py OUTPUT [WIDTH HEIGHT FRAMES]
"""

import sys


def plane(width, height, seed):
    state = seed
    rows = []
    for y in range(height):
        row = bytearray(width)
        for x in range(width):
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF
            value = (x * 255) // max(width - 1, 1)
            if (x // 16 + y // 16) % 2:
                value = 255 - value
            value = (value + ((state >> 16) & 0x3F) - 32) & 0xFF
            row[x] = value
        rows.append(bytes(row))
    return b"".join(rows)


def main():
    if len(sys.argv) not in (2, 5):
        sys.exit(__doc__)
    output = sys.argv[1]
    width, height, frames = (
        (int(v) for v in sys.argv[2:5]) if len(sys.argv) == 5 else (320, 240, 10)
    )
    if width % 2 or height % 2:
        sys.exit("WIDTH and HEIGHT must be even for I420")
    with open(output, "wb") as out:
        out.write(f"YUV4MPEG2 W{width} H{height} F25:1 Ip A1:1 C420jpeg\n".encode())
        picture = (plane(width, height, 1)
                   + plane(width // 2, height // 2, 2)
                   + plane(width // 2, height // 2, 3))
        for _ in range(frames):
            out.write(b"FRAME\n")
            out.write(picture)


if __name__ == "__main__":
    main()
