#!/usr/bin/env bash
# Downloads the latest VLC 4 nightly for this machine from
# artifacts.videolan.org, checks its SHA-512 and unpacks it into DEST:
#   DEST/vlc       starts that VLC (for tests/VlcEndToEnd/run.sh)
#   DEST/include   its plugin headers (for tests/AbiValidation)
#
# Linux x64 uses the nightly snap (unpacked, not installed; needs unsquashfs)
# with headers from the same VLC commit, macOS the nightly SDK, Windows the
# nightly win64 build (needs 7z). Nothing is installed system-wide.
#
# Usage: tests/VlcEndToEnd/download-vlc.sh DEST
set -euo pipefail

if [ $# -ne 1 ]; then
    sed -n '2,12p' "$0" >&2
    exit 2
fi
dest="$1"
artifacts="https://artifacts.videolan.org/vlc"
rm -rf "$dest"
mkdir -p "$dest/download" "$dest/root"
dest="$(cd "$dest" && pwd)"

fetch() {
    local directory="$1" pattern="$2" build file expected actual
    build="$(curl -fsSL "$artifacts/$directory/" | grep -oE 'href="[0-9]{8}-[0-9]{4}/"' | cut -d'"' -f2 | sort | tail -n 1)"
    [ -n "$build" ] || { echo "No nightly build under $artifacts/$directory/" >&2; exit 1; }
    file="$(curl -fsSL "$artifacts/$directory/$build" | grep -oE "href=\"$pattern\"" | cut -d'"' -f2 | head -n 1)"
    [ -n "$file" ] || { echo "No file matching $pattern in $artifacts/$directory/$build" >&2; exit 1; }
    echo "Downloading $artifacts/$directory/$build$file" >&2
    curl -fsSL -o "$dest/download/$file" "$artifacts/$directory/$build$file"
    expected="$(curl -fsSL "$artifacts/$directory/${build}SHA512SUM" | grep -E "[ /]$file\$" | cut -d' ' -f1)"
    if command -v sha512sum > /dev/null; then
        actual="$(sha512sum "$dest/download/$file" | cut -d' ' -f1)"
    else
        actual="$(shasum -a 512 "$dest/download/$file" | cut -d' ' -f1)"
    fi
    [ -n "$expected" ] && [ "$expected" = "$actual" ] || { echo "SHA-512 mismatch for $file" >&2; exit 1; }
    echo "$dest/download/$file"
}

write_launcher() {
    printf '#!/bin/sh\n%s\n' "$1" > "$dest/vlc"
    chmod +x "$dest/vlc"
}

case "$(uname -s)-$(uname -m)" in
    Linux-x86_64)
        snap="$(fetch nightly-snap 'vlc_4\.0\.0-dev-[0-9]+-g[0-9a-f]+_amd64\.snap')"
        unsquashfs -q -f -d "$dest/root" "$snap" > /dev/null
        root="$dest/root/usr"
        write_launcher "LD_LIBRARY_PATH=\"$root/lib:$root/lib/vlc:$root/lib/x86_64-linux-gnu\${LD_LIBRARY_PATH:+:\$LD_LIBRARY_PATH}\" exec \"$root/bin/vlc\" \"\$@\""
        commit="$(basename "$snap" | sed -E 's/.*-g([0-9a-f]+)_amd64\.snap/\1/')"
        curl -fsSL "https://code.videolan.org/videolan/vlc/-/archive/$commit/vlc-$commit.tar.gz?path=include" \
            | tar xz -C "$dest/download"
        ;;
    Darwin-arm64 | Darwin-x86_64)
        sdk="$(fetch "nightly-macos-$(uname -m)" 'vlc-macos-sdk-4\.0\.0-dev-[a-z0-9_]+-[0-9a-f]+\.tar\.gz')"
        tar xzf "$sdk" -C "$dest/root"
        root="$dest/root"
        write_launcher "DYLD_LIBRARY_PATH=\"$root/lib\" VLC_LIB_PATH=\"$root/lib/vlc\" exec \"$root/bin/vlc\" \"\$@\""
        ;;
    MINGW*-x86_64 | MSYS*-x86_64 | CYGWIN*-x86_64)
        archive="$(fetch nightly-win64 'vlc-4\.0\.0-dev-win64-[0-9a-f]+\.7z')"
        7z x -y -o"$dest/root" "$archive" > /dev/null
        exe="$(find "$dest/root" -maxdepth 2 -name vlc.exe | head -n 1)"
        write_launcher "exec \"$exe\" \"\$@\""
        ;;
    *)
        echo "No VLC 4 nightly for $(uname -s)-$(uname -m)" >&2
        exit 1
        ;;
esac

header="$(find "$dest/root" "$dest/download" -name vlc_common.h -path '*include*' | head -n 1)"
[ -n "$header" ] || { echo "The download has no vlc_common.h" >&2; exit 1; }
ln -s "$(dirname "$header")" "$dest/include" 2> /dev/null || cp -R "$(dirname "$header")" "$dest/include"
echo "VLC: $("$dest/vlc" --version 2> /dev/null | head -n 1)"
echo "Headers: $dest/include"
