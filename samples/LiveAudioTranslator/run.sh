#!/usr/bin/env bash
set -euo pipefail

mode=sync
if [[ ${1:-} == "--live" ]]; then
    mode=live
    shift
fi

if [[ $# -lt 1 ]]; then
    echo "Usage: samples/LiveAudioTranslator/run.sh [--live] <video> [extra VLC options...]" >&2
    exit 1
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
vlc_dir=${VLC_DIR:-"$repo_root/vlc-binaries/vlc-4.0.0-dev"}
video=$1
shift

if [[ ! -f "$video" ]]; then
    echo "Video not found: $video" >&2
    exit 2
fi
if [[ ! -x "$vlc_dir/vlc.exe" ]]; then
    echo "VLC not found: $vlc_dir/vlc.exe" >&2
    exit 3
fi

video_uri="file:///$(cygpath -am "$video")"
if [[ $mode == live ]]; then
    exec "$vlc_dir/vlc.exe" \
        --live-translator-mode=live \
        --audio-filter=dotnet_audio_translator \
        --sub-source=dotnet_live_subtitles \
        --no-video-title-show \
        "$@" \
        "$video_uri"
fi

if ! command -v pwsh >/dev/null 2>&1; then
    echo "PowerShell 7 (pwsh) is required for synchronized preparation." >&2
    exit 4
fi

script_windows=$(cygpath -aw "$script_dir/prepare-and-run.ps1")
video_windows=$(cygpath -aw "$video")
vlc_windows=$(cygpath -aw "$vlc_dir")
pwsh_args=(
    -NoLogo -NoProfile -File "$script_windows"
    -VideoPath "$video_windows"
    -VlcDirectory "$vlc_windows"
)
export VLCLR_EXTRA_VLC_ARGUMENT_COUNT=$#
extra_index=0
for extra_argument in "$@"; do
    export "VLCLR_EXTRA_VLC_ARGUMENT_${extra_index}=$extra_argument"
    ((extra_index += 1))
done
exec pwsh "${pwsh_args[@]}"
