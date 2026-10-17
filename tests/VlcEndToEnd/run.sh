#!/usr/bin/env bash
# Runs the VLCLR sample plugins inside a real VLC 4 on this machine.
#
# Publishes GaussianBlur, VideoOverlay and SubtitleRenderer with Native AOT
# for the host, checks their VLC entry points, then runs a generated I420
# clip through each one:
#   - dotnet_gaussianblur must give the output of VLC's own gaussianblur on
#     every frame (VLC transcodes the clip through each filter, twice),
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
width=320
height=240
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

# A path inside a --sout chain, where backslashes are escapes.
sout_path() {
    if command -v cygpath > /dev/null; then cygpath -m "$1"; else echo "$1"; fi
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
"$python" -I "$here/make-y4m.py" "$clip" $width $height 25
subtitles="$work/clip.srt"
printf '1\n00:00:00,000 --> 00:00:10,000\nVLCLR end-to-end\n' > "$subtitles"

# run_vlc NAME OPTIONS...: runs VLC on the clip, logging to out/NAME.log.
run_vlc() {
    local name="$1"
    shift
    # The launcher turns the media library on. It and preparsing run in
    # vlc-preparser child processes, which the macOS arm64 nightly fails to
    # spawn; the test needs neither.
    VLC_PLUGIN_PATH="$(native_path "$work/plugins")" "$vlc" -I dummy --ignore-config --no-plugins-cache \
        --no-media-library --no-auto-preparse --no-audio -vv "$@" > "$work/out/$name.log" 2>&1 &
    local pid=$!
    (sleep 120 && kill -9 "$pid" 2> /dev/null) &
    local watchdog=$!
    local status=0
    wait "$pid" || status=$?
    kill "$watchdog" 2> /dev/null || true
    [ "$status" -eq 0 ] || fail "$name: VLC exited with status $status (see $work/out/$name.log)"
}

# play NAME CHAIN [OPTIONS...]: plays the clip through the video filter chain,
# snapshot of the last frame in out/NAME/f.png.
play() {
    local name="$1" chain="$2"
    shift 2
    local out="$work/out/$name"
    mkdir -p "$out"
    run_vlc "$name" --vout=vdummy --video-filter="$chain" \
        --scene-format=png --scene-path="$(native_path "$out")" --scene-prefix=f --scene-ratio=1 --scene-replace \
        --play-and-exit "$@" "$(native_path "$clip")"
    [ -f "$out/f.png" ] || fail "$name: no snapshot written"
}

# transcode NAME FILTER [OPTIONS...]: every frame of the clip through the
# video filter (none if empty), as raw I420 in out/NAME.yuv. Transcoding
# follows no clock, so no frame is dropped; the subtitles next to the clip
# stay out of the file.
transcode() {
    local name="$1" filter="$2"
    shift 2
    local out="$work/out/$name.yuv"
    run_vlc "$name" --no-sout-spu --no-sout-audio "$@" "$(native_path "$clip")" \
        --sout "#transcode{vcodec=I420${filter:+,vfilter=$filter}}:std{access=file,mux=dummy,dst=\"$(sout_path "$out")\"}" \
        vlc://quit
    [ -s "$out" ] || fail "$name: no frames written"
}

expect_log() {
    grep -aqE "$2" "$work/out/$1.log" || fail "$1: $3 (see $work/out/$1.log)"
}

# parity NAME VLC_FILTER DOTNET_FILTER VLC_OPTION DOTNET_OPTION: the ported
# filter must give the output of VLC's on every frame. Each runs twice:
# values that change between two runs of the same filter (a read of
# uninitialized padding) are left out of the comparison.
parity() {
    local name="$1" vlc_filter="$2" dotnet_filter="$3" vlc_option="$4" dotnet_option="$5" run
    for run in 1 2; do
        transcode "$name-vlc-$run" "$vlc_filter" $vlc_option
        transcode "$name-dotnet-$run" "$dotnet_filter" $dotnet_option
    done
    expect_log "$name-vlc-1" "using video filter module \"$vlc_filter\"" "VLC's filter did not open"
    expect_log "$name-dotnet-1" 'Filter [A-Za-z]+ initialized successfully' "filter did not open"
    local out="$work/out/$name" result status=0
    "$python" -I "$here/framediff.py" $width $height "$work/out/unfiltered.yuv" "$out-dotnet-1.yuv" \
        --expect-different > /dev/null || fail "$name: $dotnet_filter left the video unchanged"
    result="$("$python" -I "$here/framediff.py" $width $height "$out-vlc-1.yuv" "$out-dotnet-1.yuv" \
        --again "$out-vlc-2.yuv" "$out-dotnet-2.yuv")" || status=$?
    echo "   $name: $result"
    [ "$status" -eq 0 ] || fail "$name: $dotnet_filter does not give the output of VLC's $vlc_filter"
}

echo "== Comparing the ported filters with VLC's in $("$vlc" --version 2> /dev/null | head -n 1 || echo VLC)"
transcode unfiltered ""

# dotnet_gaussianblur's sigma defaults to 2: this run also checks that a
# float config default reaches VLC.
parity blur-2 gaussianblur dotnet_gaussianblur --gaussianblur-sigma=2 ""
expect_log blur-2-dotnet-1 'distribution is 13 pixels wide' "the sigma default (a float config value) did not reach VLC"
parity blur-0.5 gaussianblur dotnet_gaussianblur --gaussianblur-sigma=0.5 --dotnet-gaussianblur-sigma=0.5
parity blur-6 gaussianblur dotnet_gaussianblur --gaussianblur-sigma=6 --dotnet-gaussianblur-sigma=6

echo "== Playing the clip"
play original "scene"

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
