#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "Usage: samples/LiveAudioTranslator/run.sh <video> [extra VLC options...]" >&2
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
exec "$vlc_dir/vlc.exe" \
    --audio-filter=dotnet_audio_translator \
    --sub-source=dotnet_live_subtitles \
    --no-hw-dec \
    --no-video-title-show \
    "$@" \
    "$video_uri"
