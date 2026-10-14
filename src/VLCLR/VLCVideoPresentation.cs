using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VLCLR.Native;
using VLCLR.Types;

namespace VLCLR;

public sealed record VLCVideoChoice(string Value, string Label);
/// <summary>
/// VLC 3 video filters and subpicture sources for
/// <see cref="VLCVideoPresentation.SetFilters"/>: module names joined with
/// ':' (VLC's <c>video-filter</c> and <c>sub-source</c> syntax) and their
/// parameters, each a string, float, integer or Boolean.
/// </summary>
public sealed record VLCVideoFilterSettings(string VideoFilter, string SubSource, IReadOnlyDictionary<string, object> Parameters)
{
    /// <summary>VLC 3's own video filters this contract accepts.</summary>
    public static IReadOnlySet<string> Filters { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "adjust", "transform", "rotate", "invert", "sepia", "posterize", "colorthres", "extract", "gradient", "sharpen",
        "gaussianblur", "motionblur", "mirror", "grain", "hqdn3d", "antiflicker", "psychedelic", "ripple", "wave", "puzzle", "wall", "anaglyph", "gradfun",
    };

    /// <summary>VLC 3's subpicture sources this contract accepts: the logo and the marquee.</summary>
    public static IReadOnlySet<string> Sources { get; } = new HashSet<string>(StringComparer.Ordinal) { "logo", "marq" };

    /// <summary>Parameters of those filters and sources.</summary>
    public static IReadOnlySet<string> ParameterNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "contrast", "brightness", "hue", "saturation", "gamma", "brightness-threshold",
        "transform-type", "rotate-angle", "sepia-intensity", "posterize-level",
        "colorthres-color", "colorthres-saturationthres", "colorthres-similaritythres", "extract-component",
        "gradient-mode", "gradient-type", "gradient-cartoon", "sharpen-sigma", "gaussianblur-sigma", "blur-factor",
        "mirror-split", "mirror-direction", "grain-variance", "puzzle-rows", "puzzle-cols", "wall-rows", "wall-cols",
        "gradfun-radius", "gradfun-strength", "hqdn3d-luma-spat", "antiflicker-window-size",
        "logo-file", "logo-x", "logo-y", "logo-position", "logo-opacity", "logo-delay", "logo-repeat",
        "marq-marquee", "marq-x", "marq-y", "marq-position", "marq-opacity", "marq-color", "marq-size", "marq-timeout", "marq-refresh",
    };

    public bool IsValid =>
        VideoFilter is not null && SubSource is not null && Parameters is not null &&
        VideoFilter.Split(':', StringSplitOptions.RemoveEmptyEntries).All(Filters.Contains) &&
        SubSource.Split(':', StringSplitOptions.RemoveEmptyEntries).All(Sources.Contains) &&
        Parameters.All(pair => ParameterNames.Contains(pair.Key) && pair.Value switch
        {
            string text => !text.Contains('\0'),
            float number => float.IsFinite(number),
            double number => double.IsFinite(number),
            bool or int or long => true,
            _ => false,
        });
}

public sealed record VLCVideoAdjustments(VLCInputGeneration Generation, ulong OutputToken,
    IReadOnlyList<VLCVideoChoice> Aspects, IReadOnlyList<VLCVideoChoice> Crops,
    IReadOnlyList<VLCVideoChoice> Zooms, string Aspect, string Crop, float Zoom, bool AutoScale);

