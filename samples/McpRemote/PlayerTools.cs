using System.ComponentModel;
using ModelContextProtocol.Server;
using VLCLR;
using VLCLR.Types;

namespace McpRemote;

/// <summary>
/// The tools an agent sees. Each returns plain text: what it did, or why not.
/// Commands return before VLC applies them; call status to see the result.
/// </summary>
[McpServerToolType]
public sealed class PlayerTools(VLCPlayer player, VLCPlaylist playlist, IMomentSearch search)
{
    [McpServerTool(Name = "open")]
    [Description("Add a local file or a URL to the playlist, and play it.")]
    public string Open(
        [Description("Absolute path of a local file, or a URL")] string location,
        [Description("Start playing it; false only adds it to the playlist")] bool play = true)
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

        return playlist.PlayAt(index)
            ? $"Playing playlist item {index + 1}."
            : $"Added as playlist item {index + 1}, but VLC could not play it.";
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

        IReadOnlyList<Moment> moments;
        try
        {
            moments = await search.SearchAsync(media, query, Math.Clamp(count, 1, 20), cancellationToken);
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
        catch (TaskCanceledException)
        {
            return "Search failed: the search service took too long.";
        }

        if (moments.Count == 0)
            return $"No moment matches \"{query}\".";

        return string.Join('\n', moments.Select(m =>
            $"{Format(TimeSpan.FromSeconds(m.Time))} ({m.Time:0.#} s), score {m.Score:0.##}" +
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
    public string Play()
    {
        switch (player.State)
        {
            case VLCPlayerState.Paused:
                player.Resume();
                return "Resuming.";
            case VLCPlayerState.Stopped:
                return playlist.Start() ? "Starting." : "Cannot start: the playlist is empty.";
            default:
                return "Already playing.";
        }
    }

    [McpServerTool(Name = "pause")]
    [Description("Pause playback.")]
    public string Pause()
    {
        if (!player.CanPause)
            return "This media cannot be paused.";

        player.Pause();
        return "Pausing.";
    }

    [McpServerTool(Name = "seek")]
    [Description("Jump to a time in the current media.")]
    public string Seek(
        [Description("Time in seconds")] double seconds,
        [Description("Count from the current time instead of from the start; negative goes back")] bool relative = false)
    {
        if (!player.CanSeek)
            return "This media cannot seek.";

        TimeSpan time = TimeSpan.FromSeconds(seconds);
        player.SeekByTime(time, SeekSpeed.Precise, relative ? SeekWhence.Relative : SeekWhence.Absolute);
        return relative ? $"Seeking by {seconds:+0.##;-0.##} s." : $"Seeking to {Format(time)}.";
    }

    [McpServerTool(Name = "next")]
    [Description("Play the next item of the playlist.")]
    public string Next() => playlist.Next() ? "Playing the next item." : "There is no next item.";

    [McpServerTool(Name = "previous")]
    [Description("Play the previous item of the playlist.")]
    public string Previous() => playlist.Prev() ? "Playing the previous item." : "There is no previous item.";

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

    private static string Format(TimeSpan time) => time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
}
