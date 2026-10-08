using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using VLCLR.Native;
using VLCLR.Types;

namespace VLCLR;

/// <summary>Host-supplied identity of the live VLC core and media. The provider must return null before teardown or replacement.</summary>
public readonly record struct VLCCurrentInputLease(nint Playlist, long CoreGeneration, long MediaGeneration)
{
    public bool IsValid => Playlist != nint.Zero;
    internal VLCInputGeneration Generation => new(CoreGeneration, MediaGeneration);
}

/// <summary>A generation-stamped, copied view of a VLC input.</summary>
public readonly record struct VLCInputGeneration(long CoreGeneration, long MediaGeneration);

public enum VLCMediaResultCode { Success, RequestDispatched, InvalidArgument, OutOfRange, StaleSnapshot, InputUnavailable, NotSupported, NativeFailure, UnsupportedPlatform }
public readonly record struct VLCMediaResult(VLCMediaResultCode Code, int NativeError = 0)
{
    public bool Succeeded => Code == VLCMediaResultCode.Success;
    public bool RequestWasDispatched => Code == VLCMediaResultCode.RequestDispatched;
}

public enum VLCTrackKind { Audio, Subtitle }
public sealed record VLCTrackChoice(long Id, string Label, bool IsDisabled);
public sealed record VLCTrackSnapshot(VLCInputGeneration Generation, IReadOnlyList<VLCTrackChoice> AudioTracks, IReadOnlyList<VLCTrackChoice> SubtitleTracks, long? SelectedAudioId, long? SelectedSubtitleId);
public sealed record VLCTitleChoice(long Id, string Label);
public sealed record VLCChapterChoice(long Id, string Label);
public sealed record VLCTitleChapterSnapshot(VLCInputGeneration Generation, IReadOnlyList<VLCTitleChoice> Titles, IReadOnlyList<VLCChapterChoice> Chapters, long? SelectedTitleId, long? SelectedChapterId);
public enum VLCMediaDelayKind { Audio, Subtitle }
/// <summary>Effective VLC delays, represented as <see cref="TimeSpan"/>. VLC stores signed microseconds.</summary>
public readonly record struct VLCMediaDelaySnapshot(VLCInputGeneration Generation, TimeSpan Audio, TimeSpan Subtitle);
public enum VLCExternalTrackKind { Subtitle, Audio }

/// <summary>Outcome of fixed-ABI input-item attachment. AcceptedIntoItem never means a live track appeared.</summary>
public sealed record VLCExternalTrackAttachmentResult(
    VLCMediaResult Result,
    bool AcceptedIntoItem,
    bool PendingUntilNextOpen,
    bool ObservedLive,
    bool? ObservedSelected,
    VLCTrackSnapshot? FreshTracks);

