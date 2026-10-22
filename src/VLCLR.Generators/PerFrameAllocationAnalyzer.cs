// Reports managed allocations in code that runs for each video frame
// VLCLR001: per-frame code allocates on the managed heap

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace VLCLR.Generators;

/// <summary>
/// Reports the allocations of the code that runs for each video frame: the
/// overrides of VLCVideoFilterBase's frame methods, methods marked
/// [VLCPerFrame], and the methods of the same project they call (directly or
/// through a virtual method of the project, not through a delegate or an
/// interface).
/// </summary>
/// <remarks>
/// It sees the code of the project only: an allocation inside a library it
/// calls (ImageSharp's Mutate, for example) is left to the runtime check,
/// VLCLRFrameAllocationCheck. The call graph is complete only once the whole
/// project has been analyzed, so the diagnostics come at the end of the
/// compilation (on build, or with full solution analysis in an editor).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PerFrameAllocationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "VLCLR001";

    private const string VideoFilterBase = "VLCLR.Plugin.VLCVideoFilterBase";
    private const string PerFrameAttribute = "VLCLR.Plugin.VLCPerFrameAttribute";
    private static readonly ImmutableHashSet<string> FrameMethods =
        ImmutableHashSet.Create("ProcessFrame", "ProcessFrameToOutput", "SetOutputProperties");

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Per-frame code allocates on the managed heap",
        messageFormat: "{0} allocates on each frame ({1})",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each VLCLR plugin has its own garbage collector. A plugin whose frames allocate keeps "
            + "several MB more and makes the GC run while video plays: allocate in OnOpen or on the first frame, "
            + "and reuse buffers. A cold path that allocates on purpose can be suppressed with #pragma.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    private sealed class Allocation
    {
        public Allocation(Location location, string what)
        {
            Location = location;
            What = what;
        }

        public Location Location { get; }
        public string What { get; }
    }

    private sealed class MethodFacts
    {
        public readonly List<Allocation> Allocations = new();
        public readonly HashSet<IMethodSymbol> Callees = new(SymbolEqualityComparer.Default);

        // Called virtually: an override may run instead.
        public readonly HashSet<IMethodSymbol> VirtualCallees = new(SymbolEqualityComparer.Default);
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? videoFilterBase = start.Compilation.GetTypeByMetadataName(VideoFilterBase);
            INamedTypeSymbol? perFrameAttribute = start.Compilation.GetTypeByMetadataName(PerFrameAttribute);
            if (videoFilterBase is null && perFrameAttribute is null)
            {
                return;
            }

            var methods = new ConcurrentDictionary<IMethodSymbol, MethodFacts>(SymbolEqualityComparer.Default);

            start.RegisterOperationBlockAction(block =>
            {
                if (block.OwningSymbol is not IMethodSymbol method)
                {
                    return;
                }

                var facts = methods.GetOrAdd(method, _ => new MethodFacts());
                lock (facts)
                {
                    // Attribute arguments are blocks of their own and never run per frame.
                    foreach (IOperation operation in block.OperationBlocks.Where(o => o.Kind != OperationKind.Attribute))
                    {
                        Collect(operation, facts);
                    }
                }
            });

            start.RegisterCompilationEndAction(end =>
            {
                var overrides = Overrides(methods.Keys, end.Compilation.Assembly);
                var reported = new HashSet<Location>();
                foreach (var root in methods.Keys.Where(m => IsFrameRoot(m, videoFilterBase, perFrameAttribute)).OrderBy(m => m.Name))
                {
                    string rootName = $"{root.ContainingType.Name}.{root.Name}";
                    foreach (var (method, via) in Reachable(root, methods, overrides))
                    {
                        string where = via is null
                            ? $"in {rootName}"
                            : $"{method.ContainingType.Name}.{method.Name} runs per frame from {rootName}";
                        foreach (Allocation allocation in methods[method].Allocations)
                        {
                            if (reported.Add(allocation.Location))
                            {
                                end.ReportDiagnostic(Diagnostic.Create(Rule, allocation.Location, allocation.What, where));
                            }
                        }
                    }
                }
            });
        });
    }

    private static bool IsFrameRoot(IMethodSymbol method, INamedTypeSymbol? videoFilterBase, INamedTypeSymbol? perFrameAttribute)
    {
        if (perFrameAttribute is not null
            && method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, perFrameAttribute)))
        {
            return true;
        }

        if (videoFilterBase is null || !method.IsOverride || !FrameMethods.Contains(method.Name))
        {
            return false;
        }

        for (IMethodSymbol? overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(overridden.ContainingType, videoFilterBase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The analyzed overrides of each virtual or abstract method of the project.</summary>
    private static Dictionary<IMethodSymbol, List<IMethodSymbol>> Overrides(IEnumerable<IMethodSymbol> methods, IAssemblySymbol project)
    {
        var overrides = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (IMethodSymbol method in methods)
        {
            // Overrides of library methods (ToString, Equals) are left out: a
            // virtual call to one of those would reach them all.
            for (IMethodSymbol? overridden = method.OverriddenMethod;
                 overridden is not null && SymbolEqualityComparer.Default.Equals(overridden.ContainingAssembly, project);
                 overridden = overridden.OverriddenMethod)
            {
                IMethodSymbol key = overridden.OriginalDefinition;
                if (!overrides.TryGetValue(key, out List<IMethodSymbol>? list))
                {
                    overrides[key] = list = new List<IMethodSymbol>();
                }
                list.Add(method);
            }
        }
        return overrides;
    }

    /// <summary>The root and the analyzed methods it calls, each with the method it was first reached from.</summary>
    private static IEnumerable<(IMethodSymbol Method, IMethodSymbol? Via)> Reachable(
        IMethodSymbol root,
        ConcurrentDictionary<IMethodSymbol, MethodFacts> methods,
        Dictionary<IMethodSymbol, List<IMethodSymbol>> overrides)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { root };
        var queue = new Queue<(IMethodSymbol, IMethodSymbol?)>();
        queue.Enqueue((root, null));
        while (queue.Count > 0)
        {
            var (method, via) = queue.Dequeue();
            yield return (method, via);
            MethodFacts facts = methods[method];
            var callees = facts.Callees.Concat(facts.VirtualCallees.SelectMany(
                callee => overrides.TryGetValue(callee, out List<IMethodSymbol>? list) ? list : Enumerable.Empty<IMethodSymbol>()));
            foreach (IMethodSymbol callee in callees)
            {
                if (methods.ContainsKey(callee) && seen.Add(callee))
                {
                    queue.Enqueue((callee, method));
                }
            }
        }
    }

    private static void Collect(IOperation operation, MethodFacts facts)
    {
        // Error paths allocate on purpose (exception, message).
        if (operation is IThrowOperation or ICatchClauseOperation)
        {
            return;
        }

        string? what = Allocates(operation);
        if (what is not null)
        {
            facts.Allocations.Add(new Allocation(operation.Syntax.GetLocation(), what));
        }

        switch (operation)
        {
            case IInvocationOperation invocation:
                AddCallee(facts, invocation.TargetMethod, invocation.IsVirtual);
                break;
            case IObjectCreationOperation creation:
                AddCallee(facts, creation.Constructor, isVirtual: false);
                break;
            case IPropertyReferenceOperation property:
                // base.P runs the base accessor only.
                bool isVirtual = property.Instance is not null && property.Instance.Syntax is not BaseExpressionSyntax;
                if (IsRead(property))
                {
                    AddCallee(facts, property.Property.GetMethod, isVirtual);
                }
                if (IsWritten(property))
                {
                    AddCallee(facts, property.Property.SetMethod, isVirtual);
                }
                break;
            case IBinaryOperation binary:
                AddCallee(facts, binary.OperatorMethod, isVirtual: false);
                break;
            case IUnaryOperation unary:
                AddCallee(facts, unary.OperatorMethod, isVirtual: false);
                break;
            case ICompoundAssignmentOperation compound:
                AddCallee(facts, compound.OperatorMethod, isVirtual: false);
                break;
            case IIncrementOrDecrementOperation increment:
                AddCallee(facts, increment.OperatorMethod, isVirtual: false);
                break;
            case IConversionOperation conversion:
                AddCallee(facts, conversion.OperatorMethod, isVirtual: false);
                break;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            Collect(child, facts);
        }
    }

    private static void AddCallee(MethodFacts facts, IMethodSymbol? callee, bool isVirtual)
    {
        if (callee is null)
        {
            return;
        }

        facts.Callees.Add(callee.OriginalDefinition);
        if (isVirtual && (callee.IsVirtual || callee.IsAbstract || callee.IsOverride))
        {
            facts.VirtualCallees.Add(callee.OriginalDefinition);
        }
    }

    /// <summary>Every use of a property but the target of <c>=</c> runs its getter.</summary>
    private static bool IsRead(IPropertyReferenceOperation property) =>
        !(property.Parent is ISimpleAssignmentOperation assignment && assignment.Target == property);

    /// <summary><c>=</c>, <c>+=</c>, <c>??=</c>, <c>++</c> and <c>--</c> run the setter.</summary>
    private static bool IsWritten(IPropertyReferenceOperation property) => property.Parent switch
    {
        IAssignmentOperation assignment => assignment.Target == property,
        IIncrementOrDecrementOperation => true,
        _ => false,
    };

    /// <summary>What the operation allocates, for the message, or null.</summary>
    private static string? Allocates(IOperation operation)
    {
        switch (operation)
        {
            case IObjectCreationOperation creation when creation.Type is { IsReferenceType: true }:
                return $"'new {creation.Type.Name}'";

            case IAnonymousObjectCreationOperation:
                return "An anonymous object";

            case IArrayCreationOperation array:
                // The empty params array is Array.Empty.
                if (array.IsImplicit && array.DimensionSizes.All(d => d.ConstantValue is { HasValue: true, Value: 0 }))
                {
                    return null;
                }
                if (IsConstantReadOnlySpan(array))
                {
                    return null;
                }
                return array.IsImplicit ? "A params array" : $"'new {Display(array.Type)}'";

            // By its syntax: ICollectionExpressionOperation came after Roslyn 4.8.
            // The outermost operation of the expression has the target type.
            case { Syntax: CollectionExpressionSyntax collection }
                when operation.Parent?.Syntax != collection && AllocatesCollection(collection, operation.Type):
                return $"A collection expression ({Display(operation.Type)})";

            case IConversionOperation conversion
                when conversion.Operand.Type is { IsValueType: true } operandType
                    && conversion.Type is { IsReferenceType: true }
                    && !conversion.ConstantValue.HasValue
                    && !IsStringConcatenation(conversion.Parent):
                return $"Boxing a {operandType.Name}";

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary
                when binary.Type?.SpecialType == SpecialType.System_String && !binary.ConstantValue.HasValue:
                return "A string concatenation";

            case ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add } compound
                when compound.Type?.SpecialType == SpecialType.System_String:
                return "A string concatenation";

            case IInterpolatedStringOperation interpolated
                when interpolated.Type?.SpecialType == SpecialType.System_String
                    && !interpolated.ConstantValue.HasValue
                    && interpolated.Parent is not IInterpolatedStringHandlerCreationOperation:
                return "An interpolated string";

            case IDelegateCreationOperation creation when AllocatesDelegate(creation):
                return creation.Target is IAnonymousFunctionOperation ? "A capturing lambda" : "A delegate";

            case IForEachLoopOperation loop when EnumeratorIsReference(loop):
                return "Enumerating through an interface";

            case IInvocationOperation invocation:
                return AllocatingCall(invocation.TargetMethod);
        }
        return null;
    }

    /// <summary>"a" + 1 calls ToString on the value: the concatenation allocates, not a box.</summary>
    private static bool IsStringConcatenation(IOperation? operation) => operation switch
    {
        IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } binary => binary.Type?.SpecialType == SpecialType.System_String,
        ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add } compound => compound.Type?.SpecialType == SpecialType.System_String,
        _ => false,
    };

    private static string Display(ITypeSymbol? type) =>
        type is IArrayTypeSymbol array ? $"{array.ElementType.Name}[]" : type?.Name ?? "?";

    /// <summary>
    /// <c>ReadOnlySpan&lt;byte&gt; t = new byte[] { 1, 2 }</c>: a span over the
    /// assembly's data, no array.
    /// </summary>
    private static bool IsConstantReadOnlySpan(IArrayCreationOperation array) =>
        array.Parent is IConversionOperation conversion
            && IsSpan(conversion.Type, "ReadOnlySpan")
            && array.Type is IArrayTypeSymbol { Rank: 1 } arrayType
            && arrayType.ElementType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char
                or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double
            && array.Initializer is not null
            && array.Initializer.ElementValues.All(e => e.ConstantValue.HasValue);

    /// <summary>
    /// A span gets the stack or the assembly's data, unless it spreads a
    /// collection; an empty array or read-only interface gets Array.Empty.
    /// </summary>
    private static bool AllocatesCollection(CollectionExpressionSyntax collection, ITypeSymbol? type)
    {
        if (IsSpan(type, "Span") || IsSpan(type, "ReadOnlySpan"))
        {
            return collection.Elements.Any(e => e is SpreadElementSyntax);
        }

        return collection.Elements.Count > 0
            || !(type is IArrayTypeSymbol
                || type?.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
                    or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
                    or SpecialType.System_Collections_Generic_IReadOnlyList_T);
    }

    private static bool IsSpan(ITypeSymbol? type, string name) =>
        type is INamedTypeSymbol { Arity: 1 } named
            && named.Name == name
            && named.ContainingNamespace.ToDisplayString() == "System";

    /// <summary>
    /// A lambda that captures nothing and a static method group are cached;
    /// anything that captures a local or the instance allocates.
    /// </summary>
    private static bool AllocatesDelegate(IDelegateCreationOperation creation)
    {
        switch (creation.Target)
        {
            case IAnonymousFunctionOperation lambda:
                return !lambda.Symbol.IsStatic && Captures(lambda);
            case IMethodReferenceOperation reference:
                return !reference.Method.IsStatic;
            default:
                return true;
        }
    }

    private static bool Captures(IAnonymousFunctionOperation lambda)
    {
        foreach (IOperation operation in lambda.Body.Descendants())
        {
            switch (operation)
            {
                case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }:
                    return true;
                case ILocalReferenceOperation local when !IsInside(local.Local, lambda.Symbol):
                    return true;
                case IParameterReferenceOperation parameter when !IsInside(parameter.Parameter, lambda.Symbol):
                    return true;
            }
        }
        return false;
    }

    private static bool IsInside(ISymbol symbol, IMethodSymbol lambda)
    {
        for (ISymbol? container = symbol.ContainingSymbol; container is not null; container = container.ContainingSymbol)
        {
            if (SymbolEqualityComparer.Default.Equals(container, lambda))
            {
                return true;
            }
            if (container is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
            {
                return false;
            }
        }
        return false;
    }

    private static bool EnumeratorIsReference(IForEachLoopOperation loop)
    {
        if (loop.Syntax is not CommonForEachStatementSyntax syntax || loop.SemanticModel is null)
        {
            return false;
        }

        // Arrays and strings become indexed loops.
        IOperation collection = loop.Collection is IConversionOperation { IsImplicit: true } conversion
            ? conversion.Operand
            : loop.Collection;
        if (collection.Type is IArrayTypeSymbol || collection.Type?.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        IMethodSymbol? getEnumerator = loop.SemanticModel.GetForEachStatementInfo(syntax).GetEnumeratorMethod;
        return getEnumerator?.ReturnType is { IsReferenceType: true };
    }

    /// <summary>Calls into the base library that return a new object.</summary>
    private static string? AllocatingCall(IMethodSymbol method)
    {
        string? ns = method.ContainingType?.ContainingNamespace?.ToDisplayString();
        if (ns == "System.Linq" && method.ContainingType!.Name == "Enumerable")
        {
            return $"'{method.Name}' (LINQ)";
        }

        if (method.Name == "ToString" && method.Parameters.Length <= 1 && method.ReturnType.SpecialType == SpecialType.System_String)
        {
            return "'ToString'";
        }

        if (method.ContainingType?.SpecialType == SpecialType.System_String
            && (method.ReturnType.SpecialType == SpecialType.System_String || method.ReturnType is IArrayTypeSymbol)
            && method.Name is "Format" or "Concat" or "Join" or "Substring" or "Replace" or "Split"
                or "ToUpper" or "ToLower" or "ToUpperInvariant" or "ToLowerInvariant" or "Trim" or "TrimStart" or "TrimEnd"
                or "PadLeft" or "PadRight" or "Insert" or "Remove")
        {
            return $"'string.{method.Name}'";
        }

        if (method.Name is "ToArray" or "ToList" && method.ReturnType is { IsReferenceType: true })
        {
            return $"'{method.Name}'";
        }

        return null;
    }
}
