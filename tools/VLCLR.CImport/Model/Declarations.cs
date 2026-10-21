using System.Text.Json.Serialization;

namespace VLCLR.CImport.Model;

/// <summary>
/// A struct or union. Anonymous records get a synthesised name:
/// <c>parent::field</c> for the type of a named field (<c>video_format_t::dovi</c>),
/// <c>parent::@N</c> for the N-th (0-based) anonymous member of the parent
/// (<c>es_format_t::@0</c>). Nested anonymous records chain:
/// <c>es_format_t::@0::@0</c>.
/// </summary>
public sealed record TypeDecl
{
    /// <summary>C tag name (<c>filter_t</c>, <c>vlc_filter_operations</c>) or synthesised name.</summary>
    public required string Name { get; init; }

    public required RecordKind Kind { get; init; }

    /// <summary>Header file name relative to the include directory, e.g. <c>vlc_es.h</c>.</summary>
    public required string Header { get; init; }

    /// <summary>True for records declared without a tag (name is synthesised).</summary>
    public bool IsAnonymous { get; init; }

    /// <summary>Synthesised-name parent (the enclosing record's <see cref="Name"/>), anonymous records only.</summary>
    public string? Parent { get; init; }

    /// <summary>
    /// The record has no tag and takes the name of its typedef
    /// (<c>typedef struct { ... } DIR;</c>): C spells it <c>DIR</c>, not <c>struct DIR</c>.
    /// </summary>
    public bool IsTypedefNamed { get; init; }

    /// <summary>
    /// Size, alignment and fields are absent: the record is never defined in
    /// the parsed headers, or it is reached only through pointers (it is not a
    /// root and no kept record embeds it by value).
    /// </summary>
    public bool IsOpaque { get; init; }

    /// <summary>sizeof, in bytes. Null when opaque.</summary>
    public long? Size { get; init; }

    /// <summary>alignof, in bytes. Null when opaque.</summary>
    public long? Alignment { get; init; }

    /// <summary>True when the record has <c>__attribute__((packed))</c> or <c>#pragma pack</c> applied.</summary>
    public bool IsPacked { get; init; }

    /// <summary>Fields in declaration order.</summary>
    public IReadOnlyList<FieldDecl> Fields { get; init; } = [];
}

public enum RecordKind
{
    Struct,
    Union,
}

/// <summary>
/// A field of a <see cref="TypeDecl"/>. An anonymous member (C11 unnamed
/// struct/union whose fields are accessed through the parent) has
/// <see cref="Name"/> = "" and <see cref="IsAnonymousMember"/> = true, and its
/// <see cref="Type"/> refers to the synthesised <c>parent::@N</c> record.
/// </summary>
public sealed record FieldDecl
{
    public required string Name { get; init; }

    public required TypeRef Type { get; init; }

    /// <summary>Offset from the start of the record in bytes (for bitfields: <see cref="BitOffset"/> / 8, rounded down).</summary>
    public required long Offset { get; init; }

    /// <summary>Bitfields only: offset of the first bit from the start of the record, in bits.</summary>
    public long? BitOffset { get; init; }

    /// <summary>Bitfields only: width in bits.</summary>
    public int? BitWidth { get; init; }

    public bool IsAnonymousMember { get; init; }

    [JsonIgnore]
    public bool IsBitfield => BitWidth is not null;
}

/// <summary>
/// An enum. Anonymous enums (<c>enum { A, B };</c>) are named
/// <c>@FIRST_MEMBER</c> and have <see cref="IsAnonymous"/> = true; the emitter
/// turns their members into constants.
/// </summary>
public sealed record EnumDecl
{
    /// <summary>Tag name, or typedef name for <c>typedef enum { } name;</c>, or <c>@FIRST_MEMBER</c>.</summary>
    public required string Name { get; init; }

    public required string Header { get; init; }

    public bool IsAnonymous { get; init; }

    /// <summary>Underlying integer type (canonical, a <see cref="TypeRefKind.Builtin"/>).</summary>
    public required TypeRef UnderlyingType { get; init; }

    /// <summary>True when the underlying type was written explicitly (<c>enum e : uint8_t</c>).</summary>
    public bool HasFixedUnderlyingType { get; init; }

    public IReadOnlyList<EnumMember> Members { get; init; } = [];
}

/// <summary>
/// An enumerator. <see cref="Value"/> is the signed 64-bit interpretation; for
/// an unsigned underlying type reinterpret with <c>unchecked((ulong)Value)</c>
/// (only values above <see cref="long.MaxValue"/> look negative).
/// </summary>
public sealed record EnumMember(string Name, long Value);

public sealed record TypedefDecl
{
    public required string Name { get; init; }

    public required string Header { get; init; }

    /// <summary>The aliased type, one level down (may itself be a typedef).</summary>
    public required TypeRef Target { get; init; }
}

public sealed record FunctionDecl
{
    public required string Name { get; init; }

    public required string Header { get; init; }

    public required TypeRef ReturnType { get; init; }

    public IReadOnlyList<ParameterDecl> Parameters { get; init; } = [];

    public bool IsVariadic { get; init; }

    /// <summary><c>static inline</c> in a header: not exported by libvlccore.</summary>
    public bool IsInline { get; init; }

    /// <summary>Declared with VLC_API (exported by libvlccore).</summary>
    public bool IsExported { get; init; }

    public CallingConvention CallingConvention { get; init; } = CallingConvention.Cdecl;
}

public sealed record ParameterDecl(string Name, TypeRef Type);

public enum CallingConvention
{
    Cdecl,
    Stdcall,
    Other,
}

/// <summary>An object-like macro that clang evaluated to a constant.</summary>
public sealed record ConstantDecl
{
    public required string Name { get; init; }

    public required string Header { get; init; }

    public required ConstantKind Kind { get; init; }

    /// <summary>Type of the evaluated expression (a <see cref="TypeRefKind.Builtin"/>, or a pointer to char for strings).</summary>
    public required TypeRef Type { get; init; }

    /// <summary>Integer constants: signed 64-bit interpretation (see <see cref="EnumMember"/>).</summary>
    public long? Integer { get; init; }

    public double? Float { get; init; }

    public string? String { get; init; }

    /// <summary>The macro body as written, tokens joined by single spaces.</summary>
    public required string Expression { get; init; }
}

public enum ConstantKind
{
    Integer,
    Float,
    String,
}

/// <summary>A declaration extract saw and could not represent (for cimport-report.md).</summary>
public sealed record SkippedDecl
{
    public required string Name { get; init; }

    public required string Header { get; init; }

    /// <summary>What it is: <c>constant</c>, <c>function</c>, <c>record</c>, <c>enum</c>, <c>typedef</c>.</summary>
    public required string Kind { get; init; }

    public required string Reason { get; init; }
}
