using VLCLR.CImport.Manifest;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Naming;

/// <summary>
/// Rule-based <see cref="INameMap"/> with manifest overrides. Every name is
/// computed, and every collision checked, in <see cref="Create"/>.
/// </summary>
/// <remarks>
/// Static classes that hold functions or constants are the only classes
/// checked: a header class without members is never emitted.
/// </remarks>
public sealed class NameMap : INameMap
{
    private readonly Dictionary<string, string> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Declaring, string Field), string> _fields = [];
    private readonly Dictionary<(string Enum, string Member), string> _enumMembers = [];
    private readonly Dictionary<string, QualifiedName> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedName> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _headers = new(StringComparer.Ordinal);

    private NameMap(string @namespace) => Namespace = @namespace;

    public string Namespace { get; }

    /// <summary>Builds the map, throwing <see cref="NameCollisionException"/> or <see cref="StaleNameOverrideException"/> when the snapshot and overrides do not fit.</summary>
    public static NameMap Create(MergedSnapshot merged, NameOverrides overrides, string @namespace)
    {
        var map = new NameMap(@namespace);
        new Builder(map, merged, overrides).Build();
        return map;
    }

    public string Type(string cName) => Lookup(_types, cName, "type");

    public string Field(string cDeclaringType, string cField)
        => _fields.TryGetValue((cDeclaringType, cField), out var name)
            ? name
            : throw new ArgumentException($"Unknown field '{cDeclaringType}.{cField}'.");

    public string EnumMember(string cEnum, string cMember)
        => _enumMembers.TryGetValue((cEnum, cMember), out var name)
            ? name
            : throw new ArgumentException($"Unknown enum member '{cEnum}.{cMember}'.");

    public QualifiedName Function(string cName) => Lookup(_functions, cName, "function");

    public QualifiedName Constant(string cName) => Lookup(_constants, cName, "constant");

    public string HeaderClass(string header) => Lookup(_headers, header, "header");

    private static T Lookup<T>(Dictionary<string, T> map, string key, string kind)
        => map.TryGetValue(key, out var value) ? value : throw new ArgumentException($"Unknown {kind} '{key}'.");

    private sealed class Builder(NameMap map, MergedSnapshot merged, NameOverrides overrides)
    {
        private readonly Dictionary<string, MergedType> _records = merged.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedOverrides = new(StringComparer.Ordinal);

        public void Build()
        {
            FindEmittedRecords();
            NameHeaders();
            NameTypes();
            NameFields();
            NameEnumMembers();
            NameFunctions();
            NameConstants();
            CheckStale();
            CheckScopes();
        }

        private void FindEmittedRecords()
        {
            foreach (var type in merged.Types)
            {
                if (!type.IsAnonymous)
                    _emitted.Add(type.Name);
                foreach (var field in type.Fields.Where(f => !f.IsAnonymousMember))
                {
                    for (var t = field.Type; t is not null; t = t.Element)
                    {
                        if (t.Kind == TypeRefKind.Record && t.Name is { } name && _records.TryGetValue(name, out var target) && target.IsAnonymous)
                            _emitted.Add(name);
                    }
                }
            }
        }

        // Overrides

        private bool TryOverride(IReadOnlyDictionary<string, string> section, string sectionName, string key, out string value)
        {
            if (section.TryGetValue(key, out value!))
            {
                _usedOverrides.Add(sectionName + "\0" + key);
                return true;
            }
            return false;
        }

        private void CheckStale()
        {
            foreach (var (sectionName, section) in new[]
            {
                ("types", overrides.Types), ("fields", overrides.Fields), ("enumMembers", overrides.EnumMembers),
                ("functions", overrides.Functions), ("constants", overrides.Constants), ("headers", overrides.Headers),
            })
            {
                foreach (var key in section.Keys)
                {
                    if (!_usedOverrides.Contains(sectionName + "\0" + key))
                        throw new StaleNameOverrideException(sectionName, key);
                }
            }
        }

        private static QualifiedName ParseQualified(string value, string key)
        {
            var dot = value.LastIndexOf('.');
            if (dot <= 0 || dot == value.Length - 1)
                throw new InvalidOperationException($"Override for '{key}' must be 'Class.Member', got '{value}'.");
            return new(NameRules.Escape(value[..dot], $"class override of '{key}'"), NameRules.Escape(value[(dot + 1)..], $"member override of '{key}'"));
        }

        // Headers

        private void NameHeaders()
        {
            var headers = merged.Types.Select(t => t.Header)
                .Concat(merged.Enums.Select(e => e.Header))
                .Concat(merged.Typedefs.Select(t => t.Header))
                .Concat(merged.Functions.Select(f => f.Declaration.Header))
                .Concat(merged.Constants.Select(c => c.Header));
            foreach (var header in headers)
            {
                if (map._headers.ContainsKey(header))
                    continue;
                map._headers[header] = TryOverride(overrides.Headers, "headers", header, out var name)
                    ? NameRules.Escape(name, $"header class of '{header}'")
                    : NameRules.Escape(HeaderStem(header), $"header class of '{header}'");
            }
        }

        private static string HeaderStem(string header)
        {
            var parts = header.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "";
            parts[^1] = Path.GetFileNameWithoutExtension(parts[^1]);
            return string.Concat(parts.Select(NameRules.Pascal));
        }

        // Types

        private void NameTypes()
        {
            foreach (var type in merged.Types)
                map._types[type.Name] = RecordName(type.Name);

            foreach (var e in merged.Enums.Where(e => !e.IsAnonymous))
            {
                map._types[e.Name] = TryOverride(overrides.Types, "types", e.Name, out var name)
                    ? NameRules.Escape(name, $"enum '{e.Name}'")
                    : NameRules.Escape(NameRules.Pascal(NameRules.StripType(e.Name, isEnum: true)), $"enum '{e.Name}'");
            }
            foreach (var e in merged.Enums.Where(e => e.IsAnonymous))
                TryOverride(overrides.Types, "types", e.Name, out _);

            foreach (var typedef in merged.Typedefs)
            {
                if (map._types.ContainsKey(typedef.Name))
                {
                    TryOverride(overrides.Types, "types", typedef.Name, out _);
                    continue;
                }

                var target = typedef.Target.Desugar();
                if (TryOverride(overrides.Types, "types", typedef.Name, out var name))
                    map._types[typedef.Name] = NameRules.Escape(name, $"typedef '{typedef.Name}'");
                else if (target is { Kind: TypeRefKind.Record or TypeRefKind.Enum, Name: { } targetName } && map._types.TryGetValue(targetName, out var targetCs))
                    map._types[typedef.Name] = targetCs;
                else
                    map._types[typedef.Name] = NameRules.Escape(NameRules.Pascal(NameRules.StripType(typedef.Name, isEnum: false)), $"typedef '{typedef.Name}'");
            }
        }

        private string RecordName(string cName)
        {
            if (TryOverride(overrides.Types, "types", cName, out var name))
                return NameRules.Escape(name, $"record '{cName}'");

            var split = cName.LastIndexOf("::", StringComparison.Ordinal);
            if (split < 0)
                return NameRules.Escape(NameRules.Pascal(NameRules.StripType(cName, isEnum: false)), $"record '{cName}'");

            var parent = RecordName(cName[..split]);
            var segment = cName[(split + 2)..];
            var suffix = segment.StartsWith('@') ? "Anon" + segment[1..] : NameRules.Pascal(segment);
            return NameRules.Escape(parent.TrimStart('@') + suffix, $"record '{cName}'");
        }

        // Fields

        private static readonly HashSet<string> ObjectMembers = new(StringComparer.Ordinal)
        {
            "Equals", "Finalize", "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals", "ToString",
        };

        private void NameFields()
        {
            var overridden = new HashSet<(string, string)>();
            foreach (var type in merged.Types)
            {
                foreach (var field in type.Fields.Concat(type.Members).Where(f => !f.IsAnonymousMember))
                {
                    var key = (field.DeclaringType, field.Name);
                    if (map._fields.ContainsKey(key))
                        continue;

                    if (TryOverride(overrides.Fields, "fields", $"{field.DeclaringType}.{field.Name}", out var over))
                    {
                        map._fields[key] = NameRules.Escape(over, $"field '{field.DeclaringType}.{field.Name}'");
                        overridden.Add(key);
                        continue;
                    }

                    var name = NameRules.Pascal(NameRules.StripHungarian(field.Name));
                    // Named like its struct, or like a member every struct inherits
                    // from object (stream_operations.get_type).
                    if (name == EnclosingTypeName(field.DeclaringType).TrimStart('@') || ObjectMembers.Contains(name))
                        name += "Value";
                    map._fields[key] = NameRules.Escape(name, $"field '{field.DeclaringType}.{field.Name}'");
                }
            }

            // An i_ count and the items it counts (i_attachments, attachments; i_slaves,
            // pp_slaves) strip to the same name: the count gets "Count".
            foreach (var type in merged.Types)
            {
                var clashes = type.Members
                    .Where(f => !f.IsAnonymousMember)
                    .GroupBy(f => map._fields[(f.DeclaringType, f.Name)], StringComparer.Ordinal)
                    .Where(g => g.Count() > 1);
                foreach (var field in clashes.SelectMany(g => g))
                {
                    var key = (field.DeclaringType, field.Name);
                    if (field.Name.StartsWith("i_", StringComparison.Ordinal) && !overridden.Contains(key))
                        map._fields[key] = NameRules.Escape(map._fields[key] + "Count", $"field '{field.DeclaringType}.{field.Name}'");
                }
            }
        }

        /// <summary>C# name of the struct a field of <paramref name="declaring"/> ends up in.</summary>
        private string EnclosingTypeName(string declaring)
        {
            var current = declaring;
            while (_records.TryGetValue(current, out var type) && type.IsAnonymous && !_emitted.Contains(current) && type.Parent is { } parent)
                current = parent;
            return map._types[current];
        }

        // Enum members

        private void NameEnumMembers()
        {
            foreach (var e in merged.Enums.Where(e => !e.IsAnonymous))
            {
                var prefix = NameRules.CommonEnumPrefix([.. e.Members.Select(m => m.Name)]);
                foreach (var member in e.Members)
                {
                    var what = $"enum member '{e.Name}.{member.Name}'";
                    map._enumMembers[(e.Name, member.Name)] = TryOverride(overrides.EnumMembers, "enumMembers", $"{e.Name}.{member.Name}", out var over)
                        ? NameRules.Escape(over, what)
                        : NameRules.Escape(NameRules.Pascal(member.Name[prefix.Length..]), what);
                }
            }
        }

        // Functions and constants

        private void NameFunctions()
        {
            foreach (var function in merged.Functions)
            {
                var name = function.Name;
                if (TryOverride(overrides.Functions, "functions", name, out var over))
                {
                    map._functions[name] = ParseQualified(over, name);
                    continue;
                }

                var what = $"function '{name}'";
                var split = NameRules.FunctionSplit(name);
                if (split > 0)
                {
                    map._functions[name] = new(
                        NameRules.Escape(NameRules.Pascal(name[..split]), what),
                        NameRules.Escape(NameRules.Pascal(name[(split + 1)..]), what));
                    continue;
                }

                var stem = HeaderFileStem(function.Declaration.Header);
                var rest = stem.Length > 0 && name.Length > stem.Length + 1 && name.StartsWith(stem + "_", StringComparison.Ordinal)
                    ? name[(stem.Length + 1)..]
                    : name;
                map._functions[name] = new(map._headers[function.Declaration.Header], NameRules.Escape(NameRules.Pascal(rest), what));
            }
        }

        private static string HeaderFileStem(string header)
            => Path.GetFileNameWithoutExtension(header.Replace('\\', '/').Split('/')[^1]);

        private void NameConstants()
        {
            var constants = merged.Constants.Select(c => (c.Name, c.Header))
                .Concat(merged.Enums.Where(e => e.IsAnonymous).SelectMany(e => e.Members.Select(m => (m.Name, e.Header))));
            foreach (var (name, header) in constants)
            {
                if (map._constants.ContainsKey(name))
                    continue;

                if (TryOverride(overrides.Constants, "constants", name, out var over))
                {
                    map._constants[name] = ParseQualified(over, name);
                    continue;
                }

                var rest = name.StartsWith("VLC_", StringComparison.Ordinal) && name.Length > 4 ? name[4..] : name;
                map._constants[name] = new(map._headers[header], NameRules.Escape(NameRules.Pascal(rest), $"constant '{name}'"));
            }
        }

        // Collisions

        private readonly List<NameCollisionException> _collisions = [];

        private void Collide(NameCollisionException collision) => _collisions.Add(collision);

        private void CheckScopes()
        {
            CollectCollisions();
            if (_collisions.Count == 1)
                throw _collisions[0];
            if (_collisions.Count > 1)
                throw new NameCollisionsException(_collisions);
        }

        private void CollectCollisions()
        {
            // 1. Namespace: emitted records, enums, static classes.
            var typeScope = new Dictionary<string, (string CName, bool IsRecord)>(StringComparer.Ordinal);
            foreach (var name in _emitted)
                AddType(typeScope, map._types[name], name, isRecord: true);
            foreach (var e in merged.Enums.Where(e => !e.IsAnonymous))
                AddType(typeScope, map._types[e.Name], e.Name, isRecord: false);

            // Functions the C# never binds (static inline, not exported, variadic) take no name.
            var unbound = merged.Functions
                .Where(f => f.Declaration.IsInline || !f.Declaration.IsExported || f.Declaration.IsVariadic)
                .Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal);
            var classMembers = new Dictionary<string, List<(string Member, string CName)>>(StringComparer.Ordinal);
            foreach (var (cName, q) in map._functions.Where(f => !unbound.Contains(f.Key)).Concat(map._constants))
            {
                if (!classMembers.TryGetValue(q.Class, out var list))
                    classMembers[q.Class] = list = [];
                list.Add((q.Member, cName));
            }

            foreach (var (className, members) in classMembers)
            {
                if (typeScope.TryGetValue(className, out var existing) && !existing.IsRecord)
                    Collide(new NameCollisionException("the namespace", className, existing.CName, members[0].CName));
            }

            // 2. Struct members, shared with a static class of the same name.
            var structScopes = new Dictionary<string, Scope>(StringComparer.Ordinal);
            foreach (var name in _emitted)
            {
                var csName = map._types[name];
                if (!_records.TryGetValue(name, out var type))
                    continue;

                var scope = new Scope($"struct {csName}", Collide);
                structScopes[csName] = scope;
                foreach (var member in type.Members)
                    scope.Add(map._fields[(member.DeclaringType, member.Name)], $"{member.DeclaringType}.{member.Name}");
            }

            // 4. Static class members.
            foreach (var (className, members) in classMembers)
            {
                if (!structScopes.TryGetValue(className, out var scope))
                    scope = new Scope($"static class {className}", Collide);
                foreach (var (member, cName) in members)
                {
                    if (member == className)
                        Collide(new NameCollisionException($"static class {className}", member, $"the class {className}", cName));
                    scope.Add(member, cName);
                }
            }

            // 3. Enum members.
            foreach (var e in merged.Enums.Where(e => !e.IsAnonymous))
            {
                var scope = new Scope($"enum {map._types[e.Name]}", Collide);
                foreach (var member in e.Members)
                    scope.Add(map._enumMembers[(e.Name, member.Name)], member.Name);
            }
        }

        private void AddType(Dictionary<string, (string CName, bool IsRecord)> scope, string csName, string cName, bool isRecord)
        {
            if (!scope.TryAdd(csName, (cName, isRecord)))
                Collide(new NameCollisionException("the namespace", csName, scope[csName].CName, cName));
        }
    }

    private sealed class Scope(string description, Action<NameCollisionException> collide)
    {
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

        public void Add(string csName, string cName)
        {
            if (!_names.TryAdd(csName, cName))
                collide(new NameCollisionException(description, csName, _names[csName], cName));
        }
    }
}
