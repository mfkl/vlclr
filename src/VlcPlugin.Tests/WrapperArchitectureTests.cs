using System.Reflection;
using Xunit;

namespace VlcPlugin.Tests;

/// <summary>
/// Tests that verify wrapper classes have correct structure and public API.
/// These tests verify architecture without calling native code.
/// </summary>
public class WrapperArchitectureTests
{
    #region VlcLogger Tests

    [Fact]
    public void VlcLogger_Constructor_AcceptsNintParameter()
    {
        var constructor = typeof(VlcLogger).GetConstructor([typeof(nint)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void VlcLogger_HasInfoMethod()
    {
        var method = typeof(VlcLogger).GetMethod("Info", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcLogger_HasErrorMethod()
    {
        var method = typeof(VlcLogger).GetMethod("Error", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcLogger_HasWarningMethod()
    {
        var method = typeof(VlcLogger).GetMethod("Warning", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcLogger_HasDebugMethod()
    {
        var method = typeof(VlcLogger).GetMethod("Debug", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    #endregion

    #region VlcVariable Tests

    [Fact]
    public void VlcVariable_Constructor_AcceptsNintParameter()
    {
        var constructor = typeof(VlcVariable).GetConstructor([typeof(nint)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void VlcVariable_HasCreateIntegerMethod()
    {
        var method = typeof(VlcVariable).GetMethod("CreateInteger");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcVariable_HasCreateStringMethod()
    {
        var method = typeof(VlcVariable).GetMethod("CreateString");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcVariable_HasSetIntegerMethod()
    {
        var method = typeof(VlcVariable).GetMethod("SetInteger");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcVariable_HasGetIntegerMethod()
    {
        var method = typeof(VlcVariable).GetMethod("GetInteger");
        Assert.NotNull(method);
        Assert.Equal(typeof(long), method.ReturnType);
    }

    [Fact]
    public void VlcVariable_HasSetStringMethod()
    {
        var method = typeof(VlcVariable).GetMethod("SetString");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcVariable_HasGetStringMethod()
    {
        var method = typeof(VlcVariable).GetMethod("GetString");
        Assert.NotNull(method);
        // Returns string? (nullable)
        Assert.True(method.ReturnType == typeof(string));
    }

    [Fact]
    public void VlcVariable_HasDestroyMethod()
    {
        var method = typeof(VlcVariable).GetMethod("Destroy");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    #endregion

    #region VlcPlayer Tests

    [Fact]
    public void VlcPlayer_ImplementsIDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(VlcPlayer)));
    }

    [Fact]
    public void VlcPlayer_HasCreateStaticMethod()
    {
        var method = typeof(VlcPlayer).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        // Returns VlcPlayer? (nullable)
        Assert.True(method.ReturnType == typeof(VlcPlayer));
    }

    [Fact]
    public void VlcPlayer_HasStateProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("State");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
    }

    [Fact]
    public void VlcPlayer_HasStateChangedEvent()
    {
        var eventInfo = typeof(VlcPlayer).GetEvent("StateChanged");
        Assert.NotNull(eventInfo);
    }

    [Fact]
    public void VlcPlayer_HasMediaChangedEvent()
    {
        var eventInfo = typeof(VlcPlayer).GetEvent("MediaChanged");
        Assert.NotNull(eventInfo);
    }

    [Fact]
    public void VlcPlayer_HasPositionChangedEvent()
    {
        var eventInfo = typeof(VlcPlayer).GetEvent("PositionChanged");
        Assert.NotNull(eventInfo);
    }

    #endregion

    #region VlcPlaylist Tests

    [Fact]
    public void VlcPlaylist_ImplementsIDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(VlcPlaylist)));
    }

    [Fact]
    public void VlcPlaylist_HasCreateStaticMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(method.ReturnType == typeof(VlcPlaylist));
    }

    [Fact]
    public void VlcPlaylist_HasStartMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Start");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcPlaylist_HasStopMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Stop");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlaylist_HasPauseMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Pause");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlaylist_HasResumeMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Resume");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlaylist_HasNextMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Next");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcPlaylist_HasPrevMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("Prev");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VlcPlaylist_HasCountProperty()
    {
        var property = typeof(VlcPlaylist).GetProperty("Count");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VlcPlaylist_HasCurrentIndexProperty()
    {
        var property = typeof(VlcPlaylist).GetProperty("CurrentIndex");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VlcPlaylist_HasHasNextProperty()
    {
        var property = typeof(VlcPlaylist).GetProperty("HasNext");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VlcPlaylist_HasHasPrevProperty()
    {
        var property = typeof(VlcPlaylist).GetProperty("HasPrev");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VlcPlaylist_HasGoToMethod()
    {
        var method = typeof(VlcPlaylist).GetMethod("GoTo");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    #endregion

    #region VlcBridge P/Invoke Tests

    [Fact]
    public void VlcBridge_HasObjectParentMethod()
    {
        var type = typeof(VlcLogger).Assembly.GetType("VlcPlugin.Native.VlcBridge");
        Assert.NotNull(type);

        var method = type.GetMethod("ObjectParent", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(nint), method.ReturnType);
    }

    [Fact]
    public void VlcBridge_HasObjectTypenameMethod()
    {
        var type = typeof(VlcLogger).Assembly.GetType("VlcPlugin.Native.VlcBridge");
        Assert.NotNull(type);

        var method = type.GetMethod("ObjectTypename", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(nint), method.ReturnType);
    }

    #endregion
}
