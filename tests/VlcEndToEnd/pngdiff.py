#!/usr/bin/env python3
"""Compare two 8-bit RGB/RGBA PNG snapshots pixel by pixel (stdlib only).

Usage: pngdiff.py EXPECTED ACTUAL [--expect-different]

Exit status 0 when the pictures match (or differ, with --expect-different).
"""

import argparse
import struct
import sys
import zlib


def load(path):
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        sys.exit(f"{path}: not a PNG")
    pos, idat, width, height, channels = 8, b"", 0, 0, 0
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            if depth != 8 or color not in (2, 6) or interlace:
                sys.exit(f"{path}: only 8-bit non-interlaced RGB/RGBA is supported")
            channels = 3 if color == 2 else 4
        elif kind == b"IDAT":
            idat += body
    raw = zlib.decompress(idat)
    stride = width * channels
    rows, previous = [], bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        kind, line = raw[start], bytearray(raw[start + 1:start + 1 + stride])
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b = previous[x]
            c = previous[x - channels] if x >= channels else 0
            if kind == 1:
                line[x] = (line[x] + a) & 0xFF
            elif kind == 2:
                line[x] = (line[x] + b) & 0xFF
            elif kind == 3:
                line[x] = (line[x] + (a + b) // 2) & 0xFF
            elif kind == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 0xFF
        rows.append(bytes(line))
        previous = line
    return width, height, channels, rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("expected")
    parser.add_argument("actual")
    parser.add_argument("--expect-different", action="store_true")
    args = parser.parse_args()

    w1, h1, c1, expected = load(args.expected)
    w2, h2, c2, actual = load(args.actual)
    if (w1, h1, c1) != (w2, h2, c2):
        print(f"FAIL: {w1}x{h1}x{c1} vs {w2}x{h2}x{c2}")
        return 0 if args.expect_different else 1

    compared = w1 * c1
    differing, largest = 0, 0
    for row_e, row_a in zip(expected, actual):
        for e, a in zip(row_e[:compared], row_a[:compared]):
            if e != a:
                differing += 1
                largest = max(largest, abs(e - a))
    total = compared * h1
    print(f"{w1}x{h1}: {differing} of {total} channel values differ"
          + (f" (max delta {largest})" if differing else ""))
    if args.expect_different:
        return 0 if differing else 1
    return 1 if differing else 0


if __name__ == "__main__":
    sys.exit(main())
