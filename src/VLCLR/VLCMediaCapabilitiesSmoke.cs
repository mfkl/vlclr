using System.Runtime.Versioning;

namespace VLCLR;

/// <summary>Narrow host-facing surface used by deterministic media capability smoke orchestration.</summary>
public interface IVLCMediaCapabilitySmokeAdapter
{
    VLCTrackSnapshot? TryGetTracks(out VLCMediaResult result);
    VLCTitleChapterSnapshot? TryGetTitlesAndChapters(out VLCMediaResult result);
    VLCMediaDelaySnapshot? TryGetDelays(out VLCMediaResult result);
    VLCMediaResult SelectTrack(VLCTrackSnapshot snapshot, VLCTrackKind kind, long id);
    VLCMediaResult SelectTitle(VLCTitleChapterSnapshot snapshot, long id);
    VLCMediaResult SelectChapter(VLCTitleChapterSnapshot snapshot, long id);
    VLCMediaResult SetDelay(VLCMediaDelayKind kind, TimeSpan delay);
    VLCMediaResult ResetDelay(VLCMediaDelayKind kind);
    VLCExternalTrackAttachmentResult AttachExternalTrack(string localPathOrUri, VLCExternalTrackKind kind);
}

public enum VLCMediaSmokePhase { Tracks, Titles, Chapters, AudioDelay, SubtitleDelay, Attachment, Complete }
public enum VLCMediaSmokeDisposition { Passed, Skipped, Failed }
public sealed record VLCMediaSmokeStep(VLCMediaSmokePhase Phase, VLCMediaSmokeDisposition Disposition, VLCMediaResult Result, string? Detail = null);
public sealed record VLCMediaSmokeReport(IReadOnlyList<VLCMediaSmokeStep> Steps)
{
    public bool Passed => Steps.All(x => x.Disposition is VLCMediaSmokeDisposition.Passed or VLCMediaSmokeDisposition.Skipped);
}
public sealed record VLCMediaSmokeAttachment(string LocalPathOrUri, VLCExternalTrackKind Kind);
public sealed record VLCMediaSmokeOptions(int MaxPolls = 20)
{
    internal bool IsValid => MaxPolls is > 0 and <= 1000;
}

/// <summary>
/// Host-driven smoke for track/title/chapter/delay requests. The supplied poll advances the product
/// dispatcher; VLCLR never sleeps or captures host pointers. Missing media capabilities are skipped,
/// while capabilities exposed by a fixture receive positive readback assertions.
/// </summary>
[SupportedOSPlatform("windows")]
[SupportedOSPlatform("linux")]
public static class VLCMediaCapabilitiesSmoke
{
    public static async ValueTask<VLCMediaSmokeReport> RunAsync(
        IVLCMediaCapabilitySmokeAdapter adapter,
        Func<CancellationToken, ValueTask> poll,
        VLCMediaSmokeAttachment? attachment = null,
        VLCMediaSmokeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (adapter is null || poll is null) return new([Fail(VLCMediaSmokePhase.Complete, new(VLCMediaResultCode.InvalidArgument))]);
        options ??= new();
        if (!options.IsValid) return new([Fail(VLCMediaSmokePhase.Complete, new(VLCMediaResultCode.InvalidArgument))]);
        var steps = new List<VLCMediaSmokeStep>();
        steps.Add(await ExerciseTracks(adapter, poll, options, cancellationToken));
        steps.Add(await ExerciseTitles(adapter, poll, options, cancellationToken));
        steps.Add(await ExerciseDelay(adapter, VLCMediaDelayKind.Audio, VLCMediaSmokePhase.AudioDelay, poll, options, cancellationToken));
        steps.Add(await ExerciseDelay(adapter, VLCMediaDelayKind.Subtitle, VLCMediaSmokePhase.SubtitleDelay, poll, options, cancellationToken));
        if (attachment is not null)
        {
            var attached = adapter.AttachExternalTrack(attachment.LocalPathOrUri, attachment.Kind);
            // A fixed input-item attachment is valid but deliberately not a live-track assertion.
            steps.Add(attached.AcceptedIntoItem && attached.PendingUntilNextOpen
                ? new(VLCMediaSmokePhase.Attachment, VLCMediaSmokeDisposition.Passed, attached.Result, "Accepted for the next input open; live rendering/selection was not asserted.")
                : Fail(VLCMediaSmokePhase.Attachment, attached.Result));
        }
        else steps.Add(new(VLCMediaSmokePhase.Attachment, VLCMediaSmokeDisposition.Skipped, new(VLCMediaResultCode.NotSupported), "No attachment fixture was supplied."));
        steps.Add(new(VLCMediaSmokePhase.Complete, steps.All(x => x.Disposition != VLCMediaSmokeDisposition.Failed) ? VLCMediaSmokeDisposition.Passed : VLCMediaSmokeDisposition.Failed, new(VLCMediaResultCode.Success)));
        return new(steps);
    }

