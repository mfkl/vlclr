using VLCLR.CImport.Merge;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Emit;

/// <summary>Settings of the C# emitter.</summary>
/// <param name="Library">Library name used in <c>[LibraryImport]</c>, e.g. <c>libvlccore</c>.</param>
public sealed record CSharpEmitOptions(string Library);

/// <summary>A symbol the emitter left out and why (for cimport-report.md).</summary>
/// <param name="Symbol">C symbol; members are <c>record.field</c> or <c>enum.MEMBER</c>.</param>
/// <param name="Header">Header that declares the symbol.</param>
/// <param name="Kind"><c>field</c>, <c>enum</c>, <c>enum member</c>, <c>constant</c> or <c>function</c>.</param>
/// <param name="Reason">Why nothing was written.</param>
public sealed record SkippedSymbol(string Symbol, string Header, string Kind, string Reason);

/// <summary>Generated files (file name to content, ordinal order) and everything that was not emitted.</summary>
public sealed record CSharpEmitResult(IReadOnlyDictionary<string, string> Files, IReadOnlyList<SkippedSymbol> Skipped);

/// <summary>
/// Writes the merged snapshot as C#: one <c>{HeaderClass}.g.cs</c> per header
/// plus <c>CImport.g.cs</c> with the shared helpers. Throws when the merge
/// reported errors or when two symbols would get the same C# name.
/// </summary>
public sealed class CSharpEmitter(MergedSnapshot merged, INameMap names, CSharpEmitOptions options)
{
    public CSharpEmitResult Emit() => new CSharpGeneration(merged, names, options).Run();
}
