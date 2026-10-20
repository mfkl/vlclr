#!/usr/bin/env python3
"""Write a small deterministic I420 YUV4MPEG2 clip for VLC end-to-end runs.

The picture mixes gradients, hard edges and pseudo-random noise so a blur
visibly changes every plane. Every frame is the same picture, unless
"interlaced" is given: then the picture moves left by 4 pixels per frame, and
the odd lines show it half a frame later than the even ones, as a top field
first camera would, so moving edges comb.
No third-party packages are needed.

Usage: make-y4m.py OUTPUT [WIDTH HEIGHT FRAMES [interlaced]]
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


def interlaced(pixels, width, height, shift, half):
    """The plane moved left by SHIFT pixels on even lines and by SHIFT + HALF
    on odd ones: the second field, half a frame later."""
    rows = []
    for y in range(height):
        row = pixels[y * width:(y + 1) * width]
        s = (shift + half * (y % 2)) % width
        rows.append(row[s:] + row[:s])
    return b"".join(rows)


def main():
    if len(sys.argv) not in (2, 5, 6) or sys.argv[5:] not in ([], ["interlaced"]):
        sys.exit(__doc__)
    output = sys.argv[1]
    width, height, frames = (
        (int(v) for v in sys.argv[2:5]) if len(sys.argv) >= 5 else (320, 240, 10)
    )
    moving = len(sys.argv) == 6
    if width % 2 or height % 2:
        sys.exit("WIDTH and HEIGHT must be even for I420")
    with open(output, "wb") as out:
        out.write(f"YUV4MPEG2 W{width} H{height} F25:1 {'It' if moving else 'Ip'} A1:1 C420jpeg\n".encode())
        planes = [(plane(width, height, 1), width, height),
                  (plane(width // 2, height // 2, 2), width // 2, height // 2),
                  (plane(width // 2, height // 2, 3), width // 2, height // 2)]
        for frame in range(frames):
            out.write(b"FRAME\n")
            for pixels, w, h in planes:
                # 4 luma pixels per frame, 2 chroma pixels.
                scale = width // w
                out.write(interlaced(pixels, w, h, 4 * frame // scale, 2 // scale) if moving else pixels)


if __name__ == "__main__":
    main()
