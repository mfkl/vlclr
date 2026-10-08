# VLCLR for VLC 3

VLCLR is a C# framework for writing VLC plugins and driving the VLC core from
.NET, including Native AOT plugins. This `3.x` branch targets the VLC 3.0.x
internal ABI (module suffix `3_0_0f`); `main` follows VLC 4.

It is what [VLC Avalonia](https://github.com/mfkl/vlc-avalonia) builds on, as a
git submodule.

## Contents

| Path | What it is |
| --- | --- |
| `src/VLCLR` | Module registration, native layouts and bindings for the VLC 3 core: playlist queue, transport, tracks and delays, media information, video presentation, subpictures and filters |
| `src/VLCLR.Generators` | Source generator for module entry points |
| `lib/libvlccore.lib` | Import library from the official VLC 3.0.24 win64 SDK, for Native AOT plugins on Windows |
| `tests/AbiValidation` | C++ probe that checks the managed layout constants against VLC 3 headers |

## Platforms

Windows x64, Linux x64, and macOS on arm64 and x64. VLC's variadic control
calls take a `va_list` that VLCLR builds in managed code for each platform
(`VLCVaList`), and calls to variadic functions such as `vlc_set` place their
arguments where each platform's convention expects them (`VLCVariadic`; Apple
arm64 passes them on the stack), so no native helper library is needed. Linux
distributions that renamed the module suffix for their 64-bit `time_t`
transition (`3_0_0ft64`, Debian and Ubuntu) use the same ABI on 64-bit targets.

Hosts that load VLC from an explicit path, such as `VLC.app` on macOS or a
distribution's versioned sonames, bind VLCLR to it with
`VLCCore.UseLibraries`. Plugins on Linux and macOS find the core that loaded
them with `VLCCore.UseLibraryContaining`, passing the `vlc_set` callback.

## Build

```
dotnet build vlclr.slnx
```

To check the native layouts against VLC 3 source headers:

```
c++ -std=c++17 -w -I <vlc-3.0.x>/include tests/AbiValidation/vlclr_abi.cpp -o vlclr-abi && ./vlclr-abi
```

## License

VLCLR is free software: you can redistribute it and/or modify it under the
terms of the GNU General Public License as published by the Free Software
Foundation, either version 2 of the License, or (at your option) any later
version. It is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See [LICENSE](LICENSE) for the full text.
