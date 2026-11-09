using System.Text.RegularExpressions;
using Xunit;

namespace VLCLR.Tests.Interop;

/// <summary>
/// VLC's struct layouts move upstream, so the offsets and sizes in this
/// repository come from the generated interop (VLCLR.Native.Interop). A number
/// written by hand goes stale at the next header bump.
/// </summary>
public sealed class NoHardcodedLayoutTests
{
    private static readonly Regex[] HardcodedLayout =
    [
        new(@"IsWindows\(\)\s*\?\s*\d+\s*:\s*\d+"),
        new(@"\*\(\w+\*\)\(\w+Base\s*\+\s*\d+\)"),
        new(@"Assert\.Equal\(\s*\d+,\s*(Marshal|Unsafe)\.(SizeOf|OffsetOf)<VLC"),
    ];

    /// <summary>
    /// Declared in modules/video_chroma/d3d11_fmt.h, which VLC does not
    /// install, so there is no generated layout to use.
    /// </summary>
    private const string NoGeneratedLayout = "VLCD3D11Picture";

    [Theory]
    [InlineData("int ops = OperatingSystem.IsWindows() ? 560 : 544;")]
    [InlineData("*(uint*)(filterBase + 96) = chroma;")]
    [InlineData("Assert.Equal(24, Marshal.SizeOf<VLCFilterOwner>());")]
    [InlineData("Assert.Equal(8, Marshal.OffsetOf<VLCBlock>(nameof(VLCBlock.Buffer)).ToInt32());")]
    public void A_hardcoded_layout_is_recognized(string line)
    {
        Assert.True(IsHardcodedLayout(line));
    }

    [Theory]
    [InlineData("int ops = Gen.Filter.Offsets.Ops;")]
    [InlineData("*(uint*)(filterBase + Gen.VideoFormat.Offsets.Chroma) = chroma;")]
    [InlineData("Assert.Equal(Gen.FilterOwner.MaxSizeOf, Marshal.SizeOf<VLCFilterOwner>());")]
    [InlineData("Assert.Equal(104, Marshal.SizeOf<VLCD3D11PictureSystem>());")]
    public void A_generated_or_private_layout_is_allowed(string line)
    {
        Assert.False(IsHardcodedLayout(line));
    }

    [Fact]
    public void Layout_numbers_come_from_the_generated_interop()
    {
        string src = Path.Combine(RepositoryRoot(), "src");

        var hardcoded = new[] { "VLCLR", "VLCLR.Tests" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(src, project), "*.cs", SearchOption.AllDirectories))
            .Where(IsHandWrittenSource)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => IsHardcodedLayout(entry.line))
                .Select(entry => $"{Path.GetRelativePath(src, file)}:{entry.number}: {entry.line.Trim()}"))
            .ToList();

        Assert.True(hardcoded.Count == 0, "Use VLCLR.Native.Interop instead:\n" + string.Join('\n', hardcoded));
    }

    private static bool IsHardcodedLayout(string line)
        => !line.Contains(NoGeneratedLayout, StringComparison.Ordinal)
            && HardcodedLayout.Any(pattern => pattern.IsMatch(line));

    private static bool IsHandWrittenSource(string file)
    {
        string[] directories = Path.GetDirectoryName(file)!.Split(Path.DirectorySeparatorChar);
        return !file.EndsWith(".g.cs", StringComparison.Ordinal)
            && Path.GetFileName(file) != nameof(NoHardcodedLayoutTests) + ".cs"
            && !directories.Contains("bin")
            && !directories.Contains("obj");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "vlclr.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
