using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Module;
using Xunit;

namespace VLCLR.Tests;

public unsafe class ModuleBuilderTests
{
    private static readonly List<int> s_properties = [];
    private static readonly List<nint> s_rangeTargets = [];
    private static nint s_nextConfig;

    [Fact]
    public void WithNoUnload_SendsNoUnloadFlagToVlc()
    {
        s_properties.Clear();

        var result = ModuleBuilder
            .Create((nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint*, int>)&VlcSet, 123)
            .WithNoUnload()
            .Register();

        Assert.Equal(0, result);
        Assert.Equal(
            [VLCModuleConstants.VLC_MODULE_CREATE, VLCModuleConstants.VLC_MODULE_NO_UNLOAD],
            s_properties);
    }

    [Fact]
    public void WithShortcut_SendsShortcutAfterModuleCreation()
    {
        s_properties.Clear();

        var result = ModuleBuilder
            .Create((nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint*, int>)&VlcSet, 123)
            .WithShortcut("select-me")
            .Register();

        Assert.Equal(0, result);
        Assert.Equal(
            [VLCModuleConstants.VLC_MODULE_CREATE, VLCModuleConstants.VLC_MODULE_SHORTCUT],
            s_properties);
    }

    [Fact]
    public void RangedConfigs_ApplyRangeToCreatedConfigItem()
    {
        s_rangeTargets.Clear();
        s_nextConfig = 1_000;

        var result = ModuleBuilder
            .Create((nint)(delegate* unmanaged[Cdecl]<nint, nint, int, nint, nint, int>)&VlcSetWithConfig, 123)
            .AddIntegerConfig("integer", 5, 0, 10, "Integer")
            .AddFloatConfig("float", 0.5, 0.0, 1.0, "Float")
            .Register();

        Assert.Equal(0, result);
        Assert.Equal([1_000, 1_001], s_rangeTargets);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int VlcSet(nint opaque, nint target, int property, nint* output)
    {
        s_properties.Add(property);
        if (property == VLCModuleConstants.VLC_MODULE_CREATE)
            *output = 456;

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int VlcSetWithConfig(nint opaque, nint target, int property, nint arg1, nint arg2)
    {
        if (property == VLCModuleConstants.VLC_MODULE_CREATE)
        {
            *(nint*)arg1 = 456;
        }
        else if (property == VLCModuleConstants.VLC_CONFIG_CREATE)
        {
            *(nint*)arg2 = s_nextConfig++;
        }
        else if (property == VLCModuleConstants.VLC_CONFIG_RANGE)
        {
            s_rangeTargets.Add(target);
        }

        return 0;
    }
}
