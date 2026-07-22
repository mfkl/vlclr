using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubtitleTranslator;

/// <summary>
/// Versioned description of an offline translation model bundle.
/// </summary>
public sealed class ModelManifest
{
    public const int SupportedFormatVersion = 1;

    public int FormatVersion { get; init; }
    public string SourceLanguage { get; init; } = "";
    public string TargetLanguage { get; init; } = "";
    public string ModelFamily { get; init; } = "";
    public string TokenizerType { get; init; } = "";
    public int MaximumSourceTokens { get; init; }
    public int MaximumOutputTokens { get; init; }
    public string OnnxRuntimeVersion { get; init; } = "";
    public ModelSource Source { get; init; } = new();
    public ModelTensorContract Tensors { get; init; } = new();
    public List<ModelFile> Files { get; init; } = [];

    public static string ResolveModelDirectory(string modelDirectory, string sourceLanguage, string targetLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        string fullPath = Path.GetFullPath(modelDirectory);
        string pairPath = Path.Combine(fullPath, $"opus-mt-{sourceLanguage}-{targetLanguage}");
        return File.Exists(Path.Combine(pairPath, "model-manifest.json")) ? pairPath : fullPath;
    }

    public static ModelManifest LoadAndValidate(
        string modelDirectory,
        string? expectedSourceLanguage = null,
        string? expectedTargetLanguage = null,
        bool verifyHashes = true)
    {
        string fullDirectory = Path.GetFullPath(modelDirectory);
        string manifestPath = Path.Combine(fullDirectory, "model-manifest.json");
        if (!File.Exists(manifestPath))
            throw new ModelValidationException($"Model manifest not found: {manifestPath}");

        ModelManifest manifest;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            manifest = JsonSerializer.Deserialize(stream, ModelManifestJsonContext.Default.ModelManifest)
                ?? throw new ModelValidationException($"Model manifest is empty: {manifestPath}");
        }
        catch (ModelValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new ModelValidationException($"Could not read model manifest '{manifestPath}': {ex.Message}", ex);
        }

        manifest.ValidateMetadata(expectedSourceLanguage, expectedTargetLanguage);
        manifest.ValidateFiles(fullDirectory, verifyHashes);
        return manifest;
    }

    public ModelFile GetFile(string role)
    {
        var match = Files.SingleOrDefault(file => string.Equals(file.Role, role, StringComparison.Ordinal));
        return match ?? throw new ModelValidationException($"Model manifest does not define the '{role}' file.");
    }

    public string GetFilePath(string modelDirectory, string role) =>
        GetSafeFilePath(Path.GetFullPath(modelDirectory), GetFile(role).FileName);

    public void ValidateTensorNames(
        IEnumerable<string> encoderInputs,
        IEnumerable<string> encoderOutputs,
        IEnumerable<string> decoderInputs,
        IEnumerable<string> decoderOutputs)
    {
        RequireNames("encoder input", Tensors.EncoderInputs, encoderInputs);
        RequireNames("encoder output", Tensors.EncoderOutputs, encoderOutputs);
        RequireNames("decoder input", Tensors.DecoderInputs, decoderInputs);
        RequireNames("decoder output", Tensors.DecoderOutputs, decoderOutputs);
    }

    private void ValidateMetadata(string? expectedSourceLanguage, string? expectedTargetLanguage)
    {
        if (FormatVersion != SupportedFormatVersion)
            throw new ModelValidationException(
                $"Unsupported model manifest format {FormatVersion}; expected {SupportedFormatVersion}.");
        if (string.IsNullOrWhiteSpace(SourceLanguage) || string.IsNullOrWhiteSpace(TargetLanguage))
            throw new ModelValidationException("Model manifest must define sourceLanguage and targetLanguage.");
        if (!string.IsNullOrWhiteSpace(expectedSourceLanguage) &&
            !string.Equals(SourceLanguage, expectedSourceLanguage, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelValidationException(
                $"Model source language is '{SourceLanguage}', not requested '{expectedSourceLanguage}'.");
        }
        if (!string.IsNullOrWhiteSpace(expectedTargetLanguage) &&
            !string.Equals(TargetLanguage, expectedTargetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelValidationException(
                $"Model target language is '{TargetLanguage}', not requested '{expectedTargetLanguage}'.");
        }
        if (MaximumSourceTokens <= 0 || MaximumOutputTokens <= 0)
            throw new ModelValidationException("Model token limits must be positive.");
        if (Files.Count == 0)
            throw new ModelValidationException("Model manifest does not contain any files.");

        foreach (string requiredRole in new[] { "encoder", "decoder", "tokenizer" })
            _ = GetFile(requiredRole);
    }

    private void ValidateFiles(string modelDirectory, bool verifyHashes)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModelFile file in Files)
        {
            if (!roles.Add(file.Role))
                throw new ModelValidationException($"Duplicate model file role: {file.Role}");
            if (!names.Add(file.FileName))
                throw new ModelValidationException($"Duplicate model file name: {file.FileName}");
            if (file.Size <= 0)
                throw new ModelValidationException($"Invalid expected size for '{file.FileName}'.");
            if (file.Sha256.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new ModelValidationException($"Invalid SHA-256 for '{file.FileName}'.");

            string path = GetSafeFilePath(modelDirectory, file.FileName);
            if (!File.Exists(path))
                throw new ModelValidationException($"Required model file not found: {path}");

            long actualSize = new FileInfo(path).Length;
            if (actualSize != file.Size)
            {
                throw new ModelValidationException(
                    $"Model file '{file.FileName}' has size {actualSize}, expected {file.Size}.");
            }

            if (!verifyHashes)
                continue;

            using var stream = File.OpenRead(path);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelValidationException(
                    $"Model file '{file.FileName}' failed SHA-256 validation. " +
                    $"Expected {file.Sha256}, got {actualHash}.");
            }
        }
    }

    private static string GetSafeFilePath(string modelDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName) ||
            fileName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
        {
            throw new ModelValidationException($"Unsafe model file name: '{fileName}'.");
        }

        string path = Path.GetFullPath(Path.Combine(modelDirectory, fileName));
        string expectedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(modelDirectory));
        string actualParent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? "");
        if (!string.Equals(actualParent, expectedParent, StringComparison.OrdinalIgnoreCase))
            throw new ModelValidationException($"Model file escapes its bundle directory: '{fileName}'.");
        return path;
    }

    private static void RequireNames(string kind, IReadOnlyCollection<string> expected, IEnumerable<string> actual)
    {
        var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
        string[] missing = expected.Where(name => !actualSet.Contains(name)).ToArray();
        if (missing.Length > 0)
            throw new ModelValidationException($"Missing {kind} tensor(s): {string.Join(", ", missing)}");
    }
}

public sealed class ModelFile
{
    public string Role { get; init; } = "";
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public string SourcePath { get; init; } = "";
}

public sealed class ModelSource
{
    public string Repository { get; init; } = "";
    public string Revision { get; init; } = "";
    public string License { get; init; } = "";
    public string LicenseUrl { get; init; } = "";
}

public sealed class ModelTensorContract
{
    public List<string> EncoderInputs { get; init; } = [];
    public List<string> EncoderOutputs { get; init; } = [];
    public List<string> DecoderInputs { get; init; } = [];
    public List<string> DecoderOutputs { get; init; } = [];
}

public sealed class ModelValidationException : InvalidOperationException
{
    public ModelValidationException(string message) : base(message) { }
    public ModelValidationException(string message, Exception innerException) : base(message, innerException) { }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ModelManifest))]
internal partial class ModelManifestJsonContext : JsonSerializerContext;
