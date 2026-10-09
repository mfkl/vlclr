// LGPL-2.1-or-later, like the VLC source the blur is ported from.

using VLCLR.Plugin;

namespace VLCLR.Filters.Video;

/// <summary>
/// A VLC video filter that applies <see cref="GaussianBlur"/>. A plugin derives
/// from it and adds the module attributes, including a float option for sigma:
/// <code>
/// [VLCModule("my_blur")]
/// [VLCCapability("video filter")]
/// [VLCConfig("my-blur-sigma", VLCConfigType.Float, Default = 2.0f, Min = 0.01f, Max = 4096.0f)]
/// public partial class MyBlur : GaussianBlurFilterBase
/// {
///     protected override string SigmaOption => "my-blur-sigma";
/// }
/// </code>
/// </summary>
public abstract class GaussianBlurFilterBase : VLCVideoFilterBase
{
    /// <summary>Name of the plugin's float option holding sigma.</summary>
    protected abstract string SigmaOption { get; }

    /// <summary>Sigma used when the option is not set.</summary>
    protected virtual float DefaultSigma => 2.0f;

    /// <summary>The blur, from <see cref="OnOpen"/> to <see cref="OnClose"/>.</summary>
    protected GaussianBlur? Blur { get; private set; }

    /// <summary>The implementation to request; <see cref="GaussianBlurImplementation.Auto"/> by default.</summary>
    protected virtual GaussianBlurImplementation SelectImplementation(VLCFilterContext context)
        => GaussianBlurImplementation.Auto;

    /// <inheritdoc />
    protected override bool OnOpen(VLCFilterContext context)
    {
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
        try
        {
            Blur = new GaussianBlur(sigma, SelectImplementation(context));
        }
        catch (PlatformNotSupportedException e)
        {
            context.Logger.Error(e.Message);
            return false;
        }
        context.Logger.Debug($"gaussian distribution is {Blur.WindowSize} pixels wide ({Blur.Implementation})");
        return true;
    }

    /// <inheritdoc />
    protected override nint ProcessFrameToOutput(VLCFrame frame)
    {
        nint outputPicture = Context.NewPicture();
        if (outputPicture == 0)
        {
            return 0;
        }
        Filter(frame, new VLCFrame(outputPicture, Context));
        return outputPicture;
    }

    /// <summary>Blurs one picture; override to add work around it.</summary>
    protected virtual void Filter(VLCFrame input, VLCFrame output) => Blur!.Apply(input, output);

    /// <inheritdoc />
    protected override void OnClose()
    {
        Blur?.Dispose();
        Blur = null;
    }
}