    private static async ValueTask<VLCMediaSmokeStep> ExerciseTracks(IVLCMediaCapabilitySmokeAdapter adapter, Func<CancellationToken, ValueTask> poll, VLCMediaSmokeOptions options, CancellationToken ct)
    {
        var snapshot = adapter.TryGetTracks(out var read);
        if (snapshot is null) return Fail(VLCMediaSmokePhase.Tracks, read);
        VLCTrackChoice? audio = snapshot.AudioTracks.FirstOrDefault(x => !x.IsDisabled);
        VLCTrackChoice? subtitle = snapshot.SubtitleTracks.FirstOrDefault(x => !x.IsDisabled);
        if (audio is null && subtitle is null) return new(VLCMediaSmokePhase.Tracks, VLCMediaSmokeDisposition.Skipped, read, "Fixture exposes no selectable audio or subtitle track.");
        if (audio is not null)
        {
            var request = adapter.SelectTrack(snapshot, VLCTrackKind.Audio, audio.Id);
            if (!request.RequestWasDispatched) return Fail(VLCMediaSmokePhase.Tracks, request);
            var wait = await WaitTracks(adapter, poll, options, x => x.SelectedAudioId == audio.Id, ct);
            if (wait is not null) return wait;
        }
        if (subtitle is not null)
        {
            var now = adapter.TryGetTracks(out read); if (now is null) return Fail(VLCMediaSmokePhase.Tracks, read);
            var request = adapter.SelectTrack(now, VLCTrackKind.Subtitle, subtitle.Id);
            if (!request.RequestWasDispatched) return Fail(VLCMediaSmokePhase.Tracks, request);
            var wait = await WaitTracks(adapter, poll, options, x => x.SelectedSubtitleId == subtitle.Id, ct);
            if (wait is not null) return wait;
        }
        return new(VLCMediaSmokePhase.Tracks, VLCMediaSmokeDisposition.Passed, new(VLCMediaResultCode.Success));
    }

    private static async ValueTask<VLCMediaSmokeStep> ExerciseTitles(IVLCMediaCapabilitySmokeAdapter adapter, Func<CancellationToken, ValueTask> poll, VLCMediaSmokeOptions options, CancellationToken ct)
    {
        var snapshot = adapter.TryGetTitlesAndChapters(out var read);
        if (snapshot is null) return Fail(VLCMediaSmokePhase.Titles, read);
        if (snapshot.Titles.Count == 0 && snapshot.Chapters.Count == 0) return new(VLCMediaSmokePhase.Titles, VLCMediaSmokeDisposition.Skipped, read, "Fixture exposes no title/chapter controls.");
        VLCTitleChoice? title = snapshot.Titles.FirstOrDefault();
        if (title is not null)
        {
            var request = adapter.SelectTitle(snapshot, title.Id); if (!request.RequestWasDispatched) return Fail(VLCMediaSmokePhase.Titles, request);
            var wait = await WaitTitles(adapter, poll, options, x => x.SelectedTitleId == title.Id, ct); if (wait is not null) return wait;
        }
        var current = adapter.TryGetTitlesAndChapters(out read); if (current is null) return Fail(VLCMediaSmokePhase.Chapters, read);
        VLCChapterChoice? chapter = current.Chapters.FirstOrDefault();
        if (chapter is null) return new(VLCMediaSmokePhase.Chapters, VLCMediaSmokeDisposition.Skipped, new(VLCMediaResultCode.Success), "Selected title has no chapters.");
        var chapterRequest = adapter.SelectChapter(current, chapter.Id); if (!chapterRequest.RequestWasDispatched) return Fail(VLCMediaSmokePhase.Chapters, chapterRequest);
        var chapterWait = await WaitTitles(adapter, poll, options, x => x.SelectedChapterId == chapter.Id, ct); return chapterWait ?? new(VLCMediaSmokePhase.Chapters, VLCMediaSmokeDisposition.Passed, new(VLCMediaResultCode.Success));
    }