/// <summary>
/// Safe, polling-oriented VLC 3 media controls. Snapshot objects contain only copied IDs and strings;
/// native input, ES, title, chapter and variable-list pointers never leave this type. It intentionally
/// does not make any claim about subtitle/subpicture rendering.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class VLCMediaCapabilities : IVLCMediaCapabilitySmokeAdapter
{
    /// <summary>Managed safety bound for delay writes. VLC accepts signed 64-bit microseconds; this API limits daily-use requests to one day.</summary>
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(1);

    private const int SlaveTypeSubtitle = 0;
    private const int SlaveTypeAudio = 1;
    private const int SlavePriorityUser = 5;
    private readonly Func<VLCCurrentInputLease?> _leaseProvider;
    private readonly IVLCMediaNative _native;

    public VLCMediaCapabilities(Func<VLCCurrentInputLease?> leaseProvider)
        : this(leaseProvider, new VLCMediaNative()) { }

    internal VLCMediaCapabilities(Func<VLCCurrentInputLease?> leaseProvider, IVLCMediaNative native)
    {
        _leaseProvider = leaseProvider ?? throw new ArgumentNullException(nameof(leaseProvider));
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public VLCTrackSnapshot? TryGetTracks(out VLCMediaResult result)
    {
        if (!TryAcquire(out var held, out result)) return null;
        using (held) { var value = ReadTracks(held.Input, held.Generation, out result); if (!GenerationStillCurrent(held.Lease)) { result = new(VLCMediaResultCode.StaleSnapshot); return null; } return value; }
    }

    public VLCTitleChapterSnapshot? TryGetTitlesAndChapters(out VLCMediaResult result)
    {
        if (!TryAcquire(out var held, out result)) return null;
        using (held) { var value = ReadTitlesAndChapters(held.Input, held.Generation, out result); if (!GenerationStillCurrent(held.Lease)) { result = new(VLCMediaResultCode.StaleSnapshot); return null; } return value; }
    }

    public VLCMediaDelaySnapshot? TryGetDelays(out VLCMediaResult result)
    {
        if (!TryAcquire(out var held, out result)) return null;
        using (held) { var value = ReadDelays(held.Input, held.Generation, out result); if (!GenerationStillCurrent(held.Lease)) { result = new(VLCMediaResultCode.StaleSnapshot); return null; } return value; }
    }

    public VLCMediaResult SelectTrack(VLCTrackSnapshot snapshot, VLCTrackKind kind, long id)
    {
        if (snapshot is null || !Contains(kind == VLCTrackKind.Audio ? snapshot.AudioTracks : snapshot.SubtitleTracks, id)) return new(VLCMediaResultCode.InvalidArgument);
        return WithMutation(snapshot.Generation, input => SetInteger(input, kind == VLCTrackKind.Audio ? "audio-es" : "spu-es", id));
    }

    public VLCMediaResult SelectTitle(VLCTitleChapterSnapshot snapshot, long id)
    {
        if (snapshot is null || !Contains(snapshot.Titles, id)) return new(VLCMediaResultCode.InvalidArgument);
        return WithMutation(snapshot.Generation, input => SetInteger(input, "title", id));
    }

    public VLCMediaResult SelectChapter(VLCTitleChapterSnapshot snapshot, long id)
    {
        if (snapshot is null || !Contains(snapshot.Chapters, id)) return new(VLCMediaResultCode.InvalidArgument);
        return WithMutation(snapshot.Generation, input => SetInteger(input, "chapter", id));
    }

    public VLCMediaResult SetDelay(VLCMediaDelayKind kind, TimeSpan delay)
    {
        if (delay < -MaximumDelay || delay > MaximumDelay || delay.Ticks % 10 != 0) return new(VLCMediaResultCode.OutOfRange);
        return WithMutation(null, input => SetInteger(input, kind == VLCMediaDelayKind.Audio ? "audio-delay" : "spu-delay", delay.Ticks / 10));
    }

    public VLCMediaResult ResetDelay(VLCMediaDelayKind kind) => SetDelay(kind, TimeSpan.Zero);

    /// <summary>
    /// Attaches a slave to the current input item through exported fixed-signature APIs. VLC 3 source
    /// appends it to the item only; it does not inject it into an already-running source, so this result
    /// always distinguishes pending-next-open from an observed live/selected track.
    /// </summary>
    public VLCExternalTrackAttachmentResult AttachExternalTrack(string localPathOrUri, VLCExternalTrackKind kind)
    {
        if (!TryNormalizeUri(localPathOrUri, out string? uri)) return new(new(VLCMediaResultCode.InvalidArgument), false, false, false, null, null);
        if (!TryAcquire(out var held, out var result)) return new(result, false, false, false, null, null);
        using (held)
        {
            nint item = _native.InputGetItem(held.Input);
            nint itemHold = item == nint.Zero ? nint.Zero : _native.InputItemHold(item);
            if (itemHold == nint.Zero) return new(new(VLCMediaResultCode.InputUnavailable), false, false, false, null, null);
            try
            {
                nint slave = _native.NewSlave(uri!, kind == VLCExternalTrackKind.Audio ? SlaveTypeAudio : SlaveTypeSubtitle, SlavePriorityUser);
                if (slave == nint.Zero) return new(new(VLCMediaResultCode.NativeFailure), false, false, false, null, null);
                int error = _native.AddSlave(itemHold, slave);
                if (error != 0)
                {
                    // 3.0.23 item.c transfers ownership only after TAB_APPEND and success.
                    _native.Free(slave);
                    return new(new(VLCMediaResultCode.NativeFailure, error), false, false, false, null, null);
                }

                // A fresh read is deliberately made after attachment. The fixed item API is pending-only,
                // therefore no current-source selection can be attributed to this request.
                VLCTrackSnapshot? tracks = ReadTracks(held.Input, held.Generation, out var read);
                if (!GenerationStillCurrent(held.Lease)) return new(new(VLCMediaResultCode.StaleSnapshot), true, true, false, false, null);
                return new(read.Code == VLCMediaResultCode.Success ? new(VLCMediaResultCode.RequestDispatched) : read, true, true, false, false, tracks);
            }
            finally { _native.InputItemRelease(itemHold); }
        }
    }

    private VLCTrackSnapshot? ReadTracks(nint input, VLCInputGeneration generation, out VLCMediaResult result)
    {
        if (!TryReadChoices(input, "audio-es", out var audio, out result) || !TryReadChoices(input, "spu-es", out var subtitle, out result)) return null;
        if (!_native.TryGetInteger(input, "audio-es", out long selectedAudio, out int error) || !_native.TryGetInteger(input, "spu-es", out long selectedSubtitle, out error)) { result = new(VLCMediaResultCode.NativeFailure, error); return null; }
        result = new(VLCMediaResultCode.Success);
        return new(generation, audio.Select(x => new VLCTrackChoice(x.Id, x.Label ?? string.Empty, x.Id == -1)).ToArray(), subtitle.Select(x => new VLCTrackChoice(x.Id, x.Label ?? string.Empty, x.Id == -1)).ToArray(), selectedAudio, selectedSubtitle);
    }

    private VLCTitleChapterSnapshot? ReadTitlesAndChapters(nint input, VLCInputGeneration generation, out VLCMediaResult result)
    {
        if (!TryReadChoices(input, "title", out var titles, out result) || !TryReadChoices(input, "chapter", out var chapters, out result)) return null;
        if (!_native.TryGetInteger(input, "title", out long title, out int error) || !_native.TryGetInteger(input, "chapter", out long chapter, out error)) { result = new(VLCMediaResultCode.NativeFailure, error); return null; }
        result = new(VLCMediaResultCode.Success);
        return new(generation, titles.Select(x => new VLCTitleChoice(x.Id, x.Label ?? string.Empty)).ToArray(), chapters.Select(x => new VLCChapterChoice(x.Id, x.Label ?? string.Empty)).ToArray(), title, chapter);
    }

    private VLCMediaDelaySnapshot? ReadDelays(nint input, VLCInputGeneration generation, out VLCMediaResult result)
    {
        if (!_native.TryGetInteger(input, "audio-delay", out long audio, out int error) || !_native.TryGetInteger(input, "spu-delay", out long subtitle, out error)) { result = new(VLCMediaResultCode.NativeFailure, error); return null; }
        try { result = new(VLCMediaResultCode.Success); return new(generation, TimeSpan.FromTicks(checked(audio * 10)), TimeSpan.FromTicks(checked(subtitle * 10))); }
        catch (OverflowException) { result = new(VLCMediaResultCode.NativeFailure); return null; }
    }

    private bool TryReadChoices(nint input, string name, out IReadOnlyList<VLCNativeChoice> choices, out VLCMediaResult result)
    {
        using var list = _native.GetChoices(input, name, out int error);
        if (list is null) { choices = Array.Empty<VLCNativeChoice>(); result = new(VLCMediaResultCode.NativeFailure, error); return false; }
        if (!list.HasMatchingCounts) { choices = Array.Empty<VLCNativeChoice>(); result = new(VLCMediaResultCode.NativeFailure); return false; }
        choices = list.Copy(); result = new(VLCMediaResultCode.Success); return true;
    }

    private VLCMediaResult WithMutation(VLCInputGeneration? required, Func<nint, VLCMediaResult> action)
    {
        if (!TryAcquire(out var held, out var result)) return result;
        using (held)
        {
            if (required is VLCInputGeneration generation && generation != held.Generation) return new(VLCMediaResultCode.StaleSnapshot);
            VLCMediaResult mutation = action(held.Input);
            if (!GenerationStillCurrent(held.Lease)) return new(VLCMediaResultCode.StaleSnapshot);
            return mutation;
        }
    }

    private VLCMediaResult SetInteger(nint input, string name, long value)
    {
        int error = _native.SetInteger(input, name, value);
        return error == 0 ? new(VLCMediaResultCode.RequestDispatched) : new(VLCMediaResultCode.NativeFailure, error);
    }

    private bool TryAcquire(out HeldInputLease held, out VLCMediaResult result)
    {
        held = default;
        if (!_native.IsSupported) { result = new(VLCMediaResultCode.UnsupportedPlatform); return false; }
        VLCCurrentInputLease? lease = _leaseProvider();
        if (lease is not { IsValid: true } value) { result = new(VLCMediaResultCode.InputUnavailable); return false; }
        nint input = _native.CurrentInput(value.Playlist);
        if (input == nint.Zero) { result = new(VLCMediaResultCode.InputUnavailable); return false; }
        held = new(_native, input, value); result = new(VLCMediaResultCode.Success); return true;
    }

    private bool GenerationStillCurrent(VLCCurrentInputLease lease) => _leaseProvider() is VLCCurrentInputLease current && current.IsValid && current.CoreGeneration == lease.CoreGeneration && current.MediaGeneration == lease.MediaGeneration && current.Playlist == lease.Playlist;
    private static bool Contains(IEnumerable<VLCTrackChoice> choices, long id) => choices.Any(x => x.Id == id);
    private static bool Contains(IEnumerable<VLCTitleChoice> choices, long id) => choices.Any(x => x.Id == id);
    private static bool Contains(IEnumerable<VLCChapterChoice> choices, long id) => choices.Any(x => x.Id == id);

    private static bool TryNormalizeUri(string? candidate, out string? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (Path.IsPathFullyQualified(candidate)) { if (!File.Exists(candidate)) return false; uri = new Uri(candidate).AbsoluteUri; return true; }
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed)) return false;
        if (parsed.IsFile && !File.Exists(parsed.LocalPath)) return false;
        uri = parsed.AbsoluteUri; return true;
    }

    private readonly struct HeldInputLease : IDisposable
    {
        private readonly IVLCMediaNative? _native;
        public HeldInputLease(IVLCMediaNative native, nint input, VLCCurrentInputLease lease) => (_native, Input, Lease) = (native, input, lease);
        public nint Input { get; }
        public VLCCurrentInputLease Lease { get; }
        public VLCInputGeneration Generation => Lease.Generation;
        public void Dispose() { if (Input != nint.Zero) _native!.ReleaseInput(Input); }
    }
}

