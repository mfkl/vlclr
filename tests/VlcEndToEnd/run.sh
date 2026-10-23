#!/usr/bin/env bash
# Runs the VLCLR sample plugins inside a real VLC 4 on this machine.
#
# Publishes GaussianBlur, Yadif, VideoOverlay, SubtitleRenderer and McpRemote
# with Native AOT for the host, checks their VLC entry points, then runs
# generated I420 clips through each filter:
#   - dotnet_gaussianblur must give the output of VLC's own gaussianblur on
#     every frame (VLC transcodes the clip through each filter, twice),
#   - dotnet_yadif must give the output of VLC's deinterlace filter in yadif
#     mode on every frame of an interlaced clip, the same way,
#   - dotnet_overlay must process every frame and change the picture,
#   - dotnet_subtitle must render the subtitles,
#   - no video filter may allocate on the managed heap per frame after its
#     warm-up (the plugins are built with VLCLRFrameAllocationCheck, which
#     logs what the frames allocated when a filter closes),
#   - dotnet_mcp must answer each of its MCP tools as expected
#     (mcp-check.py), and stop with VLC.
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
for sample in GaussianBlur Yadif VideoOverlay SubtitleRenderer McpRemote; do
    dotnet publish "$repo/samples/$sample" -c Release -o "$work/plugins/$sample" --nologo -v quiet \
        -p:VLCLRFrameAllocationCheck=true \
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

# parity NAME VLC_FILTER DOTNET_FILTER VLC_OPTION DOTNET_OPTION [FRAMEDIFF
# OPTIONS...]: the ported filter must give the output of VLC's on every frame
# of the clip, and change it (out/$unfiltered.yuv is the clip unfiltered).
# Each runs twice:
# values that change between two runs of VLC's filter (a read of
# uninitialized padding) are left out of the comparison; any that change
# between two runs of the ported filter fail it.
parity() {
    local name="$1" vlc_filter="$2" dotnet_filter="$3" vlc_option="$4" dotnet_option="$5" run
    shift 5
    for run in 1 2; do
        transcode "$name-vlc-$run" "$vlc_filter" $vlc_option
        transcode "$name-dotnet-$run" "$dotnet_filter" $dotnet_option
    done
    expect_log "$name-vlc-1" "using video filter module \"$vlc_filter\"" "VLC's filter did not open"
    expect_log "$name-dotnet-1" 'Filter [A-Za-z]+ initialized successfully' "filter did not open"
    local out="$work/out/$name" result status=0
    "$python" -I "$here/framediff.py" $width $height "$work/out/$unfiltered.yuv" "$out-dotnet-1.yuv" \
        --expect-different > /dev/null || fail "$name: $dotnet_filter left the video unchanged"
    result="$("$python" -I "$here/framediff.py" $width $height "$out-vlc-1.yuv" "$out-dotnet-1.yuv" \
        --again "$out-vlc-2.yuv" "$out-dotnet-2.yuv" "$@")" || status=$?
    echo "   $name: $result"
    [ "$status" -eq 0 ] || fail "$name: $dotnet_filter does not give the output of VLC's $vlc_filter"
}

echo "== Comparing the ported filters with VLC's in $("$vlc" --version 2> /dev/null | head -n 1 || echo VLC)"
transcode unfiltered ""
unfiltered=unfiltered

# dotnet_gaussianblur's sigma defaults to 2: this run also checks that a
# float config default reaches VLC.
parity blur-2 gaussianblur dotnet_gaussianblur --gaussianblur-sigma=2 ""
expect_log blur-2-dotnet-1 'distribution is 13 pixels wide' "the sigma default (a float config value) did not reach VLC"
parity blur-0.5 gaussianblur dotnet_gaussianblur --gaussianblur-sigma=0.5 --dotnet-gaussianblur-sigma=0.5
parity blur-6 gaussianblur dotnet_gaussianblur --gaussianblur-sigma=6 --dotnet-gaussianblur-sigma=6

# Yadif on a moving, interlaced clip: VLC's first picture is X's, the
# second is dropped, then each output is yadif's of the previous input.
# Yadif reads three samples past each end of the lines it rebuilds, in the
# picture padding: those columns are left out.
progressive_clip="$clip"
clip="$work/interlaced.y4m"
"$python" -I "$here/make-y4m.py" "$clip" $width $height 25 interlaced
transcode unfiltered-interlaced ""
unfiltered=unfiltered-interlaced
parity yadif deinterlace dotnet_yadif --sout-deinterlace-mode=yadif "" --skip-edges 3
expect_log yadif-vlc-1 'using yadif deinterlace method' "VLC's deinterlace filter did not use yadif"
clip="$progressive_clip"

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

echo "== Per-frame allocations"
expect_log blur-2-dotnet-1 'GaussianBlurPlugin: Frame allocations: none in [1-9]' "allocations were not measured"
expect_log yadif-dotnet-1 'YadifPlugin: Frame allocations: none in [1-9]' "allocations were not measured"
expect_log dotnet-overlay 'VideoOverlayFilter: Frame allocations: none in [1-9]' "allocations were not measured"
# Any video filter, in any run above, whose frames allocated.
while IFS= read -r line; do
    fail "${line#*\[VLCLR\] }"
done < <(grep -ahE '\[VLCLR\] [A-Za-z0-9_]+: Frame allocations: [0-9]+ bytes' "$work"/out/*.log | grep -v ' renders ' | sort -u)

echo "== Controlling VLC over MCP"
# dotnet_mcp serves VLC's player as MCP tools; mcp-check.py calls each one as
# an agent would, on a tone with subtitles next to it. On Linux, VLC's ALSA
# output on the null device has a (software) volume, so the volume is checked;
# other runners may have no audio device, and it is checked only when VLC
# finds an output with a volume.
mcp="$work/mcp"
"$python" -I "$here/mcp-check.py" media "$(native_path "$mcp")"
case "$library_suffix" in
    so) audio=(--aout=alsa --alsa-audio-device=null); volume=required ;;
    *) audio=(); volume=optional ;;
esac
port=18765
VLC_PLUGIN_PATH="$(native_path "$work/plugins")" "$vlc" -I dummy --extraintf=dotnet_mcp --mcp-port=$port \
    --ignore-config --no-plugins-cache --no-media-library --no-auto-preparse --vout=vdummy "${audio[@]}" \
    --play-and-exit -vv > "$work/out/mcp.log" 2>&1 &
vlc_pid=$!
(sleep 120 && kill -9 "$vlc_pid" 2> /dev/null) &
watchdog=$!
# VLC exits once the tone has played; after a failure the tone may not play.
"$python" -I "$here/mcp-check.py" check "http://127.0.0.1:$port/mcp" "$(native_path "$mcp")" --volume $volume \
    || { fail "mcp: a tool did not answer as expected (see $work/out/mcp.log)"; kill "$vlc_pid" 2> /dev/null || true; }
status=0
wait "$vlc_pid" || status=$?
kill "$watchdog" 2> /dev/null || true
[ "$status" -eq 0 ] || fail "mcp: VLC exited with status $status (see $work/out/mcp.log)"
expect_log mcp '\[McpRemote\] MCP server on' "the MCP server did not start"
expect_log mcp '\[McpRemote\] MCP server stopped' "the MCP server did not stop with VLC"

if [ "$failures" -ne 0 ]; then
    echo "== $failures check(s) failed; logs and snapshots in $work"
    exit 1
fi
echo "== All VLC end-to-end checks passed"
