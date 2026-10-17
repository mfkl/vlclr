using ClangSharp.Interop;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Extract;

/// <summary>Small helpers over ClangSharp.Interop used by the extractor.</summary>
internal static class ClangExtensions
{
    public static string Text(this CXCursor cursor) => cursor.Spelling.ToString();

    /// <summary>The cursor that stands for every declaration of the same entity.</summary>
    public static CXCursor Identity(this CXCursor cursor) => cursor.CanonicalCursor;

    /// <summary>Clang prints "struct (unnamed at path:1:1)" / "union (anonymous at ...)" for tagless records and enums.</summary>
    public static bool IsTagless(this CXCursor cursor)
    {
        var spelling = cursor.Text();
        return spelling.Length == 0 || spelling.Contains("(unnamed", StringComparison.Ordinal) || spelling.Contains("(anonymous", StringComparison.Ordinal);
    }

    public static (string File, uint Offset) FileLocation(this CXSourceLocation location)
    {
        location.GetFileLocation(out var file, out _, out _, out var offset);
        return (file.Name.ToString(), offset);
    }

    public static CXCursor Field(this CXCursor record, int index) => record.GetField((uint)index);

    /// <summary>Removes qualifiers, elaboration, arrays, pointers and atomics until a record, enum or other leaf type remains (typedefs are kept).</summary>
    public static CXType PeelToDeclaration(this CXType type)
    {
        while (true)
        {
            switch (type.kind)
            {
                case CXTypeKind.CXType_Elaborated:
                    type = type.NamedType;
                    break;
                case CXTypeKind.CXType_ConstantArray or CXTypeKind.CXType_IncompleteArray:
                    type = type.ElementType;
                    break;
                case CXTypeKind.CXType_Pointer:
                    type = type.PointeeType;
                    break;
                case CXTypeKind.CXType_Atomic:
                    type = type.ValueType;
                    break;
                default:
                    return type;
            }
        }
    }

    public static BuiltinType? ToBuiltin(this CXTypeKind kind) => kind switch
    {
        CXTypeKind.CXType_Void => BuiltinType.Void,
        CXTypeKind.CXType_Bool => BuiltinType.Bool,
        CXTypeKind.CXType_Char_S or CXTypeKind.CXType_Char_U => BuiltinType.Char,
        CXTypeKind.CXType_SChar => BuiltinType.SChar,
        CXTypeKind.CXType_UChar => BuiltinType.UChar,
        CXTypeKind.CXType_Short => BuiltinType.Short,
        CXTypeKind.CXType_UShort => BuiltinType.UShort,
        CXTypeKind.CXType_Int => BuiltinType.Int,
        CXTypeKind.CXType_UInt => BuiltinType.UInt,
        CXTypeKind.CXType_Long => BuiltinType.Long,
        CXTypeKind.CXType_ULong => BuiltinType.ULong,
        CXTypeKind.CXType_LongLong => BuiltinType.LongLong,
        CXTypeKind.CXType_ULongLong => BuiltinType.ULongLong,
        CXTypeKind.CXType_Int128 => BuiltinType.Int128,
        CXTypeKind.CXType_UInt128 => BuiltinType.UInt128,
        CXTypeKind.CXType_Float => BuiltinType.Float,
        CXTypeKind.CXType_Double => BuiltinType.Double,
        CXTypeKind.CXType_LongDouble => BuiltinType.LongDouble,
        CXTypeKind.CXType_WChar => BuiltinType.WChar,
        CXTypeKind.CXType_Char16 => BuiltinType.Char16,
        CXTypeKind.CXType_Char32 => BuiltinType.Char32,
        _ => null,
    };
}

/// <summary>A declaration extract cannot represent; the declaration goes to <see cref="Snapshot.Skipped"/>.</summary>
internal sealed class UnsupportedException(string reason) : Exception(reason);
