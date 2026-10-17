using System.Globalization;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Emit;

/// <summary>One run of the C# emitter: collects symbols per header, then renders the files.</summary>
internal sealed partial class CSharpGeneration
{
    private readonly MergedSnapshot merged;
    private readonly INameMap names;
    private readonly CSharpEmitOptions options;
    private readonly List<SkippedSymbol> skipped = [];
    private readonly SortedDictionary<string, HeaderUnit> headers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> memberNames = new(StringComparer.Ordinal);

    private Dictionary<string, MergedType> records = null!;
    private Dictionary<string, CSharpEnumInfo> enums = null!;
    private Dictionary<string, string> recordTypeNames = null!;
    private HashSet<string> enumTypeNames = null!;
    private CSharpTypeMapper mapper = null!;

    public CSharpGeneration(MergedSnapshot merged, INameMap names, CSharpEmitOptions options)
    {
        this.merged = merged;
        this.names = names;
        this.options = options;
    }

    public CSharpEmitResult Run()
    {
        ThrowOnMergeErrors();
        if (!merged.Abis.SequenceEqual(Abis.All))
            throw new InvalidOperationException($"The C# emitter needs the ABIs {string.Join(", ", Abis.All)}, got {string.Join(", ", merged.Abis)}.");

        enums = ResolveEnums();
        records = SelectRecords();
        mapper = new CSharpTypeMapper(records, enums, names);
        recordTypeNames = records.Values.ToDictionary(r => names.Type(r.Name), r => r.Name, StringComparer.Ordinal);
        enumTypeNames = [.. enums.Values.Where(e => e.TypeName is not null).Select(e => e.TypeName!)];
        CheckTypeNames();

        foreach (var record in records.Values.OrderBy(r => r.Name, StringComparer.Ordinal))
            Unit(record.Header).Records.Add((record.Name, EmitRecord(record)));

        foreach (var info in enums.Values.OrderBy(e => e.Source.Name, StringComparer.Ordinal))
            EmitEnum(info);

        foreach (var constant in merged.Constants.OrderBy(c => c.Name, StringComparer.Ordinal))
            EmitConstant(constant);

        foreach (var function in merged.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
            EmitFunction(function);

        return new CSharpEmitResult(RenderFiles(), [.. skipped
            .OrderBy(s => s.Header, StringComparer.Ordinal)
            .ThenBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Symbol, StringComparer.Ordinal)]);
    }

