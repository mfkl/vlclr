using VLCLR.ObjectDetection;

namespace PrivacyShield;

internal sealed class PrivacyClassSelection
{
    private readonly HashSet<int> _classIds;

    private PrivacyClassSelection(
        bool includesAll,
        IEnumerable<int> classIds,
        string description)
    {
        IncludesAll = includesAll;
        _classIds = new HashSet<int>(classIds);
        Description = description;
    }

    public bool IncludesAll { get; }

    public string Description { get; }

    public bool Contains(int classId) =>
        IncludesAll || _classIds.Contains(classId);

    public bool HasExplicitClass(int classId) =>
        _classIds.Contains(classId);

    public bool HasExplicitCocoClass =>
        _classIds.Any(classId =>
            classId >= 0 && classId < PrivacyObjectCatalog.FaceClassId);

    public static bool TryParse(
        string? text,
        ObjectClassCatalog catalog,
        out PrivacyClassSelection? selection,
        out string? error)
    {
        string value = string.IsNullOrWhiteSpace(text)
            ? "person"
            : text.Trim();
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            value == "*")
        {
            selection = new PrivacyClassSelection(
                true,
                Array.Empty<int>(),
                "all configured detector classes");
            error = null;
            return true;
        }

        string[] terms = value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            selection = null;
            error = "At least one privacy class is required.";
            return false;
        }

        var classes = new List<ObjectClassDescriptor>(terms.Length);
        foreach (string term in terms)
        {
            if (!catalog.TryResolve(
                    term,
                    out ObjectClassDescriptor? objectClass))
            {
                selection = null;
                error =
                    $"'{term}' is not in the privacy object vocabulary.";
                return false;
            }

            if (classes.All(candidate => candidate.Id != objectClass.Id))
            {
                classes.Add(objectClass);
            }
        }

        classes.Sort((left, right) => left.Id.CompareTo(right.Id));
        selection = new PrivacyClassSelection(
            false,
            classes.Select(objectClass => objectClass.Id),
            string.Join(", ", classes.Select(objectClass =>
                objectClass.Label)));
        error = null;
        return true;
    }
}
