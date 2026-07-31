using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class RedactionEffectModeParserTests
{
    [Theory]
    [InlineData(null, RedactionEffectMode.Solid)]
    [InlineData("", RedactionEffectMode.Solid)]
    [InlineData("   ", RedactionEffectMode.Solid)]
    [InlineData("solid", RedactionEffectMode.Solid)]
    [InlineData("MOSAIC", RedactionEffectMode.Mosaic)]
    [InlineData("pixelate", RedactionEffectMode.Mosaic)]
    [InlineData(" pixelated ", RedactionEffectMode.Mosaic)]
    [InlineData("blur", RedactionEffectMode.Blur)]
    [InlineData("gaussian", RedactionEffectMode.Blur)]
    public void ParsesSupportedModes(
        string? text,
        RedactionEffectMode expected)
    {
        Assert.True(
            RedactionEffectModeParser.TryParse(
                text,
                out RedactionEffectMode actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RejectsUnknownMode()
    {
        Assert.False(
            RedactionEffectModeParser.TryParse(
                "inpaint",
                out _));
    }
}
