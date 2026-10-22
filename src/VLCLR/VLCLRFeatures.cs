// VLCLR feature switches
// Set from the plugin's project; Native AOT removes the code of a switch that is off

#if NET9_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace VLCLR;

/// <summary>
/// Build-time switches. A plugin sets them with MSBuild properties (see
/// build/VLCLR.targets); the AOT compiler takes their value as a constant and
/// leaves out the code of a switch that is off.
/// </summary>
internal static class VLCLRFeatures
{
    public const string FrameAllocationCheckSwitch = "VLCLR.FrameAllocationCheck";

    /// <summary>
    /// Measures what each frame allocates on the managed heap and logs it when
    /// the filter closes (<see cref="Plugin.FrameAllocationMeter"/>). Off unless
    /// the plugin is built with <c>-p:VLCLRFrameAllocationCheck=true</c>.
    /// </summary>
#if NET9_0_OR_GREATER
    [FeatureSwitchDefinition(FrameAllocationCheckSwitch)]
#endif
    public static bool FrameAllocationCheck { get; } =
        AppContext.TryGetSwitch(FrameAllocationCheckSwitch, out bool enabled) && enabled;
}
