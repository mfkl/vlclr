namespace VLCLR.CImport.Naming;

/// <summary>
/// C name to C# name, for everything the emitters write. Built once from the
/// merged snapshot and the manifest overrides; building it fails when two C
/// symbols would get the same C# name in the same scope.
/// </summary>
/// <remarks>
/// All arguments are C names exactly as they appear in the snapshot
/// (<see cref="Model.TypeDecl.Name"/>, <see cref="Model.FieldDecl.Name"/>, ...).
/// Results are valid C# identifiers (keywords escaped with <c>@</c>).
/// </remarks>
public interface INameMap
{
    /// <summary>C# namespace of the generated code (manifest <c>namespace</c>).</summary>
    string Namespace { get; }

    /// <summary>
    /// Records, enums and typedefs: <c>video_format_t</c> → <c>VideoFormat</c>,
    /// <c>video_format_t::dovi</c> → <c>VideoFormatDovi</c>, <c>es_format_t::@0</c> → <c>EsFormatAnon0</c>.
    /// </summary>
    string Type(string cName);

    /// <summary>
    /// A field. <paramref name="cDeclaringType"/> is <see cref="Merge.MergedField.DeclaringType"/>:
    /// for a member flattened out of an anonymous record it is the anonymous
    /// record, and the name is unique within the nearest named ancestor.
    /// </summary>
    string Field(string cDeclaringType, string cField);

    /// <summary>Enum member, unique within its enum: <c>ORIENT_TOP_LEFT</c> in <c>video_orientation_t</c> → <c>TopLeft</c>.</summary>
    string EnumMember(string cEnum, string cMember);

    /// <summary>Exported function grouped by prefix: <c>vlc_object_Log</c> → (<c>VlcObject</c>, <c>Log</c>).</summary>
    QualifiedName Function(string cName);

    /// <summary>Constant macro or anonymous-enum member, grouped per header: <c>VLC_CODEC_I420</c> → (<c>VlcFourcc</c>, <c>CodecI420</c>).</summary>
    QualifiedName Constant(string cName);

    /// <summary>Per-header class and file stem: <c>vlc_es.h</c> → <c>VlcEs</c> (file <c>VlcEs.g.cs</c>).</summary>
    string HeaderClass(string header);
}

/// <summary>A static class and a member in it.</summary>
public readonly record struct QualifiedName(string Class, string Member)
{
    public override string ToString() => $"{Class}.{Member}";
}

/// <summary>Two C symbols map to one C# name in the same scope.</summary>
/// <summary>Several <see cref="NameCollisionException"/>s, reported together so all overrides can be added at once.</summary>
public sealed class NameCollisionsException(IReadOnlyList<NameCollisionException> collisions)
    : Exception($"{collisions.Count} C# name collisions:\n  " + string.Join("\n  ", collisions.Select(c => c.Message)))
{
    public IReadOnlyList<NameCollisionException> Collisions { get; } = collisions;
}

public sealed class NameCollisionException(string scope, string csharpName, string firstCName, string secondCName)
    : Exception($"C# name '{csharpName}' in {scope} is produced by both '{firstCName}' and '{secondCName}'. Add an override to vlc.cimport.json.")
{
    public string Scope { get; } = scope;
    public string CSharpName { get; } = csharpName;
    public string FirstCName { get; } = firstCName;
    public string SecondCName { get; } = secondCName;
}
