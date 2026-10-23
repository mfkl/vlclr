using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace McpRemote;

/// <summary>
/// Searches what is said: the SubRip subtitle file next to a local media
/// (<c>movie.srt</c> or <c>movie.en.srt</c> for <c>movie.mp4</c>). A line
/// scores by the share of the query's words it contains, compared by their
/// English stems ("argue" finds "argument"), plus one when it
/// contains the query as a phrase. A sentence often spans two lines, so a line
/// and the next also score together, when that beats both alone.
/// </summary>
public sealed partial class SubtitleSearch : IMomentSearch
{
    // Results closer than this to a better one are the same moment.
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(5);

    private readonly Lock _cacheLock = new();
    private (string Path, DateTime Written, Cue[] Cues)? _cache;

    public Task<IReadOnlyList<Moment>> SearchAsync(string mediaUri, string query, int count, CancellationToken cancellationToken)
    {
        string? path = FindSubtitles(mediaUri)
            ?? throw new InvalidOperationException("No subtitle file next to the media: search needs one, such as movie.srt for movie.mp4.");
        Cue[] cues = Load(path);

        // Words of one or two letters match almost every line: score the
        // others, unless the query has only those.
        string[] all = Words(query).Distinct().ToArray();
        string[] terms = (all.Any(w => w.Length > 2) ? all.Where(w => w.Length > 2) : all).Select(Stem).Distinct().ToArray();
        if (terms.Length == 0)
            return Task.FromResult<IReadOnlyList<Moment>>([]);
        string phrase = string.Join(' ', Words(query));

        double[] alone = cues.Select(c => Score(c.Text, terms, phrase)).ToArray();
        var scored = new List<(TimeSpan Start, double Score, string Text)>();
        for (int i = 0; i < cues.Length; i++)
        {
            if (alone[i] > 0)
                scored.Add((cues[i].Start, alone[i], cues[i].Text));

            if (i + 1 < cues.Length)
            {
                string text = cues[i].Text + " " + cues[i + 1].Text;
                double together = Score(text, terms, phrase);
                if (together > Math.Max(alone[i], alone[i + 1]))
                    scored.Add((cues[i].Start, together, text));
            }
        }

        var moments = new List<Moment>();
        foreach (var (start, score, text) in scored.OrderByDescending(s => s.Score).ThenBy(s => s.Start))
        {
            if (moments.Count == count)
                break;
            if (moments.Any(m => Math.Abs(m.Time - start.TotalSeconds) < Spacing.TotalSeconds))
                continue;
            moments.Add(new Moment(start.TotalSeconds, Math.Round(score, 2), text));
        }

        return Task.FromResult<IReadOnlyList<Moment>>(moments);
    }

    private static double Score(string text, string[] terms, string phrase)
    {
        string normalized = string.Join(' ', Words(text));
        var words = normalized.Split(' ').Select(Stem).ToHashSet();
        double score = terms.Count(words.Contains) / (double)terms.Length;
        if (score > 0 && terms.Length > 1 && normalized.Contains(phrase, StringComparison.Ordinal))
            score += 1;
        return score;
    }

    // Common English endings, longest first: "argue", "argued", "arguing" and
    // "argument" all become "argu". Words of four letters or fewer stay whole.
    private static readonly string[] Suffixes = ["ments", "ment", "ings", "ing", "ers", "ies", "ed", "es", "er", "ly", "s", "e"];

    internal static string Stem(string word)
    {
        if (word.Length <= 4)
            return word;

        foreach (string suffix in Suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal) && word.Length - suffix.Length >= 3)
                return word[..^suffix.Length];
        }

        return word;
    }

    private static string? FindSubtitles(string mediaUri)
    {
        if (!Uri.TryCreate(mediaUri, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
            return null;

        string media = uri.LocalPath;
        string? directory = Path.GetDirectoryName(media);
        if (directory is null || !Directory.Exists(directory))
            return null;

        string name = Path.GetFileNameWithoutExtension(media);
        string exact = Path.Combine(directory, name + ".srt");
        return File.Exists(exact)
            ? exact
            : Directory.EnumerateFiles(directory, name + ".*.srt").Order(StringComparer.Ordinal).FirstOrDefault();
    }

    private Cue[] Load(string path)
    {
        DateTime written = File.GetLastWriteTimeUtc(path);
        lock (_cacheLock)
        {
            if (_cache is { } cache && cache.Path == path && cache.Written == written)
                return cache.Cues;

            Cue[] cues = Parse(File.ReadAllText(path));
            _cache = (path, written, cues);
            return cues;
        }
    }

    internal static Cue[] Parse(string srt)
    {
        var cues = new List<Cue>();
        string[] lines = srt.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match timing = TimingLine().Match(lines[i]);
            if (!timing.Success)
                continue;

            var text = new StringBuilder();
            for (i++; i < lines.Length && lines[i].Trim().Length > 0; i++)
                text.Append(text.Length > 0 ? " " : "").Append(lines[i].Trim());

            cues.Add(new Cue(ParseTime(timing.Groups[1].Value), Markup().Replace(text.ToString(), "")));
        }

        return cues.ToArray();
    }

    private static TimeSpan ParseTime(string value)
        => TimeSpan.ParseExact(value.Replace('.', ','), @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);

    private static IEnumerable<string> Words(string text)
        => WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    internal sealed record Cue(TimeSpan Start, string Text);

    [GeneratedRegex(@"^(\d\d:\d\d:\d\d[,.]\d\d\d)\s*-->")]
    private static partial Regex TimingLine();

    // Tags such as <i> and override blocks such as {\an8}.
    [GeneratedRegex(@"<[^>]*>|\{\\[^}]*\}")]
    private static partial Regex Markup();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordPattern();
}
