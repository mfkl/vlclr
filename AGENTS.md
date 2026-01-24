## Build & Run

### C Glue Layer
- Compiler: Clang/LLVM
- Build: `clang -shared -o libvlc_csharp_glue.dll src/glue/*.c -I./vlc/include`

### C# Native AOT Library
- SDK: .NET 10
- Build: `dotnet publish src/VlcPlugin -c Release -r win-x64`
- Output: Native DLL with exported functions

### Combined Plugin
- Copy both DLLs to VLC plugin directory
- Test: `vlc --verbose 2 --plugin-path ./plugins`

## Validation

- C# build: `dotnet build src/VlcPlugin`
- C# tests: `dotnet test src/VlcPlugin.Tests`
- Check exports: `dumpbin /exports src/VlcPlugin/bin/Release/net10.0/win-x64/native/VlcPlugin.dll`
- VLC load test: `vlc -vvv --list`

## Operational Notes

### VLC Headers
- Location: `vlc/include/vlc/*.h`
- Key headers: `vlc_plugin.h`, `vlc_common.h`, `vlc_interface.h`

### ClangSharp Bindings
- Generate: `ClangSharpPInvokeGenerator -f vlc/include/vlc/libvlc.h -o src/VlcPlugin/Generated`
- Config file: `src/VlcPlugin/clangsharp.rsp`

## Codebase Patterns

(Ralph will update this section as patterns emerge)
