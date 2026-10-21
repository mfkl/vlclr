using System.Text;
using ClangSharp.Interop;
using VLCLR.CImport.Manifest;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Extract;

/// <summary>
/// One extraction run: parses the synthetic translation unit, indexes what the
/// include directories declare, selects roots and their transitive closure and
/// builds the <see cref="Snapshot"/>.
/// </summary>
internal sealed partial class Extraction : IDisposable
{
    private const string TuFileName = "__cimport_tu.c";

    private readonly ExtractOptions _options;
    private readonly string[] _includeDirs;
    private readonly HashSet<string> _headerSet;
    private readonly string _triple;
    private readonly string _sourcePath = Path.Combine(Directory.GetCurrentDirectory(), TuFileName);
    private readonly Dictionary<string, (string Header, bool Candidate)> _locations = new(StringComparer.Ordinal);

    private CXIndex _index;
    private ClangSharp.TranslationUnit _unit = null!;
    private CXTranslationUnit _tu;
    private string[] _clangArgs = [];
    private string _source = "";

    // Indexed declarations, keyed by C name.
    private readonly Dictionary<string, RecordEntry> _recordEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CXCursor> _typedefEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CXCursor> _enumEntries = new(StringComparer.Ordinal);
    private readonly List<CXCursor> _anonymousEnumEntries = [];
    private readonly Dictionary<string, FunctionEntry> _functionEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MacroEntry> _macroEntries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rootHits = new(StringComparer.Ordinal);

    // Results.
    private readonly Dictionary<string, TypedefDecl> _typedefs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fullTypedefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDecl> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumDecl> _enums = new(StringComparer.Ordinal);
    private readonly List<ConstantDecl> _constants = [];
    private readonly List<SkippedDecl> _skipped = [];

    private readonly Queue<Action> _work = new();
    private string _owner = "the requested roots";

    public Extraction(ExtractOptions options)
    {
        _options = options;
        _triple = options.Target ?? AbiTargets.TripleFor(options.Abi);
        _includeDirs = [.. options.IncludeDirs.Select(d => Path.GetFullPath(d))];
        _headerSet = [.. options.Headers.Select(h => h.Replace('\\', '/'))];
        if (options.Headers.Count == 0)
            throw new CommandException("No headers to parse.");
        foreach (var dir in _includeDirs.Where(d => !Directory.Exists(d)))
            throw new CommandException($"Include directory '{dir}' does not exist.");
    }

    public Snapshot Run()
    {
        Parse();
        IndexTranslationUnit();
        SelectRoots();
        Drain();
        EvaluateConstants();
        return Build();
    }

    public void Dispose()
    {
        _unit?.Dispose();
        _index.Dispose();
    }

    // ---- parsing ---------------------------------------------------------------------------------

    private void Parse()
    {
        var source = new StringBuilder();
        foreach (var line in _options.Prelude)
            source.Append(line).Append('\n');
        foreach (var header in _options.Headers)
            source.Append("#include \"").Append(header.Replace('\\', '/')).Append("\"\n");
        _source = source.ToString();

        var args = new List<string> { "-x", "c", "-std=gnu11", "--target=" + _triple, "-w" };
        if (_options.ResourceDir is { } resourceDir)
            args.Add("-resource-dir=" + resourceDir);
        // Apple's toolchain takes its SDK with -isysroot; mingw's with --sysroot.
        if (_options.Sysroot is { } sysroot)
            args.Add(_options.Abi == Abis.Darwin ? "-isysroot" + sysroot : "--sysroot=" + sysroot);
        args.AddRange(_includeDirs.Select(d => "-I" + d));
        args.AddRange(_options.Defines.Select(d => "-D" + d));
        args.AddRange(_options.ExtraArgs);
        _clangArgs = [.. args];

        _index = CXIndex.Create(excludeDeclarationsFromPch: false, displayDiagnostics: false);
        _unit = ParseSource(_source);
        _tu = _unit.Handle;

        var errors = new List<string>();
        for (uint i = 0; i < _tu.NumDiagnostics; i++)
        {
            using var diagnostic = _tu.GetDiagnostic(i);
            if (diagnostic.Severity >= CXDiagnosticSeverity.CXDiagnostic_Error)
                errors.Add(diagnostic.Format(CXDiagnostic.DefaultDisplayOptions).ToString());
        }
        if (errors.Count > 0)
        {
            var shown = errors.Take(25).Select(e => "  " + e);
            var more = errors.Count > 25 ? $"\n  ... and {errors.Count - 25} more" : "";
            var hint = _options.ResourceDir is null && errors.Any(e => e.Contains("file not found", StringComparison.Ordinal))
                ? $"\nNo clang resource directory was found; pass --resource-dir <dir> (the directory holding include/stddef.h) or set {ResourceDirectory.EnvironmentVariable}."
                : "";
            throw new CommandException($"clang reported {errors.Count} error(s) parsing the headers for {_options.Abi} ({_triple}):\n{string.Join("\n", shown)}{more}{hint}");
        }
    }

