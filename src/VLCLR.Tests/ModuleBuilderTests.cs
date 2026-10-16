using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Module;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Drives <see cref="ModuleBuilder"/> against a fake vlc_set that receives its
/// arguments where a variadic C callee finds them on the running ABI, and
/// keeps config items the way VLC does: a module_config_t at offset 24 of a
/// struct vlc_param.
/// </summary>
public unsafe class ModuleBuilderTests : IDisposable
{
    private record struct Call(nint Target, int Property, byte TargetType, long First, long Second);

    private static readonly List<Call> s_calls = [];
    private static byte* s_configs;
    private static int s_configCount;

    public ModuleBuilderTests()
    {
        s_calls.Clear();
        s_configs = (byte*)NativeMemory.AllocZeroed(16, 64);
        s_configCount = 0;
    }

    public void Dispose() => NativeMemory.Free(s_configs);

    [Fact]
    public void WithNoUnload_SendsNoUnloadFlagToVlc()
    {
        var result = ModuleBuilder.Create(FakeVlcSet, 123)
            .WithNoUnload()
            .Register();

        Assert.Equal(0, result);
        Assert.Equal(
            [VLCModuleConstants.VLC_MODULE_CREATE, VLCModuleConstants.VLC_MODULE_NO_UNLOAD],
            s_calls.Select(c => c.Property));
    }

    [Fact]
    public void WithShortcut_SendsShortcutAfterModuleCreation()
    {
        var result = ModuleBuilder.Create(FakeVlcSet, 123)
            .WithShortcut("select-me")
            .Register();

        Assert.Equal(0, result);
        Assert.Equal(
            [VLCModuleConstants.VLC_MODULE_CREATE, VLCModuleConstants.VLC_MODULE_SHORTCUT],
            s_calls.Select(c => c.Property));
        Assert.Equal(1, s_calls[1].First);
    }

    [Fact]
    public void RangedConfigs_ApplyRangeToCreatedConfigItem()
    {
        var result = ModuleBuilder.Create(FakeVlcSet, 123)
            .AddIntegerConfig("integer", 5, 0, 10, "Integer")
            .AddFloatConfig("float", 0.5, 0.0, 1.0, "Float")
            .Register();

        Assert.Equal(0, result);
        Assert.Equal(
            [Config(0), Config(1)],
            s_calls.Where(c => c.Property == VLCModuleConstants.VLC_CONFIG_RANGE).Select(c => c.Target));
        Assert.Contains(s_calls, c => c.Property == VLCModuleConstants.VLC_CONFIG_RANGE && c.First == 0 && c.Second == 10);
    }

    [Fact]
    public void FloatConfig_PassesFloatBitsThroughTheIntegerPath()
    {
        var result = ModuleBuilder.Create(FakeVlcSet, 123)
            .AddFloatConfig("float", 0.5, 0.25, 0.75, "Float")
            .Register();

        Assert.Equal(0, result);
        Call value = s_calls.Single(c => c.Property == VLCModuleConstants.VLC_CONFIG_VALUE);
        Call range = s_calls.Single(c => c.Property == VLCModuleConstants.VLC_CONFIG_RANGE);
        Assert.Equal(VLCConfigTypes.CONFIG_ITEM_INTEGER, value.TargetType);
        Assert.Equal(FloatBits(0.5f), value.First);
        Assert.Equal(VLCConfigTypes.CONFIG_ITEM_INTEGER, range.TargetType);
        Assert.Equal((FloatBits(0.25f), FloatBits(0.75f)), (range.First, range.Second));
        Assert.Equal(VLCConfigTypes.CONFIG_ITEM_FLOAT, *(byte*)(Config(0) + 24));
    }

    private static long FloatBits(float value) => (uint)BitConverter.SingleToInt32Bits(value);

    private static nint Config(int index) => (nint)(s_configs + index * 64);

    private static nint FakeVlcSet => VLCVariadic.IsAppleArm64
        ? (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, nint, nint, nint, nint, long, long, int>)&VlcSetAppleArm64
        : (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, long, long, int>)&VlcSet;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int VlcSet(nint opaque, nint target, int property, long first, long second)
        => Handle(target, property, first, second);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int VlcSetAppleArm64(
        nint opaque, nint target, int property, nint x3, nint x4, nint x5, nint x6, nint x7, long first, long second)
        => Handle(target, property, first, second);

    private static int Handle(nint target, int property, long first, long second)
    {
        bool isConfig = target >= (nint)s_configs && target < Config(16);
        s_calls.Add(new Call(target, property, isConfig ? *(byte*)(target + 24) : (byte)0, first, second));
        if (property == VLCModuleConstants.VLC_MODULE_CREATE)
        {
            *(nint*)first = 456;
        }
        else if (property == VLCModuleConstants.VLC_CONFIG_CREATE)
        {
            nint config = Config(s_configCount++);
            *(byte*)(config + 24) = (byte)first;
            *(nint*)second = config;
        }
        else if (property == VLCModuleConstants.VLC_CONFIG_NAME)
        {
            *(nint*)(target + 24 + 16) = (nint)first;
        }
        return 0;
    }
}
