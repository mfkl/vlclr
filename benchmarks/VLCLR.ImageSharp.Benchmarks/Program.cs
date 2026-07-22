using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using VLCLR.Imaging;
using VLCLR.Native;
using VLCLR.Rendering;
using VLCLR.Text;

const int Width = 1920;
const int Height = 1080;
const string Subtitle = "Where we're going, we don't need roads.\nReal-time subtitles in managed code.";

var arguments = Arguments.Parse(args);
var outputDirectory = Path.GetFullPath(arguments.OutputDirectory);
Directory.CreateDirectory(outputDirectory);

FontManager.LoadEmbeddedFont(
    Assembly.GetExecutingAssembly(),
    "VLCLR.ImageSharp.Benchmarks.Resources.JetBrainsMono-Regular.ttf",
    setAsDefault: true);

var style = new TextStyleWrapper
{
    FontName = "JetBrains Mono",
    FontSize = 48,
    ForegroundColor = 0xFFFFFF,
    ForegroundAlpha = 255,
    HasOutline = true,
    OutlineColor = 0x000000,
    OutlineAlpha = 255,
    OutlineWidth = 3,
    HasShadow = true,
    ShadowColor = 0x000000,
    ShadowAlpha = 180,
    ShadowOffset = 3
};

using var canvas = new TextCanvas(Width, Height);
canvas.RenderText(Subtitle, style);
using var compactCanvas = new TextCanvas(1, 1);
var compactImage = compactCanvas.RenderTextRegion(
    Subtitle,
    style,
    maximumWidth: (int)(Width * 0.9f))!;

var imagePath = Path.Combine(outputDirectory, $"imagesharp-{arguments.Label}.png");
var compactPixels = GC.AllocateUninitializedArray<byte>(compactImage.Width * compactImage.Height * 4);
compactImage.CopyPixelDataTo(compactPixels);
using (var compactReference = Image.LoadPixelData<Rgba32>(
           compactPixels,
           compactImage.Width,
           compactImage.Height))
using (var referenceImage = new Image<Rgba32>(Width, Height, Color.Transparent))
{
    int x = (Width - compactReference.Width) / 2;
    int y = Height - compactReference.Height;
    referenceImage.Mutate(context => context.DrawImage(compactReference, new Point(x, y), 1f));
    referenceImage.SaveAsPng(imagePath);
}

nint nativeDestination;
unsafe
{
    nativeDestination = (nint)NativeMemory.Alloc((nuint)(Width * Height * 4));
}

var results = new List<BenchmarkResult>
{
    Measure("RenderFullFrame", RenderFullFrame),
    Measure("StageFullFramePixels", StageFullFramePixels),
    Measure("RenderAndStageFullFrame", RenderAndStageFullFrame),
    Measure("CopyFullFrameDirectToPlane", CopyFullFrameDirectToPlane),
    Measure("RenderCompactRegion", RenderCompactRegion),
    Measure("CopyCompactRegionToPlane", CopyCompactRegionToPlane),
    Measure("RenderAndCopyCompactRegion", RenderAndCopyCompactRegion)
};

