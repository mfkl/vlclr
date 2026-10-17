using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Emit;

/// <summary>
/// Which records the C# emitter writes: every complete non-anonymous record, plus
/// anonymous records used as the type of a named field. Shared by the probe and
/// layout test emitters so both cover exactly the generated structs.
/// </summary>
internal static class EmittedRecords
{
    public static IReadOnlyList<MergedType> Select(MergedSnapshot merged)
    {
        var named = merged.Types
            .SelectMany(t => t.Fields.Where(f => !f.IsAnonymousMember))
            .Select(f => ElementRecord(f.Type))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return [.. merged.Types
            .Where(t => !t.IsOpaque && (!t.IsAnonymous || named.Contains(t.Name)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];
    }

    /// <summary>The record a by-value field type (possibly an array of it) refers to.</summary>
    public static string? ElementRecord(TypeRef type)
    {
        var t = type.Desugar();
        while (t.Kind == TypeRefKind.Array)
            t = t.Element!.Desugar();
        return t.Kind == TypeRefKind.Record ? t.Name : null;
    }
}
