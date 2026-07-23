using System.Text;

namespace LiveAudioTranslator;

internal readonly record struct TimedCue(
    long Sequence,
    long StartMediaTicks,
    long EndMediaTicks,
    string Text)
{
    public const int MaximumTextLength = 2_048;

    public TimedCue NormalizeAndValidate()
    {
        string normalized = TimedCueText.Normalize(Text);
        var cue = this with { Text = normalized };
        cue.Validate();
        return cue;
    }

    public void Validate()
    {
        if (Sequence < 0)
            throw new InvalidDataException("Cue sequence cannot be negative.");
        if (StartMediaTicks < 0 || StartMediaTicks >= EndMediaTicks)
            throw new InvalidDataException("Cue media interval is invalid.");
        if (string.IsNullOrWhiteSpace(Text))
            throw new InvalidDataException("Cue text cannot be empty.");
        if (Text.Length > MaximumTextLength)
            throw new InvalidDataException($"Cue text exceeds {MaximumTextLength} UTF-16 code units.");
        if (!string.Equals(Text, TimedCueText.Normalize(Text), StringComparison.Ordinal))
            throw new InvalidDataException("Cue text is not normalized.");
    }
}

internal static class TimedCueText
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string sanitized = SanitizeUtf16(text).Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(Math.Min(sanitized.Length, TimedCue.MaximumTextLength));
        bool pendingSpace = false;
        for (int index = 0; index < sanitized.Length; index++)
        {
            char character = sanitized[index];
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace && result.Length < TimedCue.MaximumTextLength)
                result.Append(' ');
            pendingSpace = false;
            int characterLength = char.IsHighSurrogate(character) &&
                index + 1 < sanitized.Length && char.IsLowSurrogate(sanitized[index + 1])
                ? 2
                : 1;
            if (result.Length + characterLength > TimedCue.MaximumTextLength)
                break;
            result.Append(character);
            if (characterLength == 2)
                result.Append(sanitized[++index]);
        }

        return result.ToString().TrimEnd();
    }

    private static string SanitizeUtf16(string text)
    {
        var result = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (!char.IsSurrogate(character))
            {
                result.Append(character);
            }
            else if (char.IsHighSurrogate(character) &&
                     index + 1 < text.Length &&
                     char.IsLowSurrogate(text[index + 1]))
            {
                result.Append(character);
                result.Append(text[++index]);
            }
            else
            {
                result.Append('\uFFFD');
            }
        }

        return result.ToString();
    }
}