var report = new BenchmarkReport(
    Label: arguments.Label,
    TimestampUtc: DateTimeOffset.UtcNow,
    Commit: GetCommit(),
    Runtime: RuntimeInformation.FrameworkDescription,
    OperatingSystem: RuntimeInformation.OSDescription,
    Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
    ProcessorCount: Environment.ProcessorCount,
    Width: Width,
    Height: Height,
    CompactWidth: compactImage.Width,
    CompactHeight: compactImage.Height,
    Subtitle: Subtitle,
    WarmupIterations: arguments.Warmups,
    MeasurementIterations: arguments.Iterations,
    Results: results);

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var jsonPath = Path.Combine(outputDirectory, $"imagesharp-{arguments.Label}.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions));

var markdownPath = Path.Combine(outputDirectory, $"imagesharp-{arguments.Label}.md");
File.WriteAllText(markdownPath, ToMarkdown(report, Path.GetFileName(imagePath)));

Console.WriteLine(ToMarkdown(report, Path.GetFileName(imagePath)));
Console.WriteLine($"JSON: {jsonPath}");
Console.WriteLine($"PNG:  {imagePath}");

unsafe
{
    NativeMemory.Free((void*)nativeDestination);
}

return;

void RenderFullFrame()
{
    canvas.RenderText(Subtitle, style);
}

void StageFullFramePixels()
{
    // Mirrors the original PictureConverter staging step: allocate one complete
    // RGBA frame and ask ImageSharp to copy the image into it before the VLC copy.
    var pixels = GC.AllocateUninitializedArray<byte>(Width * Height * 4);
    canvas.GetImage()!.CopyPixelDataTo(pixels);
    GC.KeepAlive(pixels);
}

void RenderAndStageFullFrame()
{
    RenderFullFrame();
    StageFullFramePixels();
}

void RenderCompactRegion()
{
    compactCanvas.RenderTextRegion(Subtitle, style, maximumWidth: (int)(Width * 0.9f));
}

void CopyFullFrameDirectToPlane()
{
    CopyImageDirectToPlane(canvas.GetImage()!);
}

void CopyCompactRegionToPlane()
{
    CopyImageDirectToPlane(compactCanvas.GetImage()!);
}

void RenderAndCopyCompactRegion()
{
    RenderCompactRegion();
    CopyCompactRegionToPlane();
}

unsafe void CopyImageDirectToPlane(Image<Rgba32> image)
{
    int pitch = image.Width * 4;
    var picture = new VLCPicture
    {
        PlaneCount = 1,
        Plane0 = new VLCPlane
        {
            Pixels = nativeDestination,
            Lines = image.Height,
            Pitch = pitch,
            PixelPitch = 4,
            VisibleLines = image.Height,
            VisiblePitch = pitch
        }
    };

    if (!PictureConverter.CopyPixelsToPicture(image, (nint)(&picture), VLCFourCC.RGBA))
    {
        throw new InvalidOperationException("Could not copy the rendered image to the benchmark plane.");
    }
}

BenchmarkResult Measure(string name, Action operation)
{
    for (var i = 0; i < arguments.Warmups; i++)
    {
        operation();
    }

    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

    var durations = new double[arguments.Iterations];
    var allocations = new long[arguments.Iterations];
    var gen0Before = GC.CollectionCount(0);
    var gen1Before = GC.CollectionCount(1);
    var gen2Before = GC.CollectionCount(2);

    for (var i = 0; i < arguments.Iterations; i++)
    {
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        operation();
        durations[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocations[i] = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    }

    Array.Sort(durations);
    Array.Sort(allocations);

    return new BenchmarkResult(
        Name: name,
        MedianMilliseconds: Percentile(durations, 0.50),
        P95Milliseconds: Percentile(durations, 0.95),
        MedianAllocatedBytes: (long)PercentileLong(allocations, 0.50),
        P95AllocatedBytes: (long)PercentileLong(allocations, 0.95),
        Gen0Collections: GC.CollectionCount(0) - gen0Before,
        Gen1Collections: GC.CollectionCount(1) - gen1Before,
        Gen2Collections: GC.CollectionCount(2) - gen2Before);
}

static double Percentile(double[] sorted, double percentile)
{
    var rank = percentile * (sorted.Length - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);
    if (lower == upper)
    {
        return sorted[lower];
    }

    var weight = rank - lower;
    return sorted[lower] + ((sorted[upper] - sorted[lower]) * weight);
}

static double PercentileLong(long[] sorted, double percentile)
{
    var rank = percentile * (sorted.Length - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);
    if (lower == upper)
    {
        return sorted[lower];
    }

    var weight = rank - lower;
    return sorted[lower] + ((sorted[upper] - sorted[lower]) * weight);
}

static string GetCommit()
{
    try
    {
        var startInfo = new ProcessStartInfo("git", "rev-parse --short HEAD")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return "unknown";
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : "unknown";
    }
    catch
    {
        return "unknown";
    }
}

static string ToMarkdown(BenchmarkReport report, string imageFileName)
{
    var builder = new StringBuilder();
    builder.AppendLine($"# VLCLR ImageSharp benchmark: {report.Label}");
    builder.AppendLine();
    builder.AppendLine($"- Commit: `{report.Commit}`");
    builder.AppendLine($"- Captured: `{report.TimestampUtc:O}`");
    builder.AppendLine($"- Runtime: `{report.Runtime}`");
    builder.AppendLine($"- OS: `{report.OperatingSystem}`");
    builder.AppendLine($"- Architecture: `{report.Architecture}`");
    builder.AppendLine($"- Logical processors available: `{report.ProcessorCount}`");
    builder.AppendLine($"- Canvas: `{report.Width}x{report.Height}` RGBA");
    builder.AppendLine($"- Compact region: `{report.CompactWidth}x{report.CompactHeight}` RGBA");
    builder.AppendLine($"- Warmups / measurements: `{report.WarmupIterations} / {report.MeasurementIterations}`");
    builder.AppendLine();
    builder.AppendLine("| Scenario | Median elapsed | P95 elapsed | Median allocated | P95 allocated | GC (0/1/2) |");
    builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
    foreach (var result in report.Results)
    {
        builder.AppendLine(
            $"| {result.Name} | {result.MedianMilliseconds:F2} ms | {result.P95Milliseconds:F2} ms | " +
            $"{FormatBytes(result.MedianAllocatedBytes)} | {FormatBytes(result.P95AllocatedBytes)} | " +
            $"{result.Gen0Collections}/{result.Gen1Collections}/{result.Gen2Collections} |");
    }

    builder.AppendLine();
    builder.AppendLine("The staging scenarios deliberately reproduce the original `PictureConverter` full-frame temporary RGBA allocation.");
    builder.AppendLine();
    builder.AppendLine($"Reference render: [{imageFileName}]({imageFileName})");
    return builder.ToString();
}

static string FormatBytes(long bytes) => bytes >= 1024 * 1024
    ? $"{bytes / (1024d * 1024d):F2} MiB"
    : bytes >= 1024
        ? $"{bytes / 1024d:F2} KiB"
        : $"{bytes} B";

internal sealed record Arguments(string Label, string OutputDirectory, int Warmups, int Iterations)
{
    public static Arguments Parse(string[] args)
    {
        var label = "run";
        var output = Path.Combine("benchmarks", "results");
        var warmups = 3;
        var iterations = 15;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--label" when i + 1 < args.Length:
                    label = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--warmups" when i + 1 < args.Length:
                    warmups = int.Parse(args[++i]);
                    break;
                case "--iterations" when i + 1 < args.Length:
                    iterations = int.Parse(args[++i]);
                    break;
            }
        }

        return new Arguments(label, output, warmups, iterations);
    }
}

internal sealed record BenchmarkReport(
    string Label,
    DateTimeOffset TimestampUtc,
    string Commit,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    int Width,
    int Height,
    int CompactWidth,
    int CompactHeight,
    string Subtitle,
    int WarmupIterations,
    int MeasurementIterations,
    IReadOnlyList<BenchmarkResult> Results);

internal sealed record BenchmarkResult(
    string Name,
    double MedianMilliseconds,
    double P95Milliseconds,
    long MedianAllocatedBytes,
    long P95AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);