    /// <summary>Parses <paramref name="source"/>; the wrapper owns (and disposes) the translation unit.</summary>
    private ClangSharp.TranslationUnit ParseSource(string source)
    {
        var unsaved = new[] { CXUnsavedFile.Create(_sourcePath, source) };
        var flags = CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord
                    | CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies;
        var code = CXTranslationUnit.TryParse(_index, _sourcePath, _clangArgs, unsaved, flags, out var tu);
        if (code != CXErrorCode.CXError_Success)
            throw new CommandException($"libclang could not parse the translation unit ({code}).");
        return ClangSharp.TranslationUnit.GetOrCreate(tu)
               ?? throw new CommandException("ClangSharp could not wrap the translation unit.");
    }

    private static IEnumerable<CXCursor> TopLevel(ClangSharp.TranslationUnit unit)
    {
        foreach (var child in unit.TranslationUnitDecl.CursorChildren)
            yield return child.Handle;
    }

    // ---- locations -------------------------------------------------------------------------------

    private (string Header, bool Candidate) Locate(string file)
    {
        if (_locations.TryGetValue(file, out var known))
            return known;

        (string, bool) result;
        if (file.Length == 0 || file[0] == '<')
            result = ("<built-in>", false);
        else
        {
            var full = Path.GetFullPath(file);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var dir = _includeDirs.FirstOrDefault(d => full.StartsWith(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison));
            result = dir is null
                ? (Path.GetFileName(full), false)
                : (Path.GetRelativePath(dir, full).Replace('\\', '/'), true);
        }
        _locations[file] = result;
        return result;
    }

    private (string Header, bool Candidate, uint Offset, string File) Where(CXCursor cursor)
    {
        var (file, offset) = cursor.Location.FileLocation();
        var (header, candidate) = Locate(file);
        return (header, candidate, offset, file);
    }

    /// <summary>Stable identity of a declaration: where its first declaration is.</summary>
    private static string KeyOf(CXCursor cursor)
    {
        var (file, offset) = cursor.Identity().Location.FileLocation();
        return file + ":" + offset;
    }

    // ---- indexing --------------------------------------------------------------------------------

    private sealed class RecordEntry
    {
        public required string Name { get; init; }
        public required CXCursor Cursor { get; set; }
        public required string Header { get; set; }
        public bool Defined { get; set; }
    }

    private sealed record FunctionEntry(CXCursor Cursor, string Header, bool Exported);

    private sealed record MacroEntry(string Name, string Header, bool FunctionLike, IReadOnlyList<string> Body);

    private static readonly HashSet<string> ExportMacros = new(StringComparer.Ordinal) { "VLC_API", "VLC_EXPORT" };

