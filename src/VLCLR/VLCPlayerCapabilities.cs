using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using VLCLR.Native;

namespace VLCLR;

/// <summary>When a VLC setting may take effect.  This is deliberately not a promise that a UI control is live.</summary>
public enum VLCApplyTiming { Live, NextOpen, Restart, Unsupported }
public enum VLCPlayerCapabilityResultCode { Success, RequestDispatched, InputUnavailable, StaleSnapshot, InvalidArgument, NotSupported, NativeFailure, UnsupportedPlatform }
public readonly record struct VLCPlayerCapabilityResult(VLCPlayerCapabilityResultCode Code, VLCApplyTiming Timing = VLCApplyTiming.Unsupported, int NativeError = 0)
{ public bool Succeeded => Code == VLCPlayerCapabilityResultCode.Success; }

public sealed record VLCVideoPresentationSnapshot(VLCInputGeneration Generation, VLCApplyTiming AspectRatio, VLCApplyTiming Crop, VLCApplyTiming Zoom, string? EffectiveAspectRatio, string? EffectiveCrop, float? EffectiveZoom, string Detail);
public sealed record VLCDeinterlaceSnapshot(VLCInputGeneration Generation, VLCApplyTiming Timing, IReadOnlyList<string> Modes, string Detail);
public sealed record VLCSnapshotRequestResult(VLCPlayerCapabilityResult Result, string? OutputPath, bool OutputObserved, string Detail);
public sealed record VLCAudioOutputCapability(string Name, VLCApplyTiming Timing, string Detail);
public sealed record VLCAudioSettingsSnapshot(IReadOnlyList<VLCAudioOutputCapability> Outputs, VLCApplyTiming DeviceTiming, VLCApplyTiming NormalizationTiming, VLCApplyTiming PassthroughTiming, string Detail);
/// <summary>Owned metadata and codec fields copied from the held VLC 3 input item.</summary>
public sealed record VLCMediaInformationSnapshot(VLCInputGeneration Generation, string Name, string Uri, IReadOnlyDictionary<string, string> Metadata, IReadOnlyList<string> CodecStreams, string Diagnostic) { public IReadOnlyList<VLCCodecStreamInformation> Streams { get; init; } = Array.Empty<VLCCodecStreamInformation>(); }
public enum VLCPlayerSmokePhase { VideoPresentation, Deinterlace, Snapshot, AudioSettings, MediaInformation, Complete }
public enum VLCPlayerSmokeDisposition { Passed, Skipped, Failed }
public sealed record VLCPlayerSmokeStep(VLCPlayerSmokePhase Phase, VLCPlayerSmokeDisposition Disposition, VLCPlayerCapabilityResult Result, string Detail);
public sealed record VLCPlayerSmokeReport(IReadOnlyList<VLCPlayerSmokeStep> Steps) { public bool Passed => Steps.All(x => x.Disposition != VLCPlayerSmokeDisposition.Failed); }

/// <summary>Allowlisted startup options.  Callers cannot pass unchecked argv fragments through this contract.</summary>
public sealed record VLCAudioStartupOptions(string? OutputModule = null, float? NormalizationMaxLevel = null, bool? Passthrough = null)
{
    private static readonly HashSet<string> AllowedOutputs = new(StringComparer.Ordinal) { "directsound", "mmdevice", "waveout" };
    /// <summary>VLC normvol declares a 0.5–10.0 maximum-level range; reject values outside it.</summary>
    public bool IsValid => (OutputModule is null || AllowedOutputs.Contains(OutputModule)) && (NormalizationMaxLevel is null || (float.IsFinite(NormalizationMaxLevel.Value) && NormalizationMaxLevel.Value is >= 0.5f and <= 10f));
    public IReadOnlyList<string> ToArguments()
    {
        if (!IsValid) throw new InvalidOperationException("Audio output module is not allowlisted.");
        var result = new List<string>();
        if (OutputModule is not null) result.Add("--aout=" + OutputModule);
        if (NormalizationMaxLevel is float maximum) { result.Add("--audio-filter=normvol"); result.Add("--norm-max-level=" + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (Passthrough is bool passthrough) result.Add(passthrough ? "--spdif" : "--no-spdif");
        return result;
    }
}

/// <summary>
/// VLC 3 metadata and legacy capability boundary. Live aspect/crop/zoom controls are
/// provided separately by VLCVideoPresentation after review of INPUT_GET_VOUTS.
/// Deinterlace, snapshot and device-control qualification remain separate work.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public sealed class VLCPlayerCapabilities
{
    private readonly Func<VLCCurrentInputLease?> _leaseProvider;
    private readonly IVLCPlayerNative _native;
    public VLCPlayerCapabilities(Func<VLCCurrentInputLease?> leaseProvider) : this(leaseProvider, new VLCPlayerNative()) { }
    internal VLCPlayerCapabilities(Func<VLCCurrentInputLease?> leaseProvider, IVLCPlayerNative native) => (_leaseProvider, _native) = (leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider)), native ?? throw new ArgumentNullException(nameof(native)));

