using System.Runtime.CompilerServices;
using VLCLR.CImport.Extract;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Extract;

/// <summary>Extracts the synthetic fixture headers; they need no system headers, so both ABIs work on any host.</summary>
internal static class FixtureExtractor
{
    public const string UpdateVariable = "CIMPORT_UPDATE_GOLDEN";

    public static string FixtureDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>The fixture directory in the source tree, where updated goldens are written.</summary>
    public static string SourceFixtureDirectory([CallerFilePath] string callerFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "Fixtures"));

    public static IEnumerable<string> FixtureNames { get; } = Directory.GetFiles(FixtureDirectory, "*.h")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(n => n != "fixture_types")
        .Order(StringComparer.Ordinal)
        .ToList()!;

    public static ExtractOptions Options(string name, string abi, IReadOnlyList<string>? roots = null, IReadOnlyList<string>? excludes = null)
        => new()
        {
            Abi = abi,
            VlcCommit = "fixture",
            Headers = [name + ".h"],
            IncludeDirs = [FixtureDirectory],
            ExtraArgs = ["-nostdinc", "-ffreestanding"],
            Roots = roots,
            Excludes = excludes ?? [],
        };

    public static Snapshot Extract(string name, string abi, IReadOnlyList<string>? roots = null, IReadOnlyList<string>? excludes = null)
        => SnapshotExtractor.Extract(Options(name, abi, roots, excludes));

    public static string ExtractJson(string name, string abi) => SnapshotJson.Serialize(Extract(name, abi));
}
