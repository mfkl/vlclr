using System.Reflection;
using VLCLR.Types;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests that verify wrapper classes have correct structure and public API.
/// These tests verify architecture without calling native code.
/// </summary>
public class WrapperArchitectureTests
{
    #region VLCLogger Tests

    [Fact]
    public void VLCLogger_Constructor_AcceptsNintParameter()
    {
        var constructor = typeof(VLCLogger).GetConstructor([typeof(nint)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void VLCLogger_HasInfoMethod()
    {
        var method = typeof(VLCLogger).GetMethod("Info", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCLogger_HasErrorMethod()
    {
        var method = typeof(VLCLogger).GetMethod("Error", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCLogger_HasWarningMethod()
    {
        var method = typeof(VLCLogger).GetMethod("Warning", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCLogger_HasDebugMethod()
    {
        var method = typeof(VLCLogger).GetMethod("Debug", [typeof(string)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    #endregion

    #region VLCVariable Tests

    [Fact]
    public void VLCVariable_Constructor_AcceptsNintParameter()
    {
        var constructor = typeof(VLCVariable).GetConstructor([typeof(nint)]);
        Assert.NotNull(constructor);
    }

    [Fact]
    public void VLCVariable_HasCreateIntegerMethod()
    {
        var method = typeof(VLCVariable).GetMethod("CreateInteger");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCVariable_HasCreateStringMethod()
    {
        var method = typeof(VLCVariable).GetMethod("CreateString");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCVariable_HasSetIntegerMethod()
    {
        var method = typeof(VLCVariable).GetMethod("SetInteger");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCVariable_HasGetIntegerMethod()
    {
        var method = typeof(VLCVariable).GetMethod("GetInteger");
        Assert.NotNull(method);
        Assert.Equal(typeof(long), method.ReturnType);
    }

    [Fact]
    public void VLCVariable_HasSetStringMethod()
    {
        var method = typeof(VLCVariable).GetMethod("SetString");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCVariable_HasGetStringMethod()
    {
        var method = typeof(VLCVariable).GetMethod("GetString");
        Assert.NotNull(method);
        // Returns string? (nullable)
        Assert.True(method.ReturnType == typeof(string));
    }

    [Fact]
    public void VLCVariable_HasDestroyMethod()
    {
        var method = typeof(VLCVariable).GetMethod("Destroy");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    #endregion

    #region VLCPlayer Tests

    [Fact]
    public void VLCPlayer_ImplementsIDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(VLCPlayer)));
    }

    [Fact]
    public void VLCPlayer_HasCreateStaticMethod()
    {
        var method = typeof(VLCPlayer).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        // Returns VLCPlayer? (nullable)
        Assert.True(method.ReturnType == typeof(VLCPlayer));
    }

    [Fact]
    public void VLCPlayer_HasStateProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("State");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
    }

    [Fact]
    public void VLCPlayer_HasStateChangedEvent()
    {
        var eventInfo = typeof(VLCPlayer).GetEvent("StateChanged");
        Assert.NotNull(eventInfo);
    }

    [Fact]
    public void VLCPlayer_HasMediaChangedEvent()
    {
        var eventInfo = typeof(VLCPlayer).GetEvent("MediaChanged");
        Assert.NotNull(eventInfo);
    }

    [Fact]
    public void VLCPlayer_HasPositionChangedEvent()
    {
        var eventInfo = typeof(VLCPlayer).GetEvent("PositionChanged");
        Assert.NotNull(eventInfo);
    }

    [Fact]
    public void VLCPlayer_HasTimeProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("Time");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasTimeSpanProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("TimeSpan");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(TimeSpan), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasLengthProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("Length");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasLengthTimeSpanProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("LengthTimeSpan");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(TimeSpan), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasPositionProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("Position");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(double), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasCanSeekProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("CanSeek");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasCanPauseProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("CanPause");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasSeekByTimeMethod_WithLong()
    {
        var method = typeof(VLCPlayer).GetMethod("SeekByTime", [typeof(long), typeof(SeekSpeed), typeof(SeekWhence)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlayer_HasSeekByTimeMethod_WithTimeSpan()
    {
        var method = typeof(VLCPlayer).GetMethod("SeekByTime", [typeof(TimeSpan), typeof(SeekSpeed), typeof(SeekWhence)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlayer_HasSeekByPositionMethod()
    {
        var method = typeof(VLCPlayer).GetMethod("SeekByPosition", [typeof(double), typeof(SeekSpeed), typeof(SeekWhence)]);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlayer_HasPauseMethod()
    {
        var method = typeof(VLCPlayer).GetMethod("Pause");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlayer_HasResumeMethod()
    {
        var method = typeof(VLCPlayer).GetMethod("Resume");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlayer_HasVolumeProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("Volume");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal(typeof(float), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasIsMutedProperty()
    {
        var property = typeof(VLCPlayer).GetProperty("IsMuted");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal(typeof(bool?), property.PropertyType);
    }

    [Fact]
    public void VLCPlayer_HasToggleMuteMethod()
    {
        var method = typeof(VLCPlayer).GetMethod("ToggleMute");
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

    #region VLCPlaylist Tests

    [Fact]
    public void VLCPlaylist_ImplementsIDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(VLCPlaylist)));
    }

    [Fact]
    public void VLCPlaylist_HasCreateStaticMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(method.ReturnType == typeof(VLCPlaylist));
    }

    [Fact]
    public void VLCPlaylist_HasStartMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Start");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCPlaylist_HasStopMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Stop");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlaylist_HasPauseMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Pause");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlaylist_HasResumeMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Resume");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void VLCPlaylist_HasNextMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Next");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCPlaylist_HasPrevMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("Prev");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    [Fact]
    public void VLCPlaylist_HasCountProperty()
    {
        var property = typeof(VLCPlaylist).GetProperty("Count");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VLCPlaylist_HasCurrentIndexProperty()
    {
        var property = typeof(VLCPlaylist).GetProperty("CurrentIndex");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(long), property.PropertyType);
    }

    [Fact]
    public void VLCPlaylist_HasHasNextProperty()
    {
        var property = typeof(VLCPlaylist).GetProperty("HasNext");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VLCPlaylist_HasHasPrevProperty()
    {
        var property = typeof(VLCPlaylist).GetProperty("HasPrev");
        Assert.NotNull(property);
        Assert.True(property.CanRead);
        Assert.Equal(typeof(bool), property.PropertyType);
    }

    [Fact]
    public void VLCPlaylist_HasGoToMethod()
    {
        var method = typeof(VLCPlaylist).GetMethod("GoTo");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);
    }

    #endregion

}
