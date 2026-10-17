using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Model;

public class AbiMergerTests
{
    private static MergedSnapshot Merged => AbiMerger.Merge(SampleSnapshots.Windows, SampleSnapshots.SysV);

    [Fact]
    public void SizesAndOffsetsArePerAbi()
    {
        var format = Merged.FindType("video_format_t")!;

        Assert.Equal([24L, 16L], format.Size!.Values);
        Assert.False(format.Size.IsUniform);
        var chroma = format.Fields.Single(f => f.Name == "i_chroma");
        Assert.True(chroma.Offset.IsUniform);
        Assert.Equal(0, chroma.Offset.Uniform);
        var padding = format.Fields.Single(f => f.Name == "i_cubemap_padding");
        Assert.Equal((12L, 8L), (padding.Offset.Windows, padding.Offset.SysV));
    }

    [Fact]
    public void BitfieldsKeepPerAbiBitOffsets()
    {
        var level = Merged.FindType("video_format_t::dovi")!.Fields.Single(f => f.Name == "level");

        Assert.True(level.IsBitfield);
        Assert.Equal(6, level.BitWidth);
        Assert.Equal([39L, 23L], level.BitOffset!.Values);
    }

    [Fact]
    public void AnonymousMembersAreFlattenedWithAbsoluteOffsets()
    {
        var es = Merged.FindType("es_format_t")!;

        Assert.Equal(["i_cat", "", "p_extra"], es.Fields.Select(f => f.Name));
        Assert.Equal(["i_cat", "video", "i_codec_data", "p_extra"], es.Members.Select(f => f.Name));
        var video = es.Members.Single(f => f.Name == "video");
        Assert.Equal("es_format_t::@0", video.DeclaringType);
        Assert.Equal(8, video.Offset.Uniform);
        Assert.Equal("es_format_t", es.Members.Single(f => f.Name == "p_extra").DeclaringType);
    }

    [Fact]
    public void TypedefTargetsMayDifferInCanonicalBuiltin()
    {
        var size = Merged.FindType("video_format_t")!.Fields.Single(f => f.Name == "i_size");

        Assert.False(size.TypeDiffers);
        Assert.False(Merged.Functions.Single(f => f.Name == "vlc_tick_now").SignatureDiffers);
        Assert.DoesNotContain(Merged.Diagnostics, d => d.Symbol.StartsWith("video_format_t", StringComparison.Ordinal));
    }

    [Fact]
    public void DivergentValuesBecomeWarnings()
    {
        var merged = Merged;

        Assert.Contains(merged.Diagnostics, d => d is { Severity: DiagnosticSeverity.Warning, Symbol: "es_format_category_e" });
        Assert.Contains(merged.Diagnostics, d => d is { Severity: DiagnosticSeverity.Warning, Symbol: "DIR_SEP" });
        Assert.Equal(["\\", "/"], merged.Constants.Single(c => c.Name == "DIR_SEP").String!.Values);
        Assert.DoesNotContain(merged.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SymbolOnOneAbiOnlyIsAnError()
    {
        var sysv = SampleSnapshots.SysV;
        sysv = sysv with { Types = [.. sysv.Types.Where(t => t.Name != "vlc_filter_operations")] };

        var merged = AbiMerger.Merge(SampleSnapshots.Windows, sysv);

        Assert.Null(merged.FindType("vlc_filter_operations"));
        var error = Assert.Single(merged.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("vlc_filter_operations", error.Symbol);
        Assert.Contains("sysv", error.Message);
    }

    [Fact]
    public void SignatureShapeChangeIsAWarning()
    {
        var sysv = SampleSnapshots.SysV;
        sysv = sysv with
        {
            Functions = [.. sysv.Functions.Select(f => f.Name == "vlc_tick_now" ? f with { ReturnType = TypeRef.Of(BuiltinType.Int) } : f)],
        };

        var merged = AbiMerger.Merge(SampleSnapshots.Windows, sysv);

        Assert.True(merged.Functions.Single(f => f.Name == "vlc_tick_now").SignatureDiffers);
    }

    [Fact]
    public void DifferentCommitsAreRejected()
    {
        var sysv = SampleSnapshots.SysV with { VlcCommit = "deadbeef" };

        Assert.Throws<InvalidOperationException>(() => AbiMerger.Merge(SampleSnapshots.Windows, sysv));
    }

    [Fact]
    public void AbisAreRecordedInOrder()
    {
        Assert.Equal([Abis.Windows, Abis.SysV], Merged.Abis);
        Assert.Equal("dc69496e", Merged.VlcCommit);
    }

    [Fact]
    public void SkippedDeclarationsAreUnitedWithTheirAbis()
    {
        var skipped = Merged.Skipped;

        Assert.Equal(["VLC_TICK_FROM_SEC", "vlc_va_list_fn"], skipped.Select(s => s.Declaration.Name));
        Assert.Equal([Abis.Windows, Abis.SysV], skipped[0].Abis);
        Assert.Equal([Abis.SysV], skipped[1].Abis);
    }
}
