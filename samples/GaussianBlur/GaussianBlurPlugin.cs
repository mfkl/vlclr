using VLCLR.Filters.Video;
using VLCLR.Plugin;

namespace GaussianBlurSample;

/// <summary>
/// VLC's Gaussian blur as a C# plugin: same output as VLC's gaussianblur
/// filter, using the fastest implementation the build supports. The blur
/// itself is <see cref="GaussianBlur"/> from VLCLR.Filters; this class is the
/// VLC video filter around it.
/// </summary>
[VLCModule("dotnet_gaussianblur")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription("Gaussian blur video filter (C#)")]
[VLCConfig(SigmaOption, VLCConfigType.Float, Default = DefaultSigma, Min = 0.01f, Max = 4096.0f,
    Description = "Gaussian's std deviation",
    LongDescription = "Gaussian's standard deviation. The blurring will take into account pixels up to 3*sigma away in any direction.")]
public partial class GaussianBlurPlugin : VLCVideoFilterBase
{
    private const string SigmaOption = "dotnet-gaussianblur-sigma";
    private const float DefaultSigma = 2.0f;

    private GaussianBlur? _blur;

    protected override bool OnOpen(VLCFilterContext context)
    {
        // Same checks as VLC's filter: planar YUV in, the same chroma out.
        uint chroma = context.InputFormat.Chroma;
        if (!GaussianBlur.IsSupportedChroma(chroma))
        {
            context.Logger.Error($"Unsupported input chroma ({context.ChromaString})");
            return false;
        }
        if (context.OutputFormat.Chroma != chroma)
        {
            context.Logger.Error("Input and output chromas don't match");
            return false;
        }

        float sigma = new VLCConfiguration(context.NativePtr).GetFloat(SigmaOption, DefaultSigma);
        if (!(sigma > 0))
        {
            context.Logger.Error("sigma must be greater than zero");
            return false;
        }

        // Auto picks the fastest implementation the build and the CPU support.
        _blur = new GaussianBlur(sigma);
        context.Logger.Debug($"gaussian distribution is {_blur.WindowSize} pixels wide ({_blur.Implementation})");
        return true;
    }

    // The blur can't work in place: it writes into a new picture from VLC's
    // output pool. The base class copies the input's properties (date, ...)
    // onto it and releases the input.
    protected override nint ProcessFrameToOutput(VLCFrame frame)
    {
        nint outputPicture = Context.NewPicture();
        if (outputPicture == 0)
        {
            return 0;
        }
        _blur!.Apply(frame, new VLCFrame(outputPicture, Context));
        return outputPicture;
    }

    protected override void OnClose()
    {
        _blur?.Dispose();
        _blur = null;
    }
}
