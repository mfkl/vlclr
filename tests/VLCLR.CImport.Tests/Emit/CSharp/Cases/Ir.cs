using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Emit.CSharp.Cases;

/// <summary>Terse builders for hand-written IR.</summary>
internal static class Ir
{
    public static TypeRef CVoid => TypeRef.Of(BuiltinType.Void);
    public static TypeRef Bool => TypeRef.Of(BuiltinType.Bool);
    public static TypeRef CharT => TypeRef.Of(BuiltinType.Char);
    public static TypeRef Short => TypeRef.Of(BuiltinType.Short);
    public static TypeRef Int => TypeRef.Of(BuiltinType.Int);
    public static TypeRef UInt => TypeRef.Of(BuiltinType.UInt);
    public static TypeRef LongLong => TypeRef.Of(BuiltinType.LongLong);
    public static TypeRef Float => TypeRef.Of(BuiltinType.Float);
    public static TypeRef CDouble => TypeRef.Of(BuiltinType.Double);

    public static TypeRef Long => TypeRef.Of(BuiltinType.Long);
    public static TypeRef ULong => TypeRef.Of(BuiltinType.ULong);

    public static TypeRef U8 => TypeRef.Typedef("uint8_t", TypeRef.Of(BuiltinType.UChar));
    public static TypeRef U32 => TypeRef.Typedef("uint32_t", TypeRef.Of(BuiltinType.UInt));
    public static TypeRef Fourcc => TypeRef.Typedef("vlc_fourcc_t", U32);

    public static TypeRef U64(bool windows)
        => TypeRef.Typedef("uint64_t", TypeRef.Of(windows ? BuiltinType.ULongLong : BuiltinType.ULong));

    public static TypeRef I64(bool windows)
        => TypeRef.Typedef("int64_t", TypeRef.Of(windows ? BuiltinType.LongLong : BuiltinType.Long));

    public static TypeRef Tick(bool windows) => TypeRef.Typedef("vlc_tick_t", I64(windows));

    public static TypeRef SizeT(bool windows)
        => TypeRef.Typedef("size_t", TypeRef.Of(windows ? BuiltinType.ULongLong : BuiltinType.ULong));

    public static TypeRef Ptr(TypeRef element) => TypeRef.PointerTo(element);

    public static TypeRef CChar => TypeRef.Of(BuiltinType.Char) with { IsConst = true };

    public static TypeRef Rec(string name) => TypeRef.Record(name);

    public static TypeRef Enum(string name) => TypeRef.Enum(name);

    public static TypeRef Arr(TypeRef element, long? count) => TypeRef.ArrayOf(element, count);

    public static TypeRef FnPtr(TypeRef returns, params TypeRef[] parameters)
        => TypeRef.PointerTo(TypeRef.FunctionOf(new FunctionType
        {
            ReturnType = returns,
            Parameters = [.. parameters.Select(p => new ParameterDecl("", p))],
        }));

    public static FieldDecl F(string name, TypeRef type, long offset) => new() { Name = name, Type = type, Offset = offset };

    public static FieldDecl Bits(string name, TypeRef type, long bitOffset, int width)
        => new() { Name = name, Type = type, Offset = bitOffset / 8, BitOffset = bitOffset, BitWidth = width };

    public static FieldDecl AnonMember(string recordName, long offset)
        => new() { Name = "", Type = TypeRef.Record(recordName), Offset = offset, IsAnonymousMember = true };

    public static TypeDecl Struct(string name, string header, long size, long alignment, params FieldDecl[] fields)
        => new() { Name = name, Kind = RecordKind.Struct, Header = header, Size = size, Alignment = alignment, Fields = fields };

    public static TypeDecl Union(string name, string header, long size, long alignment, params FieldDecl[] fields)
        => new() { Name = name, Kind = RecordKind.Union, Header = header, Size = size, Alignment = alignment, Fields = fields };

    /// <summary>An anonymous record: <c>parent::field</c> or <c>parent::@N</c>.</summary>
    public static TypeDecl Anonymous(RecordKind kind, string name, string parent, string header, long size, long alignment, params FieldDecl[] fields)
        => new() { Name = name, Kind = kind, Header = header, IsAnonymous = true, Parent = parent, Size = size, Alignment = alignment, Fields = fields };

    public static TypeDecl Opaque(string name, string header)
        => new() { Name = name, Kind = RecordKind.Struct, Header = header, IsOpaque = true };

    public static EnumDecl Enumeration(string name, string header, BuiltinType underlying, params (string Name, long Value)[] members)
        => new()
        {
            Name = name,
            Header = header,
            UnderlyingType = TypeRef.Of(underlying),
            Members = [.. members.Select(m => new EnumMember(m.Name, m.Value))],
        };

    public static FunctionDecl Function(string name, string header, TypeRef returns, params (string Name, TypeRef Type)[] parameters)
        => new()
        {
            Name = name,
            Header = header,
            ReturnType = returns,
            Parameters = [.. parameters.Select(p => new ParameterDecl(p.Name, p.Type))],
            IsExported = true,
        };

    public static Snapshot Snapshot(
        bool windows,
        IEnumerable<TypeDecl>? types = null,
        IEnumerable<EnumDecl>? enums = null,
        IEnumerable<FunctionDecl>? functions = null,
        IEnumerable<ConstantDecl>? constants = null)
        => new()
        {
            Abi = windows ? Abis.Windows : Abis.SysV,
            Target = windows ? "x86_64-w64-windows-gnu" : "x86_64-pc-linux-gnu",
            VlcCommit = "dc69496e0c1b2a3f",
            PointerSize = 8,
            Types = [.. types ?? []],
            Enums = [.. enums ?? []],
            Functions = [.. functions ?? []],
            Constants = [.. constants ?? []],
        };
}
