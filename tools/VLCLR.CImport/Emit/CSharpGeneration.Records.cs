using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Emit;

internal sealed partial class CSharpGeneration
{
    /// <summary>
    /// Named records, plus anonymous ones that are the type of a named field.
    /// Anonymous records used only as anonymous members are already flattened
    /// into their parent's members.
    /// </summary>
    private Dictionary<string, MergedType> SelectRecords()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in merged.Types)
        {
            foreach (var field in type.Fields.Where(f => !f.IsAnonymousMember))
                CollectRecords(field.Type, referenced);
        }

        return merged.Types
            .Where(t => !t.IsAnonymous || referenced.Contains(t.Name))
            .ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    private static void CollectRecords(TypeRef type, HashSet<string> into)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Record:
                into.Add(type.Name!);
                break;
            case TypeRefKind.Pointer or TypeRefKind.Array or TypeRefKind.Typedef:
                CollectRecords(type.Element!, into);
                break;
            case TypeRefKind.Function:
                CollectRecords(type.Function!.ReturnType, into);
                foreach (var p in type.Function.Parameters)
                    CollectRecords(p.Type, into);
                break;
        }
    }

    private List<string> EmitRecord(MergedType t)
    {
        var typeName = names.Type(t.Name);
        var w = new CSharpWriter();
        w.Line($"/// <summary>{CSharpLiterals.Xml(t.Name)}</summary>");

        if (t.IsOpaque)
        {
            memberNames[typeName] = new HashSet<string>(StringComparer.Ordinal);
            w.Line($"public partial struct {typeName} {{ }}");
            return SplitLines(w);
        }

        var used = new HashSet<string>(StringComparer.Ordinal) { "SizeOf" };
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
                $"public static int SizeOf => {Choice(t.Size!, v => v.ToString())};",
            },
        };
        var nested = new List<IReadOnlyList<string>>();

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
                Skip(symbol, t.Header, "field", reason);
            else
                blocks.Add(block);
        }

        blocks.AddRange(nested);

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
        if (m.Offset.IsUniform)
            return [doc, $"[FieldOffset({m.Offset[0]})] public {type} {csName};"];

        if (pointer)
        {
            return
            [
                doc,
                "[UnscopedRef]",
                $"public ref {type} {csName} => ref *({type}*)((byte*)Unsafe.AsPointer(ref this) + {Choice(m.Offset, v => v.ToString(), parenthesize: true)});",
            ];
        }
        return
        [
            doc,
            "[UnscopedRef]",
            $"public ref {type} {csName} => ref {CSharpHelpers.ClassName}.FieldAt<{ownerName}, {type}>(ref this, {Choice(m.Offset, v => v.ToString())});",
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
        if (m.Type.Desugar() is { Kind: TypeRefKind.Builtin, Builtin: BuiltinType.Bool })
        {
            type = "bool";
            reason = "";
        }
        else if (!mapper.TryMap(m.Type, inSignature: false, out type, out reason))
        {
            return null;
        }

        var offset = Choice(m.BitOffset!, v => v.ToString(), parenthesize: true);
        var width = m.BitWidth!.Value;
        var read = $"{CSharpHelpers.ClassName}.ReadBits(in this, {offset}, {width})";
        var readSigned = $"{CSharpHelpers.ClassName}.ReadSignedBits(in this, {offset}, {width})";

        string get;
        string set;
        var enumInfo = enums.Values.FirstOrDefault(e => e.TypeName == type);
        switch (type)
        {
            case "bool":
                get = $"{read} != 0";
                set = "value ? 1UL : 0UL";
                break;
            case "sbyte" or "short" or "int" or "long" or "nint":
                get = $"unchecked(({type}){readSigned})";
                set = "unchecked((ulong)(long)value)";
                break;
            case "byte" or "ushort" or "uint" or "ulong" or "nuint":
                get = $"unchecked(({type}){read})";
                set = "(ulong)value";
                break;
            case "CLong":
                get = $"new CLong(unchecked((nint){readSigned}))";
                set = "unchecked((ulong)(long)value.Value)";
                break;
            case "CULong":
                get = $"new CULong(unchecked((nuint){read}))";
                set = "(ulong)value.Value";
                break;
            default:
                if (enumInfo is null)
                {
                    reason = $"bitfield of type {type} is not supported";
                    return null;
                }
                get = enumInfo.Signed
                    ? $"unchecked(({type})({enumInfo.Underlying}){readSigned})"
                    : $"unchecked(({type})({enumInfo.Underlying}){read})";
                set = enumInfo.Signed ? "unchecked((ulong)(long)value)" : "(ulong)value";
                break;
        }

        reason = "";
        return
        [
            $"/// <summary>{CSharpLiterals.Xml(m.Name)} : {width}</summary>",
            $"public {type} {csName}",
            "{",
            $"    readonly get => {get};",
            $"    set => {CSharpHelpers.ClassName}.WriteBits(ref this, {offset}, {width}, {set});",
            "}",
        ];
    }

    /// <summary>One C# expression for a per-ABI value: the literal when the ABIs agree, else a platform conditional.</summary>
    private static string Choice<T>(PerAbi<T> values, Func<T, string> format, bool parenthesize = false)
    {
        if (values.IsUniform)
            return format(values[0]);
        var expression = $"OperatingSystem.IsWindows() ? {format(values.Windows)} : {format(values.SysV)}";
        return parenthesize ? $"({expression})" : expression;
    }
}
