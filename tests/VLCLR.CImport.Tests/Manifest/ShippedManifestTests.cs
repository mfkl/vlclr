using System.Text.RegularExpressions;
using VLCLR.CImport.Manifest;

namespace VLCLR.CImport.Tests.Manifest;

/// <summary>Checks on tools/VLCLR.CImport/vlc.cimport.json as committed.</summary>
public class ShippedManifestTests
{
    private static readonly CImportManifest Manifest = CImportManifest.Load(
        Path.Combine(AppContext.BaseDirectory, "vlc.cimport.json"));

    [Fact]
    public void CommitIsTheFullPinnedSha()
    {
        Assert.Equal(40, Manifest.Vlc.Commit.Length);
        Assert.Matches("^[0-9a-f]{40}$", Manifest.Vlc.Commit);
        Assert.StartsWith("dc69496e", Manifest.Vlc.Commit);
    }

    [Fact]
    public void HeadersAreVlcHeadersWithCommonFirst()
    {
        Assert.Equal("vlc_common.h", Manifest.Headers[0]);
        Assert.All(Manifest.Headers, h => Assert.Matches(@"^vlc_[a-z0-9_]+\.h$", h));
    }

    [Fact]
    public void ExactRootsAreNotAlsoCoveredByAPrefixRoot()
    {
        var prefixes = Manifest.Roots.Where(r => r.EndsWith('*')).ToList();

        foreach (var exact in Manifest.Roots.Where(r => !r.EndsWith('*')))
        {
            var covering = prefixes.FirstOrDefault(p => CImportManifest.Matches(p, exact));
            Assert.True(covering is null, $"Root '{exact}' is already matched by '{covering}'.");
        }
    }

    [Fact]
    public void EveryExcludeRemovesSomethingTheRootsWouldKeep()
    {
        foreach (var exclude in Manifest.Excludes)
        {
            Assert.True(
                Manifest.Roots.Any(r => CImportManifest.Matches(r, exclude)),
                $"Exclude '{exclude}' is not matched by any root.");
        }
    }

    [Fact]
    public void ObjectTypeDoesNotBecomeSystemObject()
    {
        Assert.NotEqual("Object", Manifest.Names.Types.GetValueOrDefault("vlc_object_t", "Object"));
    }

    /// <summary>
    /// With the headers fetched (fetch-headers.ps1/.sh), every header exists and every root
    /// shows up in them: as a #define for constants, as a word for types and functions.
    /// A cheap stand-in for extract's own "a root that matches nothing is an error".
    /// Does nothing when the headers have not been fetched.
    /// </summary>
    [Fact]
    public void HeadersAndRootsExistInFetchedHeaders()
    {
        var includeDir = FindIncludeDir();
        if (includeDir is null)
            return;

        foreach (var header in Manifest.Headers)
            Assert.True(File.Exists(Path.Combine(includeDir, header)), $"{header} is not in {includeDir}.");

        var text = string.Join('\n', Directory
            .EnumerateFiles(includeDir, "*.h", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        var defines = Regex.Matches(text, @"^[ \t]*#[ \t]*define[ \t]+([A-Za-z_]\w*)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var words = Regex.Matches(text, @"[A-Za-z_]\w*")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var root in Manifest.Roots)
        {
            var found = root.EndsWith('*')
                ? defines.Any(d => CImportManifest.Matches(root, d))
                : words.Contains(root);
            Assert.True(found, $"Root '{root}' matches nothing in {includeDir}.");
        }
    }

    private static string? FindIncludeDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var manifest = Path.Combine(dir.FullName, "tools", "VLCLR.CImport", "vlc.cimport.json");
            if (File.Exists(manifest))
            {
                var includeDir = CImportManifest.Load(manifest) is { } m ? m.Resolve(m.Vlc.IncludeDir) : null;
                return includeDir is not null && File.Exists(Path.Combine(includeDir, "vlc_common.h")) ? includeDir : null;
            }
        }
        return null;
    }
}
