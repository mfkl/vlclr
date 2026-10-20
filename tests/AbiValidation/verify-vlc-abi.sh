#!/usr/bin/env bash
# Compile-only probe of VLCLR's managed layouts against VLC headers, with GCC
# or Clang: on Linux and macOS, and on Windows with llvm-mingw
# (CC=x86_64-w64-mingw32-clang), the toolchain VLC's Windows builds use.
# MSVC uses verify-vlc-abi.cmd.
#
# The probe compiles the headers' static inline functions too; a few call
# functions only VLC's own build declares (poll on Windows), which is not a
# layout problem.
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

"${CC:-cc}" -std=gnu11 -w -Wno-error=implicit-function-declaration -c -I"$include_dir" "$script_dir/vlclr_abi.g.c" -o "$object"
echo "VLC ABI layout matches VLCLR ($("${CC:-cc}" --version | head -n 1))."
