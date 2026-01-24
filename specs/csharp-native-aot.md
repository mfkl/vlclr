# C# Native AOT Library Specification

## Overview

The C# Native AOT library contains the actual plugin logic, compiled to a native DLL that exports C-callable functions. This is where the "real work" happens.

## .NET Version

- Target: .NET 10 (net10.0)
- Runtime: Native AOT (no CLR dependency)

## Project Structure

```
src/VlcPlugin/
├── VlcPlugin.csproj           # Project file with AOT config
├── PluginExports.cs           # [UnmanagedCallersOnly] entry points
├── PluginState.cs             # Plugin state management
├── VlcInterop.cs              # Helpers for VLC type handling
└── Generated/                 # ClangSharp-generated bindings
    └── libvlccore.cs          # P/Invoke declarations
```

## Project Configuration

### VlcPlugin.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishAot>true</PublishAot>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <!-- Minimize binary size -->
    <OptimizationPreference>Size</OptimizationPreference>
    <IlcOptimizationPreference>Size</IlcOptimizationPreference>
    <InvariantGlobalization>true</InvariantGlobalization>
    <StackTraceSupport>false</StackTraceSupport>

    <!-- Ensure exports are visible -->
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>link</TrimMode>
  </PropertyGroup>

  <ItemGroup>
    <!-- Direct export configuration -->
    <DirectPInvoke Include="libvlccore" />
  </ItemGroup>

</Project>
```

## Exported Entry Points

### PluginExports.cs

```csharp
using System;
using System.Runtime.InteropServices;

namespace VlcPlugin;

/// <summary>
/// Native entry points called by the C glue layer.
/// These functions are exported from the native DLL.
/// </summary>
public static unsafe class PluginExports
{
    private static PluginState? _state;

    /// <summary>
    /// Called when VLC loads the plugin.
    /// </summary>
    /// <param name="vlcObject">Opaque pointer to vlc_object_t</param>
    /// <returns>0 for success, -1 for failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "CSharpPluginOpen")]
    public static int Open(nint vlcObject)
    {
        try
        {
            _state = new PluginState(vlcObject);
            _state.Initialize();
            return 0; // VLC_SUCCESS
        }
        catch
        {
            return -1; // VLC_EGENERIC
        }
    }

    /// <summary>
    /// Called when VLC unloads the plugin.
    /// </summary>
    /// <param name="vlcObject">Opaque pointer to vlc_object_t</param>
    [UnmanagedCallersOnly(EntryPoint = "CSharpPluginClose")]
    public static void Close(nint vlcObject)
    {
        try
        {
            _state?.Cleanup();
            _state = null;
        }
        catch
        {
            // Swallow exceptions during cleanup
        }
    }
}
```

## Plugin State Management

### PluginState.cs

```csharp
using System;

namespace VlcPlugin;

/// <summary>
/// Manages plugin lifetime and state.
/// </summary>
public sealed class PluginState : IDisposable
{
    private readonly nint _vlcObject;
    private bool _disposed;

    public PluginState(nint vlcObject)
    {
        _vlcObject = vlcObject;
    }

    public void Initialize()
    {
        // Plugin initialization logic
        // - Register variables with VLC
        // - Set up event handlers
        // - Initialize any managed resources

        Log("C# Plugin initialized");
    }

    public void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;

        // Cleanup logic
        // - Unregister variables
        // - Remove event handlers
        // - Release managed resources

        Log("C# Plugin cleaned up");
    }

    public void Dispose() => Cleanup();

    private void Log(string message)
    {
        // TODO: Use VLC logging via libvlccore bindings
        Console.WriteLine($"[VlcPlugin] {message}");
    }
}
```

## VLC Type Interop

### VlcInterop.cs

```csharp
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VlcPlugin;

/// <summary>
/// Helper methods for VLC type marshalling.
/// </summary>
public static class VlcInterop
{
    /// <summary>
    /// Marshal a UTF-8 string from native memory.
    /// </summary>
    public static string? PtrToStringUtf8(nint ptr)
    {
        if (ptr == nint.Zero)
            return null;

        // Find string length
        int length = 0;
        unsafe
        {
            byte* p = (byte*)ptr;
            while (p[length] != 0) length++;
        }

        if (length == 0)
            return string.Empty;

        unsafe
        {
            return Encoding.UTF8.GetString((byte*)ptr, length);
        }
    }

    /// <summary>
    /// Allocate a UTF-8 string in native memory.
    /// Caller must free with Marshal.FreeHGlobal.
    /// </summary>
    public static nint StringToUtf8Ptr(string? str)
    {
        if (str == null)
            return nint.Zero;

        byte[] bytes = Encoding.UTF8.GetBytes(str + '\0');
        nint ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }
}
```

## Build and Publish

### Development Build
```bash
dotnet build src/VlcPlugin -c Debug
```

### Native AOT Publish
```bash
dotnet publish src/VlcPlugin -c Release -r win-x64
```

### Output Location
- `src/VlcPlugin/bin/Release/net10.0/win-x64/native/VlcPlugin.dll`

### Verify Exports
```bash
dumpbin /exports VlcPlugin.dll
# Should show: CSharpPluginOpen, CSharpPluginClose
```

## AOT Considerations

### Supported Features
- Static methods with `[UnmanagedCallersOnly]`
- Blittable types (int, long, pointers, etc.)
- Structs with explicit layout
- Unsafe code for pointer manipulation

### Unsupported/Limited Features
- Reflection (limited, must be explicitly preserved)
- Dynamic code generation
- COM interop
- Some LINQ operations over non-array collections

### Trimming
- Must use `[DynamicallyAccessedMembers]` for reflection
- Generated bindings should be trim-safe

## Testing

### Unit Tests (VlcPlugin.Tests)

```csharp
public class PluginExportsTests
{
    [Fact]
    public void Open_WithZeroPointer_Succeeds()
    {
        // Test that Open doesn't crash with null object
        int result = PluginExports.Open(nint.Zero);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Close_WithZeroPointer_DoesNotThrow()
    {
        PluginExports.Close(nint.Zero);
        // No exception = success
    }
}
```

## Acceptance Criteria

1. Project builds with `dotnet build`
2. Native AOT publish produces single native DLL
3. DLL exports `CSharpPluginOpen` and `CSharpPluginClose` symbols
4. Exports are callable from C code
5. Open returns 0 (success) under normal conditions
6. Close cleans up without crashing
7. No .NET runtime dependencies (truly native)
8. Binary size is reasonable (< 10MB)
9. Unit tests pass