    private static async ValueTask<VLCMediaSmokeStep> ExerciseDelay(IVLCMediaCapabilitySmokeAdapter adapter, VLCMediaDelayKind kind, VLCMediaSmokePhase phase, Func<CancellationToken, ValueTask> poll, VLCMediaSmokeOptions options, CancellationToken ct)
    {
        if (adapter.TryGetDelays(out var read) is null) return Fail(phase, read);
        TimeSpan test = TimeSpan.FromMilliseconds(250);
        var request = adapter.SetDelay(kind, test); if (!request.RequestWasDispatched) return Fail(phase, request);
        var set = await WaitDelays(adapter, poll, options, x => (kind == VLCMediaDelayKind.Audio ? x.Audio : x.Subtitle) == test, ct); if (set is not null) return set with { Phase = phase };
        request = adapter.ResetDelay(kind); if (!request.RequestWasDispatched) return Fail(phase, request);
        var reset = await WaitDelays(adapter, poll, options, x => (kind == VLCMediaDelayKind.Audio ? x.Audio : x.Subtitle) == TimeSpan.Zero, ct); return reset is null ? new(phase, VLCMediaSmokeDisposition.Passed, new(VLCMediaResultCode.Success)) : reset with { Phase = phase };
    }

    private static async ValueTask<VLCMediaSmokeStep?> WaitTracks(IVLCMediaCapabilitySmokeAdapter a, Func<CancellationToken, ValueTask> poll, VLCMediaSmokeOptions o, Func<VLCTrackSnapshot, bool> predicate, CancellationToken ct)
    { for (int i = 0; i <= o.MaxPolls; i++) { ct.ThrowIfCancellationRequested(); var s = a.TryGetTracks(out var r); if (s is null) return Fail(VLCMediaSmokePhase.Tracks, r); if (predicate(s)) return null; if (i != o.MaxPolls) await poll(ct); } return new(VLCMediaSmokePhase.Tracks, VLCMediaSmokeDisposition.Failed, new(VLCMediaResultCode.NativeFailure), "Track readback timed out."); }
    private static async ValueTask<VLCMediaSmokeStep?> WaitTitles(IVLCMediaCapabilitySmokeAdapter a, Func<CancellationToken, ValueTask> poll, VLCMediaSmokeOptions o, Func<VLCTitleChapterSnapshot, bool> predicate, CancellationToken ct)
    { for (int i = 0; i <= o.MaxPolls; i++) { ct.ThrowIfCancellationRequested(); var s = a.TryGetTitlesAndChapters(out var r); if (s is null) return Fail(VLCMediaSmokePhase.Titles, r); if (predicate(s)) return null; if (i != o.MaxPolls) await poll(ct); } return new(VLCMediaSmokePhase.Titles, VLCMediaSmokeDisposition.Failed, new(VLCMediaResultCode.NativeFailure), "Title/chapter readback timed out."); }
    private static async ValueTask<VLCMediaSmokeStep?> WaitDelays(IVLCMediaCapabilitySmokeAdapter a, Func<CancellationToken, ValueTask> poll, VLCMediaSmokeOptions o, Func<VLCMediaDelaySnapshot, bool> predicate, CancellationToken ct)
    { for (int i = 0; i <= o.MaxPolls; i++) { ct.ThrowIfCancellationRequested(); var s = a.TryGetDelays(out var r); if (s is null) return Fail(VLCMediaSmokePhase.AudioDelay, r); if (predicate(s.Value)) return null; if (i != o.MaxPolls) await poll(ct); } return new(VLCMediaSmokePhase.AudioDelay, VLCMediaSmokeDisposition.Failed, new(VLCMediaResultCode.NativeFailure), "Delay readback timed out."); }
    private static VLCMediaSmokeStep Fail(VLCMediaSmokePhase phase, VLCMediaResult result) => new(phase, VLCMediaSmokeDisposition.Failed, result);
}
