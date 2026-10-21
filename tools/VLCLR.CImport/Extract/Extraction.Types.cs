using ClangSharp.Interop;
using VLCLR.CImport.Model;
using CallingConvention = VLCLR.CImport.Model.CallingConvention;

namespace VLCLR.CImport.Extract;

internal sealed partial class Extraction
{
    private sealed class RecordState
    {
        public required string Name { get; init; }
        public required RecordKind Kind { get; init; }
        public required CXCursor Cursor { get; init; }
        public required string Header { get; init; }
        public required bool Defined { get; init; }
        public string? Parent { get; init; }
        public bool TypedefNamed { get; init; }
        public bool Full { get; set; }
        public bool Failed { get; set; }
        public TypeDecl? Decl { get; set; }
    }

    private readonly Dictionary<string, RecordState> _records = new(StringComparer.Ordinal);

    /// <summary>Names of tagless records, assigned from the enclosing record or field (key: <see cref="KeyOf"/>).</summary>
    private readonly Dictionary<string, (string Name, string Parent)> _anonymousNames = new(StringComparer.Ordinal);

    // ---- types -----------------------------------------------------------------------------------

    /// <summary>
    /// Converts a clang type. <paramref name="viaPointer"/> is true while the type is only
    /// reached through a pointer: records are then requested opaque instead of in full.
    /// </summary>
    private TypeRef Convert(CXType type, bool viaPointer)
    {
        var isConst = type.IsConstQualified;
        TypeRef result;
        switch (type.kind)
        {
            case CXTypeKind.CXType_Elaborated:
                result = Convert(type.NamedType, viaPointer);
                break;
            case CXTypeKind.CXType_Auto:
                result = Convert(type.CanonicalType, viaPointer);
                break;
            case CXTypeKind.CXType_Atomic:
                result = Convert(type.ValueType, viaPointer);
                break;
            case CXTypeKind.CXType_Typedef:
            {
                var declaration = type.Declaration;
                result = TypeRef.Typedef(declaration.Text(), Convert(declaration.TypedefDeclUnderlyingType, viaPointer));
                if (Where(declaration).Candidate)
                    Enqueue($"typedef {declaration.Text()}", () => KeepTypedef(declaration, full: false));
                break;
            }
            case CXTypeKind.CXType_Record:
            {
                var declaration = type.Declaration;
                result = TypeRef.Record(RequestRecord(declaration, full: !viaPointer));
                break;
            }
            case CXTypeKind.CXType_Enum:
                result = TypeRef.Enum(RequestEnum(type.Declaration));
                break;
            case CXTypeKind.CXType_Pointer:
                result = TypeRef.PointerTo(Convert(type.PointeeType, viaPointer: true));
                break;
            case CXTypeKind.CXType_ConstantArray:
                result = TypeRef.ArrayOf(Convert(type.ElementType, viaPointer), type.NumElements);
                break;
            case CXTypeKind.CXType_IncompleteArray:
                result = TypeRef.ArrayOf(Convert(type.ElementType, viaPointer), null);
                break;
            case CXTypeKind.CXType_FunctionProto or CXTypeKind.CXType_FunctionNoProto:
                result = TypeRef.FunctionOf(ConvertFunction(type, null));
                break;
            default:
                var builtin = type.kind.ToBuiltin()
                              ?? throw new UnsupportedException($"unsupported type '{type.Spelling}' ({type.kind.ToString().Replace("CXType_", "")})");
                result = TypeRef.Of(builtin);
                break;
        }
        return isConst && !result.IsConst ? result with { IsConst = true } : result;
    }

    private FunctionType ConvertFunction(CXType type, IReadOnlyList<string>? names)
    {
        var parameters = new List<ParameterDecl>();
        var count = type.kind == CXTypeKind.CXType_FunctionProto ? type.NumArgTypes : 0;
        for (var i = 0; i < count; i++)
            parameters.Add(new ParameterDecl(names is not null && i < names.Count ? names[i] : "", Convert(type.GetArgType((uint)i), viaPointer: false)));

        return new FunctionType
        {
            ReturnType = Convert(type.ResultType, viaPointer: false),
            Parameters = parameters,
            IsVariadic = type.kind == CXTypeKind.CXType_FunctionProto && type.IsFunctionTypeVariadic,
            CallingConvention = type.FunctionTypeCallingConv switch
            {
                CXCallingConv.CXCallingConv_Default or CXCallingConv.CXCallingConv_C => CallingConvention.Cdecl,
                CXCallingConv.CXCallingConv_X86StdCall => CallingConvention.Stdcall,
                _ => CallingConvention.Other,
            },
        };
    }

