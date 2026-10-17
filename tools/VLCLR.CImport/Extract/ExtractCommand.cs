using VLCLR.CImport.Manifest;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Extract;

/// <summary>
/// <c>extract --abi &lt;windows|sysv&gt; [--manifest m] [--include dir] [--resource-dir dir] --out snapshot.json</c>
/// </summary>
public static class ExtractCommand
{
    public const string Usage = """
        extract --abi <windows|sysv> [--manifest <vlc.cimport.json>] [--include <dir>] [--resource-dir <dir>] --out <snapshot.json>
          --include       VLC header directory (default: vlc.includeDir of the manifest)
          --resource-dir  clang resource directory holding the builtin headers (include/stddef.h ...).
                          The libclang NuGet package ships without them. Default: $CIMPORT_RESOURCE_DIR,
                          then an installed LLVM (C:\Program Files\LLVM, Visual Studio's LLVM, /usr/lib/llvm-*).
        """;

    public static int Run(IReadOnlyList<string> args)
    {
        CommandLine commandLine;
        string abi, outPath;
        try
        {
            commandLine = new CommandLine(args, "abi", "manifest", "include", "resource-dir", "out");
            abi = commandLine.Require("abi");
            outPath = commandLine.Require("out");
        }
        catch (CommandException e)
        {
            throw new CommandException(e.Message + "\n" + Usage, e);
        }
        var triple = AbiTargets.TripleFor(abi);

        CImportManifest manifest;
        try
        {
            manifest = CImportManifest.Load(commandLine.ManifestPath);
        }
        catch (Exception e) when (e is ManifestException or IOException)
        {
            throw new CommandException($"Cannot load manifest '{commandLine.ManifestPath}': {e.Message}", e);
        }

        var includeDir = commandLine.Get("include") is { } given ? Path.GetFullPath(given) : manifest.Resolve(manifest.Vlc.IncludeDir);
        if (!Directory.Exists(includeDir))
            throw new CommandException($"Include directory '{includeDir}' does not exist. Run fetch-headers or pass --include.");

        var resourceDir = commandLine.Get("resource-dir");
        if (resourceDir is not null && !ResourceDirectory.IsResourceDir(resourceDir))
            throw new CommandException($"'{resourceDir}' is not a clang resource directory (no include/stddef.h).");
        resourceDir ??= ResourceDirectory.Find();

        var snapshot = SnapshotExtractor.Extract(new ExtractOptions
        {
            Abi = abi,
            Target = triple,
            VlcCommit = manifest.Vlc.Commit,
            Headers = manifest.Headers,
            IncludeDirs = [includeDir],
            Defines = manifest.DefinesFor(abi),
            Prelude = manifest.PreludeFor(abi),
            ResourceDir = resourceDir,
            Roots = manifest.Roots,
            Excludes = manifest.Excludes,
        });

        SnapshotJson.Save(snapshot, outPath);
        Console.WriteLine($"{abi}: {snapshot.Types.Count} records, {snapshot.Enums.Count} enums, {snapshot.Typedefs.Count} typedefs, "
                          + $"{snapshot.Functions.Count} functions, {snapshot.Constants.Count} constants, {snapshot.Skipped.Count} skipped -> {outPath}");
        return 0;
    }
}

/// <summary>Extracts a <see cref="Snapshot"/> from C headers with libclang.</summary>
public static class SnapshotExtractor
{
    public static Snapshot Extract(ExtractOptions options)
    {
        using var extraction = new Extraction(options);
        return extraction.Run();
    }
}
