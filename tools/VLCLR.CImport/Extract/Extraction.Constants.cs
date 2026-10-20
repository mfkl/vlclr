using ClangSharp.Interop;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Extract;

internal sealed partial class Extraction
{
    private const string ProbePrefix = "__cimport_";

    private readonly List<MacroEntry> _selectedMacros = [];

    private void IndexMacro(CXCursor macro)
    {
        if (macro.IsMacroBuiltIn)
            return;
        var (header, candidate, _, _) = Where(macro);
        if (!candidate)
            return;

        var tokens = _tu.Tokenize(macro.Extent);
        try
        {
            var end = macro.Extent.End.FileLocation().Offset;
            var spellings = new List<string>();
            foreach (var token in tokens)
            {
                // Older libclang returned one token past the end of the macro.
                if (token.GetLocation(_tu).FileLocation().Offset >= end)
                    break;
                spellings.Add(token.GetSpelling(_tu).ToString());
            }

            var name = macro.Text();
            // The first token is the macro name; a function-like macro continues with its parameter list.
            var body = spellings.Skip(1).ToList();
            if (!macro.IsMacroFunctionLike && IsIncludeGuard(macro, name, body))
                return;
            _macroEntries[name] = new MacroEntry(name, header, macro.IsMacroFunctionLike, body);
        }
        finally
        {
            _tu.DisposeTokens(tokens);
        }
    }

    /// <summary>
    /// The guard of an include-guarded header (<c>#define VLC_COMMON_H 1</c>):
    /// named <c>*_H</c>, empty or <c>1</c>, in a file clang saw guarded. Not
    /// part of the API, so never a constant.
    /// </summary>
    private bool IsIncludeGuard(CXCursor macro, string name, IReadOnlyList<string> body)
    {
        if (!(name.EndsWith("_H", StringComparison.Ordinal) || name.EndsWith("_H_", StringComparison.Ordinal))
            || body is not ([] or ["1"]))
            return false;
        macro.Location.GetFileLocation(out var file, out _, out _, out _);
        return _tu.IsFileMultipleIncludeGuarded(file);
    }

    /// <summary>
    /// Evaluates the selected object-like macros with clang: a second parse of the
    /// translation unit with one <c>static const __auto_type</c> declaration per macro.
    /// </summary>
    private void EvaluateConstants()
    {
        var candidates = new List<MacroEntry>();
        foreach (var macro in _selectedMacros)
        {
            if (macro.FunctionLike)
            {
                // #define var_Create(a, b, c) var_Create(VLC_OBJECT(a), b, c) wraps the
                // function the root also selected: nothing is missing.
                if (!_functions.ContainsKey(macro.Name))
                    Skip(macro.Name, macro.Header, "constant", "function-like macro");
            }
            else if (macro.Body.Count > 0)
                candidates.Add(macro);
            // Empty macros (include guards, VLC_API) carry no value and are not reported.
        }
        if (candidates.Count == 0)
            return;

        var probes = new System.Text.StringBuilder(_source);
        foreach (var macro in candidates)
        {
            if (macro.Body.Any(t => t is "{" or "}" or ";"))
                continue;
            probes.Append($"static const __auto_type {ProbePrefix}{macro.Name} = ({macro.Name});\n");
        }

        using var tu = ParseSource(probes.ToString());
        var unit = tu;
        var evaluated = new Dictionary<string, CXCursor>(StringComparer.Ordinal);
        foreach (var cursor in TopLevel(unit))
        {
            if (cursor.Kind != CXCursorKind.CXCursor_VarDecl)
                continue;
            var name = cursor.Text();
            if (name.StartsWith(ProbePrefix, StringComparison.Ordinal))
                evaluated[name[ProbePrefix.Length..]] = cursor;
        }

        foreach (var macro in candidates)
        {
            var expression = string.Join(" ", macro.Body);
            if (!evaluated.TryGetValue(macro.Name, out var probe))
            {
                Skip(macro.Name, macro.Header, "constant", "not an expression clang can evaluate");
                continue;
            }

            var constant = Evaluate(macro, expression, probe, out var reason);
            if (constant is null)
                Skip(macro.Name, macro.Header, "constant", reason!);
            else
                _constants.Add(constant);
        }
    }

    private static ConstantDecl? Evaluate(MacroEntry macro, string expression, CXCursor probe, out string? reason)
    {
        reason = null;
        var type = probe.Type.CanonicalType;
        var result = probe.Evaluate;
        try
        {
            switch (result.Kind)
            {
                case CXEvalResultKind.CXEval_Int:
                {
                    var builtin = type.kind == CXTypeKind.CXType_Enum
                        ? type.Declaration.EnumDecl_IntegerType.CanonicalType.kind.ToBuiltin()
                        : type.kind.ToBuiltin();
                    if (builtin is null)
                    {
                        reason = $"integer expression of unsupported type '{type.Spelling}'";
                        return null;
                    }
                    return new ConstantDecl
                    {
                        Name = macro.Name,
                        Header = macro.Header,
                        Kind = ConstantKind.Integer,
                        Type = TypeRef.Of(builtin.Value),
                        Integer = result.IsUnsignedInt ? unchecked((long)result.AsUnsigned) : result.AsLongLong,
                        Expression = expression,
                    };
                }
                case CXEvalResultKind.CXEval_Float:
                {
                    var builtin = type.kind.ToBuiltin();
                    if (builtin is not (BuiltinType.Float or BuiltinType.Double or BuiltinType.LongDouble))
                    {
                        reason = $"floating expression of unsupported type '{type.Spelling}'";
                        return null;
                    }
                    return new ConstantDecl
                    {
                        Name = macro.Name,
                        Header = macro.Header,
                        Kind = ConstantKind.Float,
                        Type = TypeRef.Of(builtin.Value),
                        Float = result.AsDouble,
                        Expression = expression,
                    };
                }
            }
        }
        finally
        {
            result.Dispose();
        }

        // A string literal decays to a pointer, which clang does not fold; read the literal itself.
        if (type.kind == CXTypeKind.CXType_Pointer && StringLiteralOf(probe) is { } literal)
        {
            if (literal.StringLiteralKind != CX_StringKind.CX_SLK_Ordinary && literal.StringLiteralKind != CX_StringKind.CX_SLK_UTF8)
            {
                reason = "wide or UTF-16/32 string literal";
                return null;
            }
            return new ConstantDecl
            {
                Name = macro.Name,
                Header = macro.Header,
                Kind = ConstantKind.String,
                Type = TypeRef.PointerTo(TypeRef.Of(BuiltinType.Char) with { IsConst = true }),
                String = literal.StringLiteralValue.ToString(),
                Expression = expression,
            };
        }

        reason = "does not evaluate to an integer, floating or string constant";
        return null;
    }

    private static CXCursor? StringLiteralOf(CXCursor probe)
    {
        var expression = probe.InitExpr;
        for (var depth = 0; depth < 8 && !expression.IsNull; depth++)
        {
            if (expression.Kind == CXCursorKind.CXCursor_StringLiteral)
                return expression;
            if (expression.NumChildren == 0)
                return null;
            expression = expression.GetChild(0);
        }
        return null;
    }
}