    // ---- records ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>typedef struct { ... } DIR;</c>: no tag, the typedef names it. C
    /// spells the type of a tagged record <c>struct x</c>, of this one <c>DIR</c>.
    /// </summary>
    private static bool IsTypedefNamed(CXCursor record)
    {
        var spelling = record.Type.Spelling.ToString();
        return !spelling.StartsWith("struct ", StringComparison.Ordinal) && !spelling.StartsWith("union ", StringComparison.Ordinal);
    }

    private string RecordName(CXCursor declaration)
    {
        if (!declaration.IsTagless())
            return declaration.Text();
        if (_anonymousNames.TryGetValue(KeyOf(declaration), out var assigned))
            return assigned.Name;
        throw new UnsupportedException("anonymous record outside a record or typedef");
    }

    /// <summary>Marks a record as needed, in full or as an opaque declaration, and returns its name.</summary>
    private string RequestRecord(CXCursor declaration, bool full)
    {
        var name = RecordName(declaration);
        var key = KeyOf(declaration);
        var definition = declaration.Definition;
        var defined = !definition.IsNull;

        full &= defined; // a record that is never defined can only be opaque
        if (full && IsExcluded(name))
            throw new CommandException($"Excluded record '{name}' is embedded by value in '{_owner}'.");

        if (!_records.TryGetValue(key, out var state))
        {
            var cursor = defined ? definition : declaration.Identity();
            state = new RecordState
            {
                Name = name,
                Kind = declaration.Kind == CXCursorKind.CXCursor_UnionDecl ? RecordKind.Union : RecordKind.Struct,
                Cursor = cursor,
                Header = Where(cursor).Header,
                Defined = defined,
                Parent = _anonymousNames.TryGetValue(key, out var assigned) ? assigned.Parent : null,
                TypedefNamed = IsTypedefNamed(cursor),
            };
            _records[key] = state;
        }

        if (full && !state.Full)
        {
            state.Full = true;
            Enqueue($"record {name}", () => BuildRecord(state));
        }
        return name;
    }

    private void BuildRecord(RecordState state)
    {
        try
        {
            state.Decl = BuildRecordDecl(state);
        }
        catch (UnsupportedException e)
        {
            state.Failed = true;
            Skip(state.Name, state.Header, "record", e.Message);
        }
    }

    private TypeDecl BuildRecordDecl(RecordState state)
    {
        var record = state.Cursor;
        var size = record.Type.SizeOf;
        var alignment = record.Type.AlignOf;
        if (size < 0 || alignment < 0)
            throw new UnsupportedException($"record '{state.Name}' has no usable layout");

        var count = record.NumFields;
        AssignAnonymousNames(record, state.Name, count);

        var fields = new List<FieldDecl>();
        for (var i = 0; i < count; i++)
        {
            var field = record.Field(i);
            var isBitfield = field.IsBitField;
            var name = field.Text();
            var isAnonymousMember = IsAnonymousMember(field);
            if (isAnonymousMember)
                name = "";
            if (isBitfield && name.Length == 0)
                continue; // unnamed bitfields only pad; later offsets already include them

            var bits = field.OffsetOfField;
            if (bits < 0)
                throw new UnsupportedException($"field '{name}' of '{state.Name}' has no usable offset");

            fields.Add(new FieldDecl
            {
                Name = name,
                Type = Convert(field.Type, viaPointer: false),
                Offset = bits / 8,
                BitOffset = isBitfield ? bits : null,
                BitWidth = isBitfield ? field.FieldDeclBitWidth : null,
                IsAnonymousMember = isAnonymousMember,
            });
        }

        var packed = false;
        for (uint i = 0; i < record.NumAttrs; i++)
        {
            var kind = record.GetAttr(i).AttrKind;
            packed |= kind is CX_AttrKind.CX_AttrKind_Packed or CX_AttrKind.CX_AttrKind_MaxFieldAlignment;
        }

        return new TypeDecl
        {
            Name = state.Name,
            Kind = state.Kind,
            Header = state.Header,
            IsAnonymous = state.Parent is not null,
            Parent = state.Parent,
            IsTypedefNamed = state.TypedefNamed,
            Size = size,
            Alignment = alignment,
            IsPacked = packed,
            Fields = fields,
        };
    }

    // C11 unnamed struct/union members: clang gives the implicit field the spelling of its type.
    private static bool IsAnonymousMember(CXCursor field)
    {
        var name = field.Text();
        return name.Length == 0 && !field.IsBitField || name.Contains("(anonymous", StringComparison.Ordinal);
    }

