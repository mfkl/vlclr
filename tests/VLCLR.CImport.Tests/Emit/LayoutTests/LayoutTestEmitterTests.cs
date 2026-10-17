using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VLCLR.CImport.Emit;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Emit;

public class LayoutTestEmitterTests
{
    private static readonly LayoutTestOptions Options = new("VLCLR.Tests.Interop", "InteropLayoutTests");

    [Fact]
    public void MatchesGolden()
        => GoldenFile.Verify(Path.Combine("Golden", "LayoutTests", "interop_layout.golden.cs.txt"), EmitTests(Sample()));

    [Fact]
    public void OutputUsesLfAndCoversEmittedRecordsOnly()
    {
        var text = EmitTests(Sample());

        Assert.DoesNotContain("\r", text);
        Assert.Contains("public void Plane_Layout()", text);
        Assert.Contains("public void VideoFormatTDovi_Layout()", text);
        Assert.DoesNotContain("EsFormatTAnon0", text);
        Assert.DoesNotContain("ProbeOpaque", text);
        Assert.Contains("AssertEqual(Abi(16, 24), ProbeLong.SizeOf", text);
        Assert.Contains("AssertEqual(24, Unsafe.SizeOf<ProbeLong>()", text);
    }

    [Fact]
    public void GeneratedTestsPassAgainstContractStructs()
    {
        var merged = Sample();

        var results = Run(merged, merged);

        Assert.Equal(EmittedRecords.Select(merged).Count, results.Count);
        Assert.All(results, r => Assert.True(r.Error is null, $"{r.Name}: {r.Error}"));
    }

    [Fact]
    public void WrongOffsetFails()
    {
        var merged = Sample();
        var wrong = Mutate(merged, "plane_t", m => m.Name == "i_pitch" ? m with { Offset = new([4, 4]) } : m);

        var failures = Run(merged, wrong).Where(r => r.Error is not null).ToList();

        var failure = Assert.Single(failures);
        Assert.Equal("Plane_Layout", failure.Name);
        Assert.Contains("plane_t.i_pitch offset", failure.Error);
    }

    [Fact]
    public void WrongOffsetInsideAnonymousUnionFails()
    {
        var merged = Sample();
        var wrong = Mutate(merged, "es_format_t", m => m.Name == "i_codec_data" ? m with { Offset = new([20, 20]) } : m);

        var failure = Assert.Single(Run(merged, wrong), r => r.Error is not null);

        Assert.Contains("es_format_t.i_codec_data offset", failure.Error);
    }

    [Fact]
    public void WrongSizeFails()
    {
        var merged = Sample();
        var wrong = merged with
        {
            Types = [.. merged.Types.Select(t => t.Name == "probe_value_t" ? t with { Size = new([16, 16]) } : t)],
        };

        var failures = Run(merged, wrong).Where(r => r.Error is not null).ToList();

        Assert.Contains(failures, f => f.Name == "ProbeValue_Layout" && f.Error!.Contains("probe_value_t sizeof"));
    }

    [Fact]
    public void WrongBitfieldPositionFails()
    {
        var merged = Sample();
        var wrong = Mutate(merged, "probe_bits_t", m => m.Name == "b" ? m with { BitOffset = new([4, 4]) } : m);

        var failure = Assert.Single(Run(merged, wrong), r => r.Error is not null);

        Assert.Equal("ProbeBits_Layout", failure.Name);
        Assert.Contains("probe_bits_t.b", failure.Error);
    }

    [Fact]
    public void WrongBitfieldWidthFails()
    {
        var merged = Sample();
        var wrong = merged with
        {
            Types = [.. merged.Types.Select(t => t.Name != "probe_zoo_t" ? t : t with
            {
                Members = [.. t.Members.Select(m => m.Name == "f_ushort" ? m with { BitWidth = 6 } : m)],
            })],
        };

        var failure = Assert.Single(Run(merged, wrong), r => r.Error is not null);

        Assert.Contains("probe_zoo_t.f_ushort", failure.Error);
    }

    /// <summary>The probe sample IR plus a record that exercises every bitfield property kind.</summary>
    private static MergedSnapshot Sample()
    {
        var merged = ProbeSampleIr.Merged;
        var zoo = new MergedType
        {
            Name = "probe_zoo_t",
            Kind = RecordKind.Struct,
            Header = "probe_sample.h",
            Size = Uniform(16),
            Alignment = Uniform(8),
            Fields = ZooFields(),
            Members = ZooFields(),
        };
        return merged with { Types = [.. merged.Types, zoo] };

        static PerAbi<long> Uniform(long value) => new([value, value]);

        static MergedField Bits(string name, TypeRef type, long bit, int width) => new()
        {
            Name = name,
            DeclaringType = "probe_zoo_t",
            Type = type,
            Offset = Uniform(bit / 8),
            BitOffset = Uniform(bit),
            BitWidth = width,
        };

        static List<MergedField> ZooFields() =>
        [
            Bits("f_bool", TypeRef.Of(BuiltinType.Bool), 0, 1),
            Bits("f_enum", TypeRef.Enum("probe_color_e"), 1, 3),
            Bits("f_signed", TypeRef.Of(BuiltinType.Int), 4, 5),
            Bits("f_ushort", TypeRef.Of(BuiltinType.UShort), 9, 7),
            Bits("f_byte", TypeRef.Typedef("uint8_t", TypeRef.Of(BuiltinType.UChar)), 16, 8),
            Bits("f_wide", TypeRef.Typedef("uint64_t", TypeRef.Of(BuiltinType.ULongLong)), 24, 40),
            Bits("f_sbyte", TypeRef.Of(BuiltinType.SChar), 64, 4),
            Bits("f_char", TypeRef.Of(BuiltinType.Char), 68, 4),
            new MergedField { Name = "i_tail", DeclaringType = "probe_zoo_t", Type = TypeRef.Of(BuiltinType.Int), Offset = Uniform(12) },
        ];
    }

    private static MergedSnapshot Mutate(MergedSnapshot merged, string type, Func<MergedField, MergedField> change)
        => merged with
        {
            Types = [.. merged.Types.Select(t => t.Name != type ? t : t with { Members = [.. t.Members.Select(change)] })],
        };

    private static string EmitTests(MergedSnapshot merged) => new LayoutTestEmitter(merged, new TestNameMap(), Options).Emit();

    private sealed record FactResult(string Name, string? Error);

    /// <summary>Emits tests from <paramref name="expected"/>, structs from <paramref name="actual"/>, runs every fact.</summary>
    private static List<FactResult> Run(MergedSnapshot expected, MergedSnapshot actual)
    {
        var names = new TestNameMap();
        var assembly = Compile(EmitTests(expected), ContractStructs.Generate(actual, names));
        var type = assembly.GetType($"{Options.Namespace}.{Options.ClassName}")!;
        var instance = Activator.CreateInstance(type)!;

        var results = new List<FactResult>();
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() is not null).OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            try
            {
                method.Invoke(instance, null);
                results.Add(new(method.Name, null));
            }
            catch (TargetInvocationException ex)
            {
                results.Add(new(method.Name, ex.InnerException!.Message));
            }
        }
        return results;
    }

    private static Assembly Compile(params string[] sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Assert).Assembly.Location)
            .Distinct()
            .Select(p => MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "InteropLayout_" + Guid.NewGuid().ToString("N"),
            sources.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)) + "\n--- sources ---\n" + string.Join("\n=====\n", sources));

        stream.Position = 0;
        return new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(stream);
    }
}
