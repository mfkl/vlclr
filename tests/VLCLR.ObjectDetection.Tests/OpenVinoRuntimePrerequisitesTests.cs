using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class OpenVinoRuntimePrerequisitesTests
{
    [Theory]
    [InlineData("2026.2.1", true)]
    [InlineData("2026.2.1.21919", true)]
    [InlineData("2026.2.0.21919", false)]
    [InlineData("2025.4.0", false)]
    [InlineData("", false)]
    [InlineData("not-a-version", false)]
    public void IsSupportedVersion_RequiresValidatedRelease(
        string value,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpenVinoRuntimePrerequisites.IsSupportedVersion(value));
    }

    [Fact]
    public void Inspect_ExplainsHowToConfigureMissingDirectory()
    {
        OpenVinoRuntimeInspection inspection =
            OpenVinoRuntimePrerequisites.Inspect(null);

        Assert.False(inspection.IsUsable);
        Assert.Contains(
            "OPENVINO_RUNTIME_DIR",
            Assert.Single(inspection.Problems));
    }

    [Fact]
    public void Inspect_ReportsEveryMissingRuntimeComponent()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"vlclr-openvino-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            OpenVinoRuntimeInspection inspection =
                OpenVinoRuntimePrerequisites.Inspect(directory);

            Assert.False(inspection.IsUsable);
            Assert.Contains(
                inspection.Problems,
                problem => problem.Contains(
                    "openvino_c.dll",
                    StringComparison.Ordinal));
            Assert.Contains(
                inspection.Problems,
                problem => problem.Contains(
                    "openvino.dll",
                    StringComparison.Ordinal));
            Assert.Contains(
                inspection.Problems,
                problem => problem.Contains(
                    "openvino_intel_gpu_plugin.dll",
                    StringComparison.Ordinal));
            Assert.Contains(
                inspection.Problems,
                problem => problem.Contains(
                    "tbb12.dll",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
