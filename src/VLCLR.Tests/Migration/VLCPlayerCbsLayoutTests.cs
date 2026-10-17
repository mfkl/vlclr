using System.Reflection;
using System.Runtime.InteropServices;
using VLCLR.Native;
using Gen = VLCLR.Native.Interop;
using Xunit;

namespace VLCLR.Tests.Migration;

public class VLCPlayerCbsLayoutTests
{
    // Managed names that differ from the generated (C) slot names.
    private static readonly Dictionary<string, string> Renamed = new()
    {
        ["OnRecordChanged"] = "OnRecordingChanged",
        ["OnCorksChanged"] = "OnCorkChanged",
        ["OnSubitemsChanged"] = "OnMediaSubitemsChanged",
    };

    [Fact]
    public void Size_MatchesGenerated()
    {
        Assert.Equal(Gen.PlayerCbs.MaxSizeOf, Marshal.SizeOf<VLCPlayerCbs>());
        Assert.Equal(Gen.PlayerCbs.MaxSizeOf, System.Runtime.CompilerServices.Unsafe.SizeOf<VLCPlayerCbs>());
    }

    [Fact]
    public void EverySlot_OffsetMatchesGenerated()
    {
        FieldInfo[] managed = typeof(VLCPlayerCbs).GetFields(BindingFlags.Public | BindingFlags.Instance);
        FieldInfo[] generated = typeof(Gen.PlayerCbs.Offsets)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).ToArray();

        // One managed slot per C slot, none missing, none extra.
        Assert.Equal(generated.Length, managed.Length);
        Assert.Equal(37, managed.Length);

        foreach (FieldInfo field in managed)
        {
            string cName = Renamed.GetValueOrDefault(field.Name, field.Name);
            FieldInfo? slot = generated.SingleOrDefault(g => g.Name == cName);
            Assert.True(slot is not null, $"no generated slot for {field.Name}");
            Assert.Equal((int)slot!.GetRawConstantValue()!, Marshal.OffsetOf<VLCPlayerCbs>(field.Name).ToInt32());
        }
    }
}
