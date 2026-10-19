using System.Globalization;
using System.Text;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Tests.Emit;

/// <summary>A tiny INameMap: PascalCase of the C name, <c>_t</c> dropped, anonymous records joined.</summary>
internal sealed class TestNameMap(string ns = "Probe.Interop") : INameMap
{
    public string Namespace => ns;

    public string Type(string cName) => Pascal(cName, dropTrailingT: true);

    public string Field(string cDeclaringType, string cField) => Pascal(cField, dropTrailingT: false);

    public string EnumMember(string cEnum, string cMember) => Pascal(cMember, dropTrailingT: false);

    public QualifiedName Function(string cName) => throw new NotSupportedException();

    public QualifiedName Constant(string cName) => throw new NotSupportedException();

    public string HeaderClass(string header) => throw new NotSupportedException();

    private static string Pascal(string name, bool dropTrailingT)
    {
        var parts = name.Replace("::", "_").Replace("@", "anon").Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (dropTrailingT && parts.Count > 1 && parts[^1] is "t" or "e")
            parts.RemoveAt(parts.Count - 1);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

/// <summary>
/// Writes C# structs that follow the generated-code shape contract for the
/// running ABI: explicit layout sized to the maximum, FieldOffset fields,
/// InlineArray array members, bitfield properties, pointer properties for
/// records whose size differs between ABIs, a static SizeOf.
/// The real C# emitter is written elsewhere; this is the minimum the layout
/// tests need to run against.
/// </summary>
internal static class ContractStructs
{
    public static int RunningAbi => OperatingSystem.IsWindows() ? 0 : 1;

    public static string Generate(MergedSnapshot merged, INameMap names)
    {
        var abi = RunningAbi;
        var arrays = new Dictionary<string, string>();
        var records = merged.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var body = new StringBuilder();

        foreach (var e in merged.Enums.Where(e => !e.IsAnonymous))
        {
            var underlying = CsBuiltin(e.UnderlyingType[abi].Desugar().Builtin!.Value, abi);
            body.Append($"public enum {names.Type(e.Name)} : {underlying}\n{{\n");
            foreach (var m in e.Members)
                body.Append($"    {names.EnumMember(e.Name, m.Name)} = {(underlying == "ulong" ? unchecked((ulong)m.Value[abi]).ToString(CultureInfo.InvariantCulture) : m.Value[abi].ToString(CultureInfo.InvariantCulture))},\n");
            body.Append("}\n\n");
        }

        foreach (var type in VLCLR.CImport.Emit.EmittedRecords.Select(merged))
        {
            var csType = names.Type(type.Name);
            body.Append($"[StructLayout(LayoutKind.Explicit, Size = {type.Size!.Values.Max()})]\n");
            body.Append($"public struct {csType}\n{{\n");
            body.Append($"    public static int SizeOf => {type.Size![abi]};\n");
            foreach (var m in type.Members)
            {
                var csName = names.Field(m.DeclaringType, m.Name);
                if (VLCLR.CImport.Emit.EmittedRecords.IsAbiSizedRecord(m.Type, records))
                {
                    body.Append($"    public unsafe {names.Type(m.Type.Desugar().Name!)}* {csName} => ({names.Type(m.Type.Desugar().Name!)}*)((byte*)Unsafe.AsPointer(ref this) + {m.Offset[abi]});\n");
                    continue;
                }
                if (!m.IsBitfield)
                {
                    body.Append($"    [FieldOffset({m.Offset[abi]})] public {CsType(m.Type, names, abi, arrays)} {csName};\n");
                    continue;
                }

                var (cs, kind) = BitfieldType(m, merged, names, abi);
                var read = $"BitHelper.Get(ref Unsafe.As<{csType}, byte>(ref Unsafe.AsRef(in this)), {m.BitOffset![abi]}, {m.BitWidth})";
                var getter = kind switch
                {
                    "bool" => $"{read} != 0",
                    "signed" => $"unchecked(({cs})BitHelper.SignExtend({read}, {m.BitWidth}))",
                    _ => $"unchecked(({cs}){read})",
                };
                var raw = kind == "bool" ? "value ? 1UL : 0UL" : "unchecked((ulong)(long)value)";
                body.Append($"    public {cs} {csName}\n    {{\n");
                body.Append($"        get => {getter};\n");
                body.Append($"        set => BitHelper.Set(ref Unsafe.As<{csType}, byte>(ref this), {m.BitOffset[abi]}, {m.BitWidth}, {raw});\n");
                body.Append("    }\n");
            }
            body.Append("}\n\n");
        }

        var sb = new StringBuilder();
        sb.Append("using System;\nusing System.Runtime.CompilerServices;\nusing System.Runtime.InteropServices;\n\n");
        sb.Append($"namespace {names.Namespace};\n\n");
        foreach (var (name, source) in arrays)
            sb.Append($"[InlineArray({source.Split('|')[0]})]\npublic struct {name}\n{{\n    private {source.Split('|')[1]} _element;\n}}\n\n");
        sb.Append(BitHelperSource);
        sb.Append(body);
        return sb.ToString().Replace("\r\n", "\n");
    }

    private const string BitHelperSource = """
        internal static class BitHelper
        {
            public static ulong Get(ref byte start, long bitOffset, int width)
            {
                ulong value = 0;
                for (var i = 0; i < width; i++)
                {
                    var bit = bitOffset + i;
                    if (((Unsafe.Add(ref start, (int)(bit / 8)) >> (int)(bit % 8)) & 1) != 0)
                        value |= 1UL << i;
                }
                return value;
            }

            public static void Set(ref byte start, long bitOffset, int width, ulong value)
            {
                for (var i = 0; i < width; i++)
                {
                    var bit = bitOffset + i;
                    ref var b = ref Unsafe.Add(ref start, (int)(bit / 8));
                    var mask = (byte)(1 << (int)(bit % 8));
                    if (((value >> i) & 1) != 0) b |= mask; else b &= (byte)~mask;
                }
            }

            public static long SignExtend(ulong value, int width) => (long)(value << (64 - width)) >> (64 - width);
        }


        """;

    private static (string Type, string Kind) BitfieldType(MergedField field, MergedSnapshot merged, INameMap names, int abi)
    {
        var type = field.Type.Desugar();
        if (type.Kind == TypeRefKind.Enum)
        {
            var enumDecl = merged.Enums.First(e => e.Name == type.Name);
            var builtin = enumDecl.UnderlyingType[abi].Desugar().Builtin!.Value;
            return (names.Type(type.Name!), IsSigned(builtin) ? "signed" : "unsigned");
        }
        if (type.Builtin == BuiltinType.Bool)
            return ("bool", "bool");
        return (CsBuiltin(type.Builtin!.Value, abi), IsSigned(type.Builtin.Value) ? "signed" : "unsigned");
    }

    private static bool IsSigned(BuiltinType b)
        => b is BuiltinType.SChar or BuiltinType.Short or BuiltinType.Int or BuiltinType.Long or BuiltinType.LongLong;

    private static string CsType(TypeRef type, INameMap names, int abi, Dictionary<string, string> arrays)
    {
        var t = type.Desugar();
        switch (t.Kind)
        {
            case TypeRefKind.Builtin:
                return CsBuiltin(t.Builtin!.Value, abi);
            case TypeRefKind.Pointer:
                return "nint";
            case TypeRefKind.Record or TypeRefKind.Enum:
                return names.Type(t.Name!);
            case TypeRefKind.Array:
                var element = CsType(t.Element!, names, abi, arrays);
                var name = $"Arr{t.Count}_{element.Replace('.', '_')}";
                arrays[name] = $"{t.Count}|{element}";
                return name;
            default:
                throw new NotSupportedException(t.Kind.ToString());
        }
    }

    private static string CsBuiltin(BuiltinType b, int abi) => b switch
    {
        BuiltinType.Char or BuiltinType.UChar => "byte",
        BuiltinType.SChar => "sbyte",
        BuiltinType.Short => "short",
        BuiltinType.UShort => "ushort",
        BuiltinType.Int => "int",
        BuiltinType.UInt => "uint",
        BuiltinType.Long => abi == 0 ? "int" : "long",
        BuiltinType.ULong => abi == 0 ? "uint" : "ulong",
        BuiltinType.LongLong => "long",
        BuiltinType.ULongLong => "ulong",
        BuiltinType.Float => "float",
        BuiltinType.Double => "double",
        BuiltinType.Bool => "bool",
        _ => throw new NotSupportedException(b.ToString()),
    };
}
