using System.Text;

namespace VLCLR.CImport.Naming;

/// <summary>Spelling helpers behind <see cref="NameMap"/>: casing, prefix stripping and C# identifier safety.</summary>
internal static class NameRules
{
    private static readonly string[] HungarianPrefixes = ["psz_", "ppsz_", "pp_", "pf_", "p_", "i_", "b_", "f_", "e_"];

    private static readonly char[] Separators = [.. Enumerable.Range(0, 128).Select(i => (char)i).Where(c => !char.IsAsciiLetterOrDigit(c))];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>
    /// Capitalises every <c>_</c>-separated part. An all-caps identifier is
    /// UPPER_SNAKE and its parts are lowered after the first letter
    /// (<c>RGBA</c> → <c>Rgba</c>, <c>I420</c> → <c>I420</c>); anything else keeps the
    /// rest of each part as written (<c>GetChromaDescription</c>).
    /// </summary>
    public static string Pascal(string name)
    {
        var upperSnake = !name.Any(char.IsLower);
        var result = new StringBuilder(name.Length);
        foreach (var part in name.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            result.Append(char.ToUpperInvariant(part[0]));
            result.Append(upperSnake ? part[1..].ToLowerInvariant() : part[1..]);
        }
        return result.ToString();
    }

    /// <summary>Strips a trailing <c>_t</c> (<c>_e</c> too for enums), then a leading <c>vlc_</c>.</summary>
    public static string StripType(string name, bool isEnum)
    {
        if (name.Length > 2 && name.EndsWith("_t", StringComparison.Ordinal))
            name = name[..^2];
        else if (isEnum && name.Length > 2 && name.EndsWith("_e", StringComparison.Ordinal))
            name = name[..^2];
        if (name.Length > 4 && name.StartsWith("vlc_", StringComparison.Ordinal))
            name = name[4..];
        return name;
    }

    public static string StripHungarian(string field)
    {
        foreach (var prefix in HungarianPrefixes)
        {
            if (field.StartsWith(prefix, StringComparison.Ordinal)
                && field.Length > prefix.Length
                && !char.IsAsciiDigit(field[prefix.Length]))
            {
                return field[prefix.Length..];
            }
        }
        return field;
    }

    /// <summary>
    /// Longest prefix ending in <c>_</c> shared by every member, backed off
    /// until no remainder is empty or starts with a digit. Empty when none.
    /// </summary>
    public static string CommonEnumPrefix(IReadOnlyList<string> members)
    {
        if (members.Count < 2)
            return "";

        var common = members[0];
        foreach (var m in members)
        {
            var n = 0;
            while (n < common.Length && n < m.Length && common[n] == m[n])
                n++;
            common = common[..n];
        }

        for (var end = common.LastIndexOf('_'); end >= 0; end = end == 0 ? -1 : common.LastIndexOf('_', end - 1))
        {
            var length = end + 1;
            if (members.All(m => m.Length > length && !char.IsAsciiDigit(m[length])))
                return common[..length];
        }
        return "";
    }

    /// <summary>Position of the first <c>_</c> followed by an uppercase letter, or -1.</summary>
    public static int FunctionSplit(string name)
    {
        for (var i = 1; i < name.Length - 1; i++)
        {
            if (name[i] == '_' && char.IsAsciiLetterUpper(name[i + 1]))
                return i;
        }
        return -1;
    }

    /// <summary>Makes <paramref name="name"/> a usable C# identifier; empty or malformed is an error.</summary>
    public static string Escape(string name, string what)
    {
        if (name.Length == 0)
            throw new InvalidOperationException($"The C# name for {what} is empty.");
        if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException($"'{name}' (for {what}) is not a valid C# identifier.");
        if (char.IsAsciiDigit(name[0]))
            return "_" + name;
        return Keywords.Contains(name) ? "@" + name : name;
    }
}
