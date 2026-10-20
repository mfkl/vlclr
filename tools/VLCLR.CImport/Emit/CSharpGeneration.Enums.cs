using System.Globalization;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Emit;

internal sealed partial class CSharpGeneration
{
    private sealed record IntegerType(string Name, int Size, bool Signed, Int128 Min, Int128 Max);

    private static readonly IntegerType SByteType = new("sbyte", 1, true, sbyte.MinValue, sbyte.MaxValue);
    private static readonly IntegerType ByteType = new("byte", 1, false, 0, byte.MaxValue);
    private static readonly IntegerType ShortType = new("short", 2, true, short.MinValue, short.MaxValue);
    private static readonly IntegerType UShortType = new("ushort", 2, false, 0, ushort.MaxValue);
    private static readonly IntegerType IntType = new("int", 4, true, int.MinValue, int.MaxValue);
    private static readonly IntegerType UIntType = new("uint", 4, false, 0, uint.MaxValue);
    private static readonly IntegerType LongType = new("long", 8, true, long.MinValue, long.MaxValue);
    private static readonly IntegerType ULongType = new("ulong", 8, false, 0, ulong.MaxValue);

    private static IntegerType? EnumUnderlying(BuiltinType builtin, string abi) => builtin switch
    {
        BuiltinType.Bool or BuiltinType.Char or BuiltinType.UChar => ByteType,
        BuiltinType.SChar => SByteType,
        BuiltinType.Short => ShortType,
        BuiltinType.UShort => UShortType,
        BuiltinType.Int => IntType,
        BuiltinType.UInt => UIntType,
        BuiltinType.Long => abi == Abis.Windows ? IntType : LongType,
        BuiltinType.ULong => abi == Abis.Windows ? UIntType : ULongType,
        BuiltinType.LongLong => LongType,
        BuiltinType.ULongLong => ULongType,
        _ => null,
    };

    private static IntegerType SignedOf(int size) => size switch { 1 => SByteType, 2 => ShortType, 4 => IntType, _ => LongType };

    private static IntegerType UnsignedOf(int size) => size switch { 1 => ByteType, 2 => UShortType, 4 => UIntType, _ => ULongType };

    /// <summary>The IR stores unsigned 64-bit values as their signed reinterpretation.</summary>
    private static Int128 Interpret(long value, IntegerType type)
        => type is { Signed: false, Size: 8 } ? unchecked((ulong)value) : value;

    private Dictionary<string, CSharpEnumInfo> ResolveEnums()
    {
        var result = new Dictionary<string, CSharpEnumInfo>(StringComparer.Ordinal);
        foreach (var e in merged.Enums)
        {
            var info = ResolveEnum(e, out var reason);
            if (info is null)
                Skip(e.Name, e.Header, "enum", reason);
            else
                result[e.Name] = info;
        }
        return result;
    }

    private CSharpEnumInfo? ResolveEnum(MergedEnum e, out string reason)
    {
        reason = "";
        var kinds = new List<IntegerType>();
        for (var i = 0; i < e.UnderlyingType.Values.Count; i++)
        {
            var underlying = e.UnderlyingType[i].Desugar();
            var kind = underlying.Kind == TypeRefKind.Builtin ? EnumUnderlying(underlying.Builtin!.Value, merged.Abis[i]) : null;
            if (kind is null)
            {
                reason = $"underlying type {underlying.Builtin} is not supported";
                return null;
            }
            kinds.Add(kind);
        }

        var uniformValues = e.Members.Where(m => m.Value.IsUniform).ToList();
        IntegerType chosen;
        if (kinds.Distinct().Count() == 1)
        {
            chosen = kinds[0];
        }
        else if (kinds.Select(k => k.Size).Distinct().Count() == 1)
        {
            var signed = SignedOf(kinds[0].Size);
            chosen = uniformValues.All(m => Fits(Interpret(m.Value[0], signed), signed)) ? signed : UnsignedOf(kinds[0].Size);
        }
        else
        {
            reason = $"underlying type size differs between ABIs ({string.Join(", ", kinds.Select(k => k.Name))})";
            return null;
        }

        var members = new List<CSharpEnumMember>();
        foreach (var m in e.Members)
        {
            if (!m.Value.IsUniform)
            {
                members.Add(new(m, null, $"value differs between ABIs ({m.Value})"));
                continue;
            }
            var value = Interpret(m.Value[0], chosen);
            members.Add(Fits(value, chosen)
                ? new(m, value.ToString(CultureInfo.InvariantCulture), null)
                : new(m, null, $"value {value} does not fit {chosen.Name}"));
        }

        var typeName = e.IsAnonymous ? null : names.Type(e.Name);
        return new CSharpEnumInfo(e, chosen.Name, chosen.Signed, typeName, members);

        static bool Fits(Int128 value, IntegerType type) => value >= type.Min && value <= type.Max;
    }

    private void EmitEnum(CSharpEnumInfo info)
    {
        var e = info.Source;
        if (e.IsAnonymous)
        {
            foreach (var member in info.Members)
            {
                if (member.SkipReason is { } why)
                {
                    Skip(member.Source.Name, e.Header, "constant", why);
                    continue;
                }
                AddConstant(e.Header, names.Constant(member.Source.Name), member.Source.Name, info.Underlying,
                    new PerAbi<string>([member.Value!, member.Value!]), platformOf: e.Name);
            }
            return;
        }

        var w = new CSharpWriter();
        w.Line($"/// <summary>{CSharpLiterals.Xml(e.Name)}</summary>");
        if (PlatformAttribute(e.Name) is { } platform)
            w.Line(platform);
        w.Open($"public enum {info.TypeName} : {info.Underlying}");
        var blocks = new List<IReadOnlyList<string>>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in info.Members)
        {
            if (member.SkipReason is { } why)
            {
                Skip($"{e.Name}.{member.Source.Name}", e.Header, "enum member", why);
                continue;
            }
            var csName = names.EnumMember(e.Name, member.Source.Name);
            if (!used.Add(csName))
                throw new InvalidOperationException($"Enum member '{info.TypeName}.{csName}' is produced twice (second time by '{member.Source.Name}').");
            blocks.Add([
                $"/// <summary>{CSharpLiterals.Xml(member.Source.Name)}</summary>",
                $"{csName} = {member.Value},",
            ]);
        }
        w.Blocks(blocks);
        w.Close();
        Unit(e.Header).Enums.Add((e.Name, SplitLines(w)));
    }
}
