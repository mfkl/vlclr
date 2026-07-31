using VLCLR.ObjectDetection;

namespace YoloObjectSearch;

internal enum ObjectDetectionModelInputLayout
{
    Nchw,
    Nhwc
}

internal sealed record ObjectDetectionModelProfile(
    string Name,
    string ModelPath,
    ObjectDetectionModelInputLayout InputLayout,
    IObjectDetectionOutputDecoder Decoder)
{
    public string OpenVinoLayout => InputLayout switch
    {
        ObjectDetectionModelInputLayout.Nchw => "NCHW",
        ObjectDetectionModelInputLayout.Nhwc => "NHWC",
        _ => throw new ArgumentOutOfRangeException(nameof(InputLayout))
    };
}
