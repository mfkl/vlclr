## Build & Run

### C Glue Layer
- Compiler: Clang 21.1.8 at `C:\Program Files\LLVM\bin\clang.exe`
- Build: `"C:\Program Files\LLVM\bin\clang.exe" -shared -o libdotnet_bridge_plugin.dll src/glue/*.c -I./vlc/include -lvlccore`

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

### Full Build Sequence (Verified)
```bash
# With vswhere in PATH:
export PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer"

# Build C# Native AOT
dotnet publish src/VlcPlugin -c Release -r win-x64

# Generate vlccore import library (minimal exports)
cd build && echo "LIBRARY libvlccore" > vlccore_minimal.def && echo "EXPORTS" >> vlccore_minimal.def && echo "var_Create" >> vlccore_minimal.def && echo "var_Destroy" >> vlccore_minimal.def && echo "var_SetChecked" >> vlccore_minimal.def && echo "var_GetChecked" >> vlccore_minimal.def && echo "vlc_object_Log" >> vlccore_minimal.def && "C:/Program Files/LLVM/bin/llvm-dlltool.exe" -d vlccore_minimal.def -l vlccore.lib -m i386:x86-64

# Build C glue (linked against real libvlccore)
"C:/Program Files/LLVM/bin/clang.exe" -shared -o build/libdotnet_bridge_plugin.dll build/plugin_entry.o build/dotnet_bridge.o -L./build -lvlccore

# Deploy to VLC
cp build/libdotnet_bridge_plugin.dll vlc-binaries/vlc-4.0.0-dev/plugins/control/
cp src/VlcPlugin/bin/Release/net10.0/win-x64/native/VlcPlugin.dll vlc-binaries/vlc-4.0.0-dev/plugins/control/

# Test with VLC
vlc-binaries/vlc-4.0.0-dev/vlc.exe -vvv --extraintf dotnet_bridge
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
- MSVC: `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\`
- dumpbin: (same MSVC path)/dumpbin.exe

## Operational Notes

### VLC Headers
- Location: `vlc/include/vlc/*.h`
- Key headers: `vlc_plugin.h`, `vlc_common.h`, `vlc_interface.h`
- VLC 4.x API version: 4.0.6

## Codebase Patterns

(Ralph will update this section as patterns emerge)
