using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class ObjectClassCatalogTests
{
    private readonly ObjectClassCatalog _catalog = Coco80ObjectCatalog.Create();

    [Fact]
    public void Catalog_PreservesOfficialCocoClassOrder()
    {
        Assert.Equal(80, _catalog.Classes.Count);
        Assert.Equal("person", _catalog.Classes[0].Label);
        Assert.Equal("sports ball", _catalog.Classes[32].Label);
        Assert.Equal("toothbrush", _catalog.Classes[79].Label);
    }

    [Theory]
    [InlineData("ball", 32, "sports ball")]
    [InlineData("sports-ball", 32, "sports ball")]
    [InlineData("phone", 67, "cell phone")]
    [InlineData("sofa", 57, "couch")]
    public void Resolve_MapsFriendlyAliases(
        string input,
        int expectedId,
        string expectedLabel)
    {
        ObjectClassDescriptor result = _catalog.Resolve(input);

        Assert.Equal(expectedId, result.Id);
        Assert.Equal(expectedLabel, result.Label);
    }
}
