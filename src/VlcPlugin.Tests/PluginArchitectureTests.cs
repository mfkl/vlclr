using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace VlcPlugin.Tests;

/// <summary>
/// Tests that verify the plugin architecture is correctly set up.
///
/// NOTE: PluginExports.Open and Close methods are marked with [UnmanagedCallersOnly]
/// which means they cannot be called directly from managed code. These methods are
/// designed to be called only from native code (the C glue layer).
///
/// Full integration testing of the plugin lifecycle requires:
/// 1. Building the native VlcPlugin.dll via `dotnet publish -c Release -r win-x64`
/// 2. Using the C test harness (src/test/test_harness.c) or VLC itself
/// </summary>
public class PluginArchitectureTests
{
    [Fact]
    public void PluginExports_OpenMethod_HasCorrectSignature()
    {
        // Verify the Open method exists with the correct signature
        var method = typeof(PluginExports).GetMethod("Open", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(int), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(nint), parameters[0].ParameterType);
    }

    [Fact]
    public void PluginExports_OpenMethod_HasUnmanagedCallersOnlyAttribute()
    {
        var method = typeof(PluginExports).GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var attribute = method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("DotNetPluginOpen", attribute.EntryPoint);
    }

    [Fact]
    public void PluginExports_CloseMethod_HasCorrectSignature()
    {
        // Verify the Close method exists with the correct signature
        var method = typeof(PluginExports).GetMethod("Close", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(nint), parameters[0].ParameterType);
    }

    [Fact]
    public void PluginExports_CloseMethod_HasUnmanagedCallersOnlyAttribute()
    {
        var method = typeof(PluginExports).GetMethod("Close", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var attribute = method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("DotNetPluginClose", attribute.EntryPoint);
    }

    [Fact]
    public void PluginState_ImplementsIDisposable()
    {
        // Verify PluginState implements IDisposable for proper cleanup
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(PluginState)));
    }

    [Fact]
    public void PluginState_Constructor_AcceptsNintParameter()
    {
        // Verify constructor signature
        var constructor = typeof(PluginState).GetConstructor([typeof(nint)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void PluginState_HasInitializeMethod()
    {
        var method = typeof(PluginState).GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public void PluginState_HasCleanupMethod()
    {
        var method = typeof(PluginState).GetMethod("Cleanup", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }
}
