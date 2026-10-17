using System.Text;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Tests.Emit.CSharp;

/// <summary>
/// Deterministic stand-in for the real name map: PascalCase of the C names,
/// <c>_t</c>/<c>_e</c> stripped from types, functions grouped at the last underscore.
/// </summary>
internal sealed class TestNameMap(
    IReadOnlyDictionary<string, QualifiedName>? constantOverrides = null,
    IReadOnlyDictionary<string, QualifiedName>? functionOverrides = null) : INameMap
{
    public string Namespace => "Test.Interop";

    public string Type(string cName)
        => string.Concat(cName.Split("::").Select(part => part.StartsWith('@')
            ? "Anon" + part[1..]
            : Pascal(StripSuffix(part.TrimStart('@')))));

    public string Field(string cDeclaringType, string cField) => Pascal(cField);

    public string EnumMember(string cEnum, string cMember)
    {
        var name = Pascal(cMember);
        return char.IsDigit(name[0]) ? "_" + name : name;
    }

    public QualifiedName Function(string cName)
    {
        if (functionOverrides?.TryGetValue(cName, out var overridden) == true)
            return overridden;
        var cut = cName.LastIndexOf('_');
        return cut <= 0 ? new("Native", Pascal(cName)) : new(Pascal(cName[..cut]), Pascal(cName[(cut + 1)..]));
    }

    public QualifiedName Constant(string cName)
        => constantOverrides?.TryGetValue(cName, out var overridden) == true ? overridden : new("Constants", Pascal(cName));

    public string HeaderClass(string header) => Pascal(Path.GetFileNameWithoutExtension(header));

    private static string StripSuffix(string name)
        => name.EndsWith("_t", StringComparison.Ordinal) || name.EndsWith("_e", StringComparison.Ordinal) ? name[..^2] : name;

    private static string Pascal(string name)
    {
        var sb = new StringBuilder();
        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            var allUpper = part.All(c => !char.IsLower(c));
            sb.Append(char.ToUpperInvariant(part[0]));
            sb.Append(allUpper ? part[1..].ToLowerInvariant() : part[1..]);
        }
        return sb.ToString();
    }
}