internal readonly record struct VLCNativeChoice(long Id, string? Label);
internal interface IVLCChoiceList : IDisposable { bool HasMatchingCounts { get; } IReadOnlyList<VLCNativeChoice> Copy(); }
internal interface IVLCMediaNative
{
    bool IsSupported { get; }
    nint CurrentInput(nint playlist); void ReleaseInput(nint input); nint InputGetItem(nint input); nint InputItemHold(nint item); void InputItemRelease(nint item);
    bool TryGetInteger(nint input, string name, out long value, out int error); int SetInteger(nint input, string name, long value); IVLCChoiceList? GetChoices(nint input, string name, out int error);
    nint NewSlave(string uri, int type, int priority); int AddSlave(nint item, nint slave); void Free(nint ptr);
}

internal sealed unsafe class VLCMediaNative : IVLCMediaNative
{
    public bool IsSupported => VLCPlatform.IsSupported;
    public nint CurrentInput(nint playlist) => VLCCore.PlaylistCurrentInput(playlist);
    public void ReleaseInput(nint input) => VLCCore.ObjectRelease(input);
    public nint InputGetItem(nint input) => VLCCore.InputGetItem(input);
    public nint InputItemHold(nint item) => VLCCore.InputItemHold(item);
    public void InputItemRelease(nint item) => VLCCore.InputItemRelease(item);
    public bool TryGetInteger(nint input, string name, out long value, out int error) { error = VLCCore.VarGetChecked(input, name, VLCVarType.Integer, out var native); value = native.Integer; return error == 0; }
    public int SetInteger(nint input, string name, long value) => VLCCore.VarSetChecked(input, name, VLCVarType.Integer, new VLCValueNative { Integer = value });
    public IVLCChoiceList? GetChoices(nint input, string name, out int error)
    {
        VLCValueNative values = default, texts = default;
        error = VLCCore.VarChange(input, name, VLCVarAction.GetChoices, ref values, ref texts);
        return error == 0 ? new VLCNativeChoiceList(values, texts) : null;
    }
    public nint NewSlave(string uri, int type, int priority) => VLCCore.InputItemSlaveNew(uri, type, priority);
    public int AddSlave(nint item, nint slave) => VLCCore.InputItemAddSlave(item, slave);
    public void Free(nint ptr) => VLCCore.Free(ptr);

