namespace VLCLR.CImport.Model;

/// <summary>
/// A use of a C type. Typedef sugar is kept (<see cref="TypeRefKind.Typedef"/>
/// with <see cref="Name"/> and the aliased <see cref="Element"/>) so the
/// emitter can map <c>size_t</c>, <c>uint64_t</c> or <c>vlc_tick_t</c> the same
/// on both ABIs even though their canonical builtins differ.
/// </summary>
/// <remarks>
/// Shape per kind:
/// <list type="bullet">
/// <item>Builtin: <see cref="Builtin"/>.</item>
/// <item>Pointer: <see cref="Element"/> = pointee.</item>
/// <item>Array: <see cref="Element"/>, <see cref="Count"/> (null for a flexible or incomplete array).</item>
/// <item>Record: <see cref="Name"/> = <see cref="TypeDecl.Name"/>.</item>
/// <item>Enum: <see cref="Name"/> = <see cref="EnumDecl.Name"/>.</item>
/// <item>Typedef: <see cref="Name"/> = <see cref="TypedefDecl.Name"/>, <see cref="Element"/> = aliased type (one level).</item>
/// <item>Function: <see cref="Function"/>; appears as the element of a Pointer for function pointers.</item>
/// </list>
/// </remarks>
public sealed record TypeRef
{
    public required TypeRefKind Kind { get; init; }

    public BuiltinType? Builtin { get; init; }

    public string? Name { get; init; }

    public TypeRef? Element { get; init; }

    public long? Count { get; init; }

    public FunctionType? Function { get; init; }

    public bool IsConst { get; init; }

    public static TypeRef Of(BuiltinType builtin) => new() { Kind = TypeRefKind.Builtin, Builtin = builtin };

    public static TypeRef PointerTo(TypeRef element) => new() { Kind = TypeRefKind.Pointer, Element = element };

    public static TypeRef ArrayOf(TypeRef element, long? count) => new() { Kind = TypeRefKind.Array, Element = element, Count = count };

    public static TypeRef Record(string name) => new() { Kind = TypeRefKind.Record, Name = name };

    public static TypeRef Enum(string name) => new() { Kind = TypeRefKind.Enum, Name = name };

    public static TypeRef Typedef(string name, TypeRef target) => new() { Kind = TypeRefKind.Typedef, Name = name, Element = target };

    public static TypeRef FunctionOf(FunctionType function) => new() { Kind = TypeRefKind.Function, Function = function };

    /// <summary>Follows typedefs down to the first non-typedef type.</summary>
    public TypeRef Desugar()
    {
        var t = this;
        while (t.Kind == TypeRefKind.Typedef)
            t = t.Element ?? throw new InvalidOperationException($"Typedef '{t.Name}' has no target.");
        return t;
    }
}

public enum TypeRefKind
{
    Builtin,
    Pointer,
    Array,
    Record,
    Enum,
    Typedef,
    Function,
}

/// <summary>C builtin types by their canonical spelling.</summary>
public enum BuiltinType
{
    Void,
    /// <summary>_Bool / bool</summary>
    Bool,
    /// <summary>plain char (signedness is target-defined; signed on both supported ABIs)</summary>
    Char,
    SChar,
    UChar,
    Short,
    UShort,
    Int,
    UInt,
    /// <summary>long: 4 bytes on windows, 8 on sysv</summary>
    Long,
    ULong,
    LongLong,
    ULongLong,
    Int128,
    UInt128,
    Float,
    Double,
    LongDouble,
    WChar,
    Char16,
    Char32,
}

public sealed record FunctionType
{
    public required TypeRef ReturnType { get; init; }

    public IReadOnlyList<ParameterDecl> Parameters { get; init; } = [];

    public bool IsVariadic { get; init; }

    public CallingConvention CallingConvention { get; init; } = CallingConvention.Cdecl;
}
