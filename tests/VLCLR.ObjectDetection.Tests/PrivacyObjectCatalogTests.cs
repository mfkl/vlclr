using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class PrivacyObjectCatalogTests
{
    [Theory]
    [InlineData("face", PrivacyObjectCatalog.FaceClassId)]
    [InlineData("faces", PrivacyObjectCatalog.FaceClassId)]
    [InlineData("license-plate", PrivacyObjectCatalog.LicensePlateClassId)]
    [InlineData("number plate", PrivacyObjectCatalog.LicensePlateClassId)]
    [InlineData("plate", PrivacyObjectCatalog.LicensePlateClassId)]
    [InlineData("person", 0)]
    public void ResolvesSensitiveAndCocoClasses(string term, int expectedId)
    {
        ObjectClassDescriptor result =
            PrivacyObjectCatalog.Create().Resolve(term);

        Assert.Equal(expectedId, result.Id);
    }
}
