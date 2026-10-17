using System.Globalization;
using System.Text;

namespace VLCLR.CImport.Emit;

/// <summary>C# literal and identifier text.</summary>
internal static class CSharpLiterals
{
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

    /// <summary>A C parameter name as a C# identifier: keywords get <c>@</c>, unnamed parameters become argN.</summary>
    public static string Parameter(string name, int index)
    {
        if (name.Length == 0)
            return $"arg{index}";
        return Keywords.Contains(name) ? "@" + name : name;
    }

    public static string String(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                case < ' ' or > '~':
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    public static string Double(double value)
    {
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    public static string Single(float value)
        => value.ToString("R", CultureInfo.InvariantCulture) + "f";

    /// <summary>Escapes text for an XML doc comment.</summary>
    public static string Xml(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
