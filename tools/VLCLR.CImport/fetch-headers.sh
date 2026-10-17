#!/usr/bin/env bash
# Fetches the VLC headers pinned in vlc.cimport.json.
#
# Reads vlc.repository, vlc.commit and vlc.includeDir from the manifest,
# fetches only the include/ directory of that exact commit (shallow, blobless
# fetch plus sparse checkout, kept in a .vlc-src directory next to the include
# directory) and copies it to the include directory, so that
# <includeDir>/vlc_common.h exists.
#
# Re-running is cheap: nothing happens when the include directory is already at
# the pinned commit, and a different pinned commit is fetched into the same
# .vlc-src directory.
#
# Usage: fetch-headers.sh [destination [manifest]]
#   destination  include directory to fill instead of vlc.includeDir
#                (relative paths are relative to the current directory)
#   manifest     path of the manifest (default: vlc.cimport.json next to this script)
#
# Needs bash, git, sed and awk. No JSON tool: the three fields are read with sed/awk.

set -euo pipefail

die() { echo "fetch-headers: $*" >&2; exit 1; }

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
destination=${1:-}
manifest=${2:-$script_dir/vlc.cimport.json}

[ -f "$manifest" ] || die "Manifest not found: $manifest"
manifest_dir=$(cd "$(dirname "$manifest")" && pwd)
manifest="$manifest_dir/$(basename "$manifest")"

# Value of a string key inside the "vlc" object. Whole-line // comments are dropped first.
vlc_field() {
  sed -e 's#^[[:space:]]*//.*$##' "$manifest" | awk -v key="$1" '
    # Reads all its input (no early exit), or sed would die of SIGPIPE under pipefail.
    /"vlc"[[:space:]]*:/ { invlc = 1 }
    invlc && !done && match($0, "\"" key "\"[[:space:]]*:[[:space:]]*\"[^\"]*\"") {
      s = substr($0, RSTART, RLENGTH)
      sub(/^[^:]*:[[:space:]]*"/, "", s)
      sub(/"$/, "", s)
      print s
      done = 1
    }
    invlc && /}/ { done = 1 }'
}

# Collapses . and .. segments without needing the path to exist.
normalize() {
  awk -v p="$1" 'BEGIN {
    n = split(p, a, "/"); m = 0
    for (i = 1; i <= n; i++) {
      if (a[i] == "" || a[i] == ".") continue
      if (a[i] == ".." && m > 0 && s[m] != "..") { m--; continue }
      s[++m] = a[i]
    }
    out = (substr(p, 1, 1) == "/") ? "/" : ""
    for (i = 1; i <= m; i++) out = out s[i] (i < m ? "/" : "")
    print out
  }'
}

repository=$(vlc_field repository)
commit=$(vlc_field commit | tr 'A-F' 'a-f')
include_rel=$(vlc_field includeDir)
[ -n "$repository" ] || die "$manifest has no vlc.repository."
[ -n "$commit" ] || die "$manifest has no vlc.commit."
[ -n "$include_rel" ] || die "$manifest has no vlc.includeDir."
case "$commit" in
  *[!0-9a-f]*) die "vlc.commit '$commit' is not a hexadecimal commit id." ;;
esac
[ "${#commit}" -ge 8 ] && [ "${#commit}" -le 40 ] || die "vlc.commit '$commit' is not a hexadecimal commit id."

if [ -n "$destination" ]; then
  case "$destination" in
    /*|[A-Za-z]:*) include_dir=$destination ;;
    *) include_dir=$PWD/$destination ;;
  esac
else
  case "$include_rel" in
    /*|[A-Za-z]:*) include_dir=$include_rel ;;
    *) include_dir=$manifest_dir/$include_rel ;;
  esac
fi
include_dir=$(normalize "$include_dir")
stamp=$include_dir/.vlc-commit

if [ -f "$stamp" ] && [ -f "$include_dir/vlc_common.h" ]; then
  current=$(tr -d '[:space:]' < "$stamp")
  case "$current" in
    "$commit"*) echo "fetch-headers: $include_dir is already at $current"; exit 0 ;;
  esac
fi

command -v git >/dev/null 2>&1 || die "git is not on PATH."
if [ "${#commit}" -lt 40 ]; then
  echo "fetch-headers: warning: vlc.commit '$commit' is abbreviated; most servers only serve a fetch by the full 40-character id." >&2
fi

# Never wipe a directory this script did not fill.
if [ -d "$include_dir" ] && [ ! -f "$stamp" ] && [ -n "$(ls -A "$include_dir")" ]; then
  die "$include_dir exists, is not empty and was not created by fetch-headers; refusing to replace it."
fi

src_dir=$(dirname "$include_dir")/.vlc-src
mkdir -p "$src_dir"
g() { git -C "$src_dir" "$@"; }

[ -d "$src_dir/.git" ] || g init --quiet
case " $(g remote | tr '\n' ' ') " in
  *" origin "*) g remote set-url origin "$repository" ;;
  *) g remote add origin "$repository" ;;
esac
g sparse-checkout set --cone include

echo "fetch-headers: fetching include/ of $commit from $repository"
g fetch --quiet --depth 1 --filter=blob:none origin "$commit" \
  || die "git fetch of $commit from $repository failed."
g checkout --quiet --force --detach FETCH_HEAD

head=$(g rev-parse HEAD)
case "$head" in
  "$commit"*) ;;
  *) die "Checked out $head, expected $commit." ;;
esac
[ -f "$src_dir/include/vlc_common.h" ] \
  || die "Commit $head has no include/vlc_common.h; is vlc.repository a VLC repository?"

rm -rf "$include_dir"
mkdir -p "$include_dir"
cp -R "$src_dir/include/." "$include_dir/"
printf '%s' "$head" > "$stamp"

count=$(find "$include_dir" -type f | wc -l | tr -d ' ')
echo "fetch-headers: VLC $head, $count files in $include_dir"
