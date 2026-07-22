using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Module;
using Xunit;

namespace VLCLR.Tests;

public unsafe class ModuleBuilderTests
{
    private static readonly List<int> s_properties = [];

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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int VlcSet(nint opaque, nint target, int property, nint* output)
    {
        s_properties.Add(property);
        if (property == VLCModuleConstants.VLC_MODULE_CREATE)
            *output = 456;

        return 0;
    }
}
