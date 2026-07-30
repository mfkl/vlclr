using System.Globalization;
using System.Text.RegularExpressions;

namespace VLCLR.ObjectDetection;

public sealed partial class DetectionQueryParser
{
    private static readonly string[] CommandPrefixes =
    [
        "show me the ",
        "show me ",
        "find the ",
        "find ",
        "where is the ",
        "wheres the ",
        "search for the ",
        "search for ",
        "look for the ",
        "look for "
    ];

    private readonly ObjectClassCatalog _catalog;

    public DetectionQueryParser(ObjectClassCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public bool TryParse(
        string text,
        out DetectionQuery? query,
        float defaultMinimumConfidence = 0.50f)
    {
        query = null;
        if (string.IsNullOrWhiteSpace(text) ||
            !float.IsFinite(defaultMinimumConfidence) ||
            defaultMinimumConfidence is < 0 or > 1)
        {
            return false;
        }

        string queryText = text.Trim().ToLowerInvariant();
        float minimumConfidence = defaultMinimumConfidence;
        Match confidenceMatch = ConfidenceExpression().Match(queryText);
        if (confidenceMatch.Success)
        {
            if (!float.TryParse(
                    confidenceMatch.Groups["value"].Value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out minimumConfidence) ||
                minimumConfidence is < 0 or > 1)
            {
                return false;
            }

            queryText = (
                queryText[..confidenceMatch.Index] +
                queryText[(confidenceMatch.Index + confidenceMatch.Length)..])
                .Trim();
        }

        string normalized = ObjectClassCatalog.Normalize(queryText);
        foreach (string prefix in CommandPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..].Trim();
                break;
            }
        }

        if (!_catalog.TryResolve(
                normalized,
                out ObjectClassDescriptor? objectClass))
        {
            return false;
        }

        query = new DetectionQuery(
            objectClass,
            minimumConfidence,
            text);
        return true;
    }

    [GeneratedRegex(
        @"(?:^|\s)confidence\s*:?\s*(?<value>(?:0(?:\.\d+)?|1(?:\.0+)?))\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConfidenceExpression();
}
