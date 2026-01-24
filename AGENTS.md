## Build & Run

### C Glue Layer
- Compiler: Clang 21.1.8 at `C:\Program Files\LLVM\bin\clang.exe`
- Build: `"C:\Program Files\LLVM\bin\clang.exe" -shared -o libhello_csharp_plugin.dll src/glue/*.c -I./vlc/include -lvlccore`

### C# Native AOT Library
- SDK: .NET 10.0.102
- Build: `dotnet publish src/VlcPlugin -c Release -r win-x64`
- Output: `src/VlcPlugin/bin/Release/net10.0/win-x64/native/VlcPlugin.dll`

### Combined Plugin
- Copy both DLLs to VLC plugin directory
- Test: `vlc --verbose 2 --plugin-path ./plugins`

## Validation

- C# build: `dotnet build src/VlcPlugin`
- C# tests: `dotnet test src/VlcPlugin.Tests`
- Check exports (via VS Dev Prompt): `dumpbin /exports VlcPlugin.dll`
- VLC load test: `vlc -vvv --list`

## Tool Paths (Windows)

- Clang: `C:\Program Files\LLVM\bin\clang.exe`
- CMake: `C:\Program Files\CMake\bin\cmake.exe`
- ClangSharp: `%USERPROFILE%\.dotnet\tools\ClangSharpPInvokeGenerator.cmd`
- MSVC: `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\`
- dumpbin: (same MSVC path)/dumpbin.exe

## Operational Notes

### VLC Headers
- Location: `vlc/include/vlc/*.h`
- Key headers: `vlc_plugin.h`, `vlc_common.h`, `vlc_interface.h`
- VLC 4.x API version: 4.0.6

### ClangSharp Bindings
- Generate: `%USERPROFILE%\.dotnet\tools\ClangSharpPInvokeGenerator.cmd @src/VlcPlugin/clangsharp.rsp`
- Config file: `src/VlcPlugin/clangsharp.rsp`

## Codebase Patterns

(Ralph will update this section as patterns emerge)
