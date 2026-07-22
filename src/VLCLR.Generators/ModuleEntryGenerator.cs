// VLC Module Entry Point Generator
// Generates vlc_entry, vlc_entry_api_version, vlc_entry_copyright exports
// and filter operation callbacks for VLC plugin classes

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VLCLR.Generators;

/// <summary>
/// Incremental source generator that creates VLC module entry points
/// from classes marked with [VLCModule] attribute.
/// </summary>
[Generator]
public class ModuleEntryGenerator : IIncrementalGenerator
{
    private const string VLCModuleAttribute = "VLCLR.Plugin.VLCModuleAttribute";
    private const string VLCCapabilityAttribute = "VLCLR.Plugin.VLCCapabilityAttribute";
    private const string VLCDescriptionAttribute = "VLCLR.Plugin.VLCDescriptionAttribute";
    private const string VLCConfigAttribute = "VLCLR.Plugin.VLCConfigAttribute";
    private const string VLCShortcutAttribute = "VLCLR.Plugin.VLCShortcutAttribute";
    private const string VLCVideoFilterBase = "VLCLR.Plugin.VLCVideoFilterBase";
    private const string VLCTextRendererBase = "VLCLR.Plugin.VLCTextRendererBase";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all class declarations with [VLCModule] attribute
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Combine with compilation
        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        // Generate source
        context.RegisterSourceOutput(compilationAndClasses,
            static (spc, source) => Execute(source.Left, source.Right!, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl &&
               classDecl.AttributeLists.Count > 0;
    }

    private static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        // Check if it has the VLCModule attribute
        foreach (var attributeList in classDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol attributeSymbol)
                {
                    var fullName = attributeSymbol.ContainingType.ToDisplayString();
                    if (fullName == VLCModuleAttribute)
                    {
                        return classDeclaration;
                    }
                }
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax?> classes, SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty)
            return;

        var distinctClasses = classes.Where(c => c is not null).Distinct().ToList();
        if (distinctClasses.Count == 0)
            return;

        foreach (var classDecl in distinctClasses)
        {
            if (classDecl is null) continue;

            var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
            if (classSymbol is null) continue;

            var moduleInfo = ExtractModuleInfo(classSymbol);
            if (moduleInfo is null) continue;

            var source = GenerateSource(moduleInfo);
            context.AddSource($"{moduleInfo.ClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
        }
    }

