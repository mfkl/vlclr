using System.Security.Cryptography;
using System.Text.Json;

namespace LiveAudioTranslator.Worker;

internal sealed record ValidatedRuntimeAsset(string FileName, string FullPath, string Sha256);

internal static class PackagedRuntimeAssets
{
    public static IReadOnlyList<ValidatedRuntimeAsset> LoadAndValidate(
        string manifestPath,
        string expectedModelId)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported runtime-asset manifest version.");
        if (!string.Equals(
                root.GetProperty("modelId").GetString(),
                expectedModelId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected runtime-asset model ID.");
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException("Runtime-asset manifest has no parent directory.");
        var result = new List<ValidatedRuntimeAsset>();
        foreach (JsonElement file in root.GetProperty("files").EnumerateArray())
        {
            string fileName = file.GetProperty("fileName").GetString() ?? "";
            if (!IsSafeFileName(fileName))
                throw new InvalidDataException("Runtime-asset manifest contains an unsafe file name.");
            string path = Path.Combine(directory, fileName);
            long expectedSize = file.GetProperty("size").GetInt64();
            if (!File.Exists(path) || new FileInfo(path).Length != expectedSize)
                throw new InvalidDataException($"Runtime asset '{fileName}' failed size validation.");
            string expectedHash = file.GetProperty("sha256").GetString() ?? "";
            using FileStream stream = File.OpenRead(path);
            string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Runtime asset '{fileName}' failed hash validation.");
            result.Add(new ValidatedRuntimeAsset(fileName, path, actualHash));
        }
        if (result.Count == 0)
            throw new InvalidDataException("Runtime-asset manifest contains no files.");
        return result;
    }

    private static bool IsSafeFileName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        !Path.IsPathRooted(value) &&
        value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
