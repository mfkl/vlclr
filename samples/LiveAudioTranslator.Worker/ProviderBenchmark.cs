using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using VLCLR.LiveTranslation.Models;

namespace LiveAudioTranslator.Worker;

internal static class ProviderBenchmark
{
    public static async Task<int> RunAsync(WorkerCommandLine command)
    {
        var configuration = new VLCLR.LiveTranslation.Protocol.LiveConfigureMessage
        {
            SpeechModelId = "whisper-tiny-multilingual",
            TranslationModelId = "opus-mt-en-fr",
            SpeechProviderId = PackagedProviders.Speech,
            SpeechDeviceId = command.SpeechDeviceId,
            TranslationProviderId = PackagedProviders.Translation,
            SourceLanguage = "auto",
            TargetLanguage = "fr",
            SpeechThreads = 2,
            TranslationThreads = 1,
            InputDelayMilliseconds = 15_000,
            VadSilenceMilliseconds = 500,
            MaximumUtteranceMilliseconds = 6_000,
            EnergyVadThreshold = 0.012f
        };
        var timer = Stopwatch.StartNew();
        (WorkerPipeline pipeline, _) = await WorkerPipeline.CreateAsync(
            command.CatalogPath,
            configuration,
            CancellationToken.None).ConfigureAwait(false);
        timer.Stop();
        await pipeline.DisposeAsync().ConfigureAwait(false);

        string hardware = HardwareFingerprint.Capture();
        ModelProfileCatalog catalog = ModelProfileCatalog.Load(command.CatalogPath);
        ResolvedModelProfile speechProfile = catalog.Resolve(
            command.CatalogPath,
            "whisper-tiny-multilingual",
            "speech-to-english");
        ResolvedModelProfile translationProfile = catalog.Resolve(
            command.CatalogPath,
            "opus-mt-en-fr",
            "translation");
        string modelHashes = string.Join(
            "|",
            HashFile(speechProfile.ManifestPath),
            HashFile(translationProfile.ManifestPath));
        string tuning =
            $"{PackagedProviders.Speech}|{command.SpeechDeviceId}|" +
            $"{PackagedProviders.Translation}|2|1|" +
            $"Whisper.net-1.9.1|{PackagedProviders.TranslationRuntimeVersion}";
        string key = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(hardware + "|" + modelHashes + "|" + tuning))).ToLowerInvariant();
        var result = new
        {
            formatVersion = 1,
            profileKey = key,
            hardware,
            speechProvider = PackagedProviders.Speech,
            translationProvider = PackagedProviders.Translation,
            runtimeVersions = new
            {
                whisper = "1.9.1",
                onnxRuntime = PackagedProviders.TranslationRuntimeVersion
            },
            modelManifestHashes = modelHashes,
            tuning = new
            {
                speechThreads = 2,
                speechDevice = command.SpeechDeviceId,
                translationThreads = 1
            },
            initializationAndWarmupMilliseconds = timer.ElapsedMilliseconds,
            totalRealTimeFactor = 0d,
            qualityAccepted = false,
            qualified = false,
            note = "Run the representative quality and timing corpus before marking an accelerated provider qualified."
        };
        string? directory = Path.GetDirectoryName(command.BenchmarkOutputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            command.BenchmarkOutputPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        Console.WriteLine($"event=benchmark output={command.BenchmarkOutputPath} qualified=false");
        return 0;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal static class HardwareFingerprint
{
    public static string Capture()
    {
        string cpu =
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ??
            $"{Environment.ProcessorCount}-logical-processors";
        string driverFingerprint = "";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                driverFingerprint = CaptureWindowsDisplayDrivers();
            }
            catch
            {
                driverFingerprint = "display-driver-unavailable";
            }
        }
        return $"{Environment.OSVersion.VersionString}|{cpu}|{driverFingerprint}";
    }

    [SupportedOSPlatform("windows")]
    private static string CaptureWindowsDisplayDrivers()
    {
        using Microsoft.Win32.RegistryKey? classKey =
            Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (classKey == null)
            return "display-driver-unavailable";
        return string.Join(
            ";",
            classKey.GetSubKeyNames().Order(StringComparer.Ordinal).Select(name =>
            {
                using Microsoft.Win32.RegistryKey? adapter = classKey.OpenSubKey(name);
                return string.Join(
                    ",",
                    adapter?.GetValue("DriverDesc")?.ToString() ?? "",
                    adapter?.GetValue("DriverVersion")?.ToString() ?? "");
            }));
    }
}
