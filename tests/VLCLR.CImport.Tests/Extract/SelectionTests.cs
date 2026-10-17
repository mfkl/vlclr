using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Extract;

[Collection("libclang")]
public class SelectionTests
{
    public static IEnumerable<object[]> BothAbis() => Abis.All.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(BothAbis))]
    public void UnknownRootIsAnErrorNamingTheRoot(string abi)
    {
        var e = Assert.Throws<CommandException>(() => FixtureExtractor.Extract("nested", abi, roots: ["outer", "no_such_symbol"]));
        Assert.Contains("no_such_symbol", e.Message);
    }

    [Fact]
    public void UnmatchedPrefixRootIsAnError()
    {
        var e = Assert.Throws<CommandException>(() => FixtureExtractor.Extract("constants", Abis.SysV, roots: ["NOPE_*"]));
        Assert.Contains("NOPE_*", e.Message);
    }

    [Fact]
    public void RecordsEmbeddedByValueAreKeptInFullAndPointersStayOpaque()
    {
        var snapshot = FixtureExtractor.Extract("nested", Abis.SysV, roots: ["middle"]);

        var middle = Assert.Single(snapshot.Types, t => t.Name == "middle");
        Assert.False(middle.IsOpaque);
        var inner = Assert.Single(snapshot.Types, t => t.Name == "inner");
        Assert.False(inner.IsOpaque);
        Assert.NotNull(inner.Size);
        Assert.DoesNotContain(snapshot.Types, t => t.Name == "outer");
    }

    [Fact]
    public void ClosureFollowsValueEmbeddingTransitively()
    {
        var snapshot = FixtureExtractor.Extract("nested", Abis.Windows, roots: ["outer"]);

        Assert.Equal(["inner", "middle", "outer"], snapshot.Types.Select(t => t.Name));
        Assert.All(snapshot.Types, t => Assert.False(t.IsOpaque));
    }

    [Theory]
    [MemberData(nameof(BothAbis))]
    public void RecordReachedOnlyThroughAPointerIsOpaque(string abi)
    {
        var snapshot = FixtureExtractor.Extract("opaque", abi, roots: ["holder_of_visible"]);

        var visible = Assert.Single(snapshot.Types, t => t.Name == "visible");
        Assert.True(visible.IsOpaque);
        Assert.Null(visible.Size);
        Assert.Empty(visible.Fields);
        Assert.False(Assert.Single(snapshot.Types, t => t.Name == "holder_of_visible").IsOpaque);
    }

    [Fact]
    public void RecordThatIsAlsoARootIsKeptInFullEvenWhenReachedByPointer()
    {
        var snapshot = FixtureExtractor.Extract("opaque", Abis.SysV, roots: ["holder_of_visible", "visible"]);

        Assert.False(Assert.Single(snapshot.Types, t => t.Name == "visible").IsOpaque);
    }

    [Fact]
    public void NeverDefinedRecordIsOpaqueAndItsTypedefIsKept()
    {
        var snapshot = FixtureExtractor.Extract("opaque", Abis.SysV, roots: ["holder"]);

        Assert.True(Assert.Single(snapshot.Types, t => t.Name == "hidden").IsOpaque);
        Assert.Contains(snapshot.Typedefs, t => t.Name == "hidden_t");
        Assert.Empty(snapshot.Skipped);
    }

    [Fact]
    public void TypedefRootKeepsItsTargetInFull()
    {
        var snapshot = FixtureExtractor.Extract("typedef_chains", Abis.SysV, roots: ["base_alias_t"]);

        Assert.False(Assert.Single(snapshot.Types, t => t.Name == "base").IsOpaque);
        Assert.Contains(snapshot.Typedefs, t => t.Name == "base_alias_t");
        Assert.Contains(snapshot.Typedefs, t => t.Name == "base_t");
    }

    [Fact]
    public void SystemTypedefsAreSugarOnly()
    {
        var snapshot = FixtureExtractor.Extract("size_t", Abis.SysV, roots: ["size_types"]);

        // fixture_types.h is under the include dir, so its typedefs are declarations; they are kept only because they are referenced.
        Assert.Equal(["intptr_t", "ptrdiff_t", "size_t", "ssize_t", "uint64_t", "uintptr_t"], snapshot.Typedefs.Select(t => t.Name));
    }

    [Fact]
    public void ExcludedRecordEmbeddedByValueIsAnErrorNamingBoth()
    {
        var e = Assert.Throws<CommandException>(() => FixtureExtractor.Extract("nested", Abis.SysV, roots: ["outer"], excludes: ["inner"]));
        Assert.Contains("inner", e.Message);
        Assert.Contains("middle", e.Message);
    }

    [Fact]
    public void ExcludedRecordReachedThroughAPointerStaysOpaque()
    {
        var snapshot = FixtureExtractor.Extract("opaque", Abis.SysV, roots: ["holder_of_visible"], excludes: ["visible"]);

        Assert.True(Assert.Single(snapshot.Types, t => t.Name == "visible").IsOpaque);
    }

    [Fact]
    public void ExcludesRemoveRootsFunctionsAndConstants()
    {
        var constants = FixtureExtractor.Extract("constants", Abis.SysV, roots: ["K_*"], excludes: ["K_HEX", "K_FOURCC*"]);
        Assert.DoesNotContain(constants.Constants, c => c.Name == "K_HEX");
        Assert.Contains(constants.Constants, c => c.Name == "K_DECIMAL");
        Assert.DoesNotContain(constants.Skipped, s => s.Name == "K_FOURCC");

        var functions = FixtureExtractor.Extract("functions", Abis.SysV, excludes: ["add", "point"]);
        Assert.DoesNotContain(functions.Functions, f => f.Name == "add");
        Assert.Contains(functions.Functions, f => f.Name == "square");
        Assert.True(Assert.Single(functions.Types, t => t.Name == "point").IsOpaque); // still reached through point_new
    }

    [Fact]
    public void FunctionsAndConstantsAreKeptOnlyWhenARootMatchesThem()
    {
        var snapshot = FixtureExtractor.Extract("functions", Abis.SysV, roots: ["point_new"]);

        Assert.Equal(["point_new"], snapshot.Functions.Select(f => f.Name));
        Assert.True(Assert.Single(snapshot.Types, t => t.Name == "point").IsOpaque);
    }

    [Fact]
    public void PrefixRootsMatchMacros()
    {
        var snapshot = FixtureExtractor.Extract("constants", Abis.SysV, roots: ["K_FLOAT*"]);

        Assert.Equal(["K_FLOAT", "K_FLOAT_SINGLE"], snapshot.Constants.Select(c => c.Name));
    }

    [Fact]
    public void AnonymousEnumIsSelectedByAMemberName()
    {
        var snapshot = FixtureExtractor.Extract("enums", Abis.SysV, roots: ["ANON_B"]);

        var anon = Assert.Single(snapshot.Enums);
        Assert.Equal("@ANON_A", anon.Name);
        Assert.True(anon.IsAnonymous);
    }

    [Fact]
    public void MacrosThatDoNotEvaluateAreSkippedWithAReason()
    {
        var snapshot = FixtureExtractor.Extract("constants", Abis.SysV);

        Assert.Contains(snapshot.Skipped, s => s is { Name: "K_TICKS", Kind: "constant" } && s.Reason.Contains("function-like"));
        Assert.Contains(snapshot.Skipped, s => s.Name == "K_NOT_A_VALUE");
        Assert.DoesNotContain(snapshot.Skipped, s => s.Name == "K_EMPTY");
    }
}
