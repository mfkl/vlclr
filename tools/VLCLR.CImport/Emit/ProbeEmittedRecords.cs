using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Emit;

/// <summary>
/// Which records the C# emitter writes: every non-anonymous record, plus
/// anonymous records named by a field that is not an anonymous member. Shared
/// by the C# emitter, the probe and the layout test emitters so all three
/// cover exactly the generated structs.
/// </summary>
internal static class EmittedRecords
{
    /// <summary>Emitted records in C name order; opaque ones only when <paramref name="includeOpaque"/>.</summary>
    public static IReadOnlyList<MergedType> Select(MergedSnapshot merged, bool includeOpaque = false)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in merged.Types.SelectMany(t => t.Fields.Where(f => !f.IsAnonymousMember)))
            CollectRecords(field.Type, named);

        return [.. merged.Types
            .Where(t => (includeOpaque || !t.IsOpaque) && (!t.IsAnonymous || named.Contains(t.Name)))
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

    /// <summary>Every record a field type names, through pointers, arrays, typedefs and function signatures.</summary>
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
}