    private void ThrowOnMergeErrors()
    {
        var errors = merged.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "The ABI snapshots do not merge cleanly:" + string.Concat(errors.Select(e => "\n  " + e)));
    }

    private void CheckTypeNames()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (typeName, cName) in recordTypeNames.Select(p => (p.Key, p.Value)).Concat(enums.Values.Where(e => e.TypeName is not null).Select(e => (e.TypeName!, e.Source.Name))))
        {
            if (seen.TryGetValue(typeName, out var other))
                throw new InvalidOperationException($"C# type '{typeName}' is produced by both '{other}' and '{cName}'.");
            seen[typeName] = cName;
        }
    }

    private void Skip(string symbol, string header, string kind, string reason)
        => skipped.Add(new SkippedSymbol(symbol, header, kind, reason));

    private HeaderUnit Unit(string header)
    {
        if (!headers.TryGetValue(header, out var unit))
            headers[header] = unit = new HeaderUnit(header);
        return unit;
    }

    // ---- constants -------------------------------------------------------------------------------

    private void EmitConstant(MergedConstant c)
    {
        var line = c.Kind switch
        {
            ConstantKind.Integer => IntegerConstant(c),
            ConstantKind.Float => FloatConstant(c),
            _ => StringConstant(c),
        };
        if (line.Reason is { } reason)
        {
            Skip(c.Name, c.Header, "constant", reason);
            return;
        }

        var q = names.Constant(c.Name);
        AddConstant(c.Header, q, c.Name, line.Type!, line.Values!);
    }

    private void AddConstant(string header, QualifiedName q, string cName, string type, PerAbi<string> values)
    {
        RegisterMember(q.Class, q.Member, cName);
        var lines = new List<string> { $"/// <summary>{CSharpLiterals.Xml(cName)}</summary>" };
        lines.Add(values.IsUniform
            ? $"public const {type} {q.Member} = {values[0]};"
            : Getter($"public static {type} {q.Member}", $"OperatingSystem.IsWindows() ? {values.Windows} : {values.SysV}"));
        Class(header, q.Class).Entries.Add((0, cName, lines));
    }

    private readonly record struct ConstantText(string? Type, PerAbi<string>? Values, string? Reason);

    private static ConstantText Fail(string reason) => new(null, null, reason);

    private ConstantText IntegerConstant(MergedConstant c)
    {
        var types = c.Type.Values.Select(t => t.Desugar()).ToList();
        if (types.Any(t => t.Kind != TypeRefKind.Builtin))
            return Fail("integer constant with a non-builtin type");
        var csTypes = types.Select(t => ConstantType(t.Builtin!.Value)).Distinct().ToList();
        if (csTypes.Count != 1 || csTypes[0] is null)
            return Fail($"integer constant type {string.Join(" | ", types.Select(t => t.Builtin))} is not supported or differs between ABIs");
        var type = csTypes[0]!;
        if (c.Integer!.Values.Any(v => v is null))
            return Fail("integer constant without a value");

        var values = new List<string>();
        foreach (var v in c.Integer.Values)
        {
            var text = IntegerLiteral(type, v!.Value, c.Integer.IsUniform ? Suffix.None : Suffix.Typed);
            if (text is null)
                return Fail($"value {v} does not fit {type}");
            values.Add(text);
        }
        return new(type, new PerAbi<string>(values), null);
    }

    private static string? ConstantType(BuiltinType builtin) => builtin switch
    {
        BuiltinType.Bool or BuiltinType.Char or BuiltinType.SChar or BuiltinType.UChar
            or BuiltinType.Short or BuiltinType.UShort or BuiltinType.Int => "int",
        BuiltinType.UInt => "uint",
        BuiltinType.Long or BuiltinType.LongLong => "long",
        BuiltinType.ULong or BuiltinType.ULongLong => "ulong",
        _ => null,
    };

    private enum Suffix
    {
        None,
        Typed,
    }

    /// <summary>
    /// Decimal literal of <paramref name="value"/> for a C# integer type; null
    /// when it does not fit. Unsigned 64-bit values are the reinterpretation of
    /// the signed IR value. <see cref="Suffix.Typed"/> adds U/L/UL so both arms
    /// of a conditional have the target type.
    /// </summary>
    private static string? IntegerLiteral(string type, long value, Suffix suffix)
    {
        var typed = suffix == Suffix.Typed;
        switch (type)
        {
            case "int" or "short" or "sbyte":
            {
                var (min, max) = Range(type);
                if (value < min || value > max)
                    return null;
                return value.ToString(CultureInfo.InvariantCulture);
            }
            case "byte" or "ushort" or "uint":
            {
                var (_, max) = Range(type);
                if (value < 0 || value > max)
                    return null;
                return value.ToString(CultureInfo.InvariantCulture) + (typed && type == "uint" ? "U" : "");
            }
            case "long":
                return value.ToString(CultureInfo.InvariantCulture) + (typed ? "L" : "");
            case "ulong":
                return unchecked((ulong)value).ToString(CultureInfo.InvariantCulture) + (typed ? "UL" : "");
            default:
                return null;
        }
    }

    private static (long Min, long Max) Range(string type) => type switch
    {
        "sbyte" => (sbyte.MinValue, sbyte.MaxValue),
        "byte" => (0, byte.MaxValue),
        "short" => (short.MinValue, short.MaxValue),
        "ushort" => (0, ushort.MaxValue),
        "int" => (int.MinValue, int.MaxValue),
        "uint" => (0, uint.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };

    private ConstantText FloatConstant(MergedConstant c)
    {
        var isFloat = c.Type.Values.All(t => t.Desugar() is { Kind: TypeRefKind.Builtin, Builtin: BuiltinType.Float });
        var values = new List<string>();
        foreach (var v in c.Float!.Values)
        {
            if (v is not { } d || !double.IsFinite(d))
                return Fail("floating-point constant without a finite value");
            values.Add(isFloat ? CSharpLiterals.Single((float)d) : CSharpLiterals.Double(d));
        }
        return new(isFloat ? "float" : "double", new PerAbi<string>(values), null);
    }

    private ConstantText StringConstant(MergedConstant c)
    {
        var values = new List<string>();
        foreach (var v in c.String!.Values)
        {
            if (v is null)
                return Fail("string constant without a value");
            values.Add(CSharpLiterals.String(v));
        }
        return new("string", new PerAbi<string>(values), null);
    }

    // ---- functions -------------------------------------------------------------------------------

    private void EmitFunction(MergedFunction f)
    {
        var d = f.Declaration;
        string? reason = null;
        if (d.IsVariadic)
            reason = "variadic";
        else if (d.IsInline)
            reason = "static inline (not exported)";
        else if (!d.IsExported)
            reason = "not exported";
        else if (f.SignatureDiffers)
            reason = "signature differs between ABIs";
        else if (d.CallingConvention == Model.CallingConvention.Other)
            reason = "unsupported calling convention";

        string? returnType = null;
        var parameters = new List<string>();
        if (reason is null)
        {
            if (!mapper.TryMap(d.ReturnType, inSignature: true, out var mappedReturn, out var why))
                reason = $"return type: {why}";
            else
                returnType = mappedReturn;
        }

        if (reason is null)
        {
            for (var i = 0; i < d.Parameters.Count; i++)
            {
                var p = d.Parameters[i];
                if (!mapper.TryMap(p.Type, inSignature: true, out var type, out var why) || type == "void")
                {
                    reason = $"parameter {i}: {(type == "void" ? "void parameter" : why)}";
                    break;
                }
                parameters.Add($"{type} {CSharpLiterals.Parameter(p.Name, i)}");
            }
        }

        if (reason is not null)
        {
            Skip(d.Name, d.Header, "function", reason);
            return;
        }

        var q = names.Function(d.Name);
        RegisterMember(q.Class, q.Member, d.Name);
        var convention = d.CallingConvention == Model.CallingConvention.Stdcall ? "CallConvStdcall" : "CallConvCdecl";
        var lines = new List<string>
        {
            $"/// <summary>{CSharpLiterals.Xml(d.Name)}</summary>",
            $"[LibraryImport({CSharpLiterals.String(options.Library)}, EntryPoint = {CSharpLiterals.String(d.Name)})]",
            $"[UnmanagedCallConv(CallConvs = [typeof({convention})])]",
            $"public static partial {returnType} {q.Member}({string.Join(", ", parameters)});",
        };
        var unit = Class(d.Header, q.Class);
        unit.HasFunctions = true;
        unit.Entries.Add((1, d.Name, lines));
    }

    // ---- naming ----------------------------------------------------------------------------------

    private ClassUnit Class(string header, string className)
    {
        var unit = Unit(header);
        if (unit.Classes.TryGetValue(className, out var existing))
            return existing;
        if (enumTypeNames.Contains(className))
            throw new InvalidOperationException($"Class '{className}' (for functions or constants in {header}) has the same name as an enum.");
        var created = new ClassUnit(className, recordTypeNames.ContainsKey(className));
        unit.Classes[className] = created;
        return created;
    }

    private void RegisterMember(string className, string member, string cName)
    {
        if (!memberNames.TryGetValue(className, out var set))
            memberNames[className] = set = new HashSet<string>(StringComparer.Ordinal);
        if (!set.Add(member))
            throw new InvalidOperationException($"C# member '{className}.{member}' is produced twice (second time by '{cName}').");
    }

    // ---- rendering -------------------------------------------------------------------------------

    private SortedDictionary<string, string> RenderFiles()
    {
        var commit = merged.VlcCommit.Length > 8 ? merged.VlcCommit[..8] : merged.VlcCommit;
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [CSharpHelpers.FileName] = CSharpHelpers.Render(names.Namespace, commit),
        };

        var fileOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var unit in headers.Values)
        {
            var fileName = names.HeaderClass(unit.Header) + ".g.cs";
            var clash = fileOwners.GetValueOrDefault(fileName) ?? (fileName == CSharpHelpers.FileName ? "the helpers" : null);
            if (clash is not null)
                throw new InvalidOperationException($"Header '{unit.Header}' maps to file '{fileName}', which is already used by '{clash}'.");
            fileOwners[fileName] = unit.Header;

            var w = new CSharpWriter();
            w.Line($"// <auto-generated> from {unit.Header} @ vlc {commit} by VLCLR.CImport. Do not edit.");
            w.Line("using System;");
            w.Line("using System.Diagnostics.CodeAnalysis;");
            w.Line("using System.Runtime.CompilerServices;");
            w.Line("using System.Runtime.InteropServices;");
            w.Line();
            w.Line($"namespace {names.Namespace};");
            w.Line();

            var blocks = new List<IReadOnlyList<string>>();
            blocks.AddRange(unit.Records.OrderBy(r => r.CName, StringComparer.Ordinal).Select(r => r.Lines));
            blocks.AddRange(unit.Enums.OrderBy(e => e.CName, StringComparer.Ordinal).Select(e => e.Lines));
            blocks.AddRange(unit.Classes.Values.OrderBy(c => c.Name, StringComparer.Ordinal).Select(RenderClass));
            w.Blocks(blocks);
            files[fileName] = w.ToString();
        }
        return files;
    }

    private static IReadOnlyList<string> RenderClass(ClassUnit unit)
    {
        var w = new CSharpWriter();
        var unsafeModifier = unit.HasFunctions ? "unsafe " : "";
        if (unit.IsStruct)
        {
            w.Open($"public {unsafeModifier}partial struct {unit.Name}");
        }
        else
        {
            w.Line("/// <summary>Constants and functions from the C headers.</summary>");
            w.Open($"public static {unsafeModifier}partial class {unit.Name}");
        }
        w.Blocks(unit.Entries
            .OrderBy(e => e.Group)
            .ThenBy(e => e.CName, StringComparer.Ordinal)
            .Select(e => (IReadOnlyList<string>)e.Lines));
        w.Close();
        return SplitLines(w);
    }

    private static List<string> SplitLines(CSharpWriter w) => [.. w.ToString().TrimEnd('\n').Split('\n')];

    private sealed class HeaderUnit(string header)
    {
        public string Header { get; } = header;
        public List<(string CName, List<string> Lines)> Records { get; } = [];
        public List<(string CName, List<string> Lines)> Enums { get; } = [];
        public SortedDictionary<string, ClassUnit> Classes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ClassUnit(string name, bool isStruct)
    {
        public string Name { get; } = name;
        public bool IsStruct { get; } = isStruct;
        public bool HasFunctions { get; set; }

        /// <summary>Group 0 = constants, 1 = functions; ordered by C name within a group.</summary>
        public List<(int Group, string CName, List<string> Lines)> Entries { get; } = [];
    }
}
