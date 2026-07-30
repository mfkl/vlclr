using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace VLCLR.ObjectDetection;

public sealed record ObjectClassDescriptor(
    int Id,
    string Label,
    string DisplayLabel,
    IReadOnlyList<string> Aliases);

public sealed class ObjectClassCatalog
{
    private readonly IReadOnlyList<ObjectClassDescriptor> _classes;
    private readonly Dictionary<string, ObjectClassDescriptor> _byTerm;

    public ObjectClassCatalog(IEnumerable<ObjectClassDescriptor> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);

        ObjectClassDescriptor[] values = classes
            .OrderBy(value => value.Id)
            .ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException(
                "At least one object class is required.",
                nameof(classes));
        }

        var byTerm = new Dictionary<string, ObjectClassDescriptor>(
            StringComparer.Ordinal);
        foreach (ObjectClassDescriptor value in values)
        {
            AddTerm(byTerm, value.Label, value);
            AddTerm(byTerm, value.DisplayLabel, value);
            foreach (string alias in value.Aliases)
            {
                AddTerm(byTerm, alias, value);
            }
        }

        _classes = Array.AsReadOnly(values);
        _byTerm = byTerm;
    }

    public IReadOnlyList<ObjectClassDescriptor> Classes => _classes;

    public bool TryResolve(
        string term,
        [NotNullWhen(true)]
        out ObjectClassDescriptor? objectClass)
    {
        ArgumentNullException.ThrowIfNull(term);
        return _byTerm.TryGetValue(Normalize(term), out objectClass);
    }

    public ObjectClassDescriptor Resolve(string term)
    {
        return TryResolve(term, out ObjectClassDescriptor? objectClass)
            ? objectClass
            : throw new KeyNotFoundException(
                $"'{term}' is not in the active object vocabulary.");
    }

    internal static string Normalize(string value)
    {
        var normalized = new StringBuilder(value.Length);
        bool previousWasSpace = true;
        foreach (char character in value.Trim())
        {
            char next = character is '-' or '_' ? ' ' : character;
            if (char.IsWhiteSpace(next))
            {
                if (!previousWasSpace)
                {
                    normalized.Append(' ');
                    previousWasSpace = true;
                }
                continue;
            }

            if (char.IsLetterOrDigit(next))
            {
                normalized.Append(char.ToLowerInvariant(next));
                previousWasSpace = false;
            }
        }

        return normalized.ToString().Trim();
    }

    private static void AddTerm(
        IDictionary<string, ObjectClassDescriptor> terms,
        string term,
        ObjectClassDescriptor objectClass)
    {
        string normalized = Normalize(term);
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                $"Class {objectClass.Id} contains an empty label or alias.");
        }

        if (terms.TryGetValue(
                normalized,
                out ObjectClassDescriptor? existing) &&
            existing.Id != objectClass.Id)
        {
            throw new ArgumentException(
                $"The term '{term}' is assigned to both " +
                $"'{existing.Label}' and '{objectClass.Label}'.");
        }

        terms[normalized] = objectClass;
    }
}
