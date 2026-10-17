using System.Text;
using VLCLR.CImport.Manifest;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Emit;

/// <summary>
/// <c>emit [--manifest m]</c>: reads <c>abi/windows.json</c> and <c>abi/sysv.json</c>
/// from the output directory and writes the generated C# (<c>*.g.cs</c>),
/// <c>cimport-report.md</c>, the C++ probe and the C# layout tests.
/// </summary>
public static class EmitCommand
{
    public const string LayoutTestsNamespace = "VLCLR.Tests.Interop";
    public const string LayoutTestsClass = "InteropLayoutTests";

    public static int Run(IReadOnlyList<string> args)
    {
        var cl = new CommandLine(args, "manifest");
        CImportManifest manifest;
        try
        {
            manifest = CImportManifest.Load(cl.ManifestPath);
        }
        catch (Exception e) when (e is ManifestException or IOException)
        {
            throw new CommandException(e.Message, e);
        }

        var written = Emit(manifest);
        foreach (var path in written)
            Console.WriteLine(Path.GetRelativePath(manifest.BaseDirectory, path));
        return 0;
    }

    /// <summary>Runs the whole emit stage and returns the paths it wrote.</summary>
    public static IReadOnlyList<string> Emit(CImportManifest manifest)
    {
        var outputDir = manifest.Resolve(manifest.Output.Directory);
        var snapshots = Abis.All
            .Select(abi => Path.Combine(outputDir, "abi", abi + ".json"))
            .Select(path => File.Exists(path)
                ? SnapshotJson.Load(path)
                : throw new CommandException($"Snapshot not found: {path}. Run extract for that ABI first."))
            .ToList();

        var merged = AbiMerger.Merge(snapshots);
        var errors = merged.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new CommandException("The snapshots do not merge:\n  " + string.Join("\n  ", errors));

        INameMap names;
        try
        {
            names = NameMap.Create(merged, manifest.Names, manifest.Output.Namespace);
        }
        catch (Exception e) when (e is NameCollisionException or StaleNameOverrideException or InvalidOperationException)
        {
            throw new CommandException(e.Message, e);
        }

        var csharp = new CSharpEmitter(merged, names, new CSharpEmitOptions(manifest.Output.Library)).Emit();
        var written = new List<string>();

        foreach (var stale in Directory.Exists(outputDir) ? Directory.GetFiles(outputDir, "*.g.cs") : [])
        {
            if (!csharp.Files.ContainsKey(Path.GetFileName(stale)))
                File.Delete(stale);
        }
        foreach (var (file, content) in csharp.Files)
            written.Add(Write(Path.Combine(outputDir, file), content));

        written.Add(Write(Path.Combine(outputDir, "cimport-report.md"), ReportWriter.Write(merged, csharp.Skipped)));

        if (manifest.Output.Probe is { } probe)
        {
            var prelude = Abis.All.ToDictionary(abi => abi, manifest.PreludeFor);
            written.Add(Write(manifest.Resolve(probe), new ProbeEmitter(merged, new ProbeOptions(manifest.Headers, prelude)).Emit()));
        }

        if (manifest.Output.LayoutTests is { } layoutTests)
        {
            var options = new LayoutTestOptions(LayoutTestsNamespace, LayoutTestsClass);
            written.Add(Write(manifest.Resolve(layoutTests), new LayoutTestEmitter(merged, names, options).Emit()));
        }

        return written;
    }

    private static string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
