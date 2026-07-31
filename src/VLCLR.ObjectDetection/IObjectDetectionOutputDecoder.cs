namespace VLCLR.ObjectDetection;

public enum ObjectDetectionInputResizeMode
{
    CenteredLetterbox,
    Stretch
}

public interface IObjectDetectionOutputDecoder
{
    int InputWidth { get; }

    int InputHeight { get; }

    ObjectDetectionInputResizeMode InputResizeMode { get; }

    int ExpectedOutputLength { get; }

    IReadOnlyList<ObjectDetection> Decode(
        ReadOnlySpan<float> output,
        int sourceWidth,
        int sourceHeight);
}
