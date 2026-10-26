// The Gaussian blur from VLCLR.Filters as a benchmark plugin: one module per
// runtime and instruction set, an option to pick the implementation, and
// per-frame timing (BenchStats) around the blur only.
//
// LGPL-2.1-or-later, like the VLC source the blur is ported from.

using System.Runtime.InteropServices;
using VLCLR.Filters.Video;
using VLCLR.Plugin;

namespace DotnetGaussianBlur;

/// <summary>
/// Module and option names carry the runtime, so the .NET 8, 10 and 11 builds
/// and their AVX2 builds can be installed side by side in one VLC
/// (dotnet_gaussianblur_net10, --dotnet-gaussianblur-net10-sigma,
/// dotnet_gaussianblur_net10_avx2, ...).
/// </summary>
internal static class BlurNames
{
#if NET11_0
    public const string Runtime = "net11";
#elif NET10_0
    public const string Runtime = "net10";
#elif NET8_0
    public const string Runtime = "net8";
#else
#error Add the runtime name for this target framework.
#endif
#if BLUR_ISA_AVX2
    private const string Isa = "_avx2";
    private const string IsaOption = "-avx2";
#else
    private const string Isa = "";
    private const string IsaOption = "";
#endif
    public const string Module = "dotnet_gaussianblur_" + Runtime + Isa;
    public const string Prefix = "dotnet-gaussianblur-" + Runtime + IsaOption + "-";
}

[VLCModule(BlurNames.Module)]
[VLCCapability("video filter", Score = 0)]
[VLCDescription("Gaussian blur video filter (C# port of VLC's gaussianblur, benchmark build)")]
[VLCConfig(BlurNames.Prefix + "sigma", VLCConfigType.Float, Default = 2.0f, Min = 0.01f, Max = 4096.0f,
    Description = "Gaussian's std deviation",
    LongDescription = "Gaussian's standard deviation. The blurring will take into account pixels up to 3*sigma away in any direction.")]
[VLCConfig(BlurNames.Prefix + "impl", VLCConfigType.String, Default = "scalar",
    Description = "Implementation",
    LongDescription = "scalar: line-for-line port of VLC's C code. hoisted: the same with loop bounds in locals and nint indices. tuned: restructured scalar code. safe: tuned without unsafe code. simd: AVX2. simdmax: AVX2 with wider loops. All give identical output; simd and simdmax need a -p:BlurIsa=avx2 build.")]
public partial class GaussianBlurFilter : VLCVideoFilterBase
{
#if BLUR_ISA_AVX2
    private const string IsaSuffix = "-AVX2";
#else
    private const string IsaSuffix = "";
#endif

    private GaussianBlur? _blur;
    private BenchStats _stats = new("CS");

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

        var config = new VLCConfiguration(context.NativePtr);
        float sigma = config.GetFloat(BlurNames.Prefix + "sigma", 2.0f);
        if (!(sigma > 0))
        {
            context.Logger.Error("sigma must be greater than zero");
            return false;
        }
        string name = config.GetString(BlurNames.Prefix + "impl", "scalar") ?? "scalar";
        GaussianBlurImplementation implementation = name.ToLowerInvariant() switch
        {
            "hoisted" => GaussianBlurImplementation.ReferenceHoisted,
            "tuned" => GaussianBlurImplementation.Tuned,
            "safe" => GaussianBlurImplementation.Safe,
            "simd" => GaussianBlurImplementation.Simd,
            "simdmax" => GaussianBlurImplementation.SimdMax,
            _ => GaussianBlurImplementation.Reference,
        };
        try
        {
            _blur = new GaussianBlur(sigma, implementation);
        }
        catch (PlatformNotSupportedException e)
        {
            context.Logger.Error(e.Message);
            return false;
        }

        string label = _blur.Implementation switch
        {
            GaussianBlurImplementation.ReferenceHoisted => "CS-Hoisted",
            GaussianBlurImplementation.Tuned => "CS-Tuned",
            GaussianBlurImplementation.Safe => "CS-Safe",
            GaussianBlurImplementation.Simd => "CS-SIMD",
            GaussianBlurImplementation.SimdMax => "CS-SIMDMax",
            _ => "CS-Scalar",
        } + IsaSuffix;
        _stats = new BenchStats(label);
        context.Logger.Info($"gaussian distribution is {_blur.WindowSize} pixels wide ({label}, {RuntimeInformation.FrameworkDescription})");
        return true;
    }

    protected override nint ProcessFrameToOutput(VLCFrame frame)
    {
        nint outputPicture = Context.NewPicture();
        if (outputPicture == 0)
        {
            return 0;
        }
        var output = new VLCFrame(outputPicture, Context);
        _stats.Begin();
        _blur!.Apply(frame, output);
        _stats.End();
        _stats.Dump(output);
        return outputPicture;
    }

    protected override void OnClose()
    {
        if (_blur is not null)
        {
            _stats.Report(Context.Logger, _blur.Sigma);
            _blur.Dispose();
            _blur = null;
        }
    }
}
