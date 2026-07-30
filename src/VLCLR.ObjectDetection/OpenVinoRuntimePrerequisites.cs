using System.Diagnostics;

namespace VLCLR.ObjectDetection;

/// <summary>
/// Result of inspecting the native files required by the OpenVINO GPU path.
/// </summary>
public sealed record OpenVinoRuntimeInspection(
    string? RuntimeDirectory,
    string? RuntimeVersion,
    string? TbbPath,
    IReadOnlyList<string> Problems)
{
    public bool IsUsable => Problems.Count == 0;
}

/// <summary>
/// Validates the OpenVINO runtime layout used by the YOLOX sample.
/// </summary>
public static class OpenVinoRuntimePrerequisites
{
    public const string SupportedVersion = "2026.2.1";

    private static readonly string[] RequiredRuntimeFiles =
    [
        "openvino_c.dll",
        "openvino.dll",
        "openvino_intel_gpu_plugin.dll"
    ];

    public static OpenVinoRuntimeInspection Inspect(
        string? configuredRuntimeDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredRuntimeDirectory))
        {
            return new OpenVinoRuntimeInspection(
                null,
                null,
                null,
                [
                    "No runtime directory was configured. Set " +
                    "--dotnet-yolo-search-runtime-dir=<directory> or " +
                    "OPENVINO_RUNTIME_DIR."
                ]);
        }

        string runtimeDirectory;
        try
        {
            runtimeDirectory = Path.GetFullPath(
                configuredRuntimeDirectory.Trim());
        }
        catch (Exception exception)
        {
            return new OpenVinoRuntimeInspection(
                configuredRuntimeDirectory,
                null,
                null,
                [$"The runtime path is invalid: {exception.Message}"]);
        }

        if (!Directory.Exists(runtimeDirectory))
        {
            return new OpenVinoRuntimeInspection(
                runtimeDirectory,
                null,
                null,
                [$"Runtime directory not found: {runtimeDirectory}"]);
        }

        var problems = new List<string>();
        foreach (string fileName in RequiredRuntimeFiles)
        {
            if (!File.Exists(Path.Combine(runtimeDirectory, fileName)))
            {
                problems.Add(
                    $"Required runtime file is missing: {fileName}");
            }
        }

        string flatTbbPath = Path.Combine(
            runtimeDirectory,
            "tbb12.dll");
        string archiveTbbPath = Path.GetFullPath(
            Path.Combine(
                runtimeDirectory,
                "..",
                "..",
                "..",
                "3rdparty",
                "tbb",
                "bin",
                "tbb12.dll"));
        string? tbbPath = File.Exists(flatTbbPath)
            ? flatTbbPath
            : File.Exists(archiveTbbPath)
                ? archiveTbbPath
                : null;
        if (tbbPath is null)
        {
            problems.Add(
                "Required runtime file is missing: tbb12.dll " +
                "(checked the runtime directory and the standard " +
                "OpenVINO 3rdparty/tbb/bin layout).");
        }

        string openVinoCPath = Path.Combine(
            runtimeDirectory,
            "openvino_c.dll");
        string? runtimeVersion = null;
        if (File.Exists(openVinoCPath))
        {
            try
            {
                runtimeVersion = FileVersionInfo
                    .GetVersionInfo(openVinoCPath)
                    .FileVersion;
            }
            catch (Exception exception)
            {
                problems.Add(
                    "Could not read the OpenVINO runtime version from " +
                    $"openvino_c.dll: {exception.Message}");
            }

            if (string.IsNullOrWhiteSpace(runtimeVersion))
            {
                problems.Add(
                    "openvino_c.dll does not expose a readable file version.");
            }
            else if (!IsSupportedVersion(runtimeVersion))
            {
                problems.Add(
                    $"Unsupported OpenVINO runtime {runtimeVersion}; " +
                    $"this sample requires {SupportedVersion}.");
            }
        }

        return new OpenVinoRuntimeInspection(
            runtimeDirectory,
            runtimeVersion,
            tbbPath,
            problems);
    }

    public static bool IsSupportedVersion(string? fileVersion)
    {
        return Version.TryParse(fileVersion, out Version? parsed) &&
            parsed.Major == 2026 &&
            parsed.Minor == 2 &&
            parsed.Build == 1;
    }
}
