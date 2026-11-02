#!/usr/bin/env python3
"""Compare the samples of two WAV files (stdlib only).

Only the format (channels, rate, sample size) and the data chunk are
compared, not the rest of the header, which depends on the writer.

Usage: wavdiff.py EXPECTED ACTUAL [--expect-different]

Exit status 0 when the samples are the same bytes (or differ, with
--expect-different).
"""

import argparse
import struct
import sys


def read(path):
    with open(path, "rb") as f:
        blob = f.read()
    if blob[:4] != b"RIFF" or blob[8:12] != b"WAVE":
        raise ValueError(f"{path}: not a WAV file")
    fmt = data = None
    offset = 12
    while offset + 8 <= len(blob):
        chunk, size = blob[offset:offset + 4], struct.unpack_from("<I", blob, offset + 4)[0]
        body = blob[offset + 8:offset + 8 + size]
        if chunk == b"fmt ":
            tag, channels, rate, _, _, bits = struct.unpack_from("<HHIIHH", body)
            fmt = (tag, channels, rate, bits)
        elif chunk == b"data":
            data = body
        offset += 8 + size + (size & 1)
    if fmt is None or data is None:
        raise ValueError(f"{path}: no fmt or data chunk")
    return fmt, data


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("expected")
    parser.add_argument("actual")
    parser.add_argument("--expect-different", action="store_true")
    args = parser.parse_args()

    (_, channels, rate, bits), expected = read(args.expected)
    (_, channels2, rate2, bits2), actual = read(args.actual)
    same_format = (channels, rate, bits) == (channels2, rate2, bits2)
    frame = channels * bits // 8
    if not same_format or len(expected) != len(actual):
        print(f"{channels} ch {rate} Hz {bits} bit, {len(expected) // max(frame, 1)} frames"
              f" vs {channels2} ch {rate2} Hz {bits2} bit, {len(actual) // max(channels2 * bits2 // 8, 1)} frames")
        return 0 if args.expect_different else 1

    differing = sum(1 for i in range(0, len(expected), 4) if expected[i:i + 4] != actual[i:i + 4])
    total = len(expected) // 4
    print(f"{channels} ch {rate} Hz, {total // channels} frames: {differing} of {total} samples differ")
    if args.expect_different:
        return 0 if differing else 1
    return 0 if differing == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
