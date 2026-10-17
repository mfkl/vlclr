using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VLCLR.CImport.Manifest;

/// <summary>
/// vlc.cimport.json: which VLC headers to read, which symbols to keep and how
/// to name them. Relative paths are relative to the manifest file.
/// </summary>
public sealed record CImportManifest
{
    public required VlcSource Vlc { get; init; }

    /// <summary>Headers included, in this order, by the single translation unit extract parses (<c>vlc_common.h</c> first).</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>Preprocessor definitions for every ABI, <c>NAME</c> or <c>NAME=VALUE</c>.</summary>
    public IReadOnlyList<string> Defines { get; init; } = [];

    /// <summary>Extra definitions per ABI name (<c>windows</c>, <c>sysv</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AbiDefines { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Source lines placed before the headers in the translation unit, per ABI
    /// (for example <c>typedef long long ssize_t;</c> on windows, where the
    /// MSVC CRT has no ssize_t).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Prelude { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Symbols to keep: record tags, typedefs, enums, functions or constant
    /// macros, by C name. A trailing <c>*</c> matches a prefix
    /// (<c>VLC_CODEC_*</c>). Types reachable from a root are kept too. A root
    /// that matches nothing is an error.
    /// </summary>
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>Symbols removed even when a root or the transitive closure reaches them. Same pattern syntax.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    public required Output Output { get; init; }

    public NameOverrides Names { get; init; } = new();

    public static CImportManifest Load(string path)
    {
        var manifest = Parse(File.ReadAllText(path, Encoding.UTF8));
        return manifest with { BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))! };
    }

    public static CImportManifest Parse(string json)
    {
        CImportManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CImportManifest>(json, JsonOptions)
                       ?? throw new ManifestException("Manifest is empty.");
        }
        catch (JsonException e)
        {
            throw new ManifestException($"Manifest is not valid: {e.Message}", e);
        }
        manifest.Validate();
        return manifest;
    }

    /// <summary>Directory the manifest was loaded from; relative paths resolve against it.</summary>
    [JsonIgnore]
    public string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();

    public string Resolve(string relativePath) => Path.GetFullPath(Path.Combine(BaseDirectory, relativePath));

    /// <summary>Defines for <paramref name="abi"/>: common ones first, then the ABI's own.</summary>
    public IReadOnlyList<string> DefinesFor(string abi)
        => [.. Defines, .. AbiDefines.TryGetValue(abi, out var extra) ? extra : []];

    public IReadOnlyList<string> PreludeFor(string abi)
        => Prelude.TryGetValue(abi, out var lines) ? lines : [];

    public static bool Matches(string pattern, string name)
        => pattern.EndsWith('*')
            ? name.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(pattern, name, StringComparison.Ordinal);

    public bool IsExcluded(string name) => Excludes.Any(p => Matches(p, name));

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Vlc.Commit) || Vlc.Commit.Length < 8 || !Vlc.Commit.All(Uri.IsHexDigit))
            throw new ManifestException($"vlc.commit must be a hexadecimal commit id of at least 8 characters, got '{Vlc.Commit}'.");
        if (Headers.Count == 0)
            throw new ManifestException("headers must list at least one header.");
        if (Roots.Count == 0)
            throw new ManifestException("roots must list at least one symbol.");

        foreach (var (field, patterns) in new[] { ("roots", Roots), ("excludes", Excludes) })
        {
            foreach (var p in patterns)
            {
                var star = p.IndexOf('*');
                if (string.IsNullOrWhiteSpace(p) || p == "*" || (star >= 0 && star != p.Length - 1))
                    throw new ManifestException($"{field}: '{p}' is not a valid pattern (a C name, optionally ending in '*').");
            }
        }

        foreach (var (field, duplicates) in new[] { ("headers", Headers), ("roots", Roots), ("excludes", Excludes) })
        {
            var dup = duplicates.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (dup is not null)
                throw new ManifestException($"{field}: '{dup.Key}' is listed twice.");
        }

        foreach (var abi in AbiDefines.Keys.Concat(Prelude.Keys))
        {
            if (!Model.Abis.All.Contains(abi))
                throw new ManifestException($"Unknown ABI '{abi}'. Known: {string.Join(", ", Model.Abis.All)}.");
        }

        if (string.IsNullOrWhiteSpace(Output.Namespace))
            throw new ManifestException("output.namespace is required.");
        if (string.IsNullOrWhiteSpace(Output.Library))
            throw new ManifestException("output.library is required.");
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}

public sealed record VlcSource
{
    public required string Repository { get; init; }

    /// <summary>Pinned commit (full or abbreviated hash).</summary>
    public required string Commit { get; init; }

    /// <summary>Directory holding the VLC headers (<c>vlc_common.h</c>), filled by fetch-headers.</summary>
    public required string IncludeDir { get; init; }
}

public sealed record Output
{
    public required string Namespace { get; init; }

    /// <summary>Native library name for <c>LibraryImport</c>.</summary>
    public required string Library { get; init; }

    /// <summary>Generated C# (<c>*.g.cs</c>), <c>abi/*.json</c> and <c>cimport-report.md</c>.</summary>
    public required string Directory { get; init; }

    /// <summary>Generated C++ ABI probe file.</summary>
    public string? Probe { get; init; }

    /// <summary>Generated C# layout test file.</summary>
    public string? LayoutTests { get; init; }
}

/// <summary>
/// Explicit C# names, by C name. They win over the naming rules and still take
/// part in collision checks.
/// </summary>
public sealed record NameOverrides
{
    /// <summary>Records, enums, typedefs (synthesised anonymous names allowed).</summary>
    public IReadOnlyDictionary<string, string> Types { get; init; } = new Dictionary<string, string>();

    /// <summary>Key <c>record.field</c> (record as in the snapshot).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Key <c>enum.MEMBER</c>.</summary>
    public IReadOnlyDictionary<string, string> EnumMembers { get; init; } = new Dictionary<string, string>();

    /// <summary>Value <c>Class.Member</c>.</summary>
    public IReadOnlyDictionary<string, string> Functions { get; init; } = new Dictionary<string, string>();

    /// <summary>Value <c>Class.Member</c>.</summary>
    public IReadOnlyDictionary<string, string> Constants { get; init; } = new Dictionary<string, string>();

    /// <summary>Header file name to class name.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

public sealed class ManifestException(string message, Exception? inner = null) : Exception(message, inner);
