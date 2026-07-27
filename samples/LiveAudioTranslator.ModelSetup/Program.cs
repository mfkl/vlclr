using System.Security.Cryptography;
using System.Text.Json;
using Whisper.net.Ggml;

try
{
    string output = ParseOutput(args);
    string openVinoDirectory = Path.Combine(output, "openvino-tiny");
    string vadDirectory = Path.Combine(output, "silero-vad");
    Directory.CreateDirectory(openVinoDirectory);
    Directory.CreateDirectory(vadDirectory);

    await WhisperGgmlDownloader.Default
        .GetEncoderOpenVinoModelAsync(GgmlType.Tiny)
        .ExtractToPath(openVinoDirectory);

    string vadPath = Path.Combine(vadDirectory, "ggml-silero-v6.2.0.bin");
    string temporaryVadPath = vadPath + ".download";
    try
    {
        await using Stream source = await WhisperGgmlDownloader.Default
            .GetGgmlSileroVadModelAsync();
        await using (FileStream destination = File.Create(temporaryVadPath))
            await source.CopyToAsync(destination);
        File.Move(temporaryVadPath, vadPath, true);
    }
    finally
    {
        if (File.Exists(temporaryVadPath))
            File.Delete(temporaryVadPath);
    }

    await WriteManifestAsync(
        Path.Combine(openVinoDirectory, "model-manifest.json"),
        "whisper-openvino-encoder-tiny",
        openVinoDirectory);
    await WriteManifestAsync(
        Path.Combine(vadDirectory, "model-manifest.json"),
        "silero-vad-v6.2.0",
        vadDirectory);

    Console.WriteLine($"OpenVINO and Silero assets ready: {output}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
    return 1;
}

static string ParseOutput(string[] arguments)
{
    if (arguments.Length != 2 ||
        !string.Equals(arguments[0], "--output", StringComparison.Ordinal))
    {
        throw new ArgumentException("Usage: --output <whisper-model-directory>");
    }
    return Path.GetFullPath(arguments[1]);
}

static async Task WriteManifestAsync(string manifestPath, string modelId, string directory)
{
    string manifestFileName = Path.GetFileName(manifestPath);
    var files = new List<object>();
    foreach (string path in Directory.EnumerateFiles(directory)
                 .Where(path => !string.Equals(
                     Path.GetFileName(path),
                     manifestFileName,
                     StringComparison.OrdinalIgnoreCase))
                 .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
    {
        await using FileStream stream = File.OpenRead(path);
        files.Add(new
        {
            fileName = Path.GetFileName(path),
            size = stream.Length,
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream))
                .ToLowerInvariant()
        });
    }
    if (files.Count == 0)
        throw new InvalidDataException($"No model files were downloaded into '{directory}'.");

    var manifest = new
    {
        formatVersion = 1,
        modelId,
        files
    };
    await File.WriteAllTextAsync(
        manifestPath,
        JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}
