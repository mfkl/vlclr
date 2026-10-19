using System.Reflection;
using System.Runtime.InteropServices;

namespace VLCLR.Tests.Interop;

/// <summary>
/// Finds where a struct whose C size differs between ABIs is held or passed by
/// value. The managed struct has a single size, so on the other ABI a
/// whole-value write into an embedded member overruns it, and a by-value call
/// passes the wrong bytes. Flagged: an instance field of a struct, a ref
/// return (ref properties included), and a by-value parameter or return of a
/// native import, an [UnmanagedCallersOnly] method or a function pointer.
/// Pointers and by-ref parameters are fine.
/// Shared by VLCLR.Tests and VLCLR.CImport.Tests (linked source).
/// </summary>
internal static class AbiSizedRecords
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>
    /// True for a generated record (it has <c>MinSizeOf</c> and <c>MaxSizeOf</c>
    /// constants) whose two sizes differ.
    /// </summary>
    public static bool IsGeneratedAbiSized(Type type)
        => type.IsValueType
            && type.GetField("MinSizeOf", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } min
            && type.GetField("MaxSizeOf", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true } max
            && !Equals(min.GetRawConstantValue(), max.GetRawConstantValue());

    /// <summary>One line per offending member, e.g. <c>Filter.FmtIn holds EsFormat by value</c>.</summary>
    public static List<string> Violations(IEnumerable<Type> types, Func<Type, bool> isAbiSized)
    {
        var found = new List<string>();
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(Declared))
            {
                if (!field.IsStatic && type.IsValueType && isAbiSized(field.FieldType))
                    found.Add($"{type.Name}.{field.Name} holds {field.FieldType.Name} by value");
                if (field.FieldType.IsFunctionPointer)
                {
                    Signature(field.FieldType.GetFunctionPointerReturnType(), field.FieldType.GetFunctionPointerParameterTypes(),
                        $"{type.Name}.{field.Name} (function pointer)", isAbiSized, found);
                }
            }

            foreach (var method in type.GetMethods(Declared))
            {
                var name = $"{type.Name}.{(method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal) ? method.Name[4..] : method.Name)}";
                if (method.ReturnType.IsByRef && isAbiSized(method.ReturnType.GetElementType()!))
                    found.Add($"{name} returns a ref to {method.ReturnType.GetElementType()!.Name}");
                if (method.Attributes.HasFlag(MethodAttributes.PinvokeImpl)
                    || method.IsDefined(typeof(LibraryImportAttribute))
                    || method.IsDefined(typeof(UnmanagedCallersOnlyAttribute)))
                {
                    Signature(method.ReturnType, [.. method.GetParameters().Select(p => p.ParameterType)], name, isAbiSized, found);
                }
            }
        }
        return found;
    }

    private static void Signature(Type returnType, Type[] parameters, string name, Func<Type, bool> isAbiSized, List<string> found)
    {
        if (isAbiSized(returnType))
            found.Add($"{name} returns {returnType.Name} by value");
        foreach (var parameter in parameters.Where(isAbiSized))
            found.Add($"{name} takes {parameter.Name} by value");
    }
}
