using System.Security.Cryptography;
using System.Text.Json;
using SubtitleTranslator;

namespace SubtitleTranslator.UnitTests;

public sealed class ModelManifestTests
{
    [Fact]
    public void LoadAndValidate_AcceptsCompleteHashedBundle()
    {
        using var bundle = TestModelBundle.Create();

        ModelManifest manifest = ModelManifest.LoadAndValidate(bundle.Path, "en", "fr");

        Assert.Equal("encoder.onnx", manifest.GetFile("encoder").FileName);
        Assert.Equal(3, manifest.Files.Count);
    }

    [Fact]
    public void LoadAndValidate_RejectsCorruptFileBeforeSessionCreation()
    {
        using var bundle = TestModelBundle.Create();
        File.AppendAllText(System.IO.Path.Combine(bundle.Path, "encoder.onnx"), "corrupt");

        ModelValidationException exception = Assert.Throws<ModelValidationException>(
            () => ModelManifest.LoadAndValidate(bundle.Path, "en", "fr"));

        Assert.Contains("size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAndValidate_RejectsWrongLanguagePair()
    {
        using var bundle = TestModelBundle.Create();

        Assert.Throws<ModelValidationException>(
            () => ModelManifest.LoadAndValidate(bundle.Path, "de", "fr", verifyHashes: false));
    }

    private sealed class TestModelBundle : IDisposable
    {
        public string Path { get; }

        private TestModelBundle(string path) => Path = path;

        public static TestModelBundle Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "vlclr-manifest-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var files = new[]
            {
                WriteFile(path, "encoder", "encoder.onnx", new byte[] { 1, 2, 3 }),
                WriteFile(path, "decoder", "decoder.onnx", new byte[] { 4, 5, 6 }),
                WriteFile(path, "tokenizer", "tokenizer.json", new byte[] { 7, 8, 9 })
            };
            var manifest = new
            {
                formatVersion = 1,
                sourceLanguage = "en",
                targetLanguage = "fr",
                modelFamily = "test",
                tokenizerType = "test",
                maximumSourceTokens = 8,
                maximumOutputTokens = 8,
                onnxRuntimeVersion = "test",
                source = new { repository = "test", revision = "test", license = "test", licenseUrl = "test" },
                tensors = new
                {
                    encoderInputs = Array.Empty<string>(),
                    encoderOutputs = Array.Empty<string>(),
                    decoderInputs = Array.Empty<string>(),
                    decoderOutputs = Array.Empty<string>()
                },
                files
            };
            File.WriteAllText(
                System.IO.Path.Combine(path, "model-manifest.json"),
                JsonSerializer.Serialize(manifest));
            return new TestModelBundle(path);
        }

        private static object WriteFile(string path, string role, string name, byte[] contents)
        {
            File.WriteAllBytes(System.IO.Path.Combine(path, name), contents);
            return new
            {
                role,
                fileName = name,
                size = contents.LongLength,
                sha256 = Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant(),
                sourcePath = name
            };
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
