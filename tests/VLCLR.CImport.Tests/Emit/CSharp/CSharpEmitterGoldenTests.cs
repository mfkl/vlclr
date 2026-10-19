using System.Text;
using VLCLR.CImport.Emit;
using VLCLR.CImport.Tests.Emit.CSharp.Cases;
using VLCLR.Tests.Interop;

namespace VLCLR.CImport.Tests.Emit.CSharp;

public class CSharpEmitterGoldenTests
{
    private const string SkippedFile = "skipped.txt";

    // Golden C# is stored as *.g.cs.golden so the test project does not compile it.
    private const string GoldenExtension = ".golden";

    public static TheoryData<string> Cases => new(CSharpCases.Names);

    internal static CSharpEmitResult Emit(string name)
        => new CSharpEmitter(CSharpCases.Merged(name), CSharpCases.NameMap(name), new CSharpEmitOptions("libvlccore")).Emit();

    [Theory]
    [MemberData(nameof(Cases))]
    public void Output_matches_golden(string name)
    {
        var result = Emit(name);
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, text) in result.Files)
            actual[file] = text;
        actual[SkippedFile] = RenderSkipped(result.Skipped);

        if (Environment.GetEnvironmentVariable("CIMPORT_UPDATE_GOLDEN") == "1")
        {
            var directory = Path.Combine(SourceGoldenRoot(), name);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            Directory.CreateDirectory(directory);
            foreach (var (file, text) in actual)
                File.WriteAllText(Path.Combine(directory, file + GoldenExtension), text, new UTF8Encoding(false));
            return;
        }

        var goldenDirectory = Path.Combine(AppContext.BaseDirectory, "Golden", "CSharp", name);
        Assert.True(Directory.Exists(goldenDirectory), $"No golden files for '{name}'. Run with CIMPORT_UPDATE_GOLDEN=1.");
        var expectedFiles = Directory.GetFiles(goldenDirectory).Select(f => Path.GetFileName(f)![..^GoldenExtension.Length]).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expectedFiles, actual.Keys.ToList());
        foreach (var (file, text) in actual)
            Assert.Equal(File.ReadAllText(Path.Combine(goldenDirectory, file + GoldenExtension)).ReplaceLineEndings("\n"), text);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Output_compiles_without_warnings(string name)
    {
        var result = CSharpCompilationHelper.Compile(Emit(name).Files);
        Assert.True(result.Diagnostics.Count == 0, "Generated code does not compile cleanly:\n" + string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void No_record_whose_size_differs_is_held_or_passed_by_value(string name)
    {
        var assembly = CSharpCompilationHelper.Compile(Emit(name).Files).Load();

        Assert.Equal([], AbiSizedRecords.Violations(assembly.GetTypes(), AbiSizedRecords.IsGeneratedAbiSized));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_accessor_that_picks_a_per_abi_value_is_inlined(string name)
    {
        // Native AOT folds OperatingSystem.IsWindows() to a constant only where the accessor is inlined.
        var notInlined = Emit(name).Files
            .SelectMany(f => f.Value.Split('\n').Select(line => (File: f.Key, Line: line.Trim())))
            .Where(l => l.Line.Contains("OperatingSystem.IsWindows()", StringComparison.Ordinal)
                && !l.Line.Contains("[MethodImpl(MethodImplOptions.AggressiveInlining)]", StringComparison.Ordinal))
            .Select(l => $"{l.File}: {l.Line}")
            .ToList();

        Assert.Equal([], notInlined);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Output_is_deterministic_with_lf_line_endings(string name)
    {
        var first = Emit(name);
        var second = Emit(name);
        Assert.Equal(first.Files.Keys, second.Files.Keys);
        foreach (var (file, text) in first.Files)
        {
            Assert.Equal(text, second.Files[file]);
            Assert.DoesNotContain('\r', text);
            Assert.EndsWith("\n", text);
        }
        Assert.Equal(first.Files.Keys.Order(StringComparer.Ordinal), first.Files.Keys);
        Assert.Equal(first.Skipped, second.Skipped);
    }

    private static string RenderSkipped(IEnumerable<SkippedSymbol> skipped)
        => string.Concat(skipped.Select(s => $"{s.Header}\t{s.Kind}\t{s.Symbol}\t{s.Reason}\n"));

    private static string SourceGoldenRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VLCLR.CImport.Tests.csproj")))
                return Path.Combine(directory.FullName, "Golden", "CSharp");
        }
        throw new InvalidOperationException("Cannot find the test project directory.");
    }
}
