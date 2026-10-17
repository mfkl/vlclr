using VLCLR.CImport.Model;

namespace VLCLR.CImport.Merge;

/// <summary>
/// Matches snapshots of the same headers taken on different ABIs. Records,
/// enums, functions and constants are matched by C name, fields by name
/// (anonymous members by their synthesised type name). A symbol present on
/// one ABI only, or with a different shape, becomes an error diagnostic and is
/// left out; values that may differ (sizes, offsets, enum values) become
/// <see cref="PerAbi{T}"/>.
/// </summary>
public static class AbiMerger
{
    /// <summary>Merges snapshots given in <see cref="Abis.All"/> order.</summary>
    public static MergedSnapshot Merge(params IReadOnlyList<Snapshot> snapshots)
    {
        if (snapshots.Count == 0)
            throw new ArgumentException("At least one snapshot is required.", nameof(snapshots));

        var commit = snapshots[0].VlcCommit;
        if (snapshots.Any(s => s.VlcCommit != commit))
            throw new InvalidOperationException(
                $"Snapshots come from different VLC commits: {string.Join(", ", snapshots.Select(s => $"{s.Abi}={s.VlcCommit}"))}");

        var diagnostics = new List<MergeDiagnostic>();
        var context = new Context(snapshots, diagnostics);

        var types = Match(snapshots, s => s.Types, t => t.Name, "record", diagnostics)
            .Select(context.MergeType)
            .OfType<MergedType>()
            .ToList();
        var byName = types.ToDictionary(t => t.Name);
        types = [.. types.Select(t => t with { Members = Flatten(t, byName) })];

        var enums = Match(snapshots, s => s.Enums, e => e.Name, "enum", diagnostics)
            .Select(context.MergeEnum)
            .OfType<MergedEnum>()
            .ToList();

        var typedefs = Match(snapshots, s => s.Typedefs, t => t.Name, "typedef", diagnostics)
            .Select(group => group[0])
            .ToList();

        var functions = Match(snapshots, s => s.Functions, f => f.Name, "function", diagnostics)
            .Select(context.MergeFunction)
            .ToList();

        var constants = Match(snapshots, s => s.Constants, c => c.Name, "constant", diagnostics)
            .Select(context.MergeConstant)
            .OfType<MergedConstant>()
            .ToList();

        var skipped = snapshots
            .SelectMany(s => s.Skipped.Select(d => (d, s.Abi)))
            .GroupBy(x => x.d)
            .Select(g => new MergedSkipped(g.Key, [.. g.Select(x => x.Abi)]))
            .OrderBy(x => x.Declaration.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Declaration.Kind, StringComparer.Ordinal)
            .ToList();

        return new MergedSnapshot
        {
            Abis = [.. snapshots.Select(s => s.Abi)],
            VlcCommit = commit,
            Types = types,
            Enums = enums,
            Typedefs = typedefs,
            Functions = functions,
            Constants = constants,
            Skipped = skipped,
            Diagnostics = [.. diagnostics.OrderBy(d => d.Symbol, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// True when two type references are the same type, ignoring the
    /// canonical builtin under a typedef (<c>uint64_t</c> is
    /// <c>unsigned long long</c> on windows and <c>unsigned long</c> on sysv).
    /// </summary>
    public static bool SameShape(TypeRef a, TypeRef b, bool underTypedef = false)
    {
        if (a.Kind != b.Kind || a.IsConst != b.IsConst || a.Name != b.Name || a.Count != b.Count)
            return false;

        return a.Kind switch
        {
            TypeRefKind.Builtin => underTypedef || a.Builtin == b.Builtin,
            TypeRefKind.Pointer or TypeRefKind.Array => SameShape(a.Element!, b.Element!, underTypedef),
            TypeRefKind.Typedef => SameShape(a.Element!, b.Element!, underTypedef: true),
            TypeRefKind.Function => SameShape(a.Function!, b.Function!, underTypedef),
            _ => true,
        };
    }

    private static bool SameShape(FunctionType a, FunctionType b, bool underTypedef)
        => a.IsVariadic == b.IsVariadic
           && a.CallingConvention == b.CallingConvention
           && SameShape(a.ReturnType, b.ReturnType, underTypedef)
           && a.Parameters.Count == b.Parameters.Count
           && a.Parameters.Zip(b.Parameters).All(p => SameShape(p.First.Type, p.Second.Type, underTypedef));

    private static List<T[]> Match<T>(
        IReadOnlyList<Snapshot> snapshots,
        Func<Snapshot, IReadOnlyList<T>> list,
        Func<T, string> name,
        string kind,
        List<MergeDiagnostic> diagnostics)
    {
        var maps = snapshots.Select(s => list(s).ToDictionary(name, StringComparer.Ordinal)).ToList();
        var names = maps.SelectMany(m => m.Keys).Distinct().Order(StringComparer.Ordinal);
        var result = new List<T[]>();
        foreach (var n in names)
        {
            var missing = snapshots.Where((_, i) => !maps[i].ContainsKey(n)).Select(s => s.Abi).ToList();
            if (missing.Count > 0)
            {
                diagnostics.Add(new(DiagnosticSeverity.Error, n, $"{kind} is missing on {string.Join(", ", missing)}"));
                continue;
            }
            result.Add([.. maps.Select(m => m[n])]);
        }
        return result;
    }

    /// <summary>Replaces anonymous members by their fields, recursively, with offsets relative to <paramref name="type"/>.</summary>
    private static List<MergedField> Flatten(MergedType type, IReadOnlyDictionary<string, MergedType> types)
    {
        var members = new List<MergedField>();
        foreach (var field in type.Fields)
        {
            if (!field.IsAnonymousMember)
            {
                members.Add(field);
                continue;
            }

            if (field.Type.Name is not { } inner || !types.TryGetValue(inner, out var innerType))
                continue;

            foreach (var member in Flatten(innerType, types))
            {
                members.Add(member with
                {
                    Offset = Add(member.Offset, field.Offset),
                    BitOffset = member.BitOffset is { } bits ? Add(bits, field.Offset.Select(o => o * 8)) : null,
                });
            }
        }
        return members;
    }

    private static PerAbi<long> Add(PerAbi<long> a, PerAbi<long> b) => new([.. a.Values.Zip(b.Values, (x, y) => x + y)]);

    private sealed class Context(IReadOnlyList<Snapshot> snapshots, List<MergeDiagnostic> diagnostics)
    {
        private void Error(string symbol, string message) => diagnostics.Add(new(DiagnosticSeverity.Error, symbol, message));

        private void Warning(string symbol, string message) => diagnostics.Add(new(DiagnosticSeverity.Warning, symbol, message));

        private string Abi(int i) => snapshots[i].Abi;

        public MergedType? MergeType(TypeDecl[] decls)
        {
            var first = decls[0];
            if (decls.Any(d => d.Kind != first.Kind || d.IsOpaque != first.IsOpaque || d.Fields.Count != first.Fields.Count))
            {
                Error(first.Name, "record kind, completeness or field count differs between ABIs");
                return null;
            }

            var fields = new List<MergedField>();
            for (var f = 0; f < first.Fields.Count; f++)
            {
                var perAbi = decls.Select(d => d.Fields[f]).ToArray();
                var field = perAbi[0];
                if (perAbi.Any(p => p.Name != field.Name || p.IsAnonymousMember != field.IsAnonymousMember || p.BitWidth != field.BitWidth))
                {
                    Error(first.Name, $"field #{f} ('{field.Name}') differs in name, anonymity or bit width between ABIs");
                    return null;
                }

                var typeDiffers = perAbi.Any(p => !SameShape(p.Type, field.Type));
                if (typeDiffers)
                    Warning($"{first.Name}.{field.Name}", "field type differs between ABIs");

                fields.Add(new MergedField
                {
                    Name = field.Name,
                    DeclaringType = first.Name,
                    Type = field.Type,
                    TypeDiffers = typeDiffers,
                    Offset = new([.. perAbi.Select(p => p.Offset)]),
                    BitOffset = field.IsBitfield ? new([.. perAbi.Select(p => p.BitOffset!.Value)]) : null,
                    BitWidth = field.BitWidth,
                    IsAnonymousMember = field.IsAnonymousMember,
                });
            }

            return new MergedType
            {
                Name = first.Name,
                Kind = first.Kind,
                Header = first.Header,
                IsAnonymous = first.IsAnonymous,
                Parent = first.Parent,
                IsOpaque = first.IsOpaque,
                IsPacked = first.IsPacked,
                Size = first.IsOpaque ? null : new([.. decls.Select(d => d.Size!.Value)]),
                Alignment = first.IsOpaque ? null : new([.. decls.Select(d => d.Alignment!.Value)]),
                Fields = fields,
            };
        }

        public MergedEnum? MergeEnum(EnumDecl[] decls)
        {
            var first = decls[0];
            if (decls.Any(d => !d.Members.Select(m => m.Name).SequenceEqual(first.Members.Select(m => m.Name))))
            {
                Error(first.Name, "enum members differ between ABIs");
                return null;
            }

            var underlying = new PerAbi<TypeRef>([.. decls.Select(d => d.UnderlyingType)]);
            if (!underlying.IsUniform)
                Warning(first.Name, $"underlying type differs between ABIs: {string.Join(", ", decls.Select((d, i) => $"{Abi(i)}={d.UnderlyingType.Builtin}"))}");

            var members = first.Members
                .Select((m, i) => new MergedEnumMember(m.Name, new([.. decls.Select(d => d.Members[i].Value)])))
                .ToList();
            foreach (var m in members.Where(m => !m.Value.IsUniform))
                Warning($"{first.Name}.{m.Name}", $"value differs between ABIs: {m.Value}");

            return new MergedEnum
            {
                Name = first.Name,
                Header = first.Header,
                IsAnonymous = first.IsAnonymous,
                HasFixedUnderlyingType = first.HasFixedUnderlyingType,
                UnderlyingType = underlying,
                Members = members,
            };
        }

        public MergedFunction MergeFunction(FunctionDecl[] decls)
        {
            var first = decls[0];
            var differs = decls.Any(d =>
                d.IsVariadic != first.IsVariadic
                || d.CallingConvention != first.CallingConvention
                || !SameShape(d.ReturnType, first.ReturnType)
                || d.Parameters.Count != first.Parameters.Count
                || !d.Parameters.Zip(first.Parameters).All(p => SameShape(p.First.Type, p.Second.Type)));
            if (differs)
                Warning(first.Name, "signature differs between ABIs");
            return new MergedFunction { Declaration = first, SignatureDiffers = differs };
        }

        public MergedConstant? MergeConstant(ConstantDecl[] decls)
        {
            var first = decls[0];
            if (decls.Any(d => d.Kind != first.Kind))
            {
                Error(first.Name, "constant kind differs between ABIs");
                return null;
            }

            var constant = new MergedConstant
            {
                Name = first.Name,
                Header = first.Header,
                Kind = first.Kind,
                Type = new([.. decls.Select(d => d.Type)]),
                Integer = first.Kind == ConstantKind.Integer ? new([.. decls.Select(d => d.Integer)]) : null,
                Float = first.Kind == ConstantKind.Float ? new([.. decls.Select(d => d.Float)]) : null,
                String = first.Kind == ConstantKind.String ? new([.. decls.Select(d => d.String)]) : null,
                Expression = first.Expression,
            };
            if (constant.Integer is { IsUniform: false } || constant.Float is { IsUniform: false } || constant.String is { IsUniform: false })
                Warning(first.Name, "value differs between ABIs");
            return constant;
        }
    }
}
