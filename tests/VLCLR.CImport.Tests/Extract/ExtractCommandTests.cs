using VLCLR.CImport.Extract;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Extract;

[Collection("libclang")]
public sealed class ExtractCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vlclr-cimport-" + Guid.NewGuid().ToString("N"));

    public ExtractCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "include"));
        foreach (var header in new[] { "nested.h", "opaque.h", "fixture_types.h" })
            File.Copy(Path.Combine(FixtureExtractor.FixtureDirectory, header), Path.Combine(_directory, "include", header));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteManifest(string roots, string includeDir = "include", string prelude = "")
    {
        var path = Path.Combine(_directory, "vlc.cimport.json");
        File.WriteAllText(path, $$"""
            {
              "vlc": { "repository": "https://example.invalid/vlc.git", "commit": "abcdef12", "includeDir": "{{includeDir}}" },
              "headers": [ "nested.h", "opaque.h" ],
              "defines": [ "FROM_MANIFEST=1" ],
              "prelude": { {{prelude}} },
              "roots": [ {{roots}} ],
              "output": { "namespace": "Test", "library": "test", "directory": "out" }
            }
            """);
        return path;
    }

    [Theory]
    [InlineData(Abis.Windows, "x86_64-w64-windows-gnu")]
    [InlineData(Abis.SysV, "x86_64-pc-linux-gnu")]
    public void ManifestDrivesTheExtraction(string abi, string triple)
    {
        var manifest = WriteManifest("\"outer\", \"holder\"");
        var output = Path.Combine(_directory, "snapshots", $"{abi}.json");
        string[] sysroot = abi == Abis.Windows ? ["--sysroot", FakeMingwSysroot()] : [];

        Assert.Equal(0, ExtractCommand.Run(["--abi", abi, "--manifest", manifest, .. sysroot, "--out", output]));

        var snapshot = SnapshotJson.Load(output);
        Assert.Equal(abi, snapshot.Abi);
        Assert.Equal(triple, snapshot.Target);
        Assert.Equal("abcdef12", snapshot.VlcCommit);
        Assert.Equal(["hidden", "holder", "inner", "middle", "outer"], snapshot.Types.Select(t => t.Name));
        Assert.Equal("nested.h", snapshot.Types.Single(t => t.Name == "outer").Header);
        Assert.True(snapshot.Types.Single(t => t.Name == "hidden").IsOpaque);
        Assert.Equal(File.ReadAllText(output), SnapshotJson.Serialize(snapshot));
    }

    [Fact]
    public void WindowsNeedsAMingwSysroot()
    {
        var manifest = WriteManifest("\"outer\"");
        var notMingw = Path.Combine(_directory, "not-mingw");
        Directory.CreateDirectory(notMingw);

        var e = Assert.Throws<CommandException>(() => ExtractCommand.Run(
            ["--abi", Abis.Windows, "--manifest", manifest, "--sysroot", notMingw, "--out", Path.Combine(_directory, "w.json")]));
        Assert.Contains("not a mingw-w64 toolchain", e.Message);
    }

    // The fixture headers include nothing from the system: mingw's marker header is enough.
    private string FakeMingwSysroot()
    {
        var root = Path.Combine(_directory, "mingw");
        Directory.CreateDirectory(Path.Combine(root, "include"));
        File.WriteAllText(Path.Combine(root, "include", "_mingw.h"), "");
        return root;
    }

    [Fact]
    public void PreludeAndIncludeOverrideAreHonoured()
    {
        var other = Path.Combine(_directory, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "nested.h"), "typedef unsigned int tag_t; struct outer { tag_t tag; };\n");
        File.WriteAllText(Path.Combine(other, "opaque.h"), "struct holder { prelude_t value; };\n");
        var manifest = WriteManifest("\"outer\", \"holder\"", prelude: "\"sysv\": [ \"typedef long prelude_t;\" ]");
        var output = Path.Combine(_directory, "snapshot.json");

        ExtractCommand.Run(["--abi", "sysv", "--manifest", manifest, "--include", other, "--out", output]);

        var snapshot = SnapshotJson.Load(output);
        var holder = snapshot.Types.Single(t => t.Name == "holder");
        Assert.Equal("prelude_t", holder.Fields.Single().Type.Name);
        Assert.Equal(8, holder.Size);
    }

    [Fact]
    public void ManifestErrorsAreReportedAsCommandExceptions()
    {
        var output = Path.Combine(_directory, "snapshot.json");

        var unknownRoot = Assert.Throws<CommandException>(() =>
            ExtractCommand.Run(["--abi", "sysv", "--manifest", WriteManifest("\"missing_thing\""), "--out", output]));
        Assert.Contains("missing_thing", unknownRoot.Message);

        var missingDirectory = Assert.Throws<CommandException>(() =>
            ExtractCommand.Run(["--abi", "sysv", "--manifest", WriteManifest("\"outer\"", includeDir: "nowhere"), "--out", output]));
        Assert.Contains("nowhere", missingDirectory.Message);

        Assert.Throws<CommandException>(() => ExtractCommand.Run(["--abi", "arm", "--manifest", WriteManifest("\"outer\""), "--out", output]));
        Assert.Throws<CommandException>(() => ExtractCommand.Run(["--manifest", WriteManifest("\"outer\"")]));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void ClangErrorsListTheDiagnostics()
    {
        File.WriteAllText(Path.Combine(_directory, "include", "nested.h"), "struct outer { unknown_type member; };\n");
        var manifest = WriteManifest("\"outer\"");

        var e = Assert.Throws<CommandException>(() =>
            ExtractCommand.Run(["--abi", "sysv", "--manifest", manifest, "--out", Path.Combine(_directory, "snapshot.json")]));

        Assert.Contains("unknown_type", e.Message);
        Assert.Contains("nested.h", e.Message);
    }

    [Fact]
    public void ResourceDirectoryOptionIsValidated()
    {
        var e = Assert.Throws<CommandException>(() =>
            ExtractCommand.Run(["--abi", "sysv", "--manifest", WriteManifest("\"outer\""), "--resource-dir", _directory, "--out", Path.Combine(_directory, "s.json")]));

        Assert.Contains("resource", e.Message);
    }
}
