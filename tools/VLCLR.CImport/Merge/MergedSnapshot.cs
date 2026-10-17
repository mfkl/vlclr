using VLCLR.CImport.Model;

namespace VLCLR.CImport.Merge;

/// <summary>
/// One value per ABI, indexed in <see cref="MergedSnapshot.Abis"/> order
/// (windows first, then sysv).
/// </summary>
public sealed record PerAbi<T>(IReadOnlyList<T> Values)
{
    public T this[int abi] => Values[abi];

    public T Windows => Values[0];

    public T SysV => Values[1];

    public bool IsUniform => Values.All(v => EqualityComparer<T>.Default.Equals(v, Values[0]));

    /// <summary>The shared value; throws when the ABIs disagree.</summary>
    public T Uniform => IsUniform
        ? Values[0]
        : throw new InvalidOperationException($"Values differ between ABIs: {string.Join(", ", Values)}");

    public PerAbi<TResult> Select<TResult>(Func<T, TResult> selector) => new([.. Values.Select(selector)]);

    public override string ToString() => IsUniform ? $"{Values[0]}" : string.Join(" | ", Values);

    public bool Equals(PerAbi<T>? other) => other is not null && Values.SequenceEqual(other.Values);

    public override int GetHashCode() => Values.Aggregate(0, (h, v) => HashCode.Combine(h, v));
}

/// <summary>
/// The windows and sysv snapshots matched symbol by symbol. Consumed by the
/// C# emitter, the C++ probe emitter and the layout test emitter.
/// </summary>
public sealed record MergedSnapshot
{
    public required IReadOnlyList<string> Abis { get; init; }

    public required string VlcCommit { get; init; }

    public IReadOnlyList<MergedType> Types { get; init; } = [];
    public IReadOnlyList<MergedEnum> Enums { get; init; } = [];

    /// <summary>Typedefs from the first ABI (typedef targets differ only in canonical builtins).</summary>
    public IReadOnlyList<TypedefDecl> Typedefs { get; init; } = [];

    public IReadOnlyList<MergedFunction> Functions { get; init; } = [];
    public IReadOnlyList<MergedConstant> Constants { get; init; } = [];

    /// <summary>Symbols that could not be merged cleanly. Errors must stop emission; warnings go to the report.</summary>
    public IReadOnlyList<MergeDiagnostic> Diagnostics { get; init; } = [];

    public MergedType? FindType(string name) => Types.FirstOrDefault(t => t.Name == name);
}

public sealed record MergedType
{
    public required string Name { get; init; }
    public required RecordKind Kind { get; init; }
    public required string Header { get; init; }
    public bool IsAnonymous { get; init; }
    public string? Parent { get; init; }
    public bool IsOpaque { get; init; }
    public bool IsPacked { get; init; }

    /// <summary>Null when opaque.</summary>
    public PerAbi<long>? Size { get; init; }

    /// <summary>Null when opaque.</summary>
    public PerAbi<long>? Alignment { get; init; }

    /// <summary>Direct fields as declared (anonymous members appear as one field each).</summary>
    public IReadOnlyList<MergedField> Fields { get; init; } = [];

    /// <summary>
    /// The fields a C caller can name on this record: anonymous members are
    /// replaced, recursively, by their own fields with offsets made relative to
    /// this record. These become the C# members of the generated struct.
    /// </summary>
    public IReadOnlyList<MergedField> Members { get; init; } = [];
}

public sealed record MergedField
{
    /// <summary>C field name; "" for an anonymous member.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The record that declares the field. For a member flattened out of an
    /// anonymous record this is the anonymous record (<c>es_format_t::@0</c>);
    /// naming scopes it to the nearest named ancestor.
    /// </summary>
    public required string DeclaringType { get; init; }

    /// <summary>Type as seen by the first ABI.</summary>
    public required TypeRef Type { get; init; }

    /// <summary>True when the ABIs disagree on the type beyond typedef targets' canonical builtins.</summary>
    public bool TypeDiffers { get; init; }

    /// <summary>Byte offset from the start of the owning record (the record whose list holds this field).</summary>
    public required PerAbi<long> Offset { get; init; }

    /// <summary>Bitfields only: bit offset from the start of the owning record.</summary>
    public PerAbi<long>? BitOffset { get; init; }

    public int? BitWidth { get; init; }

    public bool IsAnonymousMember { get; init; }

    public bool IsBitfield => BitWidth is not null;
}

public sealed record MergedEnum
{
    public required string Name { get; init; }
    public required string Header { get; init; }
    public bool IsAnonymous { get; init; }
    public bool HasFixedUnderlyingType { get; init; }
    public required PerAbi<TypeRef> UnderlyingType { get; init; }
    public IReadOnlyList<MergedEnumMember> Members { get; init; } = [];
}

public sealed record MergedEnumMember(string Name, PerAbi<long> Value);

public sealed record MergedFunction
{
    /// <summary>Declaration as seen by the first ABI.</summary>
    public required FunctionDecl Declaration { get; init; }

    /// <summary>True when the ABIs disagree on a parameter or return type beyond typedef targets' canonical builtins.</summary>
    public bool SignatureDiffers { get; init; }

    public string Name => Declaration.Name;
}

public sealed record MergedConstant
{
    public required string Name { get; init; }
    public required string Header { get; init; }
    public required ConstantKind Kind { get; init; }
    public required PerAbi<TypeRef> Type { get; init; }
    public PerAbi<long?>? Integer { get; init; }
    public PerAbi<double?>? Float { get; init; }
    public PerAbi<string?>? String { get; init; }
    public required string Expression { get; init; }
}

public enum DiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record MergeDiagnostic(DiagnosticSeverity Severity, string Symbol, string Message)
{
    public override string ToString() => $"{Severity.ToString().ToLowerInvariant()}: {Symbol}: {Message}";
}
