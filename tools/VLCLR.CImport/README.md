# VLCLR.CImport

Generates `VLCLR.Native.Interop` (`src/VLCLR/Native/Interop/*.g.cs`) from the
VLC headers. The VLC headers are the only source of struct layouts, enums,
constants and function signatures; the generated C# is committed.

```
vlc.cimport.json ──> fetch-headers ──> extract (windows) ──> abi/windows.json ─┐
                                   └─> extract (sysv)    ──> abi/sysv.json   ──┴─> emit ──> *.g.cs
                                                                                     ├─> cimport-report.md
                                                                                     ├─> tests/AbiValidation/vlclr_abi.g.cpp
                                                                                     └─> src/VLCLR.Tests/Interop/InteropLayoutTests.g.cs
```

- **extract** runs libclang on the real system headers of one ABI and writes a
  deterministic JSON snapshot: record sizes, alignments, field and bitfield
  offsets, enums, functions, and the object-like macros clang can evaluate.
  `windows` must run on Windows (target `x86_64-pc-windows-msvc`), `sysv` on
  Linux (`x86_64-pc-linux-gnu`; macOS uses the same layouts, which the per-OS
  probe in CI checks).
- **emit** runs anywhere. It merges the two snapshots: a member at the same
  offset on both ABIs becomes a `[FieldOffset]` field, one that moves becomes a
  `ref` property that picks the offset with `OperatingSystem.IsWindows()`
  (a constant under Native AOT). It also writes the C++ probe and the C#
  layout tests that check the snapshots against the compiler and the
  generated C# against the snapshots.

## Regenerate

From the repository root:

```sh
# 1. Pinned headers (commit from vlc.cimport.json) into artifacts/vlc-headers
pwsh tools/VLCLR.CImport/fetch-headers.ps1      # or: tools/VLCLR.CImport/fetch-headers.sh

# 2. One snapshot per ABI
dotnet run --project tools/VLCLR.CImport -- extract --abi windows \
    --manifest tools/VLCLR.CImport/vlc.cimport.json \
    --out src/VLCLR/Native/Interop/abi/windows.json          # on Windows
dotnet run --project tools/VLCLR.CImport -- extract --abi sysv \
    --manifest tools/VLCLR.CImport/vlc.cimport.json \
    --out src/VLCLR/Native/Interop/abi/sysv.json             # on Linux or WSL

# 3. C#, report, probe and layout tests
dotnet run --project tools/VLCLR.CImport -- emit --manifest tools/VLCLR.CImport/vlc.cimport.json
```

Without a Linux machine, run the `CImport` workflow with `upload-snapshots`
set and take `sysv.json` from its `cimport-snapshots` artifact.

libclang comes from NuGet without clang's builtin headers (`stddef.h`, ...).
extract looks for them in `$CIMPORT_RESOURCE_DIR`, then an installed LLVM
(`C:\Program Files\LLVM`, Visual Studio's LLVM, `/usr/lib/llvm-*`), or takes
`--resource-dir <dir>`.

CI (`.github/workflows/cimport.yml`) repeats steps 1 to 3 and fails when the
committed files differ.

## Bump the VLC commit

`vlc.commit` in `vlc.cimport.json` is the only place to edit by hand. The
generated files carry the commit too (the `*.g.cs` headers, `vlcCommit` in
`abi/*.json`, `cimport-report.md`, the probe), and extract and emit rewrite them.

1. Pick the commit, usually the one of the latest VLC 4 nightly. The nightly
   file names on artifacts.videolan.org carry a short SHA
   (`vlc-4.0.0-dev-win64-942cdaea.7z`, `vlc_4.0.0-dev-39450-g942cdaea17_amd64.snap`);
   the full one is the `id` of
   `https://code.videolan.org/api/v4/projects/videolan%2Fvlc/repository/commits/<short sha>`.
2. Set `vlc.commit` to the full 40-character SHA.
3. Run the three steps of [Regenerate](#regenerate), both ABIs. In WSL, run
   extract from a separate clone: sharing `bin/` and `obj/` with a Windows
   build breaks the Linux build.
4. Review the diff without the lines that only carry the commit:

   ```sh
   git diff -I '<old sha>|<new sha>'
   ```

   Nothing left means the VLC change touched nothing VLCLR imports. Otherwise
   read the `cimport-report.md` and `.g.cs` changes:

   | VLC change | What happens |
   |---|---|
   | New fields, constants, functions | Generated, nothing else to do |
   | Field offsets or record sizes | Absorbed by the generated offsets; layout tests and probe check them |
   | Rename, removal, signature change | Hand-written wrappers in `src/VLCLR` stop compiling; fix them |
   | Inline function body | `tests/InlineParity` fails; update the C# reimplementation |

5. Build, run the unit tests, `tests/InlineParity` and `tests/VLCLR.CImport.Tests`,
   then open a PR. The `Cross-platform` workflow probes the layouts against
   the new pin on every OS and runs the plugins in VLC.

## Change what is generated

Everything is driven by `vlc.cimport.json`:

| Key | Meaning |
|---|---|
| `vlc.commit` | Pinned VLC commit (full SHA) the headers come from |
| `headers` | Headers included, in order, by the parsed translation unit |
| `prelude`, `defines`, `abiDefines` | What the headers need to parse on each ABI |
| `roots` | Symbols to keep (records, typedefs, enums, functions, macros); a trailing `*` matches a prefix. Records embedded by value are kept in full, records reached only through pointers become opaque |
| `excludes` | Symbols to drop even when a root reaches them |
| `names` | C# names that win over the naming rules |

Naming rules: `_t` and `vlc_` are stripped from type names (`es_format_t` →
`EsFormat`), Hungarian prefixes from fields (`i_chroma` → `Chroma`), the
common prefix from enum members, `VLC_` from constants (grouped per header:
`VLC_CODEC_I420` → `VlcFourcc.CodecI420`), and functions are grouped by their
prefix (`vlc_player_GetTime` → `VlcPlayer.GetTime`). Two C names that end up
with the same C# name stop emit with an error naming both; add an override.

## Layout of the generated code

- One file per header (`vlc_es.h` → `VlcEs.g.cs`), plus `CImport.g.cs` with
  the internal helpers.
- Every record has `SizeOf` (running ABI), `MinSizeOf`, `MaxSizeOf` and an
  `Offsets` class (constants where both ABIs agree), so hand-written wrappers
  can place fields with `[FieldOffset(Plane.Offsets.Pixels)]`.
- `StructLayout.Size` is the largest ABI size. Do not copy a record whose size
  differs between ABIs by value into VLC memory: on the smaller ABI it writes
  past the C struct. For that reason a member holding such a record
  (`filter_t.fmt_in`, `picture_t.format`, ...) is a pointer to it
  (`filter->FmtIn->Video->Width`), and such records are never passed by value
  or used as inline array elements (those are skipped and reported).
- Variadic functions and anything extract or emit could not represent are
  listed in `cimport-report.md` with the reason.
