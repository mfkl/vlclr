using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Emit;

internal sealed partial class CSharpGeneration
{
    private Dictionary<string, MergedType> SelectRecords()
        => EmittedRecords.Select(merged, includeOpaque: true).ToDictionary(t => t.Name, StringComparer.Ordinal);

    private List<string> EmitRecord(MergedType t)
    {
        var typeName = names.Type(t.Name);
        var w = new CSharpWriter();
        w.Line($"/// <summary>{CSharpLiterals.Xml(t.Name)}</summary>");
        foreach (var platform in PlatformAttributes(t.Name))
            w.Line(platform);

        if (t.IsOpaque)
        {
            memberNames[typeName] = new HashSet<string>(StringComparer.Ordinal);
            w.Line($"public partial struct {typeName} {{ }}");
            return SplitLines(w);
        }

        var used = new HashSet<string>(StringComparer.Ordinal) { "SizeOf", "MinSizeOf", "MaxSizeOf", "Offsets" };
        var csNames = new List<string>();
        foreach (var m in t.Members)
        {
            var csName = names.Field(m.DeclaringType, m.Name);
            if (csName == typeName || !used.Add(csName))
                throw new InvalidOperationException($"C# member '{typeName}.{csName}' is produced twice (second time by '{m.DeclaringType}.{m.Name}').");
            csNames.Add(csName);
        }
        memberNames[typeName] = used;

        var blocks = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "/// <summary>Size of the C struct in bytes on the running platform.</summary>",
                Getter("public static int SizeOf", AbiText.Choice(t.Size!, v => v.ToString())),
            },
            new[]
            {
                "/// <summary>Smallest size of the C struct over the supported ABIs.</summary>",
                $"public const int MinSizeOf = {t.Size!.Values.Min()};",
            },
            new[]
            {
                "/// <summary>Largest size of the C struct over the supported ABIs (the managed size).</summary>",
                $"public const int MaxSizeOf = {t.Size!.Values.Max()};",
            },
        };
        var nested = new List<IReadOnlyList<string>>();
        var offsets = new List<string>();

        for (var i = 0; i < t.Members.Count; i++)
        {
            var m = t.Members[i];
            var symbol = $"{t.Name}.{m.Name}";
            IReadOnlyList<string>? block;
            string reason;
            if (m.TypeDiffers)
            {
                block = null;
                reason = "field type differs between ABIs";
            }
            else if (m.IsBitfield)
            {
                block = Bitfield(m, csNames[i], out reason);
            }
            else
            {
                block = PlainMember(typeName, m, csNames[i], used, nested, out reason);
            }

            if (block is null)
            {
                Skip(symbol, t.Header, "field", reason);
                continue;
            }
            blocks.Add(block);
            if (!m.IsBitfield)
            {
                offsets.Add($"/// <summary>offsetof({CSharpLiterals.Xml(t.Name)}, {CSharpLiterals.Xml(m.Name)})</summary>");
                offsets.Add(m.Offset.IsUniform
                    ? $"public const int {csNames[i]} = {m.Offset[0]};"
                    : Getter($"public static int {csNames[i]}", AbiText.Choice(m.Offset, v => v.ToString())));
            }
        }

        blocks.AddRange(nested);
        if (offsets.Count > 0)
        {
            // Lets hand-written wrappers place their fields without literal offsets:
            // [FieldOffset(Plane.Offsets.Pixels)] when the offset is the same on every ABI.
            blocks.Add(
            [
                "/// <summary>Member offsets on the running platform; constants where every ABI agrees.</summary>",
                "public static class Offsets",
                "{",
                .. offsets.Select(l => "    " + l),
                "}",
            ]);
        }

        var size = t.Size!.Values.Max();
        w.Line(size > 0
            ? $"[StructLayout(LayoutKind.Explicit, Size = {size})]"
            : "[StructLayout(LayoutKind.Explicit)]");
        w.Open($"public unsafe partial struct {typeName}");
        w.Blocks(blocks);
        w.Close();
        return SplitLines(w);
    }

    private IReadOnlyList<string>? PlainMember(
        string ownerName,
        MergedField m,
        string csName,
        HashSet<string> used,
        List<IReadOnlyList<string>> nested,
        out string reason)
    {
        string type;
        var pointer = false;
        if (m.Type.Desugar().Kind == TypeRefKind.Array)
        {
            var arrays = new List<IReadOnlyList<string>>();
            if (!TryArray(m, csName, used, arrays, out type, out reason))
                return null;
            nested.AddRange(arrays);
        }
        else
        {
            if (!mapper.TryMap(m.Type, inSignature: false, out type, out reason))
                return null;
            if (type == "void")
            {
                reason = "void field";
                return null;
            }
            pointer = m.Type.Desugar().Kind == TypeRefKind.Pointer;
        }

        var doc = $"/// <summary>{CSharpLiterals.Xml(m.Name)}</summary>";
        var offset = AbiText.Choice(m.Offset, v => v.ToString(), parenthesize: true);
        if (EmittedRecords.IsAbiSizedRecord(m.Type, records))
        {
            return
            [
                doc,
                $"/// <remarks>A pointer because <see cref=\"{type}\"/> has a different size on each ABI: assign its members, not a whole value.</remarks>",
                Getter($"public {type}* {csName}", offset.Map(o => $"({type}*)((byte*)Unsafe.AsPointer(ref this) + {o})")),
            ];
        }

        if (m.Offset.IsUniform)
            return [doc, $"[FieldOffset({m.Offset[0]})] public {type} {csName};"];

        if (pointer)
        {
            return
            [
                doc,
                "[UnscopedRef]",
                Getter($"public ref {type} {csName}", offset.Map(o => $"ref *({type}*)((byte*)Unsafe.AsPointer(ref this) + {o})")),
            ];
        }
        return
        [
            doc,
            "[UnscopedRef]",
            Getter($"public ref {type} {csName}", AbiText.Choice(m.Offset, v => v.ToString()).Map(o => $"ref {CSharpHelpers.ClassName}.FieldAt<{ownerName}, {type}>(ref this, {o})")),
        ];
    }

    /// <summary>
    /// Declares nested <c>[InlineArray]</c> structs for an array member and
    /// returns the outermost type's name. Pointers and function pointers are
    /// stored as <c>nint</c>; inner dimensions are nested types
    /// (<c>NameArray</c>, <c>NameArray2</c>, ...).
    /// </summary>
    private bool TryArray(MergedField m, string csName, HashSet<string> used, List<IReadOnlyList<string>> declarations, out string typeName, out string reason)
    {
        typeName = "";
        var counts = new List<long>();
        var current = m.Type;
        while (current.Desugar() is { Kind: TypeRefKind.Array } array)
        {
            if (array.Count is null)
            {
                reason = "flexible array member";
                return false;
            }
            if (array.Count <= 0)
            {
                reason = "zero-length array";
                return false;
            }
            counts.Add(array.Count.Value);
            current = array.Element!;
        }

        if (!mapper.TryMapElement(current, out var element, out reason))
            return false;

        var arrayNames = new List<string>();
        for (var level = 0; level < counts.Count; level++)
        {
            var name = csName + "Array" + (level == 0 ? "" : (level + 1).ToString());
            if (!used.Add(name))
                throw new InvalidOperationException($"C# nested type '{name}' for array '{m.Name}' collides with another member.");
            arrayNames.Add(name);
        }

        for (var level = 0; level < counts.Count; level++)
        {
            var elementType = level == counts.Count - 1 ? element : arrayNames[level + 1];
            declarations.Add(
            [
                $"/// <summary>Inline array of {counts[level]} elements of <c>{CSharpLiterals.Xml(m.Name)}</c>.</summary>",
                $"[InlineArray({counts[level]})]",
                $"public struct {arrayNames[level]}",
                "{",
                $"    private {elementType} _e0;",
                "}",
            ]);
        }

        typeName = arrayNames[0];
        return true;
    }

    private IReadOnlyList<string>? Bitfield(MergedField m, string csName, out string reason)
    {
        string type;
        if (m.Type.Desugar() is { Kind: TypeRefKind.Builtin, Builtin: BuiltinType.Char })
        {
            // Signed on x86 and on Windows and Apple ARM64, unsigned on Linux ARM64:
            // no single read matches C everywhere.
            reason = "plain char bitfield: its signedness depends on the target";
            return null;
        }
        if (m.Type.Desugar() is { Kind: TypeRefKind.Builtin, Builtin: BuiltinType.Bool })
        {
            type = "bool";
            reason = "";
        }
        else if (!mapper.TryMap(m.Type, inSignature: false, out type, out reason))
        {
            return null;
        }

        var offset = AbiText.Choice(m.BitOffset!, v => v.ToString(), parenthesize: true);
        var width = m.BitWidth!.Value;
        var read = offset.Map(o => $"{CSharpHelpers.ClassName}.ReadBits(in this, {o}, {width})");
        var readSigned = offset.Map(o => $"{CSharpHelpers.ClassName}.ReadSignedBits(in this, {o}, {width})");

        AbiText get;
        string set;
        var enumInfo = enums.Values.FirstOrDefault(e => e.TypeName == type);
        switch (type)
        {
            case "bool":
                get = read.Map(r => $"{r} != 0");
                set = "value ? 1UL : 0UL";
                break;
            case "sbyte" or "short" or "int" or "long" or "nint":
                get = readSigned.Map(r => $"unchecked(({type}){r})");
                set = "unchecked((ulong)(long)value)";
                break;
            case "byte" or "ushort" or "uint" or "ulong" or "nuint":
                get = read.Map(r => $"unchecked(({type}){r})");
                set = "(ulong)value";
                break;
            case "CLong":
                get = readSigned.Map(r => $"new CLong(unchecked((nint){r}))");
                set = "unchecked((ulong)(long)value.Value)";
                break;
            case "CULong":
                get = read.Map(r => $"new CULong(unchecked((nuint){r}))");
                set = "(ulong)value.Value";
                break;
            default:
                if (enumInfo is null)
                {
                    reason = $"bitfield of type {type} is not supported";
                    return null;
                }
                get = enumInfo.Signed
                    ? readSigned.Map(r => $"unchecked(({type})({enumInfo.Underlying}){r})")
                    : read.Map(r => $"unchecked(({type})({enumInfo.Underlying}){r})");
                set = enumInfo.Signed ? "unchecked((ulong)(long)value)" : "(ulong)value";
                break;
        }

        reason = "";
        return
        [
            $"/// <summary>{CSharpLiterals.Xml(m.Name)} : {width}</summary>",
            $"public {type} {csName}",
            "{",
            "    " + Accessor("readonly get", get),
            "    " + Accessor("set", offset.Map(o => $"{CSharpHelpers.ClassName}.WriteBits(ref this, {o}, {width}, {set})")),
            "}",
        ];
    }

    private const string AggressiveInlining = "[MethodImpl(MethodImplOptions.AggressiveInlining)] ";

    /// <summary>
    /// <c>signature => body;</c>, or a getter marked AggressiveInlining when the
    /// body picks a per-ABI value (see <see cref="AbiText"/>).
    /// </summary>
    internal static string Getter(string signature, AbiText body)
        => body.PerAbi
            ? $"{signature} {{ {AggressiveInlining}get => {body.Text}; }}"
            : $"{signature} => {body.Text};";

    /// <summary><c>accessor => body;</c> inside a property, marked AggressiveInlining when the body picks a per-ABI value.</summary>
    internal static string Accessor(string accessor, AbiText body)
        => $"{(body.PerAbi ? AggressiveInlining : "")}{accessor} => {body.Text};";
}
