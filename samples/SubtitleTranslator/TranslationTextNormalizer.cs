using System.Security.Cryptography;
using System.Text;

namespace SubtitleTranslator;

public static class TranslationTextNormalizer
{
    public static string NormalizeCacheKey(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string sanitized = SanitizeUtf16(text);
        return sanitized
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Normalize(NormalizationForm.FormC)
            .Trim();
    }

    public static string ComputeCueHash(string text)
    {
        string normalized = NormalizeCacheKey(text);
        byte[] utf8 = Encoding.UTF8.GetBytes(normalized);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(utf8, hash);
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private static string SanitizeUtf16(string text)
    {
        int invalidIndex = -1;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (!char.IsSurrogate(character))
                continue;
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
                continue;
            }

            invalidIndex = index;
            break;
        }

        if (invalidIndex < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text.AsSpan(0, invalidIndex));
        for (int index = invalidIndex; index < text.Length; index++)
        {
            char character = text[index];
            if (!char.IsSurrogate(character))
            {
                builder.Append(character);
            }
            else if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                builder.Append(character);
                builder.Append(text[++index]);
            }
            else
            {
                builder.Append('\uFFFD');
            }
        }

        return builder.ToString();
    }
}
