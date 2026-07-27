#!/usr/bin/env bash
set -euo pipefail

mode=live-immediate
speech_device=cpu
while [[ $# -gt 0 ]]; do
    case $1 in
        --prepared)
            mode=prepared
            shift
            ;;
        --live-immediate|--live)
            mode=live-immediate
            shift
            ;;
        --speech-device)
            if [[ $# -lt 2 ]]; then
                echo "Missing value for --speech-device (cpu, gpu, or auto)." >&2
                exit 1
            fi
            speech_device=$2
            shift 2
            ;;
        *)
            break
            ;;
    esac
done

case $speech_device in
    cpu|gpu|auto) ;;
    *)
        echo "Unknown speech device: $speech_device (use cpu, gpu, or auto)." >&2
        exit 1
        ;;
esac

if [[ $# -lt 1 ]]; then
    echo "Usage: samples/LiveAudioTranslator/run.sh [--prepared|--live-immediate] [--speech-device cpu|gpu|auto] <media> [extra VLC options...]" >&2
    exit 1
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
vlc_dir=${VLC_DIR:-"$repo_root/vlc-binaries/vlc-4.0.0-dev"}
media=$1
shift

if [[ $media != *"://"* && ! -f "$media" ]]; then
    echo "Media not found: $media" >&2
    exit 2
fi
if [[ ! -x "$vlc_dir/vlc.exe" ]]; then
    echo "VLC not found: $vlc_dir/vlc.exe" >&2
    exit 3
fi

runner="$repo_root/samples/LiveAudioTranslator.Runner/bin/Release/net10.0/win-x64/LiveAudioTranslator.Runner.exe"
worker="$repo_root/samples/LiveAudioTranslator.Worker/bin/Release/net10.0/win-x64/publish/LiveAudioTranslator.Worker.exe"
catalog="$(dirname "$worker")/models/model-profiles.json"
if [[ ! -x $runner ]]; then
    echo "Runner not found. Build it with: dotnet build samples/LiveAudioTranslator.Runner -c Release -r win-x64" >&2
    exit 4
fi
if [[ $media != *"://"* ]]; then
    media=$(cygpath -aw "$media")
fi

runner_args=(
    --mode "$mode"
    --vlc-root "$(cygpath -aw "$vlc_dir")"
    --worker "$(cygpath -aw "$worker")"
    --catalog "$(cygpath -aw "$catalog")"
    --speech-device "$speech_device"
    "$media"
)
if [[ $# -gt 0 ]]; then
    runner_args+=(-- "$@")
fi
exec "$runner" "${runner_args[@]}"
