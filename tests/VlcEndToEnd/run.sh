#!/usr/bin/env bash
# Runs the VLCLR sample plugins inside a real VLC 4 on this machine.
#
# Publishes GaussianBlur, VideoOverlay and SubtitleRenderer with Native AOT
# for the host, checks their VLC entry points, then plays a generated I420
# clip through each one:
#   - dotnet_gaussianblur must match VLC's own gaussianblur pixel for pixel,
#   - dotnet_overlay must process every frame and change the picture,
#   - dotnet_subtitle must render the subtitles.
# A struct layout that does not match the platform ABI makes these fail
# (the filter refuses the formats it is given, or reads garbage).
#
# Usage: tests/VlcEndToEnd/run.sh <vlc-executable> [work-dir]
# The executable must start as is: set LD_LIBRARY_PATH or similar first.
# PYTHON overrides the Python 3 interpreter (default: python3).
set -euo pipefail

if [ $# -lt 1 ]; then
    sed -n '2,15p' "$0" >&2
    exit 2
fi
vlc="$1"
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
work="${2:-$repo/artifacts/vlc-e2e}"
python="${PYTHON:-python3}"
sigma=2
failures=0

rm -rf "$work"
mkdir -p "$work/plugins" "$work/out"

fail() {
    echo "FAIL: $*"
    failures=$((failures + 1))
}

native_path() {
    if command -v cygpath > /dev/null; then cygpath -w "$1"; else echo "$1"; fi
}

case "$(uname -s)" in
    Linux) library_suffix=so ;;
    Darwin) library_suffix=dylib ;;
    *) library_suffix=dll ;;
esac

echo "== Publishing plugins"
for sample in GaussianBlur VideoOverlay SubtitleRenderer; do
    dotnet publish "$repo/samples/$sample" -c Release -o "$work/plugins/$sample" --nologo -v quiet \
        > "$work/publish-$sample.log" 2>&1 \
        || { grep -E ' error ' "$work/publish-$sample.log" | sort -u; fail "publishing $sample"; continue; }
    plugin="$(find "$work/plugins/$sample" -maxdepth 1 -name "lib*_plugin.$library_suffix" | head -n 1)"
    if [ -z "$plugin" ]; then
        fail "$sample: no lib*_plugin.$library_suffix in the publish output"
        continue
    fi
    echo "   $(basename "$plugin")"
    case "$library_suffix" in
        so) exports="$(nm -D --defined-only "$plugin")" ;;
        dylib) exports="$(nm -gU "$plugin" | sed 's/ _/ /')" ;;
        *) exports="" ;;
    esac
    if [ -n "$exports" ]; then
        for symbol in vlc_entry vlc_entry_api_version vlc_entry_copyright; do
            grep -qw "$symbol" <<< "$exports" || fail "$sample: missing export $symbol"
        done
    fi
done

clip="$work/clip.y4m"
"$python" -I "$here/make-y4m.py" "$clip" 320 240 25
subtitles="$work/clip.srt"
printf '1\n00:00:00,000 --> 00:00:10,000\nVLCLR end-to-end\n' > "$subtitles"

play() {
    local name="$1" chain="$2"
    shift 2
    local out="$work/out/$name"
    mkdir -p "$out"
    VLC_PLUGIN_PATH="$(native_path "$work/plugins")" "$vlc" -I dummy --ignore-config --no-plugins-cache \
        --no-audio --vout=vdummy --video-filter="$chain" \
        --scene-format=png --scene-path="$(native_path "$out")" --scene-prefix=f --scene-ratio=1 --scene-replace \
        --play-and-exit -vv "$@" "$(native_path "$clip")" > "$work/out/$name.log" 2>&1 &
    local pid=$!
    (sleep 120 && kill -9 "$pid" 2> /dev/null) &
    local watchdog=$!
    local status=0
    wait "$pid" || status=$?
    kill "$watchdog" 2> /dev/null || true
    [ "$status" -eq 0 ] || fail "$name: VLC exited with status $status (see $work/out/$name.log)"
    [ -f "$out/f.png" ] || fail "$name: no snapshot written"
}

expect_log() {
    grep -aqE "$2" "$work/out/$1.log" || fail "$1: $3 (see $work/out/$1.log)"
}

echo "== Playing the clip in $("$vlc" --version 2> /dev/null | head -n 1 || echo VLC)"
play original "scene"

play vlc-blur "gaussianblur:scene" --gaussianblur-sigma=$sigma
play dotnet-blur "dotnet_gaussianblur:scene"
expect_log dotnet-blur 'Filter GaussianBlurPlugin initialized successfully' "filter did not open"
expect_log dotnet-blur 'distribution is 13 pixels wide' "the sigma default (a float config value) did not reach VLC"
expect_log dotnet-blur 'processed [1-9][0-9]* frames' "filter processed no frames"
if [ -f "$work/out/vlc-blur/f.png" ] && [ -f "$work/out/dotnet-blur/f.png" ]; then
    "$python" -I "$here/pngdiff.py" "$work/out/vlc-blur/f.png" "$work/out/dotnet-blur/f.png" \
        --ignore-right $((3 * sigma)) || fail "dotnet_gaussianblur does not match VLC's gaussianblur"
    "$python" -I "$here/pngdiff.py" "$work/out/original/f.png" "$work/out/dotnet-blur/f.png" \
        --expect-different > /dev/null || fail "dotnet_gaussianblur left the picture unchanged"
fi

play dotnet-overlay "dotnet_overlay:scene"
expect_log dotnet-overlay 'Filter VideoOverlayFilter initialized successfully' "filter did not open"
expect_log dotnet-overlay 'processed [1-9][0-9]* frames' "filter processed no frames"
expect_log dotnet-overlay 'opacity=1\.00' "the opacity default (a float config value) did not reach VLC"
if [ -f "$work/out/dotnet-overlay/f.png" ]; then
    "$python" -I "$here/pngdiff.py" "$work/out/original/f.png" "$work/out/dotnet-overlay/f.png" \
        --expect-different > /dev/null || fail "dotnet_overlay left the picture unchanged"
fi

play dotnet-subtitle "scene" --text-renderer=dotnet_subtitle --sub-file="$(native_path "$subtitles")"
expect_log dotnet-subtitle 'Renderer SubtitleTextRenderer initialized successfully' "renderer did not open"
expect_log dotnet-subtitle 'rendered [1-9][0-9]* subtitles' "renderer rendered nothing"

if [ "$failures" -ne 0 ]; then
    echo "== $failures check(s) failed; logs and snapshots in $work"
    exit 1
fi
echo "== All VLC end-to-end checks passed"
