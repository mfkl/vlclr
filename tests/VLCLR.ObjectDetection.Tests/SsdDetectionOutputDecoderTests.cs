using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class SsdDetectionOutputDecoderTests
{
    private readonly SsdDetectionOutputDecoder _faceDecoder = new(
        [new SsdDetectionClassMapping(1, PrivacyObjectCatalog.Face)],
        new SsdDetectionOutputDecoderOptions(
            ConfidenceThreshold: 0.5f));

    [Fact]
    public void DecodesMappedClassAndSourceCoordinates()
    {
        float[] output = CreateOutput(_faceDecoder);
        SetDetection(
            output,
            index: 0,
            modelLabel: 1,
            confidence: 0.9f,
            left: 0.25f,
            top: 0.20f,
            right: 0.75f,
            bottom: 0.80f);
        SetEndMarker(output, 1);

        ObjectDetection detection = Assert.Single(
            _faceDecoder.Decode(output, 300, 300));

        Assert.Equal(PrivacyObjectCatalog.FaceClassId, detection.ClassId);
        Assert.Equal("face", detection.Label);
        Assert.Equal(0.9f, detection.Confidence);
        Assert.Equal(75, detection.Box.X, precision: 3);
        Assert.Equal(60, detection.Box.Y, precision: 3);
        Assert.Equal(150, detection.Box.Width, precision: 3);
        Assert.Equal(180, detection.Box.Height, precision: 3);
    }

    [Fact]
    public void ReversesCenteredLetterboxTransform()
    {
        var decoder = new SsdDetectionOutputDecoder(
            [new SsdDetectionClassMapping(1, PrivacyObjectCatalog.Face)],
            new SsdDetectionOutputDecoderOptions(
                ConfidenceThreshold: 0.5f,
                InputResizeMode:
                    ObjectDetectionInputResizeMode.CenteredLetterbox));
        float[] output = CreateOutput(decoder);
        SetDetection(
            output,
            index: 0,
            modelLabel: 1,
            confidence: 0.9f,
            left: 0.25f,
            top: 0.375f,
            right: 0.75f,
            bottom: 0.625f);
        SetEndMarker(output, 1);

        ObjectDetection detection = Assert.Single(
            decoder.Decode(output, 600, 300));

        Assert.Equal(150, detection.Box.X, precision: 3);
        Assert.Equal(75, detection.Box.Y, precision: 3);
        Assert.Equal(300, detection.Box.Width, precision: 3);
        Assert.Equal(150, detection.Box.Height, precision: 3);
    }

    [Fact]
    public void DefaultsToStretchCoordinatesForOpenModelZooSsdModels()
    {
        float[] output = CreateOutput(_faceDecoder);
        SetDetection(
            output,
            index: 0,
            modelLabel: 1,
            confidence: 0.9f,
            left: 0.25f,
            top: 0.20f,
            right: 0.75f,
            bottom: 0.80f);
        SetEndMarker(output, 1);

        ObjectDetection detection = Assert.Single(
            _faceDecoder.Decode(output, 600, 300));

        Assert.Equal(150, detection.Box.X, precision: 3);
        Assert.Equal(60, detection.Box.Y, precision: 3);
        Assert.Equal(300, detection.Box.Width, precision: 3);
        Assert.Equal(180, detection.Box.Height, precision: 3);
    }

    [Fact]
    public void IgnoresUnmappedAndLowConfidenceDetections()
    {
        float[] output = CreateOutput(_faceDecoder);
        SetDetection(output, 0, 2, 0.9f, 0.1f, 0.1f, 0.2f, 0.2f);
        SetDetection(output, 1, 1, 0.4f, 0.1f, 0.1f, 0.2f, 0.2f);
        SetEndMarker(output, 2);

        Assert.Empty(_faceDecoder.Decode(output, 300, 300));
    }

    [Fact]
    public void IgnoresModelLabelsOutsideIntegerRange()
    {
        float[] output = CreateOutput(_faceDecoder);
        SetDetection(
            output,
            0,
            int.MaxValue,
            0.9f,
            0.1f,
            0.1f,
            0.2f,
            0.2f);
        output[1] = float.MaxValue;
        SetEndMarker(output, 1);

        Assert.Empty(_faceDecoder.Decode(output, 300, 300));
    }

    [Fact]
    public void LicensePlateMappingCanIgnoreVehicleClass()
    {
        var decoder = new SsdDetectionOutputDecoder(
            [
                new SsdDetectionClassMapping(
                    2,
                    PrivacyObjectCatalog.LicensePlate)
            ]);
        float[] output = CreateOutput(decoder);
        SetDetection(output, 0, 1, 0.9f, 0.1f, 0.1f, 0.3f, 0.3f);
        SetDetection(output, 1, 2, 0.8f, 0.4f, 0.4f, 0.6f, 0.5f);
        SetEndMarker(output, 2);

        ObjectDetection detection = Assert.Single(
            decoder.Decode(output, 300, 300));

        Assert.Equal(
            PrivacyObjectCatalog.LicensePlateClassId,
            detection.ClassId);
        Assert.Equal("license plate", detection.Label);
    }

    [Fact]
    public void RejectsUnexpectedOutputLength()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _faceDecoder.Decode(new float[7], 300, 300));

        Assert.Contains("1400", exception.Message);
    }

    private static float[] CreateOutput(
        SsdDetectionOutputDecoder decoder) =>
        new float[decoder.ExpectedOutputLength];

    private static void SetDetection(
        float[] output,
        int index,
        int modelLabel,
        float confidence,
        float left,
        float top,
        float right,
        float bottom)
    {
        int offset = index * 7;
        output[offset] = 0;
        output[offset + 1] = modelLabel;
        output[offset + 2] = confidence;
        output[offset + 3] = left;
        output[offset + 4] = top;
        output[offset + 5] = right;
        output[offset + 6] = bottom;
    }

    private static void SetEndMarker(float[] output, int index)
    {
        output[index * 7] = -1;
    }
}
