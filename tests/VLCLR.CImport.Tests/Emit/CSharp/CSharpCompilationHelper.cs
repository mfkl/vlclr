using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace VLCLR.CImport.Tests.Emit.CSharp;

/// <summary>
/// Compiles generated C# in memory: unsafe on, nullable on, warnings as errors,
/// against the running framework, with the real LibraryImport source generator.
/// </summary>
internal static class CSharpCompilationHelper
{
    // The library targets net8.0 and net10.0; C# 12 is what the net8.0 SDK offers.
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))]);

    private static readonly Lazy<ISourceGenerator> LibraryImportGenerator = new(LoadLibraryImportGenerator);

    public sealed record Result(IReadOnlyList<string> Diagnostics, byte[]? Image)
    {
        public Assembly Load() => Assembly.Load(Image ?? throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", Diagnostics)));
    }

    /// <summary>Compiles the files (plus optional hand-written sources) and reports every warning and error.</summary>
    public static Result Compile(IEnumerable<KeyValuePair<string, string>> files, IEnumerable<KeyValuePair<string, string>>? extraSources = null)
    {
        var trees = files.Concat(extraSources ?? [])
            .Select(f => CSharpSyntaxTree.ParseText(f.Value, ParseOptions, path: f.Key))
            .ToList();

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithAllowUnsafe(true)
            .WithNullableContextOptions(NullableContextOptions.Enable)
            .WithGeneralDiagnosticOption(ReportDiagnostic.Error);

        var compilation = CSharpCompilation.Create("Generated_" + Guid.NewGuid().ToString("N"), trees, References.Value, options);
        var driver = CSharpGeneratorDriver.Create([LibraryImportGenerator.Value], parseOptions: ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var diagnostics = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .Distinct()
            .ToList();
        if (diagnostics.Count > 0)
            return new Result(diagnostics, null);

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        return emit.Success
            ? new Result([], stream.ToArray())
            : new Result([.. emit.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString())], null);
    }

    private static ISourceGenerator LoadLibraryImportGenerator()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd('\\', '/');
        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
        var packs = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var runtimeVersion = Path.GetFileName(runtimeDirectory);

        var pack = Directory.GetDirectories(packs)
            .Select(d => (Path: d, Version: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
            .Where(p => p.Version is { } v && v.Major == Environment.Version.Major)
            .OrderByDescending(p => Path.GetFileName(p.Path) == runtimeVersion)
            .ThenByDescending(p => p.Version)
            .First().Path;

        var dll = Path.Combine(pack, "analyzers", "dotnet", "cs", "Microsoft.Interop.LibraryImportGenerator.dll");
        var type = Assembly.LoadFrom(dll).GetType("Microsoft.Interop.LibraryImportGenerator", throwOnError: true)!;
        return ((IIncrementalGenerator)Activator.CreateInstance(type)!).AsSourceGenerator();
    }
}