    private sealed class VLCNativeChoiceList : IVLCChoiceList
    {
        private VLCValueNative _values, _texts;
        public VLCNativeChoiceList(VLCValueNative values, VLCValueNative texts) => (_values, _texts) = (values, texts);
        public bool HasMatchingCounts { get { if (_values.Address == nint.Zero || _texts.Address == nint.Zero) return false; var v = Unsafe.Read<VLCListNative>((void*)_values.Address); var t = Unsafe.Read<VLCListNative>((void*)_texts.Address); return v.Count >= 0 && v.Count == t.Count && (v.Count == 0 || (v.Values != nint.Zero && t.Values != nint.Zero)); } }
        public IReadOnlyList<VLCNativeChoice> Copy()
        {
            var values = Unsafe.Read<VLCListNative>((void*)_values.Address); var texts = Unsafe.Read<VLCListNative>((void*)_texts.Address);
            var copied = new VLCNativeChoice[values.Count];
            for (int i = 0; i < copied.Length; i++) { long id = Unsafe.Read<VLCValueNative>((void*)(values.Values + i * 8)).Integer; nint label = Unsafe.Read<VLCValueNative>((void*)(texts.Values + i * 8)).String; copied[i] = new(id, label == nint.Zero ? null : Marshal.PtrToStringUTF8(label)); }
            return copied;
        }
        public void Dispose() { if (_values.Address != nint.Zero) { VLCCore.VarFreeList(ref _values, ref _texts); _values = default; _texts = default; } }
    }

    [StructLayout(LayoutKind.Sequential)] private struct VLCListNative { public int Type; public int Count; public nint Values; }
}
