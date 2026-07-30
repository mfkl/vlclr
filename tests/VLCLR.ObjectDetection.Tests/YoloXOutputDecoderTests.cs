using VLCLR.ObjectDetection;

namespace VLCLR.ObjectDetection.Tests;

public sealed class YoloXOutputDecoderTests
{
    private readonly YoloXOutputDecoder _decoder =
        new(Coco80ObjectCatalog.Create());

    [Fact]
    public void Decoder_ExposesNanoAndTinyOutputShape()
    {
        Assert.Equal(3549, _decoder.ProposalCount);
        Assert.Equal(85, _decoder.ValuesPerProposal);
        Assert.Equal(301665, _decoder.ExpectedOutputLength);
    }

    [Fact]
    public void Decode_ConvertsRawProposalToLabeledSourceBox()
    {
        float[] output = CreateOutput(_decoder);
        SetRawProposal(
            output,
            _decoder,
            proposalIndex: 0,
            centerX: 80,
            centerY: 80,
            width: 80,
            height: 40,
            objectness: 0.90f,
            classId: 32,
            classScore: 0.80f);

        ObjectDetection detection = Assert.Single(
            _decoder.Decode(output, 416, 416));

        Assert.Equal(32, detection.ClassId);
        Assert.Equal("sports ball", detection.Label);
        Assert.Equal(0.72f, detection.Confidence, precision: 4);
        Assert.Equal(40, detection.Box.X, precision: 3);
        Assert.Equal(60, detection.Box.Y, precision: 3);
        Assert.Equal(80, detection.Box.Width, precision: 3);
        Assert.Equal(40, detection.Box.Height, precision: 3);
    }

    [Fact]
    public void Decode_ReversesCenteredLetterboxTransform()
    {
        float[] output = CreateOutput(_decoder);
        SetRawProposal(
            output,
            _decoder,
            proposalIndex: 0,
            centerX: 208,
            centerY: 208,
            width: 100,
            height: 100,
            objectness: 0.90f,
            classId: 0,
            classScore: 0.90f);

        ObjectDetection detection = Assert.Single(
            _decoder.Decode(output, 832, 416));

        Assert.Equal(316, detection.Box.X, precision: 3);
        Assert.Equal(108, detection.Box.Y, precision: 3);
        Assert.Equal(200, detection.Box.Width, precision: 3);
        Assert.Equal(200, detection.Box.Height, precision: 3);
    }

    [Fact]
    public void Decode_SuppressesOverlappingLowerConfidenceBox()
    {
        float[] output = CreateOutput(_decoder);
        SetRawProposal(
            output,
            _decoder,
            proposalIndex: 0,
            centerX: 100,
            centerY: 100,
            width: 80,
            height: 80,
            objectness: 0.95f,
            classId: 32,
            classScore: 0.90f);
        SetRawProposal(
            output,
            _decoder,
            proposalIndex: 1,
            centerX: 102,
            centerY: 100,
            width: 80,
            height: 80,
            objectness: 0.80f,
            classId: 32,
            classScore: 0.90f);

        ObjectDetection detection = Assert.Single(
            _decoder.Decode(output, 416, 416));

        Assert.Equal(0.855f, detection.Confidence, precision: 4);
    }

    [Fact]
    public void Decode_RejectsUnexpectedOutputShape()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _decoder.Decode(new float[10], 416, 416));

        Assert.Contains("301665", exception.Message);
    }

    private static float[] CreateOutput(YoloXOutputDecoder decoder)
    {
        return new float[decoder.ExpectedOutputLength];
    }

    private static void SetRawProposal(
        float[] output,
        YoloXOutputDecoder decoder,
        int proposalIndex,
        float centerX,
        float centerY,
        float width,
        float height,
        float objectness,
        int classId,
        float classScore)
    {
        const int stride = 8;
        int gridX = proposalIndex % 52;
        int gridY = proposalIndex / 52;
        int offset = proposalIndex * decoder.ValuesPerProposal;
        output[offset] = centerX / stride - gridX;
        output[offset + 1] = centerY / stride - gridY;
        output[offset + 2] = MathF.Log(width / stride);
        output[offset + 3] = MathF.Log(height / stride);
        output[offset + 4] = objectness;
        output[offset + 5 + classId] = classScore;
    }
}
