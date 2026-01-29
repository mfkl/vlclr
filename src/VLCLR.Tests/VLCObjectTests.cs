using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for VLCObject class API and architecture.
/// Note: These tests verify the API structure without calling into native code.
/// Integration tests require the actual VLC runtime or test harness.
/// </summary>
public class VLCObjectTests
{
    [Fact]
    public void VLCObject_Constructor_AcceptsNintParameter()
    {
        // Verify VLCObject can be constructed with a native pointer
        var obj = new VLCObject(nint.Zero);
        Assert.NotNull(obj);
    }

    [Fact]
    public void VLCObject_Handle_ReturnsConstructorValue()
    {
        nint testHandle = new nint(0x12345678);
        var obj = new VLCObject(testHandle);

        Assert.Equal(testHandle, obj.Handle);
    }

    [Fact]
    public void VLCObject_IsValid_ReturnsFalseForZeroHandle()
    {
        var obj = new VLCObject(nint.Zero);
        Assert.False(obj.IsValid);
    }

    [Fact]
    public void VLCObject_IsValid_ReturnsTrueForNonZeroHandle()
    {
        var obj = new VLCObject(new nint(1));
        Assert.True(obj.IsValid);
    }

    [Fact]
    public void VLCObject_GetParent_WithZeroHandle_ReturnsNull()
    {
        var obj = new VLCObject(nint.Zero);
        var parent = obj.GetParent();

        Assert.Null(parent);
    }

    [Fact]
    public void VLCObject_GetTypeName_WithZeroHandle_ReturnsNull()
    {
        var obj = new VLCObject(nint.Zero);
        var typeName = obj.GetTypeName();

        Assert.Null(typeName);
    }

    [Fact]
    public void VLCObject_StaticGetParent_WithZeroHandle_ReturnsZero()
    {
        nint result = VLCObject.GetParent(nint.Zero);
        Assert.Equal(nint.Zero, result);
    }

    [Fact]
    public void VLCObject_StaticGetTypeName_WithZeroHandle_ReturnsNull()
    {
        string? result = VLCObject.GetTypeName(nint.Zero);
        Assert.Null(result);
    }

    [Fact]
    public void VLCObject_HasGetRootMethod()
    {
        // Verify GetRoot method exists and returns VLCObject
        var method = typeof(VLCObject).GetMethod("GetRoot");
        Assert.NotNull(method);
        Assert.Equal(typeof(VLCObject), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public void VLCObject_GetRoot_WithZeroHandle_ReturnsSelf()
    {
        // When there's no parent (zero handle returns null parent),
        // GetRoot should return the object itself
        var obj = new VLCObject(nint.Zero);
        var root = obj.GetRoot();

        Assert.Same(obj, root);
    }
}
