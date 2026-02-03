using System.Runtime.InteropServices;
using VLCLR.Module;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Tests for FilterOpsBuilder and FilterOps classes.
/// Verifies builder pattern, pinning, and memory management.
/// </summary>
public class FilterOpsBuilderTests : IDisposable
{
    private readonly List<FilterOpsBuilder> _builders = new();

    public void Dispose()
    {
        foreach (var builder in _builders)
        {
            builder.Dispose();
        }
    }

    #region Builder Pattern Tests

    [Fact]
    public void Create_ReturnsNewBuilder()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        Assert.NotNull(builder);
    }

    [Fact]
    public unsafe void WithFilterVideo_SetsCallback()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        // Use a dummy function pointer
        delegate* unmanaged[Cdecl]<nint, nint, nint> callback = &DummyFilterVideo;
        builder.WithFilterVideo(callback);

        // Build and verify pointer is accessible
        builder.Build();
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    [Fact]
    public unsafe void WithClose_SetsCallback()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        delegate* unmanaged[Cdecl]<nint, void> callback = &DummyClose;
        builder.WithClose(callback);

        builder.Build();
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    [Fact]
    public unsafe void WithFlush_SetsCallback()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        delegate* unmanaged[Cdecl]<nint, void> callback = &DummyFlush;
        builder.WithFlush(callback);

        builder.Build();
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    [Fact]
    public unsafe void WithDrain_SetsCallback()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        delegate* unmanaged[Cdecl]<nint, nint> callback = &DummyDrain;
        builder.WithDrain(callback);

        builder.Build();
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    [Fact]
    public void WithChangeViewpoint_SetsCallback()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        builder.WithChangeViewpoint((nint)0x12345678);

        builder.Build();
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    [Fact]
    public void WithVideoMouse_SetsCallback()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        builder.WithVideoMouse(unchecked((nint)0xDEADBEEF));

        builder.Build();
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public unsafe void Methods_ReturnBuilderForChaining()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        delegate* unmanaged[Cdecl]<nint, nint, nint> filterCallback = &DummyFilterVideo;
        delegate* unmanaged[Cdecl]<nint, void> closeCallback = &DummyClose;
        delegate* unmanaged[Cdecl]<nint, void> flushCallback = &DummyFlush;
        delegate* unmanaged[Cdecl]<nint, nint> drainCallback = &DummyDrain;

        var result = builder
            .WithFilterVideo(filterCallback)
            .WithClose(closeCallback)
            .WithFlush(flushCallback)
            .WithDrain(drainCallback)
            .WithChangeViewpoint(nint.Zero)
            .WithVideoMouse(nint.Zero)
            .Build();

        Assert.Same(builder, result);
    }

    #endregion

    #region Build Tests

    [Fact]
    public void Build_PinsStructure()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        builder.Build();

        // Pointer should be valid and non-zero
        Assert.NotEqual(nint.Zero, builder.Pointer);
    }

    [Fact]
    public void Build_CalledTwice_ThrowsInvalidOperationException()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Pointer_BeforeBuild_ThrowsInvalidOperationException()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        Assert.Throws<InvalidOperationException>(() => _ = builder.Pointer);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var builder = FilterOpsBuilder.Create();
        builder.Build();

        // Should not throw
        builder.Dispose();
        builder.Dispose();
        builder.Dispose();
    }

    [Fact]
    public void Dispose_BeforeBuild_NoOp()
    {
        var builder = FilterOpsBuilder.Create();

        // Should not throw
        builder.Dispose();
    }

    #endregion

    #region Memory Layout Tests

    [Fact]
    public unsafe void Build_CreatesValidVLCFilterOperationsLayout()
    {
        var builder = FilterOpsBuilder.Create();
        _builders.Add(builder);

        delegate* unmanaged[Cdecl]<nint, nint, nint> filterCallback = &DummyFilterVideo;
        delegate* unmanaged[Cdecl]<nint, void> closeCallback = &DummyClose;

        builder
            .WithFilterVideo(filterCallback)
            .WithClose(closeCallback)
            .Build();

        // Read back the structure and verify callbacks are set
        ref VLCFilterOperations ops = ref *(VLCFilterOperations*)builder.Pointer;

        Assert.Equal((nint)filterCallback, ops.FilterVideo);
        Assert.Equal((nint)closeCallback, ops.Close);
    }

    #endregion

    #region FilterOps Static Helper Tests

    [Fact]
    public unsafe void FilterOps_CreateVideoFilter_ReturnsValidPointer()
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint> filterCallback = &DummyFilterVideo;

        nint opsPtr = FilterOps.CreateVideoFilter(filterCallback);

        Assert.NotEqual(nint.Zero, opsPtr);

        // Verify callback is set
        ref VLCFilterOperations ops = ref *(VLCFilterOperations*)opsPtr;
        Assert.Equal((nint)filterCallback, ops.FilterVideo);
    }

    [Fact]
    public unsafe void FilterOps_CreateVideoFilter_WithClose_ReturnsValidPointer()
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint> filterCallback = &DummyFilterVideo;
        delegate* unmanaged[Cdecl]<nint, void> closeCallback = &DummyClose;

        nint opsPtr = FilterOps.CreateVideoFilter(filterCallback, closeCallback);

        Assert.NotEqual(nint.Zero, opsPtr);

        // Verify callbacks are set
        ref VLCFilterOperations ops = ref *(VLCFilterOperations*)opsPtr;
        Assert.Equal((nint)filterCallback, ops.FilterVideo);
        Assert.Equal((nint)closeCallback, ops.Close);
    }

    [Fact]
    public unsafe void FilterOps_CreateVideoFilterFull_ReturnsValidPointer()
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint> filterCallback = &DummyFilterVideo;
        delegate* unmanaged[Cdecl]<nint, void> closeCallback = &DummyClose;
        delegate* unmanaged[Cdecl]<nint, void> flushCallback = &DummyFlush;
        delegate* unmanaged[Cdecl]<nint, nint> drainCallback = &DummyDrain;

        nint opsPtr = FilterOps.CreateVideoFilterFull(filterCallback, closeCallback, flushCallback, drainCallback);

        Assert.NotEqual(nint.Zero, opsPtr);

        // Verify all callbacks are set
        ref VLCFilterOperations ops = ref *(VLCFilterOperations*)opsPtr;
        Assert.Equal((nint)filterCallback, ops.FilterVideo);
        Assert.Equal((nint)closeCallback, ops.Close);
        Assert.Equal((nint)flushCallback, ops.Flush);
        Assert.Equal((nint)drainCallback, ops.Drain);
    }

    #endregion

    #region Dummy Callbacks

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static nint DummyFilterVideo(nint filter, nint picture) => picture;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void DummyClose(nint filter) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void DummyFlush(nint filter) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static nint DummyDrain(nint filter) => nint.Zero;

    #endregion
}