/// <summary>VLC 3 input_Control(INPUT_GET_VOUTS) and checked vout variables.
/// Call on the input owner's dispatcher. Every operation holds input/output references and
/// releases both the output array and references before returning owned values.</summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
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

    /// <summary>
    /// VLC 3's Take Snapshot (Shift+S): the vout's <c>video-snapshot</c>
    /// command. VLC writes the file itself, where its <c>snapshot-path</c>,
    /// <c>snapshot-format</c> and <c>snapshot-prefix</c> options say (the
    /// user's pictures folder by default), and shows a preview over the video.
    /// </summary>
    public VLCPlayerCapabilityResult RequestSnapshot() => WithOutput(null, (output, _) =>
        new VLCVariable(output).Trigger("video-snapshot")
            ? new(VLCPlayerCapabilityResultCode.RequestDispatched, VLCApplyTiming.Live)
            : new(VLCPlayerCapabilityResultCode.NativeFailure, VLCApplyTiming.Live));

    /// <summary>VLC 3's deinterlace-mode values; "auto" lets VLC choose.</summary>
    public static IReadOnlyList<string> DeinterlaceMethods { get; } = ["auto", "discard", "blend", "mean", "bob", "linear", "x", "yadif", "yadif2x", "phosphor", "ivtc"];

    /// <summary>
    /// Deinterlacing, as VLC 3's Video › Deinterlace menu sets it:
    /// <paramref name="mode"/> -1 is automatic, 0 off, 1 on. See
    /// <see cref="SetFilters"/> for where it applies.
    /// </summary>
    public VLCPlayerCapabilityResult SetDeinterlace(int mode, string method)
    {
        if (mode is < -1 or > 1 || !DeinterlaceMethods.Contains(method)) return new(VLCPlayerCapabilityResultCode.InvalidArgument);
        return SetOnPlaylistAndOutput([("deinterlace-mode", method), ("deinterlace", (long)mode)]);
    }

    /// <summary>
    /// Crops by pixels from each edge (VLC's <c>left+top+right+bottom</c> crop
    /// syntax); all zero removes the crop. See <see cref="SetFilters"/> for
    /// where it applies.
    /// </summary>
    public VLCPlayerCapabilityResult SetCropBorders(int left, int top, int right, int bottom)
    {
        if (left < 0 || top < 0 || right < 0 || bottom < 0) return new(VLCPlayerCapabilityResultCode.InvalidArgument);
        string value = left + top + right + bottom == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{left}+{top}+{right}+{bottom}");
        return SetOnPlaylistAndOutput([("crop", value)]);
    }

    /// <summary>
    /// Replaces the video filters and subpicture sources, as VLC 3's Effects
    /// and Filters window does. The values go on the playing output, whose
    /// filter chain restarts and reads the parameters, and on the playlist,
    /// so a later output (the next queue item) starts with them; command
    /// parameters (adjust's contrast, the marquee's text) also reach running
    /// filters. Without a video output yet, only the playlist takes them.
    /// Only VLC 3's own video filters, sources and parameters are accepted.
    /// </summary>
    public VLCPlayerCapabilityResult SetFilters(VLCVideoFilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid) return new(VLCPlayerCapabilityResultCode.InvalidArgument);
        // The parameters first: the chain restarts when video-filter is set.
        return SetOnPlaylistAndOutput([.. settings.Parameters.Select(pair => (pair.Key, pair.Value)), ("video-filter", settings.VideoFilter), ("sub-source", settings.SubSource)]);
    }

    /// <summary>A string variable of the playing output, such as its <c>video-filter</c> chain; null without an output.</summary>
    public string? ReadOutputString(string name)
    {
        string? value = null;
        WithOutput(null, (output, _) => { value = new VLCVariable(output).GetString(name); return new(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Live); });
        return value;
    }

    /// <summary>An integer variable of the playing output, such as <c>deinterlace</c>; null without an output.</summary>
    public long? ReadOutputInteger(string name)
    {
        long? value = null;
        WithOutput(null, (output, _) =>
        {
            if (VLCCore.VarGetChecked(output, name, VLCVarType.Integer, out VLCValueNative native) == 0) value = native.Integer;
            return new(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Live);
        });
        return value;
    }

    private VLCPlayerCapabilityResult SetOnPlaylistAndOutput(IReadOnlyList<(string Name, object Value)> values)
    {
        if (!VLCPlatform.IsSupported) return new(VLCPlayerCapabilityResultCode.UnsupportedPlatform);
        if (leaseProvider() is not { IsValid: true } lease) return new(VLCPlayerCapabilityResultCode.InputUnavailable);
        bool inherited = SetValues(lease.Playlist, values);
        var output = WithOutput(null, (vout, _) => new(SetValues(vout, values)
            ? VLCPlayerCapabilityResultCode.RequestDispatched : VLCPlayerCapabilityResultCode.NativeFailure, VLCApplyTiming.Live));
        return output.Code is VLCPlayerCapabilityResultCode.InputUnavailable or VLCPlayerCapabilityResultCode.NotSupported
            ? new(inherited ? VLCPlayerCapabilityResultCode.RequestDispatched : VLCPlayerCapabilityResultCode.NativeFailure, VLCApplyTiming.Live)
            : output;
    }

    // Each value is created on the object when it lacks it (inheriting its
    // option), then set: a string, float, integer or Boolean.
    private static bool SetValues(nint target, IReadOnlyList<(string Name, object Value)> values)
    {
        var variables = new VLCVariable(target);
        bool applied = true;
        foreach ((string name, object value) in values)
        {
            int type = value switch { string => VLCVarType.String, float or double => VLCVarType.Float, bool => VLCVarType.Bool, _ => VLCVarType.Integer };
            if (VLCCore.VarGetChecked(target, name, type, out VLCValueNative current) != 0)
                VLCCore.VarCreate(target, name, type | VLCVarType.DoInherit);
            else if (type == VLCVarType.String && current.String != 0)
                VLCCore.Free(current.String);
            applied &= value switch
            {
                string text => variables.SetString(name, text),
                float number => variables.SetFloat(name, number),
                double number => variables.SetFloat(name, (float)number),
                bool flag => variables.SetBool(name, flag),
                _ => variables.SetInteger(name, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            };
        }
        return applied;
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
