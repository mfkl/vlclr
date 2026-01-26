# C to C# Bridge Specification

## Overview

This spec defines how the C glue layer and C# Native AOT library connect and communicate. The bridge must handle:
- Loading the C# DLL at runtime
- Resolving exported function addresses
- Calling C# functions with correct ABI
- Handling errors and edge cases

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     C Glue Layer                                │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐      │
│  │ VLC Callback │───▶│ Bridge Layer │───▶│ C# Function  │      │
│  │   Open()     │    │              │    │   Pointer    │      │
│  └──────────────┘    └──────────────┘    └──────────────┘      │
│                              │                    │             │
│                              │ LoadLibrary        │ Call        │
│                              ▼                    ▼             │
└──────────────────────────────┼────────────────────┼─────────────┘
                               │                    │
                               ▼                    ▼
┌─────────────────────────────────────────────────────────────────┐
│                   C# Native AOT DLL                             │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  [UnmanagedCallersOnly(EntryPoint = "CSharpPluginOpen")]  │ │
│  │  public static int Open(nint vlcObject) { ... }           │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## ABI Contract

### Calling Convention
- x64 Windows: Microsoft x64 calling convention
  - First 4 integer/pointer args: RCX, RDX, R8, R9
  - Return value: RAX
- x64 Linux/macOS: System V AMD64 ABI
  - First 6 integer/pointer args: RDI, RSI, RDX, RCX, R8, R9
  - Return value: RAX

### C# Side Configuration
```csharp
[UnmanagedCallersOnly(EntryPoint = "CSharpPluginOpen", CallConvs = new[] { typeof(CallConvCdecl) })]
public static int Open(nint vlcObject)
```

### C Side Declaration
```c
// Function pointer types matching C# exports
typedef int (*csharp_open_fn)(void* vlc_object);
typedef void (*csharp_close_fn)(void* vlc_object);
```

## Parameter Passing

### Pointer Types
| VLC Type | C Type | C# Type |
|----------|--------|---------|
| `vlc_object_t*` | `void*` | `nint` (IntPtr) |
| `block_t*` | `void*` | `nint` |
| `const char*` | `const char*` | `nint` + manual UTF-8 |

### Value Types
| VLC Type | C Type | C# Type |
|----------|--------|---------|
| `int` | `int` | `int` |
| `unsigned` | `unsigned int` | `uint` |
| `vlc_tick_t` | `int64_t` | `long` |
| `bool` | `int` | `int` (0/1) |

### Struct Types
Structs must match exactly in memory layout:

```c
// C definition
struct plugin_data {
    int32_t version;
    int32_t flags;
    void* user_data;
};
```

```csharp
// C# definition - must match exactly
[StructLayout(LayoutKind.Sequential)]
public struct PluginData
{
    public int Version;
    public int Flags;
    public nint UserData;
}
```

## DLL Loading Strategy

### Windows (Primary)

```c
#include <windows.h>

static HMODULE g_csharp_dll = NULL;

int dotnet_bridge_init(void)
{
    // Look for C# DLL in same directory as glue DLL
    HMODULE glue_module;
    GetModuleHandleExA(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
        (LPCSTR)dotnet_bridge_init,
        &glue_module
    );

    char path[MAX_PATH];
    GetModuleFileNameA(glue_module, path, MAX_PATH);

    // Replace glue DLL name with C# DLL name
    char* last_slash = strrchr(path, '\\');
    if (last_slash)
        strcpy(last_slash + 1, "VlcPlugin.dll");

    g_csharp_dll = LoadLibraryA(path);
    if (!g_csharp_dll)
    {
        // Fallback: try current directory
        g_csharp_dll = LoadLibraryA("VlcPlugin.dll");
    }

    return g_csharp_dll ? 0 : -1;
}
```

### Linux/macOS (Future)

```c
#include <dlfcn.h>

static void* g_csharp_dll = NULL;

int dotnet_bridge_init(void)
{
    Dl_info info;
    if (dladdr(dotnet_bridge_init, &info))
    {
        // Construct path relative to glue library
        char path[PATH_MAX];
        strncpy(path, info.dli_fname, PATH_MAX);
        char* last_slash = strrchr(path, '/');
        if (last_slash)
            strcpy(last_slash + 1, "VlcPlugin.so");

        g_csharp_dll = dlopen(path, RTLD_NOW);
    }

    if (!g_csharp_dll)
        g_csharp_dll = dlopen("VlcPlugin.so", RTLD_NOW);

    return g_csharp_dll ? 0 : -1;
}
```

## Function Resolution