    public VLCVideoPresentationSnapshot? TryGetVideoPresentation(out VLCPlayerCapabilityResult result)
    {
        if (!TryAcquire(out var held, out result)) return null;
        using (held) { if (!StillCurrent(held.Lease)) { result = Stale(); return null; } result = Unsupported(); return new(held.Lease.Generation, VLCApplyTiming.Unsupported, VLCApplyTiming.Unsupported, VLCApplyTiming.Unsupported, null, null, null, "Use VLCVideoPresentation for held-output aspect/crop/zoom controls; this legacy probe does not dispatch video adjustments."); }
    }
    public VLCDeinterlaceSnapshot? TryGetDeinterlace(out VLCPlayerCapabilityResult result)
    {
        if (!TryAcquire(out var held, out result)) return null;
        using (held) { if (!StillCurrent(held.Lease)) { result = Stale(); return null; } result = Unsupported(); return new(held.Lease.Generation, VLCApplyTiming.Unsupported, Array.Empty<string>(), "No source-confirmed public VLC 3 live deinterlace control ABI is exported by the shipped runtime; applying filters may require vout recreation/next open."); }
    }
    public VLCSnapshotRequestResult RequestSnapshot(string? requestedPath = null)
    {
        if (!TryAcquire(out var held, out var result)) return new(result, null, false, "No input is available.");
        using (held) { if (!StillCurrent(held.Lease)) return new(Stale(), null, false, "Input changed while snapshot was requested."); return new(Unsupported(), null, false, "The shipped VLC 3 Windows SDK/export set has no public snapshot request/result ABI. No dispatch was made."); }
    }
    public VLCAudioSettingsSnapshot GetAudioSettings() => new([new("directsound", VLCApplyTiming.Restart, "Allowlisted startup module; no device enumeration claim."), new("mmdevice", VLCApplyTiming.Restart, "Allowlisted startup module; no device enumeration claim."), new("waveout", VLCApplyTiming.Restart, "Allowlisted startup module; no device enumeration claim.")], VLCApplyTiming.Unsupported, VLCApplyTiming.Restart, VLCApplyTiming.Restart, "Device enumeration/change is not qualified without target hardware; normalization and passthrough are startup options.");
    public VLCPlayerCapabilityResult ValidateAudioStartupOptions(VLCAudioStartupOptions options) => options is null || !options.IsValid ? new(VLCPlayerCapabilityResultCode.InvalidArgument) : new(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Restart);
    public VLCMediaInformationSnapshot? TryGetMediaInformation(out VLCPlayerCapabilityResult result)
    {
        if (!TryAcquire(out var held, out result)) return null;
        using (held)
        {
            nint item = _native.InputGetItem(held.Input); if (item == nint.Zero) { result = new(VLCPlayerCapabilityResultCode.InputUnavailable); return null; }
            nint namePointer = _native.InputItemGetName(item); nint uriPointer = _native.InputItemGetUri(item);
            string name; string uri;
            try { name = _native.Utf8Owned(namePointer) ?? string.Empty; uri = _native.Utf8Owned(uriPointer) ?? string.Empty; }
            finally { if (namePointer != nint.Zero) _native.Free(namePointer); if (uriPointer != nint.Zero) _native.Free(uriPointer); }
            if (!StillCurrent(held.Lease)) { result = Stale(); return null; }
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in MetaTypes) { nint text = _native.InputItemGetMeta(item, pair.Value); if (text == nint.Zero) continue; try { string? value = _native.Utf8Owned(text); if (!string.IsNullOrEmpty(value)) metadata.Add(pair.Key, value); } finally { _native.Free(text); } }
            if (!StillCurrent(held.Lease)) { result = Stale(); return null; }
            var streams = VLCCodecInformation.Read(_native, item);
            if (!StillCurrent(held.Lease)) { result = Stale(); return null; }
            result = new(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Live); return new(held.Lease.Generation, name, uri, new ReadOnlyDictionary<string, string>(metadata), Array.AsReadOnly(streams.Select(stream => $"Stream {stream.Index}: {stream.Fields["Codec"]}").ToArray()), $"nameLength={name.Length}; metadataFields={metadata.Count}; uriScheme={(Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Scheme : "none")}; codecStreams={streams.Count}") { Streams = streams };
        }
    }
    private bool TryAcquire(out Held held, out VLCPlayerCapabilityResult result)
    { held = default; if (!_native.IsSupported) { result = new(VLCPlayerCapabilityResultCode.UnsupportedPlatform); return false; } var lease = _leaseProvider(); if (lease is not { IsValid: true } value) { result = new(VLCPlayerCapabilityResultCode.InputUnavailable); return false; } nint input = _native.CurrentInput(value.Playlist); if (input == nint.Zero) { result = new(VLCPlayerCapabilityResultCode.InputUnavailable); return false; } held = new(_native, input, value); result = new(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Live); return true; }
    private bool StillCurrent(VLCCurrentInputLease l) => _leaseProvider() is VLCCurrentInputLease now && now.IsValid && now == l;
    private static VLCPlayerCapabilityResult Stale() => new(VLCPlayerCapabilityResultCode.StaleSnapshot);
    private static VLCPlayerCapabilityResult Unsupported() => new(VLCPlayerCapabilityResultCode.NotSupported, VLCApplyTiming.Unsupported);
    private static readonly IReadOnlyDictionary<string, int> MetaTypes = new Dictionary<string, int> { ["title"] = 0, ["artist"] = 1, ["genre"] = 2, ["album"] = 4, ["description"] = 6, ["date"] = 8 };
    private readonly struct Held : IDisposable { private readonly IVLCPlayerNative? _native; public Held(IVLCPlayerNative n, nint i, VLCCurrentInputLease l) => (_native, Input, Lease) = (n, i, l); public nint Input { get; } public VLCCurrentInputLease Lease { get; } public void Dispose() { if (Input != nint.Zero) _native!.ReleaseInput(Input); } }
}
/// <summary>Deterministic capability probe. Unsupported runtime features are reported as skipped, never passed.</summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public static class VLCPlayerCapabilitiesSmoke
{
    public static VLCPlayerSmokeReport Run(VLCPlayerCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var steps = new List<VLCPlayerSmokeStep>();
        var video = capabilities.TryGetVideoPresentation(out var videoResult); steps.Add(new(VLCPlayerSmokePhase.VideoPresentation, ToDisposition(videoResult), videoResult, video?.Detail ?? "No input."));
        var deinterlace = capabilities.TryGetDeinterlace(out var deinterlaceResult); steps.Add(new(VLCPlayerSmokePhase.Deinterlace, ToDisposition(deinterlaceResult), deinterlaceResult, deinterlace?.Detail ?? "No input."));
        var snapshot = capabilities.RequestSnapshot(); steps.Add(new(VLCPlayerSmokePhase.Snapshot, ToDisposition(snapshot.Result), snapshot.Result, snapshot.Detail));
        var audio = capabilities.GetAudioSettings(); var audioResult = new VLCPlayerCapabilityResult(VLCPlayerCapabilityResultCode.Success, VLCApplyTiming.Restart); steps.Add(new(VLCPlayerSmokePhase.AudioSettings, VLCPlayerSmokeDisposition.Passed, audioResult, audio.Detail));
        var media = capabilities.TryGetMediaInformation(out var mediaResult); steps.Add(new(VLCPlayerSmokePhase.MediaInformation, ToDisposition(mediaResult), mediaResult, media?.Diagnostic ?? "No input."));
        steps.Add(new(VLCPlayerSmokePhase.Complete, steps.All(x => x.Disposition != VLCPlayerSmokeDisposition.Failed) ? VLCPlayerSmokeDisposition.Passed : VLCPlayerSmokeDisposition.Failed, new(VLCPlayerCapabilityResultCode.Success), "Completed."));
        return new(steps);
    }
    private static VLCPlayerSmokeDisposition ToDisposition(VLCPlayerCapabilityResult result) => result.Code is VLCPlayerCapabilityResultCode.Success ? VLCPlayerSmokeDisposition.Passed : result.Code is VLCPlayerCapabilityResultCode.NotSupported or VLCPlayerCapabilityResultCode.InputUnavailable or VLCPlayerCapabilityResultCode.UnsupportedPlatform ? VLCPlayerSmokeDisposition.Skipped : VLCPlayerSmokeDisposition.Failed;
}
/// <summary>Optional public-libVLC controls for a host-owned media-player handle. This cannot be inferred from an internal input pointer.</summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public sealed class VLCLibVLCVideoCapabilities
{
    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal) { "", "blend", "bob", "discard", "linear", "mean", "x", "yadif", "yadif2x", "phosphor", "ivtc" };
    private readonly nint _player;
    public VLCLibVLCVideoCapabilities(nint mediaPlayer) => _player = mediaPlayer;
    public VLCPlayerCapabilityResult SetZoom(float scale) { if (_player == nint.Zero || !float.IsFinite(scale) || scale < 0) return new(VLCPlayerCapabilityResultCode.InvalidArgument); VLCLibVLCVideo.SetScale(_player, scale); return new(VLCPlayerCapabilityResultCode.RequestDispatched, VLCApplyTiming.Live); }
    public VLCPlayerCapabilityResult SetAspectRatio(string? value) { if (_player == nint.Zero || value is null || value.Contains('\0')) return new(VLCPlayerCapabilityResultCode.InvalidArgument); VLCLibVLCVideo.SetAspectRatio(_player, value); return new(VLCPlayerCapabilityResultCode.RequestDispatched, VLCApplyTiming.Live); }
    public VLCPlayerCapabilityResult SetCrop(string? value) { if (_player == nint.Zero || value is null || value.Contains('\0')) return new(VLCPlayerCapabilityResultCode.InvalidArgument); VLCLibVLCVideo.SetCrop(_player, value); return new(VLCPlayerCapabilityResultCode.RequestDispatched, VLCApplyTiming.Live); }
    public VLCPlayerCapabilityResult SetDeinterlace(string? mode) { if (_player == nint.Zero || mode is null || !Modes.Contains(mode)) return new(VLCPlayerCapabilityResultCode.InvalidArgument); VLCLibVLCVideo.SetDeinterlace(_player, mode); return new(VLCPlayerCapabilityResultCode.RequestDispatched, VLCApplyTiming.Live); }
    public VLCSnapshotRequestResult RequestSnapshot(string outputPath, uint width = 0, uint height = 0)
    { if (_player == nint.Zero || string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathFullyQualified(outputPath)) return new(new VLCPlayerCapabilityResult(VLCPlayerCapabilityResultCode.InvalidArgument), null, false, "A full output path is required."); int error = VLCLibVLCVideo.TakeSnapshot(_player, 0, outputPath, width, height); return error == 0 ? new(new VLCPlayerCapabilityResult(VLCPlayerCapabilityResultCode.RequestDispatched, VLCApplyTiming.Live), outputPath, false, "Request accepted; no completion event or output observation is available.") : new(new VLCPlayerCapabilityResult(VLCPlayerCapabilityResultCode.NativeFailure, VLCApplyTiming.Live, error), outputPath, false, "No active vout or native dispatch failure."); }
}
internal interface IVLCPlayerNative { bool IsSupported { get; } nint CurrentInput(nint playlist); void ReleaseInput(nint input); nint InputGetItem(nint input); nint InputItemGetName(nint item); nint InputItemGetUri(nint item); nint InputItemGetMeta(nint item, int type); nint InputItemGetInfo(nint item, string category, string name); string Translate(string text); string? Utf8Owned(nint text); void Free(nint text); }
internal sealed class VLCPlayerNative : IVLCPlayerNative { public bool IsSupported => VLCPlatform.IsSupported; public nint CurrentInput(nint p) => VLCCore.PlaylistCurrentInput(p); public void ReleaseInput(nint i) => VLCCore.ObjectRelease(i); public nint InputGetItem(nint i) => VLCCore.InputGetItem(i); public nint InputItemGetName(nint i) => VLCCore.InputItemGetName(i); public nint InputItemGetUri(nint i) => VLCCore.InputItemGetUri(i); public nint InputItemGetMeta(nint i, int t) => VLCCore.InputItemGetMeta(i, t); public nint InputItemGetInfo(nint i, string c, string n) => VLCCore.InputItemGetInfo(i, c, n); public unsafe string Translate(string text) { byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text + "\0"); fixed (byte* pointer = utf8) return Marshal.PtrToStringUTF8(VLCCore.GetText((nint)pointer)) ?? text; } public string? Utf8Owned(nint t) => t == nint.Zero ? null : Marshal.PtrToStringUTF8(t); public void Free(nint t) => VLCCore.Free(t); }
