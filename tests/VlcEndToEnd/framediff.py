#!/usr/bin/env python3
"""Compare two raw I420 videos frame by frame (stdlib only).

EXPECTED and ACTUAL are what VLC's transcode wrote: every frame's Y, U and V
planes, no padding. With --again, each side was run twice: values that differ
between the two runs of a side (filters that read uninitialized padding past
the visible width) are skipped and counted, and the check fails when more
than --max-unstable of the values are skipped.

Usage: framediff.py WIDTH HEIGHT EXPECTED ACTUAL [--again EXPECTED2 ACTUAL2]
                    [--tolerance N] [--max-unstable FRACTION] [--expect-different]

Exit status 0 when the videos match (or differ, with --expect-different).
"""

import argparse
import sys


def planes(width, height):
    """(name, offset, width, height) of each plane in one frame, and the frame size."""
    cw, ch = width // 2, height // 2
    layout = [("Y", 0, width, height), ("U", width * height, cw, ch),
              ("V", width * height + cw * ch, cw, ch)]
    return layout, width * height + 2 * cw * ch


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("width", type=int)
    parser.add_argument("height", type=int)
    parser.add_argument("expected")
    parser.add_argument("actual")
    parser.add_argument("--again", nargs=2, metavar=("EXPECTED2", "ACTUAL2"))
    parser.add_argument("--tolerance", type=int, default=0, metavar="N")
    parser.add_argument("--max-unstable", type=float, default=0.01, metavar="FRACTION")
    parser.add_argument("--expect-different", action="store_true")
    args = parser.parse_args()

    paths = [args.expected, args.actual] + (args.again or [])
    videos = [open(path, "rb").read() for path in paths]
    layout, frame_size = planes(args.width, args.height)
    for path, video in zip(paths, videos):
        if not video or len(video) % frame_size:
            print(f"FAIL: {path}: {len(video)} bytes is not a whole number of "
                  f"{args.width}x{args.height} I420 frames ({frame_size} bytes)")
            return 1
    if len({len(video) for video in videos}) != 1:
        print("FAIL: frame counts differ: " + ", ".join(
            f"{path} {len(video) // frame_size}" for path, video in zip(paths, videos)))
        return 1
    expected, actual = videos[0], videos[1]
    expected2, actual2 = (videos[2], videos[3]) if args.again else (expected, actual)

    frames = len(expected) // frame_size
    differing, unstable, largest, first = 0, 0, 0, None
    for frame in range(frames):
        for name, offset, width, height in layout:
            for y in range(height):
                start = frame * frame_size + offset + y * width
                row = slice(start, start + width)
                e, a, e2, a2 = expected[row], actual[row], expected2[row], actual2[row]
                if e == a and e == e2 and a == a2:
                    continue
                for x in range(width):
                    if e[x] != e2[x] or a[x] != a2[x]:
                        unstable += 1
                        continue
                    delta = abs(e[x] - a[x])
                    if delta > args.tolerance:
                        differing += 1
                        largest = max(largest, delta)
                        if first is None:
                            first = f"frame {frame} {name} x {x} y {y}: {e[x]} vs {a[x]}"

    total = frames * frame_size
    print(f"{frames} frames {args.width}x{args.height}: {differing} of {total} values differ"
          + (f" by more than {args.tolerance}" if args.tolerance else "")
          + (f" (max delta {largest}, first at {first})" if differing else "")
          + (f", {unstable} unstable between runs skipped" if args.again else ""))
    if args.expect_different:
        return 0 if differing else 1
    if unstable > args.max_unstable * total:
        print(f"FAIL: {unstable / total:.2%} of the values are unstable between runs, "
              f"more than {args.max_unstable:.2%}")
        return 1
    return 1 if differing else 0


if __name__ == "__main__":
    sys.exit(main())
