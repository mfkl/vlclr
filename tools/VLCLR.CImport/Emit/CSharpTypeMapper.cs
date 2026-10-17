using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Emit;

/// <summary>An enum the emitter will write (or turn into constants), with its resolved underlying type.</summary>
internal sealed record CSharpEnumInfo(MergedEnum Source, string Underlying, bool Signed, string? TypeName, IReadOnlyList<CSharpEnumMember> Members);

/// <summary>An enumerator with its decimal literal, or the reason it is skipped.</summary>
internal sealed record CSharpEnumMember(MergedEnumMember Source, string? Value, string? SkipReason);

/// <summary>Maps C types to C# type text for the generated, blittable code.</summary>
internal sealed class CSharpTypeMapper(
    IReadOnlyDictionary<string, MergedType> records,
    IReadOnlyDictionary<string, CSharpEnumInfo> enums,
    INameMap names)
{
    private sealed class UnsupportedException(string reason) : Exception(reason);

    private static readonly Dictionary<string, string> KnownTypedefs = new(StringComparer.Ordinal)
    {
        ["size_t"] = "nuint",
        ["ssize_t"] = "nint",
        ["ptrdiff_t"] = "nint",
        ["intptr_t"] = "nint",
        ["uintptr_t"] = "nuint",
        ["int8_t"] = "sbyte",
        ["int16_t"] = "short",
        ["int32_t"] = "int",
        ["int64_t"] = "long",
        ["uint8_t"] = "byte",
        ["uint16_t"] = "ushort",
        ["uint32_t"] = "uint",
        ["uint64_t"] = "ulong",
    };

    private static readonly HashSet<string> UnsupportedTypedefs = new(StringComparer.Ordinal) { "wchar_t", "char16_t", "char32_t" };

    /// <summary>Maps a value type (field, parameter, return type; bool becomes byte).</summary>
    public bool TryMap(TypeRef type, bool inSignature, out string csharp, out string reason)
    {
        try
        {
            csharp = Map(type, inSignature);
            reason = "";
            return true;
        }
        catch (UnsupportedException e)
        {
            csharp = "";
            reason = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Maps the element type of an inline array: pointers and function pointers
    /// are stored as <c>nint</c>, records must have the same size on every ABI.
    /// </summary>
    public bool TryMapElement(TypeRef type, out string csharp, out string reason)
    {
        try
        {
            var mapped = Map(type, inSignature: false);
            var leaf = type.Desugar();
            if (leaf.Kind == TypeRefKind.Pointer)
                csharp = "nint";
            else if (leaf.Kind == TypeRefKind.Record && !records[leaf.Name!].Size!.IsUniform)
                throw new UnsupportedException($"array of record '{leaf.Name}' whose size differs between ABIs ({records[leaf.Name!].Size})");
            else
                csharp = mapped;
            reason = "";
            return true;
        }
        catch (UnsupportedException e)
        {
            csharp = "";
            reason = e.Message;
            return false;
        }
    }

    private string Map(TypeRef type, bool inSignature)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Typedef:
                if (type.Name is { } name)
                {
                    if (KnownTypedefs.TryGetValue(name, out var known))
                        return known;
                    if (UnsupportedTypedefs.Contains(name))
                        throw new UnsupportedException($"'{name}' is not supported");
                }
                return Map(type.Element ?? throw new InvalidOperationException($"Typedef '{type.Name}' has no target."), inSignature);

            case TypeRefKind.Builtin:
                return Builtin(type.Builtin!.Value);

            case TypeRefKind.Pointer:
                return Pointer(type.Element!);

            case TypeRefKind.Array:
                throw new UnsupportedException("array type outside a field");

            case TypeRefKind.Record:
            {
                var record = Record(type.Name!);
                if (record.IsOpaque)
                    throw new UnsupportedException($"opaque record '{type.Name}' used by value");
                if (inSignature && !record.Size!.IsUniform)
                    throw new UnsupportedException($"record '{type.Name}' passed by value has an ABI-dependent size ({record.Size})");
                return names.Type(type.Name!);
            }

            case TypeRefKind.Enum:
            {
                if (!enums.TryGetValue(type.Name!, out var info))
                    throw new UnsupportedException($"enum '{type.Name}' is not emitted");
                return info.TypeName ?? info.Underlying;
            }

            default:
                throw new UnsupportedException("function type outside a pointer");
        }
    }

    private string Pointer(TypeRef pointee)
    {
        switch (pointee.Kind)
        {
            case TypeRefKind.Typedef:
                if (pointee.Name is { } name && (KnownTypedefs.ContainsKey(name) || UnsupportedTypedefs.Contains(name)))
                    return Map(pointee, inSignature: false) + "*";
                return Pointer(pointee.Element!);
            case TypeRefKind.Builtin when pointee.Builtin == BuiltinType.Void:
                return "void*";
            case TypeRefKind.Function:
                return FunctionPointer(pointee.Function!);
            case TypeRefKind.Array:
                throw new UnsupportedException("pointer to array");
            case TypeRefKind.Record:
                Record(pointee.Name!);
                return names.Type(pointee.Name!) + "*";
            default:
                return Map(pointee, inSignature: false) + "*";
        }
    }

    private string FunctionPointer(FunctionType function)
    {
        if (function.IsVariadic)
            throw new UnsupportedException("variadic function pointer");

        var convention = function.CallingConvention switch
        {
            Model.CallingConvention.Cdecl => "Cdecl",
            Model.CallingConvention.Stdcall => "Stdcall",
            _ => throw new UnsupportedException("function pointer with an unsupported calling convention"),
        };

        var parts = new List<string>();
        foreach (var parameter in function.Parameters)
        {
            var mapped = Map(parameter.Type, inSignature: true);
            if (mapped == "void")
                throw new UnsupportedException("void parameter");
            parts.Add(mapped);
        }
        parts.Add(Map(function.ReturnType, inSignature: true));
        return $"delegate* unmanaged[{convention}]<{string.Join(", ", parts)}>";
    }

    private MergedType Record(string name)
        => records.TryGetValue(name, out var record)
            ? record
            : throw new UnsupportedException($"record '{name}' is not emitted");

    private static string Builtin(BuiltinType builtin) => builtin switch
    {
        BuiltinType.Void => "void",
        BuiltinType.Bool => "byte",
        BuiltinType.Char => "byte",
        BuiltinType.SChar => "sbyte",
        BuiltinType.UChar => "byte",
        BuiltinType.Short => "short",
        BuiltinType.UShort => "ushort",
        BuiltinType.Int => "int",
        BuiltinType.UInt => "uint",
        BuiltinType.Long => "CLong",
        BuiltinType.ULong => "CULong",
        BuiltinType.LongLong => "long",
        BuiltinType.ULongLong => "ulong",
        BuiltinType.Float => "float",
        BuiltinType.Double => "double",
        BuiltinType.LongDouble => throw new UnsupportedException("long double is not supported"),
        BuiltinType.Int128 or BuiltinType.UInt128 => throw new UnsupportedException("__int128 is not supported"),
        BuiltinType.WChar => throw new UnsupportedException("wchar_t is not supported"),
        BuiltinType.Char16 or BuiltinType.Char32 => throw new UnsupportedException("char16_t/char32_t is not supported"),
        _ => throw new UnsupportedException($"builtin {builtin} is not supported"),
    };
}
