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

### Build Dependencies
- vswhere.exe must be in PATH for native AOT publish (located at C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe)
- If NuGet fails, the repo has nuget.config that uses only nuget.org

### Full Build Sequence
```bash
# With vswhere in PATH:
export PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer"

# Build C# Native AOT
dotnet publish src/VlcPlugin -c Release -r win-x64

# Generate vlccore import library (required for linking)
"C:/Program Files/LLVM/bin/llvm-dlltool.exe" -d build/vlccore.def -l build/vlccore.lib -m i386:x86-64

# Build C glue (linked against real libvlccore)
"C:/Program Files/LLVM/bin/clang.exe" -c -o build/plugin_entry.o src/glue/plugin_entry.c -I./vlc/include
"C:/Program Files/LLVM/bin/clang.exe" -c -o build/csharp_bridge.o src/glue/csharp_bridge.c
"C:/Program Files/LLVM/bin/clang.exe" -shared -o build/libhello_csharp_plugin.dll build/plugin_entry.o build/csharp_bridge.o build/vlccore.lib

# Deploy to VLC
cp build/libhello_csharp_plugin.dll vlc-binaries/vlc-4.0.0-dev/plugins/control/
cp src/VlcPlugin/bin/Release/net10.0/win-x64/native/VlcPlugin.dll vlc-binaries/vlc-4.0.0-dev/plugins/control/
vlc-binaries/vlc-4.0.0-dev/vlc-cache-gen.exe vlc-binaries/vlc-4.0.0-dev/plugins

# Test with VLC
vlc-binaries/vlc-4.0.0-dev/vlc.exe -vvv --intf hello_csharp
```

### Test
```bash
# Build and run test harness
"C:/Program Files/LLVM/bin/clang.exe" -o build/test_harness.exe src/test/test_harness.c
cd build && ./test_harness.exe
```

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
