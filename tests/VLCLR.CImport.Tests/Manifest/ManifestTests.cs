using VLCLR.CImport.Manifest;

namespace VLCLR.CImport.Tests.Manifest;

public class ManifestTests
{
    private const string Valid = """
        {
          // comments are allowed
          "vlc": { "repository": "https://code.videolan.org/videolan/vlc.git", "commit": "dc69496e", "includeDir": "include" },
          "headers": [ "vlc_common.h", "vlc_es.h" ],
          "defines": [ "A=1" ],
          "abiDefines": { "windows": [ "B" ] },
          "prelude": { "windows": [ "typedef long long ssize_t;" ] },
          "roots": [ "filter_t", "VLC_CODEC_*" ],
          "excludes": [ "vlc_object_internals" ],
          "output": { "namespace": "VLCLR.Native.Interop", "library": "libvlccore", "directory": "out" },
          "names": { "types": { "vlc_object_t": "VlcObject" }, "functions": { "var_Create": "Var.Create" } },
        }
        """;

    [Fact]
    public void ParsesEveryField()
    {
        var m = CImportManifest.Parse(Valid);

        Assert.Equal("dc69496e", m.Vlc.Commit);
        Assert.Equal(["vlc_common.h", "vlc_es.h"], m.Headers);
        Assert.Equal(["A=1", "B"], m.DefinesFor("windows"));
        Assert.Equal(["A=1"], m.DefinesFor("sysv"));
        Assert.Equal(["typedef long long ssize_t;"], m.PreludeFor("windows"));
        Assert.Empty(m.PreludeFor("sysv"));
        Assert.Equal("VlcObject", m.Names.Types["vlc_object_t"]);
        Assert.Equal("Var.Create", m.Names.Functions["var_Create"]);
        Assert.Empty(m.Names.Fields);
        Assert.Null(m.Output.Probe);
    }

    [Theory]
    [InlineData("VLC_CODEC_*", "VLC_CODEC_I420", true)]
    [InlineData("VLC_CODEC_*", "VLC_CODE", false)]
    [InlineData("filter_t", "filter_t", true)]
    [InlineData("filter_t", "filter_tt", false)]
    public void PatternMatching(string pattern, string name, bool expected)
        => Assert.Equal(expected, CImportManifest.Matches(pattern, name));

    [Fact]
    public void ExcludesUsePatterns()
    {
        var m = CImportManifest.Parse(Valid);

        Assert.True(m.IsExcluded("vlc_object_internals"));
        Assert.False(m.IsExcluded("filter_t"));
    }

    [Fact]
    public void ResolvesPathsAgainstManifestDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cimport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "vlc.cimport.json");
            File.WriteAllText(path, Valid);

            var m = CImportManifest.Load(path);

            Assert.Equal(Path.Combine(dir, "include"), m.Resolve(m.Vlc.IncludeDir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StarAloneKeepsEveryRoot()
    {
        var m = CImportManifest.Parse(Valid.Replace("\"roots\": [ \"filter_t\", \"VLC_CODEC_*\" ]", "\"roots\": [ \"*\" ]"));

        Assert.True(m.AllRoots);
        Assert.Null(m.RootPatterns);
        Assert.False(CImportManifest.Parse(Valid).AllRoots);
    }

    [Fact]
    public void StarInHeadersExpandsToTheOtherHeadersInNameOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cimport-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var h in new[] { "vlc_common.h", "vlc_es.h", "vlc_b.h", "vlc_a.h", "vlc_skip.h" })
                File.WriteAllText(Path.Combine(dir, h), "");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "");
            var m = CImportManifest.Parse(Valid.Replace(
                "\"headers\": [ \"vlc_common.h\", \"vlc_es.h\" ],",
                "\"headers\": [ \"vlc_common.h\", \"*\", \"vlc_es.h\" ], \"excludeHeaders\": [ \"vlc_skip.h\" ],"));

            Assert.Equal(["vlc_common.h", "vlc_a.h", "vlc_b.h", "vlc_es.h"], m.ExpandHeaders(dir));
            Assert.Equal(["vlc_common.h", "vlc_es.h"], CImportManifest.Parse(Valid).ExpandHeaders(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"commit\": \"dc69496e\"", "\"commit\": \"main\"", "vlc.commit")]
    [InlineData("\"headers\": [ \"vlc_common.h\", \"vlc_es.h\" ]", "\"headers\": [ \"vlc_common.h\" ], \"excludeHeaders\": [ \"vlc_es.h\" ]", "needs '*'")]
    [InlineData("\"headers\": [ \"vlc_common.h\", \"vlc_es.h\" ]", "\"headers\": [ \"*\", \"*\" ]", "listed twice")]
    [InlineData("\"roots\": [ \"filter_t\", \"VLC_CODEC_*\" ]", "\"roots\": []", "roots")]
    [InlineData("\"roots\": [ \"filter_t\", \"VLC_CODEC_*\" ]", "\"roots\": [ \"VLC_*_X\" ]", "not a valid pattern")]
    [InlineData("\"roots\": [ \"filter_t\", \"VLC_CODEC_*\" ]", "\"roots\": [ \"*\", \"filter_t\" ]", "not a valid pattern")]
    [InlineData("\"roots\": [ \"filter_t\", \"VLC_CODEC_*\" ]", "\"roots\": [ \"a\", \"a\" ]", "listed twice")]
    [InlineData("\"headers\": [ \"vlc_common.h\", \"vlc_es.h\" ]", "\"headers\": []", "headers")]
    [InlineData("\"abiDefines\": { \"windows\"", "\"abiDefines\": { \"macos\"", "Unknown ABI")]
    [InlineData("\"defines\"", "\"defynes\"", "not valid")]
    [InlineData("\"library\": \"libvlccore\", ", "", "not valid")]
    public void InvalidManifestsAreRejectedWithAReason(string find, string replace, string reason)
    {
        Assert.Contains(find, Valid);

        var e = Assert.Throws<ManifestException>(() => CImportManifest.Parse(Valid.Replace(find, replace)));

        Assert.Contains(reason, e.Message);
    }

    [Fact]
    public void ShippedManifestLoads()
    {
        var m = CImportManifest.Load(Path.Combine(AppContext.BaseDirectory, "vlc.cimport.json"));

        Assert.Equal("vlc_common.h", m.Headers[0]);
        Assert.Equal("VLCLR.Native.Interop", m.Output.Namespace);
    }
}
