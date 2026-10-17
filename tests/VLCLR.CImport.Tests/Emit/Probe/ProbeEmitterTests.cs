using VLCLR.CImport.Emit;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Emit;

public class ProbeEmitterTests
{
    private static readonly ProbeOptions Options = new(
        ["probe_sample.h"],
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Abis.Windows] = ["#define PROBE_ABI_WINDOWS 1"],
            [Abis.SysV] = ["#define PROBE_ABI_SYSV 1"],
        });

    private static readonly string HeaderDirectory = Path.Combine(GoldenFile.TestProjectDirectory, "Emit", "Probe", "Headers");

    [Fact]
    public void MatchesGolden()
        => GoldenFile.Verify(Path.Combine("Golden", "Probe", "probe_sample.golden.cpp"), Emit(ProbeSampleIr.Merged));

    [Fact]
    public void OutputIsDeterministicWithLfLineEndings()
    {
        var merged = ProbeSampleIr.Merged;
        var reversed = merged with { Types = [.. merged.Types.Reverse()], Enums = [.. merged.Enums.Reverse()], Constants = [.. merged.Constants.Reverse()] };

        var text = Emit(merged);

        Assert.Equal(text, Emit(reversed));
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void CoversEmittedRecordsOnly()
    {
        var text = Emit(ProbeSampleIr.Merged);

        Assert.Contains("VLCLR_OFFSET(cimport_video_format_t_dovi, \"video_format_t::dovi\", version_minor, 1);", text);
        Assert.Contains("VLCLR_OFFSET(es_format_t, \"es_format_t\", i_codec_data, 16);", text);
        Assert.Contains("VLCLR_SIZE(probe_long_t, \"probe_long_t\", VLCLR_ABI(16, 24));", text);
        Assert.Contains("VLCLR_OFFSET(probe_bits_t, \"probe_bits_t\", c, 4);", text);
        Assert.DoesNotContain("es_format_t::@0", text.Replace("// es_format_t::@0", ""));
        Assert.DoesNotContain("probe_opaque_t", text);
        Assert.DoesNotContain(", profile,", text);
        Assert.DoesNotContain(", a,", text);
    }

    [Fact]
    public void ProbeCompilesWithMsvcOnWindows()
    {
        var result = Compile(ProbeSampleIr.Merged, NativeToolchains.CompileWithMsvc);
        if (result is null)
            return; // MSVC not installed

        Assert.True(result.Succeeded, result.Output);
    }

    [Fact]
    public void ProbeCompilesWithGccOnSysV()
    {
        var result = Compile(ProbeSampleIr.Merged, NativeToolchains.CompileWithGcc);
        if (result is null)
            return; // no WSL with g++

        Assert.True(result.Succeeded, result.Output);
    }

    [Fact]
    public void WrongOffsetFailsMsvcCompilationNamingTheMember()
    {
        var wrong = ProbeSampleIr.WithOffset(ProbeSampleIr.Merged, "plane_t", "i_pitch", 16);

        var result = Compile(wrong, NativeToolchains.CompileWithMsvc);
        if (result is null)
            return;

        Assert.False(result.Succeeded);
        Assert.Contains("VLCLR offset mismatch: plane_t.i_pitch", result.Output);
    }

    [Fact]
    public void WrongOffsetFailsGccCompilationNamingTheMember()
    {
        var wrong = ProbeSampleIr.WithOffset(ProbeSampleIr.Merged, "es_format_t", "i_bitrate", 36);

        var result = Compile(wrong, NativeToolchains.CompileWithGcc);
        if (result is null)
            return;

        Assert.False(result.Succeeded);
        Assert.Contains("VLCLR offset mismatch: es_format_t.i_bitrate", result.Output);
    }

    [Fact]
    public void WrongEnumValueFailsCompilation()
    {
        var merged = ProbeSampleIr.Merged;
        var wrong = merged with
        {
            Enums = [.. merged.Enums.Select(e => e.Name != "probe_color_e" ? e : e with
            {
                Members = [.. e.Members.Select(m => m.Name != "PROBE_NEG" ? m : m with { Value = new([-3, -3]) })],
            })],
        };

        var result = Compile(wrong, NativeToolchains.CompileWithMsvc);
        if (result is null)
            return;

        Assert.False(result.Succeeded);
        Assert.Contains("VLCLR value mismatch: PROBE_NEG", result.Output);
    }

    private static string Emit(MergedSnapshot merged) => new ProbeEmitter(merged, Options).Emit();

    private static CompileResult? Compile(MergedSnapshot merged, Func<string, string, CompileResult?> compiler)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cimport-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "probe.cpp");
            File.WriteAllText(source, Emit(merged));
            return compiler(source, HeaderDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
