#!/usr/bin/env bash
# Decides whether a VLC commit needs a CImport run, for
# .github/workflows/cimport-bump.yml.
#
# Takes the newest VLC 4 nightly (the commit in the win64 file name on
# artifacts.videolan.org) or COMMIT, resolves the full SHA and lists the
# libvlccore headers (include/*.h; not include/vlc/, which is libvlc) that
# changed. Only commits and trees are fetched, no file contents.
#
# Usage: check-vlc-nightly.sh PIN [LAST [COMMIT]]
#   PIN     full SHA of vlc.commit in vlc.cimport.json
#   LAST    full SHA of the last commit already checked, or empty
#   COMMIT  full or short VLC SHA to check instead of the newest nightly
#
# Prints key=value lines, also appended to $GITHUB_OUTPUT when set:
#   commit       full SHA checked
#   build        nightly directory (20261020-0427), empty with COMMIT
#   status       pinned            commit is the pin, nothing to do
#                checked           commit is LAST, nothing to do
#                no-header-change  no header changed since LAST (or the pin)
#                headers-changed   run extract and emit
#   headers      headers changed since the pin, space separated
set -euo pipefail

die() { echo "check-vlc-nightly: $*" >&2; exit 1; }
is_sha() { [[ "$1" =~ ^[0-9a-f]{40}$ ]]; }

[ $# -ge 1 ] && [ $# -le 3 ] || { sed -n '2,23p' "$0" >&2; exit 2; }
pin=$1 last=${2:-} wanted=${3:-}
is_sha "$pin" || die "PIN '$pin' is not a full SHA."
[ -z "$last" ] || is_sha "$last" || die "LAST '$last' is not a full SHA."
[ -z "$wanted" ] || [[ "$wanted" =~ ^[0-9a-f]{7,40}$ ]] || die "COMMIT '$wanted' is not a SHA."

repository=https://code.videolan.org/videolan/vlc.git
api=https://code.videolan.org/api/v4/projects/videolan%2Fvlc/repository
nightlies=https://artifacts.videolan.org/vlc/nightly-win64

build=""
if [ -z "$wanted" ]; then
    # Newest dated directory holding the archive; a build still uploading has none yet.
    for dir in $(curl -fsSL "$nightlies/" | grep -oE 'href="[0-9]{8}-[0-9]{4}/"' | cut -d'"' -f2 | sort -r | head -n 3); do
        wanted=$(curl -fsSL "$nightlies/$dir" | grep -oE 'href="vlc-4\.0\.0-dev-win64-[0-9a-f]+\.7z"' \
            | sed -E 's/.*-win64-([0-9a-f]+)\.7z"/\1/' | head -n 1)
        [ -n "$wanted" ] && { build=${dir%/}; break; }
    done
    [ -n "$wanted" ] || die "No VLC 4 nightly archive under $nightlies/."
fi

commit=$(curl -fsSL "$api/commits/$wanted" | grep -oE '"id":"[0-9a-f]{40}"' | head -n 1 | cut -d'"' -f4)
is_sha "$commit" || die "Could not resolve VLC commit '$wanted'."

headers=""
if [ "$commit" = "$pin" ]; then
    status=pinned
elif [ "$commit" = "$last" ]; then
    status=checked
else
    work=$(mktemp -d)
    trap 'rm -rf "$work"' EXIT
    git -C "$work" init -q
    git -C "$work" fetch -q --depth 1 --filter=blob:none "$repository" $pin $commit $last \
        || die "git fetch of $pin $commit $last from $repository failed."
    changed() { git -C "$work" diff --no-renames --name-only "$1" "$commit" -- ':(glob)include/*.h' | tr '\n' ' ' | sed 's/ $//'; }
    headers=$(changed "$pin")
    since=$headers
    [ -z "$last" ] || since=$(changed "$last")
    if [ -n "$since" ]; then status=headers-changed; else status=no-header-change; fi
fi

result="commit=$commit
build=$build
status=$status
headers=$headers"
echo "$result"
[ -z "${GITHUB_OUTPUT:-}" ] || echo "$result" >> "$GITHUB_OUTPUT"