    private void IndexTranslationUnit()
    {
        var exportMarks = new HashSet<(string File, uint Offset)>();
        var previousEnd = new Dictionary<string, uint>(StringComparer.Ordinal);
        var pendingFunctions = new List<(CXCursor Cursor, string Header, string File, uint Start, uint Name)>();

        foreach (var c in TopLevel(_unit))
        {
            switch (c.Kind)
            {
                case CXCursorKind.CXCursor_MacroExpansion:
                    if (ExportMacros.Contains(c.Text()))
                        exportMarks.Add(c.Location.FileLocation());
                    continue;
                case CXCursorKind.CXCursor_MacroDefinition:
                    IndexMacro(c);
                    continue;
                case CXCursorKind.CXCursor_InclusionDirective:
                    continue;
            }

            var (header, candidate, nameOffset, file) = Where(c);
            var start = previousEnd.GetValueOrDefault(file);
            previousEnd[file] = c.Extent.End.FileLocation().Offset;
            if (!candidate)
                continue;

            switch (c.Kind)
            {
                case CXCursorKind.CXCursor_StructDecl or CXCursorKind.CXCursor_UnionDecl:
                    IndexRecord(c, header);
                    break;
                case CXCursorKind.CXCursor_EnumDecl:
                    if (c.IsTagless())
                        _anonymousEnumEntries.Add(c);
                    else if (!_enumEntries.TryGetValue(c.Text(), out var known) || (!known.IsDefinition && c.IsDefinition))
                        _enumEntries[c.Text()] = c;
                    break;
                case CXCursorKind.CXCursor_TypedefDecl:
                    _typedefEntries.TryAdd(c.Text(), c);
                    break;
                case CXCursorKind.CXCursor_FunctionDecl:
                    pendingFunctions.Add((c, header, file, start, nameOffset));
                    break;
            }
        }

        // VLC_API may expand to nothing, so look for the macro expansion between the previous declaration and the name.
        foreach (var (cursor, header, file, start, nameOffset) in pendingFunctions)
        {
            var name = cursor.Text();
            var exported = exportMarks.Any(m => m.File == file && m.Offset >= start && m.Offset < nameOffset)
                           && (_options.ExportedSymbols is null || _options.ExportedSymbols.Contains(name));
            if (!_functionEntries.TryGetValue(name, out var known))
                _functionEntries[name] = new FunctionEntry(cursor, header, exported);
            else if (exported && !known.Exported)
                _functionEntries[name] = known with { Exported = true };
        }
    }

    private void IndexRecord(CXCursor c, string header)
    {
        if (!c.IsTagless())
        {
            var name = c.Text();
            if (!_recordEntries.TryGetValue(name, out var entry))
                _recordEntries[name] = new RecordEntry { Name = name, Cursor = c, Header = header, Defined = c.IsDefinition };
            else if (!entry.Defined && c.IsDefinition)
            {
                entry.Cursor = c;
                entry.Header = header;
                entry.Defined = true;
            }
        }

        if (!c.IsDefinition)
            return;
        // Tagged records declared inside a record are still global in C.
        for (uint i = 0; i < c.NumDecls; i++)
        {
            var nested = c.GetDecl(i);
            if (nested.Kind is CXCursorKind.CXCursor_StructDecl or CXCursorKind.CXCursor_UnionDecl)
                IndexRecord(nested, header);
        }
    }

    // ---- selection -------------------------------------------------------------------------------

    private bool IsRoot(string name, string header)
    {
        if (_options.Roots is null)
            return _headerSet.Contains(header);

        var hit = false;
        foreach (var pattern in _options.Roots)
        {
            if (CImportManifest.Matches(pattern, name))
            {
                _rootHits.Add(pattern);
                hit = true;
            }
        }
        return hit;
    }

    private bool IsExcluded(string name) => _options.Excludes.Any(p => CImportManifest.Matches(p, name));

    private void SelectRoots()
    {
        foreach (var entry in _recordEntries.Values.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (IsRoot(entry.Name, entry.Header) && !IsExcluded(entry.Name))
                Enqueue($"root {entry.Name}", () => RequestRecord(entry.Cursor, full: entry.Defined));
        }

        foreach (var (name, cursor) in _typedefEntries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (IsRoot(name, Where(cursor).Header) && !IsExcluded(name))
                Enqueue($"root {name}", () => KeepTypedef(cursor, full: true));
        }

        foreach (var (name, cursor) in _enumEntries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (IsRoot(name, Where(cursor).Header) && !IsExcluded(name))
                Enqueue($"root {name}", () => RequestEnum(cursor));
        }

        foreach (var cursor in _anonymousEnumEntries)
        {
            var header = Where(cursor).Header;
            var members = Enumerators(cursor).Select(m => m.Text()).ToList();
            var selected = _options.Roots is null
                ? _headerSet.Contains(header)
                : members.Count(m => IsRoot(m, header)) > 0;
            if (selected && !members.Any(IsExcluded))
                Enqueue($"root {cursor.Text()}", () => RequestEnum(cursor));
        }

        foreach (var (name, entry) in _functionEntries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (IsRoot(name, entry.Header) && !IsExcluded(name))
                Enqueue($"function {name}", () => BuildFunction(name, entry));
        }

        foreach (var (name, entry) in _macroEntries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (IsRoot(name, entry.Header) && !IsExcluded(name))
                _selectedMacros.Add(entry);
        }

        if (_options.Roots is not null)
        {
            foreach (var pattern in _options.Roots.Where(p => !_rootHits.Contains(p)))
                throw new CommandException($"Root '{pattern}' matches no record, typedef, enum, function or macro declared under the include directories.");
        }
    }

