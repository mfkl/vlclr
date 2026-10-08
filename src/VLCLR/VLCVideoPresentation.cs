using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;
using VLCLR.Types;

namespace VLCLR;

public sealed record VLCVideoChoice(string Value, string Label);
public sealed record VLCVideoAdjustments(VLCInputGeneration Generation, ulong OutputToken,
    IReadOnlyList<VLCVideoChoice> Aspects, IReadOnlyList<VLCVideoChoice> Crops,
    IReadOnlyList<VLCVideoChoice> Zooms, string Aspect, string Crop, float Zoom, bool AutoScale);

/// <summary>VLC 3 input_Control(INPUT_GET_VOUTS) and checked vout variables.
/// Call on the input owner's dispatcher. Every operation holds input/output references and
/// releases both the output array and references before returning owned values.</summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public sealed unsafe class VLCVideoPresentation(Func<VLCCurrentInputLease?> leaseProvider)
{
    // VLC 3 vlc_input.h input_query_e INPUT_GET_VOUTS; takes vout_thread_t*** and size_t*.
    private const int InputGetVouts = 43;

    public VLCVideoAdjustments? Read(out VLCPlayerCapabilityResult result)
    {
        VLCVideoAdjustments? snapshot = null;
        result = WithOutput(null, (output, generation) =>
        {
            var variables = new VLCVariable(output);
            var aspects = ReadChoices(output, "aspect-ratio", false);
            var crops = ReadChoices(output, "crop", false);
            var zooms = ReadChoices(output, "zoom", true);
            string? aspect = variables.GetString("aspect-ratio"), crop = variables.GetString("crop");
            float zoom = variables.GetFloat("zoom");
            if (aspects.Count == 0 || crops.Count == 0 || zooms.Count == 0 || aspect is null || crop is null || !float.IsFinite(zoom) || zoom <= 0)
                return new(VLCPlayerCapabilityResultCode.NotSupported);
            snapshot = new(generation, (ulong)output, aspects, crops, zooms, aspect, crop, zoom, variables.GetBool("autoscale"));
            return new(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Live);
        });
        return result.Succeeded ? snapshot : null;
    }

    public VLCPlayerCapabilityResult Set(VLCVideoAdjustments expected, string setting, string value)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (setting is not ("aspect-ratio" or "crop" or "zoom")) return new(VLCPlayerCapabilityResultCode.InvalidArgument);
        return WithOutput(expected, (output, _) =>
        {
            bool fit = setting == "zoom" && value == "fit";
            if (!fit && !ReadChoices(output, setting, setting == "zoom").Any(choice => choice.Value == value))
                return new(VLCPlayerCapabilityResultCode.InvalidArgument);
            var variables = new VLCVariable(output);
            bool applied;
            if (setting == "zoom")
            {
                applied = fit || variables.SetFloat("zoom", float.Parse(value, CultureInfo.InvariantCulture));
                applied = applied && VLCCore.VarSetChecked(output, "autoscale", VLCVarType.Bool, new VLCValueNative { Bool = fit ? (byte)1 : (byte)0 }) == 0;
            }
            else applied = variables.SetString(setting, value);
            return new(applied ? VLCPlayerCapabilityResultCode.RequestDispatched : VLCPlayerCapabilityResultCode.NativeFailure, VLCApplyTiming.Live);
        });
    }

    private VLCPlayerCapabilityResult WithOutput(VLCVideoAdjustments? expected, Func<nint, VLCInputGeneration, VLCPlayerCapabilityResult> action)
    {
        if (!VLCPlatform.IsSupported)
            return new(VLCPlayerCapabilityResultCode.UnsupportedPlatform);
        if (leaseProvider() is not { IsValid: true } lease) return new(VLCPlayerCapabilityResultCode.InputUnavailable);
        if (expected is not null && expected.Generation != lease.Generation) return new(VLCPlayerCapabilityResultCode.StaleSnapshot);
        nint input = VLCCore.PlaylistCurrentInput(lease.Playlist);
        if (input == 0) return new(VLCPlayerCapabilityResultCode.InputUnavailable);
        nint outputs = 0;
        nuint count = 0;
        try
        {
            int error = VLCCore.InputControl(input, InputGetVouts, (long)&outputs, (long)&count);
            if (error != 0) return new(VLCPlayerCapabilityResultCode.InputUnavailable, NativeError: error);
            // The product presents a single video surface. Do not silently change only one
            // output for inputs with multiple simultaneous video outputs.
            if (count != 1 || outputs == 0) return new(VLCPlayerCapabilityResultCode.NotSupported);
            nint output = *(nint*)outputs;
            if (leaseProvider() != lease || (expected is not null && expected.OutputToken != (ulong)output))
                return new(VLCPlayerCapabilityResultCode.StaleSnapshot);
            var result = action(output, lease.Generation);
            return leaseProvider() == lease ? result : new(VLCPlayerCapabilityResultCode.StaleSnapshot);
        }
        finally
        {
            if (outputs != 0)
            {
                for (nuint i = 0; i < count; i++) VLCCore.ObjectRelease(((nint*)outputs)[i]);
                VLCCore.Free(outputs);
            }
            VLCCore.ObjectRelease(input);
        }
    }

    private static IReadOnlyList<VLCVideoChoice> ReadChoices(nint output, string name, bool floats)
    {
        VLCValueNative values = default, texts = default;
        if (VLCCore.VarChange(output, name, VLCVarAction.GetChoices, ref values, ref texts) != 0) return [];
        try
        {
            if (values.Address == 0 || texts.Address == 0) return [];
            var v = (NativeList*)values.Address;
            var t = (NativeList*)texts.Address;
            if (v->Count < 0 || v->Count > 128 || v->Count != t->Count || v->Values == 0 || t->Values == 0) return [];
            var copied = new VLCVideoChoice[v->Count];
            for (int i = 0; i < copied.Length; i++)
            {
                var value = ((VLCValueNative*)v->Values)[i];
                string id = floats ? value.Float.ToString(CultureInfo.InvariantCulture) : Marshal.PtrToStringUTF8(value.String) ?? "";
                copied[i] = new(id, Marshal.PtrToStringUTF8(((VLCValueNative*)t->Values)[i].String) ?? id);
            }
            return Array.AsReadOnly(copied);
        }
        finally { VLCCore.VarFreeList(ref values, ref texts); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeList { public int Type; public int Count; public nint Values; }
}
