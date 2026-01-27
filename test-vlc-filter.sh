#!/bin/bash
# Simple VLC video filter test

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VLC_EXE="$SCRIPT_DIR/vlc-binaries/vlc-4.0.0-dev/vlc.exe"
VIDEO="${1:-C:/Users/Martin/Videos/BigBuckBunny.mp4}"
FILTER="${2:-dotnet_overlay}"

# Convert to file:// URL
VIDEO_URL="file:///$VIDEO"

echo "Filter: $FILTER"
echo "Video:  $VIDEO_URL"
echo "Running VLC..."
echo ""

"$VLC_EXE" -vvv --no-hw-dec --video-filter="$FILTER" --play-and-exit "$VIDEO_URL" 2>&1
