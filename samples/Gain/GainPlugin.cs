using VLCLR.Filters.Audio;
using VLCLR.Plugin;

namespace GainSample;

/// <summary>
/// VLC's gain filter as a C# audio filter: same output as VLC's gain filter,
/// every sample times --dotnet-gain-value. The smallest audio filter there is,
/// to show <see cref="VLCAudioFilterBase"/>: blocks come in as 32-bit float
/// and are processed in place.
/// </summary>
[VLCModule("dotnet_gain")]
[VLCCapability("audio filter", Score = 0)]
[VLCDescription("Gain control filter (C#)")]
[VLCConfig(GainOption, VLCConfigType.Float, Default = DefaultGain,
    Description = "Gain multiplier",
    LongDescription = "Increase or decrease the gain (default 1.0)")]
public partial class GainPlugin : VLCAudioFilterBase
{
    private const string GainOption = "dotnet-gain-value";
    private const float DefaultGain = 1.0f;

    private float _gain;

    protected override bool OnOpen(VLCAudioFilterContext context)
    {
        _gain = new VLCConfiguration(context.NativePtr).GetFloat(GainOption, DefaultGain);
        context.Logger.Debug($"gain multiplier sets to {_gain:0.00}x");
        return true;
    }

    protected override void ProcessAudio(in VLCAudioBlock block)
        => Gain.Amplify(block.Output, _gain);
}
