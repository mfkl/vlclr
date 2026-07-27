using System.Text.Json;

namespace VLCLR.LiveTranslation.Models;

public sealed record ModelProfile
{
    public required string Id { get; init; }
    public required string Task { get; init; }
    public required string AdapterId { get; init; }
    public required string ModelFamily { get; init; }
    public required string ManifestPath { get; init; }
    public required string[] SupportedLanguages { get; init; }
    public required string[] CompatibleProviders { get; init; }
    public required string[] RuntimeRequirements { get; init; }
    public Dictionary<string, string> DefaultTuning { get; init; } = [];
}

public sealed record ResolvedModelProfile(
    ModelProfile Profile,
    string ManifestPath,
    string ModelDirectory);

public sealed class ModelProfileCatalog
{
    public const int SupportedFormatVersion = 1;
    public int FormatVersion { get; init; }
    public List<ModelProfile> Profiles { get; init; } = [];

    public static ModelProfileCatalog Load(string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        string fullPath = Path.GetFullPath(catalogPath);
        using var stream = File.OpenRead(fullPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        ModelProfileCatalog catalog = JsonSerializer.Deserialize<ModelProfileCatalog>(stream, options)
            ?? throw new InvalidDataException($"Model profile catalog is empty: {fullPath}");
        catalog.Validate(fullPath);
        return catalog;
    }

    public ResolvedModelProfile Resolve(string catalogPath, string id, string expectedTask)
    {
        string fullCatalogPath = Path.GetFullPath(catalogPath);
        ModelProfile profile = Profiles.SingleOrDefault(
            candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Unknown model profile '{id}'.");
        if (!string.Equals(profile.Task, expectedTask, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Model profile '{id}' has task '{profile.Task}', expected '{expectedTask}'.");

        string root = Path.GetDirectoryName(fullCatalogPath)
            ?? throw new InvalidDataException("Model catalog has no parent directory.");
        string manifest = ResolveSafePath(root, profile.ManifestPath);
        if (!File.Exists(manifest))
            throw new FileNotFoundException($"Profile manifest not found for '{id}'.", manifest);
        return new ResolvedModelProfile(profile, manifest, Path.GetDirectoryName(manifest)!);
    }

    private void Validate(string catalogPath)
    {
        if (FormatVersion != SupportedFormatVersion)
            throw new InvalidDataException(
                $"Unsupported model profile format {FormatVersion}; expected {SupportedFormatVersion}.");
        if (Profiles.Count == 0)
            throw new InvalidDataException("Model profile catalog contains no profiles.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        string root = Path.GetDirectoryName(catalogPath)!;
        foreach (ModelProfile profile in Profiles)
        {
            if (!IsSafeIdentifier(profile.Id) || !IsSafeIdentifier(profile.Task) ||
                !IsSafeIdentifier(profile.AdapterId))
            {
                throw new InvalidDataException("Model profiles contain an invalid identifier.");
            }
            if (!ids.Add(profile.Id))
                throw new InvalidDataException($"Duplicate model profile ID '{profile.Id}'.");
            if (profile.CompatibleProviders.Length == 0)
                throw new InvalidDataException($"Profile '{profile.Id}' has no compatible providers.");
            _ = ResolveSafePath(root, profile.ManifestPath);
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string ResolveSafePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Unsafe profile manifest path '{relativePath}'.");
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Profile manifest escapes the catalog root: '{relativePath}'.");
        return candidate;
    }
}