    private void Enqueue(string owner, Action action) => _work.Enqueue(() =>
    {
        _owner = owner;
        action();
    });

    private void Drain()
    {
        while (_work.Count > 0)
            _work.Dequeue()();
    }

    // ---- result ----------------------------------------------------------------------------------

    private Snapshot Build()
    {
        var types = new List<TypeDecl>();
        foreach (var state in _records.Values)
        {
            if (state.Failed)
                continue;
            types.Add(state.Decl ?? new TypeDecl
            {
                Name = state.Name,
                Kind = state.Kind,
                Header = state.Header,
                IsOpaque = true,
                IsAnonymous = state.Parent is not null,
                Parent = state.Parent,
                IsTypedefNamed = state.TypedefNamed,
            });
        }

        var snapshot = new Snapshot
        {
            Abi = _options.Abi,
            Target = _triple,
            VlcCommit = _options.VlcCommit,
            PointerSize = (int)(_tu.TargetInfo.PointerWidth / 8),
            Headers = [.. _options.Headers.Select(h => h.Replace('\\', '/'))],
            Types = types,
            Enums = [.. _enums.Values],
            Typedefs = [.. _typedefs.Values],
            Functions = [.. _functions.Values],
            Constants = _constants,
            Skipped = _skipped,
        };
        return DropDanglingDeclarations(snapshot).Canonicalize();
    }

    /// <summary>Removes declarations that mention a record or enum that was skipped or never emitted, repeating until stable.</summary>
    private Snapshot DropDanglingDeclarations(Snapshot snapshot)
    {
        var skipped = new List<SkippedDecl>(snapshot.Skipped);
        while (true)
        {
            var records = snapshot.Types.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var enums = snapshot.Enums.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
            string? Missing(IEnumerable<TypeRef> refs) => refs.SelectMany(Names).FirstOrDefault(n => !(n.IsEnum ? enums : records).Contains(n.Name)).Name;

            var changed = false;
            var types = new List<TypeDecl>();
            foreach (var t in snapshot.Types)
            {
                var missing = Missing(t.Fields.Select(f => f.Type));
                if (missing is null) { types.Add(t); continue; }
                skipped.Add(new SkippedDecl { Name = t.Name, Header = t.Header, Kind = "record", Reason = $"refers to '{missing}', which could not be represented" });
                changed = true;
            }

            var typedefs = new List<TypedefDecl>();
            foreach (var t in snapshot.Typedefs)
            {
                var missing = Missing([t.Target]);
                if (missing is null) { typedefs.Add(t); continue; }
                skipped.Add(new SkippedDecl { Name = t.Name, Header = t.Header, Kind = "typedef", Reason = $"refers to '{missing}', which could not be represented" });
                changed = true;
            }

            var functions = new List<FunctionDecl>();
            foreach (var f in snapshot.Functions)
            {
                var missing = Missing([f.ReturnType, .. f.Parameters.Select(p => p.Type)]);
                if (missing is null) { functions.Add(f); continue; }
                skipped.Add(new SkippedDecl { Name = f.Name, Header = f.Header, Kind = "function", Reason = $"refers to '{missing}', which could not be represented" });
                changed = true;
            }

            snapshot = snapshot with { Types = types, Typedefs = typedefs, Functions = functions, Skipped = skipped };
            if (!changed)
                return snapshot;
        }
    }

    private static IEnumerable<(string Name, bool IsEnum)> Names(TypeRef type)
    {
        switch (type.Kind)
        {
            case TypeRefKind.Record:
                yield return (type.Name!, false);
                break;
            case TypeRefKind.Enum:
                yield return (type.Name!, true);
                break;
            case TypeRefKind.Pointer or TypeRefKind.Array or TypeRefKind.Typedef:
                foreach (var n in Names(type.Element!))
                    yield return n;
                break;
            case TypeRefKind.Function:
                foreach (var n in Names(type.Function!.ReturnType).Concat(type.Function.Parameters.SelectMany(p => Names(p.Type))))
                    yield return n;
                break;
        }
    }

    private void Skip(string name, string header, string kind, string reason)
        => _skipped.Add(new SkippedDecl { Name = name, Header = header, Kind = kind, Reason = reason });
}
