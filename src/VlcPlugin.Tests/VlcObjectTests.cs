using Xunit;

namespace VlcPlugin.Tests;

/// <summary>
/// Tests for VlcObject class API and architecture.
/// Note: These tests verify the API structure without calling into native code.
/// Integration tests require the actual VLC runtime or test harness.
/// </summary>
public class VlcObjectTests
{
    [Fact]
    public void VlcObject_Constructor_AcceptsNintParameter()
    {
        // Verify VlcObject can be constructed with a native pointer
        var obj = new VlcObject(nint.Zero);
        Assert.NotNull(obj);
    }

    [Fact]
    public void VlcObject_Handle_ReturnsConstructorValue()
    {
        nint testHandle = new nint(0x12345678);
        var obj = new VlcObject(testHandle);

        Assert.Equal(testHandle, obj.Handle);
    }

    [Fact]
    public void VlcObject_IsValid_ReturnsFalseForZeroHandle()
    {
        var obj = new VlcObject(nint.Zero);
        Assert.False(obj.IsValid);
    }

    [Fact]
    public void VlcObject_IsValid_ReturnsTrueForNonZeroHandle()
    {
        var obj = new VlcObject(new nint(1));
        Assert.True(obj.IsValid);
    }

    [Fact]
    public void VlcObject_GetParent_WithZeroHandle_ReturnsNull()
    {
        var obj = new VlcObject(nint.Zero);
        var parent = obj.GetParent();

        Assert.Null(parent);
    }

    [Fact]
    public void VlcObject_GetTypeName_WithZeroHandle_ReturnsNull()
    {
        var obj = new VlcObject(nint.Zero);
        var typeName = obj.GetTypeName();

        Assert.Null(typeName);
    }

    [Fact]
    public void VlcObject_StaticGetParent_WithZeroHandle_ReturnsZero()
    {
        nint result = VlcObject.GetParent(nint.Zero);
        Assert.Equal(nint.Zero, result);
    }

    [Fact]
    public void VlcObject_StaticGetTypeName_WithZeroHandle_ReturnsNull()
    {
        string? result = VlcObject.GetTypeName(nint.Zero);
        Assert.Null(result);
    }

    [Fact]
    public void VlcObject_HasGetRootMethod()
    {
        // Verify GetRoot method exists and returns VlcObject
        var method = typeof(VlcObject).GetMethod("GetRoot");
        Assert.NotNull(method);
        Assert.Equal(typeof(VlcObject), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public void VlcObject_GetRoot_WithZeroHandle_ReturnsSelf()
    {
        // When there's no parent (zero handle returns null parent),
        // GetRoot should return the object itself
        var obj = new VlcObject(nint.Zero);
        var root = obj.GetRoot();

        Assert.Same(obj, root);
    }
}
