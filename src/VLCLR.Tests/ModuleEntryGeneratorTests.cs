using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VLCLR.Generators;
using VLCLR.Plugin;
using Xunit;

namespace VLCLR.Tests;

public class ModuleEntryGeneratorTests
{
    [Theory]
    [InlineData("VLCVideoFilterBase", "protected override void ProcessFrame(VLCLR.VLCFrame frame) { }")]
    [InlineData("VLCTextRendererBase", "protected override nint RenderText(VLCLR.VLCTextRequest request) => 0;")]
    public void GeneratedCallbacks_ContainExceptionSafeLifecycle(string baseType, string implementation)
    {
        var generated = Generate(baseType, implementation);

        Assert.Contains(".WithNoUnload()", generated);
        Assert.Contains("var opened = false;", generated);
        Assert.Contains("if (!opened)", generated);
        Assert.Contains("try { context.SetSys(0); context.SetOperations(0); } catch { }", generated);
        Assert.Contains("try { instance?.Dispose(); } catch { }", generated);
        Assert.Contains("try { if (handle.IsAllocated) handle.Free(); } catch { }", generated);
        Assert.Contains("catch { }\n        finally", generated);
    }

    [Fact]
    public void GeneratedVideoCallback_ReturnsInputPictureWhenDispatchFails()
    {
        var generated = Generate(
            "VLCVideoFilterBase",
            "protected override void ProcessFrame(VLCLR.VLCFrame frame) { }");

        Assert.Contains("catch { return picturePtr; }", generated);
        Assert.Contains("try { GetInstance(filterPtr)?.InternalFlush(filterPtr); } catch { }", generated);
    }

    [Fact]
    public void GeneratedTextCallback_ReturnsNullRegionWhenDispatchFails()
    {
        var generated = Generate(
            "VLCTextRendererBase",
            "protected override nint RenderText(VLCLR.VLCTextRequest request) => 0;");

        Assert.Contains("catch { return 0; }", generated);
    }

    private static string Generate(string baseType, string implementation)
    {
        var source = $$"""
            using VLCLR.Plugin;

            namespace GeneratorFixture;

            [VLCModule("fixture")]
            public partial class FixturePlugin : {{baseType}}
            {
                {{implementation}}
            }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(VLCModuleAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "GeneratorFixture",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ModuleEntryGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        return result.Results.Single().GeneratedSources.Single().SourceText.ToString().Replace("\r\n", "\n");
    }
}
