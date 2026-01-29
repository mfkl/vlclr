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

    [Fact]
    public void VlcPlayer_HasTimeProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("Time");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasTimeSpanProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("TimeSpan");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(TimeSpan), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasLengthProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("Length");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasLengthTimeSpanProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("LengthTimeSpan");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(TimeSpan), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasPositionProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("Position");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(double), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasCanSeekProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("CanSeek");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasCanPauseProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("CanPause");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasSeekByTimeMethod_WithLong()
    {
        var method = typeof(VlcPlayer).GetMethod("SeekByTime", [typeof(long), typeof(SeekSpeed), typeof(SeekWhence)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlayer_HasSeekByTimeMethod_WithTimeSpan()
    {
        var method = typeof(VlcPlayer).GetMethod("SeekByTime", [typeof(TimeSpan), typeof(SeekSpeed), typeof(SeekWhence)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlayer_HasSeekByPositionMethod()
    {
        var method = typeof(VlcPlayer).GetMethod("SeekByPosition", [typeof(double), typeof(SeekSpeed), typeof(SeekWhence)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlayer_HasPauseMethod()
    {
        var method = typeof(VlcPlayer).GetMethod("Pause");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlayer_HasResumeMethod()
    {
        var method = typeof(VlcPlayer).GetMethod("Resume");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VlcPlayer_HasVolumeProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("Volume");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal(typeof(float), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasIsMutedProperty()
    {
        var property = typeof(VlcPlayer).GetProperty("IsMuted");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal(typeof(bool?), property.PropertyType);
    }

    [Fact]
    public void VlcPlayer_HasToggleMuteMethod()
    {
        var method = typeof(VlcPlayer).GetMethod("ToggleMute");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    #endregion

    #region SeekSpeed and SeekWhence Enums Tests

    [Fact]
    public void SeekSpeed_HasPreciseValue()
    {
        Assert.Equal(0, (int)SeekSpeed.Precise);
    }

    [Fact]
    public void SeekSpeed_HasFastValue()
    {
        Assert.Equal(1, (int)SeekSpeed.Fast);
    }

    [Fact]
    public void SeekWhence_HasAbsoluteValue()
    {
        Assert.Equal(0, (int)SeekWhence.Absolute);
    }

    [Fact]
    public void SeekWhence_HasRelativeValue()
    {
        Assert.Equal(1, (int)SeekWhence.Relative);
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

}
