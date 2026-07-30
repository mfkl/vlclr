using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class DetectionQueryParserTests
{
    private readonly DetectionQueryParser _parser =
        new(Coco80ObjectCatalog.Create());

    [Theory]
    [InlineData("ball")]
    [InlineData("show me the ball")]
    [InlineData("find sports ball")]
    [InlineData("WHERE IS THE BALL?")]
    public void TryParse_BallPhrases_ResolveToSportsBall(string input)
    {
        bool parsed = _parser.TryParse(input, out DetectionQuery? query);

        Assert.True(parsed);
        Assert.NotNull(query);
        Assert.Equal(32, query.ObjectClass.Id);
        Assert.Equal("sports ball", query.ObjectClass.Label);
        Assert.Equal(0.50f, query.MinimumConfidence);
    }

    [Fact]
    public void TryParse_ConfidenceSuffix_OverridesDefault()
    {
        bool parsed = _parser.TryParse(
            "show me the ball confidence 0.72",
            out DetectionQuery? query);

        Assert.True(parsed);
        Assert.NotNull(query);
        Assert.Equal(0.72f, query.MinimumConfidence);
    }

    [Fact]
    public void TryParse_UnknownOpenVocabularyConcept_IsRejected()
    {
        bool parsed = _parser.TryParse(
            "show me the red tournament ball",
            out DetectionQuery? query);

        Assert.False(parsed);
        Assert.Null(query);
    }
}
