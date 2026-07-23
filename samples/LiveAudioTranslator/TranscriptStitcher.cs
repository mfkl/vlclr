namespace LiveAudioTranslator;

internal static class TranscriptStitcher
{
    public static string RemoveForcedSplitOverlap(string previousEnglish, string nextEnglish, int maximumWords = 12)
    {
        string previous = TimedCueText.Normalize(previousEnglish);
        string next = TimedCueText.Normalize(nextEnglish);
        if (previous.Length == 0 || next.Length == 0)
            return next;

        string[] left = previous.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] right = next.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int maximum = Math.Min(maximumWords, Math.Min(left.Length, right.Length));
        for (int length = maximum; length > 0; length--)
        {
            bool equal = true;
            for (int index = 0; index < length; index++)
            {
                if (!string.Equals(
                        NormalizeWord(left[left.Length - length + index]),
                        NormalizeWord(right[index]),
                        StringComparison.OrdinalIgnoreCase))
                {
                    equal = false;
                    break;
                }
            }
            if (equal)
                return string.Join(' ', right.Skip(length));
        }

        return next;
    }

    private static string NormalizeWord(string value) => value.Trim('"', '\'', '.', ',', '!', '?', ':', ';', '-', '—');
}
