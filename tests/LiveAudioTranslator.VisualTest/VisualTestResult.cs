using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveAudioTranslator.VisualTest;

internal sealed record VisualTestResult
{
    public bool Passed { get; init; }
    public string Error { get; init; } = "";
    public Dictionary<string, string> Artifacts { get; init; } = [];
    public Dictionary<string, object> Metrics { get; init; } = [];
    public QtWindowMetadata? QtWindow { get; init; }
    public string LaunchCommand { get; init; } = "";
    public int LaunchProcessId { get; init; }

    public static VisualTestResult Failure(Exception ex) =>
        new()
        {
            Passed = false,
            Error = $"{ex.GetType().Name}: {ex.Message}"
        };

    public async Task WriteAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed record QtWindowMetadata(
    [property: JsonIgnore] nint Handle,
    int ProcessId,
    string Title,
    int ClientX,
    int ClientY,
    int ClientWidth,
    int ClientHeight,
    bool Visible,
    bool Minimized,
    bool Unobscured)
{
    public string CaptureMethod { get; init; } = "BitBlt(CAPTUREBLT)";
}