```c
int dotnet_bridge_resolve(void)
{
    if (!g_csharp_dll)
        return -1;

#ifdef _WIN32
    csharp_plugin_open = (csharp_open_fn)GetProcAddress(g_csharp_dll, "CSharpPluginOpen");
    csharp_plugin_close = (csharp_close_fn)GetProcAddress(g_csharp_dll, "CSharpPluginClose");
#else
    csharp_plugin_open = (csharp_open_fn)dlsym(g_csharp_dll, "CSharpPluginOpen");
    csharp_plugin_close = (csharp_close_fn)dlsym(g_csharp_dll, "CSharpPluginClose");
#endif

    // All required exports must resolve
    if (!csharp_plugin_open || !csharp_plugin_close)
        return -1;

    return 0;
}
```

## Error Handling

### Bridge Initialization Failure
```c
static int Open(vlc_object_t *obj)
{
    if (dotnet_bridge_init() != 0)
    {
        msg_Err(obj, "Failed to load C# plugin DLL");
        return VLC_EGENERIC;
    }

    if (dotnet_bridge_resolve() != 0)
    {
        msg_Err(obj, "Failed to resolve C# plugin exports");
        dotnet_bridge_cleanup();
        return VLC_EGENERIC;
    }

    int result = csharp_plugin_open((void*)obj);
    if (result != 0)
    {
        msg_Err(obj, "C# plugin Open failed with code %d", result);
        dotnet_bridge_cleanup();
        return VLC_EGENERIC;
    }

    return VLC_SUCCESS;
}
```

### C# Exception Handling
```csharp
[UnmanagedCallersOnly(EntryPoint = "CSharpPluginOpen")]
public static int Open(nint vlcObject)
{
    try
    {
        // Plugin initialization
        return 0; // VLC_SUCCESS
    }
    catch (Exception ex)
    {
        // Log error somehow (can't throw across native boundary)
        Console.Error.WriteLine($"C# Plugin Error: {ex}");
        return -1; // VLC_EGENERIC
    }
}
```

## Memory Ownership

### Rule 1: Pointer Ownership Stays With Originator
- Pointers passed from VLC → C glue → C# are VLC-owned
- C# must not free VLC memory
- C# must not hold references beyond callback lifetime (unless explicitly documented)

### Rule 2: C# Allocated Memory
- Memory allocated in C# with `Marshal.AllocHGlobal` must be freed with `Marshal.FreeHGlobal`
- If passing to VLC that expects to own memory, use VLC allocators via bindings

### Rule 3: Strings
- VLC strings are UTF-8, null-terminated, owned by VLC
- Copy strings if needed beyond callback lifetime
- Never free VLC-provided strings

## Thread Safety

### Initialization
- Bridge initialization (`dotnet_bridge_init`) must be thread-safe
- Use atomic operations or locks if multiple callers possible

### Callbacks
- C# code must be prepared for callbacks from any thread
- Static state must be protected with locks
- Use `[ThreadStatic]` for thread-local storage if needed

```csharp
private static readonly object _lock = new();
private static PluginState? _state;

[UnmanagedCallersOnly(EntryPoint = "CSharpPluginOpen")]
public static int Open(nint vlcObject)
{
    lock (_lock)
    {
        _state = new PluginState(vlcObject);
        return 0;
    }
}
```

## Testing the Bridge

### Manual Verification
1. Build both DLLs
2. Place in same directory
3. Run test harness that calls the exports
4. Verify function calls work

### Integration Test Harness (C)
```c
#include <stdio.h>
#include "dotnet_bridge.h"

int main()
{
    printf("Initializing bridge...\n");
    if (dotnet_bridge_init() != 0)
    {
        printf("FAIL: Bridge init failed\n");
        return 1;
    }

    if (dotnet_bridge_resolve() != 0)
    {
        printf("FAIL: Function resolution failed\n");
        return 1;
    }

    printf("Calling Open...\n");
    int result = csharp_plugin_open(NULL);
    printf("Open returned: %d\n", result);

    printf("Calling Close...\n");
    csharp_plugin_close(NULL);

    dotnet_bridge_cleanup();
    printf("SUCCESS\n");
    return 0;
}
```

## Acceptance Criteria

1. C glue successfully loads C# DLL via LoadLibrary/dlopen
2. Function pointers are resolved correctly
3. Calls from C reach C# `[UnmanagedCallersOnly]` methods
4. Return values propagate correctly (C# → C)
5. Pointer parameters pass correctly (C → C#)
6. No memory leaks in bridge code
7. Graceful handling of missing DLL
8. Graceful handling of missing exports
9. Thread-safe initialization
10. Works with VLC's actual plugin loading sequence
