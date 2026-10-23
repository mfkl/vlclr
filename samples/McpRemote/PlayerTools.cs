using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using VLCLR;
using VLCLR.Types;

namespace McpRemote;

/// <summary>
/// The tools an agent sees. Each returns plain text: what it did, or why not.
/// VLC applies commands asynchronously: a command waits until VLC has applied
/// it, up to <see cref="ApplyTimeout"/>, and answers with the new state, so
/// that the agent does not have to ask for the status again. When VLC closes
/// the interface, the server stops: a command stops waiting at once, so that
/// no call outlives the player.
/// </summary>
[McpServerToolType]
public sealed class PlayerTools(VLCPlayer player, VLCPlaylist playlist, IMomentSearch search, IHostApplicationLifetime lifetime)
{
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(2);

    // How far the time may move between two polls, 50 ms apart, and still
    // count as steady playback rather than a jump.
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(0.3);

    [McpServerTool(Name = "open")]
    [Description("Add a local file or a URL to the playlist, and play it.")]
    public async Task<string> Open(
        [Description("Absolute path of a local file, or a URL")] string location,
        [Description("Start playing it; false only adds it to the playlist")] bool play = true,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out Uri? uri))
            return "Give an absolute path or a URL.";
        if (uri.IsFile && !File.Exists(uri.LocalPath))
            return $"There is no file at {uri.LocalPath}.";

        long index = playlist.Append(uri.AbsoluteUri);
        if (index < 0)
            return "VLC could not add it to the playlist.";
        if (!play)
            return $"Added as playlist item {index + 1}.";

        if (!playlist.PlayAt(index))
            return $"Added as playlist item {index + 1}, but VLC could not play it.";

        return await WaitUntil(() => playlist.CurrentIndex == index && player.State == VLCPlayerState.Playing, cancellationToken)
            ? $"Playing {player.CurrentMediaName}, playlist item {index + 1}."
            : $"Added as playlist item {index + 1}; VLC has not started playing it yet.";
    }

    [McpServerTool(Name = "search", ReadOnly = true)]
    [Description("Find the moments of the current media that match a description, best first, with their times: " +
                 "then seek to one. Depending on the search VLC is set up with, it matches what is said (the subtitles) " +
                 "or what is shown.")]
    public async Task<string> Search(
        [Description("What to find, in plain words")] string query,
        [Description("How many moments to return")] int count = 5,
        CancellationToken cancellationToken = default)
    {
        string? media = player.CurrentMediaUri;
        if (media is null)
            return "Nothing is playing: open a media first.";

        using var searching = Stoppable(cancellationToken);
        IReadOnlyList<Moment> moments;
        try
        {
            moments = await search.SearchAsync(media, query, Math.Clamp(count, 1, 20), searching.Token);
        }
        // The plugin is built without the runtime's own messages
        // (UseSystemResourceKeys): say what failed in words.
        catch (InvalidOperationException ex)
        {
            return $"Search failed: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Search failed: the search service did not answer ({ex.HttpRequestError}).";
        }
        catch (TaskCanceledException) when (!searching.IsCancellationRequested)
        {
            return "Search failed: the search service took too long.";
        }

        if (moments.Count == 0)
            return $"No moment matches \"{query}\".";

        return string.Join('\n', moments.Select(m =>
            $"{Format(TimeSpan.FromSeconds(m.Time))}, score {m.Score:0.##}" +
            (string.IsNullOrEmpty(m.Text) ? "" : $": {m.Text}")));
    }

    [McpServerTool(Name = "status", ReadOnly = true)]
    [Description("What VLC is playing: state, media name, time and length, volume, and position in the playlist.")]
    public string Status()
    {
        VLCPlayerState state = player.State;
        if (state is VLCPlayerState.Stopped or VLCPlayerState.Stopping)
            return $"state: stopped\n{PlaylistLine()}";

        TimeSpan length = player.LengthTimeSpan;
        string time = length > TimeSpan.Zero
            ? $"{Format(player.TimeSpan)} of {Format(length)} ({player.Position * 100:F0}%)"
            : Format(player.TimeSpan);
        float volume = player.Volume;

        return string.Join('\n',
            $"state: {state.ToString().ToLowerInvariant()}",
            $"media: {player.CurrentMediaName ?? "unknown"}",
            $"time: {time}",
            $"volume: {(volume < 0 ? "no audio output" : $"{volume * 100:F0}%")}",
            PlaylistLine(),
            $"can seek: {(player.CanSeek ? "yes" : "no")}, can pause: {(player.CanPause ? "yes" : "no")}");
    }

    [McpServerTool(Name = "play")]
    [Description("Start playback, or resume it when paused.")]
    public async Task<string> Play(CancellationToken cancellationToken = default)
    {
        switch (player.State)
        {
            case VLCPlayerState.Paused:
                player.Resume();
                break;
            case VLCPlayerState.Stopped:
                if (!playlist.Start())
                    return "Cannot start: the playlist is empty.";
                break;
            default:
                return $"Already playing, at {Format(player.TimeSpan)}.";
        }

        return await WaitUntil(() => player.State == VLCPlayerState.Playing, cancellationToken)
            ? $"Playing {player.CurrentMediaName}, at {Format(player.TimeSpan)}."
            : "VLC has not started playing yet.";
    }

    [McpServerTool(Name = "pause")]
    [Description("Pause playback.")]
    public async Task<string> Pause(CancellationToken cancellationToken = default)
    {
        if (!player.CanPause)
            return "This media cannot be paused.";

        player.Pause();
        return await WaitUntil(() => player.State == VLCPlayerState.Paused, cancellationToken)
            ? $"Paused at {Format(player.TimeSpan)}."
            : "VLC has not paused yet.";
    }

    [McpServerTool(Name = "seek")]
    [Description("Jump to a time in the current media.")]
    public async Task<string> Seek(
        [Description("Time in seconds")] double seconds,
        [Description("Count from the current time instead of from the start; negative goes back")] bool relative = false,
        CancellationToken cancellationToken = default)
    {
        if (!player.CanSeek)
            return "This media cannot seek.";

        TimeSpan before = player.TimeSpan;
        TimeSpan target = TimeSpan.FromSeconds(seconds) + (relative ? before : TimeSpan.Zero);
        TimeSpan length = player.LengthTimeSpan;
        if (target < TimeSpan.Zero)
            target = TimeSpan.Zero;
        else if (length > TimeSpan.Zero && target > length)
            target = length;
        player.SeekByTime(target);

        // VLC lands on the target when playing, and sometimes seconds away
        // from it when paused: wait until the time has left where it was and
        // stopped jumping, then say where it landed.
        TimeSpan last = before;
        bool arrived = await WaitUntil(() =>
        {
            TimeSpan now = player.TimeSpan;
            bool moved = (now - before).Duration() > Tolerance || (target - before).Duration() <= Tolerance;
            bool settled = (now - last).Duration() <= Tolerance;
            last = now;
            return moved && settled;
        }, cancellationToken);
        if (!arrived)
            return $"Seeking to {Format(target)}; VLC has not got there yet.";

        TimeSpan landed = player.TimeSpan;
        return (landed - target).Duration() <= TimeSpan.FromSeconds(1)
            ? $"Now at {Format(landed)}."
            : $"Now at {Format(landed)}, the nearest VLC could get to {Format(target)}.";
    }

    [McpServerTool(Name = "next")]
    [Description("Play the next item of the playlist.")]
    public Task<string> Next(CancellationToken cancellationToken = default)
        => ChangeItem(playlist.Next, "next", cancellationToken);

    [McpServerTool(Name = "previous")]
    [Description("Play the previous item of the playlist.")]
    public Task<string> Previous(CancellationToken cancellationToken = default)
        => ChangeItem(playlist.Prev, "previous", cancellationToken);

    private async Task<string> ChangeItem(Func<bool> change, string which, CancellationToken cancellationToken)
    {
        long before = playlist.CurrentIndex;
        if (!change())
            return $"There is no {which} item.";

        return await WaitUntil(() => playlist.CurrentIndex != before && player.State == VLCPlayerState.Playing, cancellationToken)
            ? $"Playing {player.CurrentMediaName}, playlist item {playlist.CurrentIndex + 1}."
            : $"Moved to the {which} item; VLC has not started playing it yet.";
    }

    [McpServerTool(Name = "set_volume")]
    [Description("Set the volume.")]
    public string SetVolume([Description("Volume in percent, from 0 to 200")] int percent)
    {
        if (player.Volume < 0)
            return "There is no audio output.";

        percent = Math.Clamp(percent, 0, 200);
        player.Volume = percent / 100f;
        return $"Volume set to {percent}%.";
    }

    private string PlaylistLine()
    {
        long count = playlist.Count;
        long index = playlist.CurrentIndex;
        return index < 0 ? $"playlist: {count} items, none current" : $"playlist: item {index + 1} of {count}";
    }

    private async Task<bool> WaitUntil(Func<bool> applied, CancellationToken cancellationToken)
    {
        using var waiting = Stoppable(cancellationToken);
        for (var elapsed = Stopwatch.StartNew(); elapsed.Elapsed < ApplyTimeout;)
        {
            if (applied())
                return true;
            await Task.Delay(50, waiting.Token);
        }

        return applied();
    }

    // Canceled with the call, or when the server stops: VLC releases the
    // player once the server has stopped.
    private CancellationTokenSource Stoppable(CancellationToken cancellationToken)
        => CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);

    private static string Format(TimeSpan time) => time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"m\:ss\.f");
}