    private void AssignAnonymousNames(CXCursor record, string parentName, int fieldCount)
    {
        var anonymousMembers = 0;
        for (var i = 0; i < fieldCount; i++)
        {
            var field = record.Field(i);
            var isMember = IsAnonymousMember(field);
            var leaf = field.Type.PeelToDeclaration();
            if (leaf.kind == CXTypeKind.CXType_Elaborated)
                leaf = leaf.NamedType;

            var memberName = isMember ? $"@{anonymousMembers++}" : field.Text();
            if (leaf.kind != CXTypeKind.CXType_Record)
                continue;
            var declaration = leaf.Declaration;
            if (declaration.IsTagless() && !_anonymousNames.ContainsKey(KeyOf(declaration)))
                _anonymousNames[KeyOf(declaration)] = ($"{parentName}::{memberName}", parentName);
        }
    }

    // ---- enums -----------------------------------------------------------------------------------

    private static IEnumerable<CXCursor> Enumerators(CXCursor enumeration)
    {
        for (uint i = 0; i < enumeration.NumEnumerators; i++)
            yield return enumeration.GetEnumerator(i);
    }

    private string RequestEnum(CXCursor declaration)
    {
        var definition = declaration.Definition;
        var enumeration = definition.IsNull ? declaration : definition;
        var members = Enumerators(enumeration).ToList();
        var anonymous = enumeration.IsTagless();
        if (anonymous && members.Count == 0)
            throw new UnsupportedException("enum without a name or members");
        var name = anonymous ? "@" + members[0].Text() : enumeration.Text();

        if (IsExcluded(name) || (anonymous && members.Any(m => IsExcluded(m.Text()))))
            throw new CommandException($"Excluded enum '{name}' is referenced by '{_owner}'.");
        if (_enums.ContainsKey(name))
            return name;

        var underlying = enumeration.EnumDecl_IntegerType.CanonicalType;
        var builtin = underlying.kind.ToBuiltin() ?? throw new UnsupportedException($"enum '{name}' has underlying type '{underlying.Spelling}'");
        var unsigned = builtin is BuiltinType.Bool or BuiltinType.UChar or BuiltinType.UShort or BuiltinType.UInt or BuiltinType.ULong or BuiltinType.ULongLong
                       or BuiltinType.UInt128 or BuiltinType.Char16 or BuiltinType.Char32;

        _enums[name] = new EnumDecl
        {
            Name = name,
            Header = Where(enumeration).Header,
            IsAnonymous = anonymous,
            UnderlyingType = TypeRef.Of(builtin),
            HasFixedUnderlyingType = HasFixedUnderlyingType(enumeration),
            Members = [.. members.Select(m => new EnumMember(m.Text(), unsigned ? unchecked((long)m.EnumConstantDeclUnsignedValue) : m.EnumConstantDeclValue))],
        };
        return name;
    }

    // enum e : type { ... }: a ':' before the opening brace.
    private bool HasFixedUnderlyingType(CXCursor enumeration)
    {
        var tokens = _tu.Tokenize(enumeration.Extent);
        try
        {
            foreach (var token in tokens)
            {
                var text = token.GetSpelling(_tu).ToString();
                if (text == "{")
                    return false;
                if (text == ":")
                    return true;
            }
            return false;
        }
        finally
        {
            _tu.DisposeTokens(tokens);
        }
    }

    // ---- typedefs --------------------------------------------------------------------------------

    private void KeepTypedef(CXCursor declaration, bool full)
    {
        var name = declaration.Text();
        if (IsExcluded(name))
            return;
        var underlying = declaration.TypedefDeclUnderlyingType;
        try
        {
            if (!_typedefs.ContainsKey(name))
            {
                _typedefs[name] = new TypedefDecl
                {
                    Name = name,
                    Header = Where(declaration).Header,
                    Target = Convert(underlying, viaPointer: true),
                };
            }
            if (full && _fullTypedefs.Add(name))
                Convert(underlying, viaPointer: false); // a typedef root keeps its target in full
        }
        catch (UnsupportedException e)
        {
            Skip(name, Where(declaration).Header, "typedef", e.Message);
        }
    }

    // ---- functions -------------------------------------------------------------------------------

    private void BuildFunction(string name, FunctionEntry entry)
    {
        try
        {
            var cursor = entry.Cursor;
            var names = new List<string>();
            for (uint i = 0; i < cursor.NumArguments; i++)
                names.Add(cursor.GetArgument(i).Text());

            var type = ConvertFunction(cursor.Type, names);
            _functions[name] = new FunctionDecl
            {
                Name = name,
                Header = entry.Header,
                ReturnType = type.ReturnType,
                Parameters = type.Parameters,
                IsVariadic = type.IsVariadic,
                IsInline = cursor.IsFunctionInlined,
                IsExported = entry.Exported,
                CallingConvention = type.CallingConvention,
            };
        }
        catch (UnsupportedException e)
        {
            Skip(name, entry.Header, "function", e.Message);
        }
    }
}
