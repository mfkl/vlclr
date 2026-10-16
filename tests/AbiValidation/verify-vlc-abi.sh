#!/usr/bin/env bash
# Compile-only probe of VLCLR's managed layouts against VLC headers, for
# Linux and macOS (GCC or Clang). Windows uses verify-vlc-abi.cmd.
#
# Usage: tests/AbiValidation/verify-vlc-abi.sh <vlc-include-dir>
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
include_dir="${1:-$script_dir/../../vlc/include}"

if [ ! -f "$include_dir/vlc_common.h" ]; then
    echo "ERROR: vlc_common.h was not found under \"$include_dir\"." >&2
    exit 2
fi

object="$(mktemp "${TMPDIR:-/tmp}/vlclr_abi_probe.XXXXXX")"
trap 'rm -f "$object"' EXIT

"${CXX:-c++}" -std=c++17 -w -c -I"$include_dir" "$script_dir/vlclr_abi.cpp" -o "$object"
echo "VLC ABI layout matches VLCLR ($("${CXX:-c++}" --version | head -n 1))."
