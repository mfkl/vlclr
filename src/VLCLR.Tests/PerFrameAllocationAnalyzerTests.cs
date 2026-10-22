using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using VLCLR.Generators;
using VLCLR.Plugin;
using Xunit;

namespace VLCLR.Tests;

public class PerFrameAllocationAnalyzerTests
{
    [Fact]
    public async Task AllocationFreeFrame_IsNotReported()
    {
        var diagnostics = await Analyze("""
            protected override void ProcessFrame(VLCFrame frame)
            {
                Span<byte> row = stackalloc byte[64];
                Span<char> text = stackalloc char[32];
                text.TryWrite($"Frame: {FrameCount}", out _);
                foreach (byte b in _buffer) { row[0] = b; }
                foreach (int i in _list) { }
                Func<int, int> twice = static x => x * 2;
                Func<int, int> alsoCached = x => x + 1;
                _buffer.AsSpan().CopyTo(row);
                ReadOnlySpan<byte> shuffle = new byte[] { 0, 2, 4, 6 };
                ReadOnlySpan<int> table = [1, 2, 3];
                Span<int> scratch = [1, 2, 3];
                int[] none = [];
                IEnumerable<int> empty = [];
            }
            private readonly byte[] _buffer = new byte[64];
            private readonly List<int> _list = new();
            """);

        Assert.True(diagnostics.IsEmpty, Messages(diagnostics));
    }

    [Theory]
    [InlineData("var list = new List<int>();", "'new List'")]
    [InlineData("var bytes = new byte[16];", "'new Byte[]'")]
    [InlineData("object boxed = FrameCount;", "Boxing a Int64")]
    [InlineData("string s = \"frame \" + FrameCount;", "A string concatenation")]
    [InlineData("string s = $\"frame {FrameCount}\";", "An interpolated string")]
    [InlineData("Func<long> f = () => FrameCount;", "A capturing lambda")]
    [InlineData("Params(1, 2, 3);", "A params array")]
    [InlineData("foreach (int i in (IEnumerable<int>)_list) { }", "Enumerating through an interface")]
    [InlineData("var first = _list.Where(i => i > 0);", "'Where' (LINQ)")]
    [InlineData("string s = FrameCount.ToString();", "'ToString'")]
    [InlineData("int[] a = [1, 2, 3];", "A collection expression (Int32[])")]
    [InlineData("List<int> hits = [];", "A collection expression (List)")]
    [InlineData("ReadOnlySpan<int> copy = [.._buffer];", "A collection expression (ReadOnlySpan)")]
    [InlineData("_label += FrameCount;", "A string concatenation")]
    public async Task AllocationInProcessFrame_IsReported(string statement, string what)
    {
        var diagnostics = await Analyze($$"""
            protected override void ProcessFrame(VLCFrame frame)
            {
                {{statement}}
            }
            private static void Params(params int[] values) { }
            private readonly List<int> _list = new();
            private readonly int[] _buffer = new int[4];
            private string _label = "";
            """);

        Assert.True(diagnostics.Length == 1, Messages(diagnostics));
        var diagnostic = diagnostics[0];
        Assert.Equal("VLCLR001", diagnostic.Id);
        Assert.StartsWith(what, diagnostic.GetMessage());
        Assert.EndsWith("(in Fixture.ProcessFrame)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AllocationInACalledMethod_IsReportedWithThePath()
    {
        var diagnostics = await Analyze("""
            protected override void ProcessFrame(VLCFrame frame) => _helper.Draw();
            private readonly Helper _helper = new();
            """, """
            public class Helper
            {
                public void Draw() => Inner();
                private void Inner() { var o = new object(); }
            }
            """);

        Assert.True(diagnostics.Length == 1, Messages(diagnostics));
        var diagnostic = diagnostics[0];
        Assert.Equal("'new Object' allocates on each frame (Helper.Inner runs per frame from Fixture.ProcessFrame)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AllocationInAnOverride_IsReportedThroughTheAbstractCall()
    {
        var diagnostics = await Analyze("""
            protected override void ProcessFrame(VLCFrame frame) => _shape.Draw();
            private readonly Shape _shape = new Circle();
            """, """
            public abstract class Shape { public abstract void Draw(); }
            public class Circle : Shape { public override void Draw() { var o = new object(); } }
            """);

        Assert.True(diagnostics.Length == 1, Messages(diagnostics));
        Assert.Equal("'new Object' allocates on each frame (Circle.Draw runs per frame from Fixture.ProcessFrame)", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task CompoundAssignmentToAProperty_RunsItsSetter()
    {
        var diagnostics = await Analyze("""
            protected override void ProcessFrame(VLCFrame frame) => Total += 1;
            private int Total { get => 0; set { var o = new object(); } }
            """);

        Assert.True(diagnostics.Length == 1, Messages(diagnostics));
        Assert.Equal("'new Object' allocates on each frame (Fixture.set_Total runs per frame from Fixture.ProcessFrame)", diagnostics[0].GetMessage());
    }

    [Fact]
    public async Task OpenCloseAndErrorPaths_AreNotReported()
    {
        var diagnostics = await Analyze("""
            protected override bool OnOpen(VLCFilterContext context) { _buffer = new byte[1024]; return true; }
            protected override void OnFirstFrame(VLCFrame frame) { var s = $"{frame.Width}"; }
            protected override void ProcessFrame(VLCFrame frame)
            {
                if (frame.Width < 0) throw new ArgumentException($"bad width {frame.Width}");
                try { } catch (Exception ex) { var message = "failed: " + ex.Message; }
            }
            private byte[]? _buffer;
            """);

        Assert.True(diagnostics.IsEmpty, Messages(diagnostics));
    }

    [Fact]
    public async Task PerFrameAttribute_MarksAnotherRoot()
    {
        var diagnostics = await Analyze("""
            protected override void ProcessFrame(VLCFrame frame) { }
            [VLCPerFrame] private void Callback() { var list = new List<int>(); }
            private void NotPerFrame() { var list = new List<int>(); }
            """);

        Assert.True(diagnostics.Length == 1, Messages(diagnostics));
        var diagnostic = diagnostics[0];
        Assert.EndsWith("(in Fixture.Callback)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Suppression_IsHonored()
    {
        var diagnostics = await Analyze("""
            protected override void ProcessFrame(VLCFrame frame)
            {
            #pragma warning disable VLCLR001
                var s = $"frame {FrameCount}";
            #pragma warning restore VLCLR001
            }
            """);

        Assert.True(diagnostics.IsEmpty, Messages(diagnostics));
    }

    [Fact]
    public async Task TextRenderer_IsNotReported()
    {
        var diagnostics = await Analyze(
            "protected override nint RenderText(VLCTextRequest request) { var s = request.Text + \"!\"; return 0; }",
            baseType: "VLCTextRendererBase");

        Assert.True(diagnostics.IsEmpty, Messages(diagnostics));
    }

    private static string Messages(ImmutableArray<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Location.GetLineSpan().StartLinePosition}: {d.GetMessage()}"));

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string members, string extra = "", string baseType = "VLCVideoFilterBase")
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using VLCLR.Plugin;

            namespace AnalyzerFixture;

            public class Fixture : {{baseType}}
            {
                {{members}}
            }

            {{extra}}
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(VLCVideoFilterBase).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "AnalyzerFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new PerFrameAllocationAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }
}