    private static ModuleInfo? ExtractModuleInfo(INamedTypeSymbol classSymbol)
    {
        string? moduleName = null;
        string? capability = null;
        int score = 0;
        string? description = null;
        FilterType filterType = FilterType.Unknown;
        var shortcuts = new List<string>();
        var configOptions = new List<ConfigOption>();

        // Check attributes
        foreach (var attribute in classSymbol.GetAttributes())
        {
            var attrName = attribute.AttributeClass?.ToDisplayString();

            if (attrName == VLCModuleAttribute)
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    moduleName = attribute.ConstructorArguments[0].Value as string;
                }
            }
            else if (attrName == VLCCapabilityAttribute)
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    capability = attribute.ConstructorArguments[0].Value as string;
                }
                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "Score")
                    {
                        score = (int)(named.Value.Value ?? 0);
                    }
                }
            }
            else if (attrName == VLCDescriptionAttribute)
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    description = attribute.ConstructorArguments[0].Value as string;
                }
            }
            else if (attrName == VLCShortcutAttribute)
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    var shortcut = attribute.ConstructorArguments[0].Value as string;
                    if (!string.IsNullOrEmpty(shortcut))
                    {
                        shortcuts.Add(shortcut!);
                    }
                }
            }
            else if (attrName == VLCConfigAttribute)
            {
                var configOption = ExtractConfigOption(attribute);
                if (configOption != null)
                {
                    configOptions.Add(configOption);
                }
            }
        }

        if (moduleName is null)
            return null;

        // Determine filter type from base class
        var baseType = classSymbol.BaseType;
        while (baseType is not null)
        {
            var baseTypeName = baseType.ToDisplayString();
            if (baseTypeName == VLCVideoFilterBase)
            {
                filterType = FilterType.VideoFilter;
                break;
            }
            else if (baseTypeName == VLCTextRendererBase)
            {
                filterType = FilterType.TextRenderer;
                break;
            }
            baseType = baseType.BaseType;
        }

        return new ModuleInfo
        {
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            ClassName = classSymbol.Name,
            ModuleName = moduleName,
            Capability = capability ?? (filterType == FilterType.VideoFilter ? "video filter" :
                                       filterType == FilterType.TextRenderer ? "text renderer" : ""),
            Score = score,
            Description = description ?? $"{classSymbol.Name} filter",
            FilterType = filterType,
            Shortcuts = shortcuts,
            ConfigOptions = configOptions
        };
    }

    private static ConfigOption? ExtractConfigOption(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 2)
            return null;

        var name = attribute.ConstructorArguments[0].Value as string;
        var typeValue = attribute.ConstructorArguments[1].Value;

        if (string.IsNullOrEmpty(name) || typeValue == null)
            return null;

        var configOption = new ConfigOption
        {
            Name = name!,
            Type = (ConfigType)(int)typeValue
        };

        // Extract named arguments
        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Default":
                    configOption.DefaultValue = named.Value.Value;
                    break;
                case "Min":
                    configOption.Min = named.Value.Value;
                    break;
                case "Max":
                    configOption.Max = named.Value.Value;
                    break;
                case "Description":
                    configOption.Description = named.Value.Value as string;
                    break;
                case "LongDescription":
                    configOption.LongDescription = named.Value.Value as string;
                    break;
            }
        }

        return configOption;
    }

    private static string GenerateSource(ModuleInfo info)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine("using VLCLR.Module;");
        sb.AppendLine("using VLCLR.Plugin;");
        sb.AppendLine();
        sb.AppendLine($"namespace {info.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"partial class {info.ClassName}");
        sb.AppendLine("{");
        sb.AppendLine("    // Multi-instance support: each filter instance stores its GCHandle in filter->p_sys");
        sb.AppendLine("    // VLC retains these metadata pointers, so keep them in unmanaged storage for process lifetime.");
        sb.AppendLine("    private static readonly nint s_vlcApiVersion = Marshal.StringToCoTaskMemUTF8(\"4.0.6\");");
        sb.AppendLine("    private static readonly nint s_vlcCopyright = Marshal.StringToCoTaskMemUTF8(\"VLCLR\");");
        sb.AppendLine();

        // vlc_entry_api_version - returns pointer to "4.0.6" string
        sb.AppendLine("    [UnmanagedCallersOnly(EntryPoint = \"vlc_entry_api_version\", CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("    public static unsafe byte* VlcEntryApiVersion()");
        sb.AppendLine("    {");
        sb.AppendLine("        return (byte*)s_vlcApiVersion;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // vlc_entry_copyright - returns pointer to copyright string
        sb.AppendLine("    [UnmanagedCallersOnly(EntryPoint = \"vlc_entry_copyright\", CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("    public static unsafe byte* VlcEntryCopyright()");
        sb.AppendLine("    {");
        sb.AppendLine("        return (byte*)s_vlcCopyright;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // vlc_entry - main entry point
        sb.AppendLine("    [UnmanagedCallersOnly(EntryPoint = \"vlc_entry\", CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("    public static unsafe int VlcEntry(nint vlcSetPtr, nint opaque)");
        sb.AppendLine("    {");
        sb.AppendLine("        var builder = ModuleBuilder.Create(vlcSetPtr, opaque)");
        sb.AppendLine($"            .WithName(\"{EscapeString(info.ModuleName)}\")");
        sb.AppendLine($"            .WithCapability(\"{EscapeString(info.Capability)}\")");
        sb.AppendLine($"            .WithScore({info.Score})");
        sb.AppendLine("            .WithNoUnload()");
        if (!string.IsNullOrEmpty(info.Description))
        {
            sb.AppendLine($"            .WithDescription(\"{EscapeString(info.Description)}\")");
        }

        // Add subcategory based on capability
        if (info.Capability == "video filter")
        {
            sb.AppendLine("            .WithSubcategory(VLCConfigSubcategory.SUBCAT_VIDEO_VFILTER)");
        }
        else if (info.Capability == "text renderer")
        {
            sb.AppendLine("            .WithSubcategory(VLCConfigSubcategory.SUBCAT_VIDEO_SUBPIC)");
        }

        // Generate config option calls
        foreach (var config in info.ConfigOptions)
        {
            GenerateConfigCall(sb, config);
        }

        sb.AppendLine("            .WithOpenCallback(&FilterOpen);");
        sb.AppendLine("        return builder.Register();");
        sb.AppendLine("    }");
        sb.AppendLine();

        if (info.ConfigOptions.Count > 0 && info.FilterType != FilterType.Unknown)
        {
            GenerateTypedConfiguration(sb, info);
        }

        // Filter callbacks based on type
        if (info.FilterType == FilterType.VideoFilter)
        {
            GenerateVideoFilterCallbacks(sb, info);
        }
        else if (info.FilterType == FilterType.TextRenderer)
        {
            GenerateTextRendererCallbacks(sb, info);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static void GenerateConfigCall(StringBuilder sb, ConfigOption config)
    {
        var name = EscapeString(config.Name);
        var desc = EscapeString(config.Description ?? config.Name);
        var longDesc = config.LongDescription != null ? $"\"{EscapeString(config.LongDescription)}\"" : "null";

        switch (config.Type)
        {
            case ConfigType.Integer:
                var intDefault = config.DefaultValue is int i ? i : (config.DefaultValue is long l ? l : 0);
                if (config.Min != null && config.Max != null)
                {
                    var intMin = Convert.ToInt64(config.Min);
                    var intMax = Convert.ToInt64(config.Max);
                    sb.AppendLine($"            .AddIntegerConfig(\"{name}\", {intDefault}L, {intMin}L, {intMax}L, \"{desc}\", {longDesc})");
                }
                else
                {
                    sb.AppendLine($"            .AddIntegerConfig(\"{name}\", {intDefault}L, \"{desc}\", {longDesc})");
                }
                break;

            case ConfigType.Float:
                var floatDefault = Convert.ToDouble(config.DefaultValue ?? 0.0);
                var floatStr = floatDefault.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!floatStr.Contains(".") && !floatStr.Contains("E") && !floatStr.Contains("e"))
                    floatStr += ".0";
                if (config.Min != null && config.Max != null)
                {
                    var floatMin = Convert.ToDouble(config.Min);
                    var floatMax = Convert.ToDouble(config.Max);
                    var minStr = floatMin.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var maxStr = floatMax.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!minStr.Contains(".")) minStr += ".0";
                    if (!maxStr.Contains(".")) maxStr += ".0";
                    sb.AppendLine($"            .AddFloatConfig(\"{name}\", {floatStr}, {minStr}, {maxStr}, \"{desc}\", {longDesc})");
                }
                else
                {
                    sb.AppendLine($"            .AddFloatConfig(\"{name}\", {floatStr}, \"{desc}\", {longDesc})");
                }
                break;

            case ConfigType.Bool:
                var boolDefault = config.DefaultValue is bool b && b;
                sb.AppendLine($"            .AddBoolConfig(\"{name}\", {(boolDefault ? "true" : "false")}, \"{desc}\", {longDesc})");
                break;

            case ConfigType.String:
            case ConfigType.Password:
                var strDefault = config.DefaultValue as string;
                var strVal = strDefault != null ? $"\"{EscapeString(strDefault)}\"" : "null";
                sb.AppendLine($"            .AddStringConfig(\"{name}\", {strVal}, \"{desc}\", {longDesc})");
                break;

            case ConfigType.File:
                var fileDefault = config.DefaultValue as string;
                var fileVal = fileDefault != null ? $"\"{EscapeString(fileDefault)}\"" : "null";
                sb.AppendLine($"            .AddFileConfig(\"{name}\", {fileVal}, \"{desc}\", {longDesc})");
                break;

            case ConfigType.Directory:
                var dirDefault = config.DefaultValue as string;
                var dirVal = dirDefault != null ? $"\"{EscapeString(dirDefault)}\"" : "null";
                sb.AppendLine($"            .AddDirectoryConfig(\"{name}\", {dirVal}, \"{desc}\", {longDesc})");
                break;

            // TODO: Add support for Module and Key types
            default:
                // Fallback to string
                sb.AppendLine($"            .AddStringConfig(\"{name}\", null, \"{desc}\", {longDesc})");
                break;
        }
    }

    private static void GenerateTypedConfiguration(StringBuilder sb, ModuleInfo info)
    {
        sb.AppendLine("    /// <summary>Typed view of this module's inherited VLC configuration.</summary>");
        sb.AppendLine("    protected PluginConfiguration Config => new(Context.NativePtr);");
        sb.AppendLine();
        sb.AppendLine("    protected readonly struct PluginConfiguration");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly VLCConfiguration _values;");
        sb.AppendLine();
        sb.AppendLine("        internal PluginConfiguration(nint objectPtr)");
        sb.AppendLine("        {");
        sb.AppendLine("            _values = new VLCConfiguration(objectPtr);");
        sb.AppendLine("        }");

        foreach (var config in info.ConfigOptions)
        {
            var propertyName = GetConfigPropertyName(info.ModuleName, config.Name);
            var optionName = EscapeString(config.Name);
            sb.AppendLine();

            switch (config.Type)
            {
                case ConfigType.Integer:
                    var intDefault = Convert.ToInt64(config.DefaultValue ?? 0);
                    sb.AppendLine($"        public long {propertyName} => _values.GetInteger(\"{optionName}\", {intDefault}L);");
                    break;

                case ConfigType.Float:
                    var floatDefault = Convert.ToSingle(config.DefaultValue ?? 0f);
                    var floatLiteral = floatDefault.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    if (!floatLiteral.Contains(".") && !floatLiteral.Contains("E") && !floatLiteral.Contains("e"))
                        floatLiteral += ".0";
                    sb.AppendLine($"        public float {propertyName} => _values.GetFloat(\"{optionName}\", {floatLiteral}f);");
                    break;

                case ConfigType.Bool:
                    var boolDefault = config.DefaultValue is bool b && b;
                    sb.AppendLine($"        public bool {propertyName} => _values.GetBool(\"{optionName}\", {(boolDefault ? "true" : "false")});");
                    break;

                default:
                    var stringDefault = config.DefaultValue as string;
                    var stringLiteral = stringDefault is null ? "null" : $"\"{EscapeString(stringDefault)}\"";
                    sb.AppendLine($"        public string? {propertyName} => _values.GetString(\"{optionName}\", {stringLiteral});");
                    break;
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string GetConfigPropertyName(string moduleName, string optionName)
    {
        var normalizedModuleName = moduleName.Replace('_', '-');
        var prefix = normalizedModuleName + "-";
        var candidate = optionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? optionName.Substring(prefix.Length)
            : optionName;

        var result = new StringBuilder();
        var capitalize = true;
        foreach (var character in candidate)
        {
            if (!char.IsLetterOrDigit(character))
            {
                capitalize = true;
                continue;
            }

            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        if (result.Length == 0)
            return "Option";
        if (char.IsDigit(result[0]) || SyntaxFacts.GetKeywordKind(result.ToString()) != SyntaxKind.None)
            result.Insert(0, "Option");

        return result.ToString();
    }

    private static void GenerateVideoFilterCallbacks(StringBuilder sb, ModuleInfo info)
    {
        // Helper to get instance from filter->p_sys
        sb.AppendLine($"    private static {info.ClassName}? GetInstance(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var context = new VLCFilterContext(filterPtr);");
        sb.AppendLine("        var handlePtr = context.Sys;");
        sb.AppendLine("        if (handlePtr == 0) return null;");
        sb.AppendLine("        var handle = GCHandle.FromIntPtr(handlePtr);");
        sb.AppendLine($"        return handle.Target as {info.ClassName};");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FilterOpen - creates instance, stores GCHandle in filter->p_sys
        sb.AppendLine("    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("    private static unsafe int FilterOpen(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var context = new VLCFilterContext(filterPtr);");
        sb.AppendLine($"        {info.ClassName}? instance = null;");
        sb.AppendLine("        GCHandle handle = default;");
        sb.AppendLine("        var opened = false;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            instance = new {info.ClassName}();");
        sb.AppendLine("            handle = GCHandle.Alloc(instance);");
        sb.AppendLine("            context.SetSys(GCHandle.ToIntPtr(handle));");
        sb.AppendLine("            var result = instance.InternalOpen(filterPtr, &FilterVideoCallback, &FilterCloseCallback, &FilterFlushCallback);");
        sb.AppendLine("            if (result != 0) return result;");
        sb.AppendLine("            opened = true;");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch");
        sb.AppendLine("        {");
        sb.AppendLine("            return -1;");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!opened)");
        sb.AppendLine("            {");
        sb.AppendLine("                try { context.SetSys(0); context.SetOperations(0); } catch { }");
        sb.AppendLine("                try { instance?.Dispose(); } catch { }");
        sb.AppendLine("                try { if (handle.IsAllocated) handle.Free(); } catch { }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FilterVideoCallback - retrieves instance from filter->p_sys
        sb.AppendLine("    [UnmanagedCallersOnly]");
        sb.AppendLine("    private static nint FilterVideoCallback(nint filterPtr, nint picturePtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var instance = GetInstance(filterPtr);");
        sb.AppendLine("            if (instance is null) return picturePtr;");
        sb.AppendLine("            return instance.InternalFilterVideo(filterPtr, picturePtr);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch { return picturePtr; }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FilterFlushCallback - retrieves instance from filter->p_sys
        sb.AppendLine("    [UnmanagedCallersOnly]");
        sb.AppendLine("    private static void FilterFlushCallback(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        try { GetInstance(filterPtr)?.InternalFlush(filterPtr); } catch { }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FilterCloseCallback - retrieves instance, cleans up, frees GCHandle
        sb.AppendLine("    [UnmanagedCallersOnly]");
        sb.AppendLine("    private static void FilterCloseCallback(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var context = new VLCFilterContext(filterPtr);");
        sb.AppendLine("        GCHandle handle = default;");
        sb.AppendLine($"        {info.ClassName}? instance = null;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var handlePtr = context.Sys;");
        sb.AppendLine("            if (handlePtr == 0) return;");
        sb.AppendLine("            context.SetSys(0);");
        sb.AppendLine("            handle = GCHandle.FromIntPtr(handlePtr);");
        sb.AppendLine($"            instance = handle.Target as {info.ClassName};");
        sb.AppendLine("            instance?.InternalClose(filterPtr);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch { }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            try { context.SetSys(0); context.SetOperations(0); } catch { }");
        sb.AppendLine("            try { instance?.Dispose(); } catch { }");
        sb.AppendLine("            try { if (handle.IsAllocated) handle.Free(); } catch { }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private static void GenerateTextRendererCallbacks(StringBuilder sb, ModuleInfo info)
    {
        // Helper to get instance from filter->p_sys
        sb.AppendLine($"    private static {info.ClassName}? GetInstance(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var context = new VLCRendererContext(filterPtr);");
        sb.AppendLine("        // Get sys pointer from filter");
        sb.AppendLine("        unsafe");
        sb.AppendLine("        {");
        sb.AppendLine("            var filter = (VLCLR.Native.VLCFilter*)filterPtr;");
        sb.AppendLine("            var handlePtr = filter->Sys;");
        sb.AppendLine("            if (handlePtr == 0) return null;");
        sb.AppendLine("            var handle = GCHandle.FromIntPtr(handlePtr);");
        sb.AppendLine($"            return handle.Target as {info.ClassName};");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FilterOpen - creates instance, stores GCHandle in filter->p_sys
        sb.AppendLine("    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("    private static unsafe int FilterOpen(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var context = new VLCFilterContext(filterPtr);");
        sb.AppendLine($"        {info.ClassName}? instance = null;");
        sb.AppendLine("        GCHandle handle = default;");
        sb.AppendLine("        var opened = false;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            instance = new {info.ClassName}();");
        sb.AppendLine("            handle = GCHandle.Alloc(instance);");
        sb.AppendLine("            context.SetSys(GCHandle.ToIntPtr(handle));");
        sb.AppendLine("            var result = instance.InternalOpen(filterPtr, &RenderCallback, &FilterCloseCallback);");
        sb.AppendLine("            if (result != 0) return result;");
        sb.AppendLine("            opened = true;");
        sb.AppendLine("            return 0;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch");
        sb.AppendLine("        {");
        sb.AppendLine("            return -1;");
        sb.AppendLine("        }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!opened)");
        sb.AppendLine("            {");
        sb.AppendLine("                try { context.SetSys(0); context.SetOperations(0); } catch { }");
        sb.AppendLine("                try { instance?.Dispose(); } catch { }");
        sb.AppendLine("                try { if (handle.IsAllocated) handle.Free(); } catch { }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // RenderCallback - retrieves instance from filter->p_sys
        sb.AppendLine("    [UnmanagedCallersOnly]");
        sb.AppendLine("    private static nint RenderCallback(nint filterPtr, nint regionPtr, nint chromaListPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var instance = GetInstance(filterPtr);");
        sb.AppendLine("            if (instance is null) return 0;");
        sb.AppendLine("            return instance.InternalRender(filterPtr, regionPtr, chromaListPtr);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch { return 0; }");
        sb.AppendLine("    }");
        sb.AppendLine();

        // FilterCloseCallback - retrieves instance, cleans up, frees GCHandle
        sb.AppendLine("    [UnmanagedCallersOnly]");
        sb.AppendLine("    private static unsafe void FilterCloseCallback(nint filterPtr)");
        sb.AppendLine("    {");
        sb.AppendLine("        var context = new VLCFilterContext(filterPtr);");
        sb.AppendLine("        GCHandle handle = default;");
        sb.AppendLine($"        {info.ClassName}? instance = null;");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            var handlePtr = context.Sys;");
        sb.AppendLine("            if (handlePtr == 0) return;");
        sb.AppendLine("            context.SetSys(0);");
        sb.AppendLine("            handle = GCHandle.FromIntPtr(handlePtr);");
        sb.AppendLine($"            instance = handle.Target as {info.ClassName};");
        sb.AppendLine("            instance?.InternalClose(filterPtr);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch { }");
        sb.AppendLine("        finally");
        sb.AppendLine("        {");
        sb.AppendLine("            try { context.SetSys(0); context.SetOperations(0); } catch { }");
        sb.AppendLine("            try { instance?.Dispose(); } catch { }");
        sb.AppendLine("            try { if (handle.IsAllocated) handle.Free(); } catch { }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }

    private class ModuleInfo
    {
        public string Namespace { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public string Capability { get; set; } = "";
        public int Score { get; set; }
        public string Description { get; set; } = "";
        public FilterType FilterType { get; set; }
        public List<string> Shortcuts { get; set; } = new List<string>();
        public List<ConfigOption> ConfigOptions { get; set; } = new List<ConfigOption>();
    }

    private class ConfigOption
    {
        public string Name { get; set; } = "";
        public ConfigType Type { get; set; }
        public object? DefaultValue { get; set; }
        public object? Min { get; set; }
        public object? Max { get; set; }
        public string? Description { get; set; }
        public string? LongDescription { get; set; }
    }

    private enum ConfigType
    {
        Integer,
        Float,
        Bool,
        String,
        Password,
        File,
        Directory,
        Module,
        Key
    }

    private enum FilterType
    {
        Unknown,
        VideoFilter,
        TextRenderer
    }
}
