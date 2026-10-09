using VLCLR.Filters.Video;
using VLCLR.Plugin;

namespace GaussianBlurSample;

/// <summary>
/// VLC's Gaussian blur as a C# plugin: same output as VLC's gaussianblur
/// filter, using the fastest implementation the build supports.
/// </summary>
[VLCModule("dotnet_gaussianblur")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription("Gaussian blur video filter (C#)")]
[VLCConfig("dotnet-gaussianblur-sigma", VLCConfigType.Float, Default = 2.0f, Min = 0.01f, Max = 4096.0f,
    Description = "Gaussian's std deviation",
    LongDescription = "Gaussian's standard deviation. The blurring will take into account pixels up to 3*sigma away in any direction.")]
public partial class GaussianBlurPlugin : GaussianBlurFilterBase
{
    protected override string SigmaOption => "dotnet-gaussianblur-sigma";
}
