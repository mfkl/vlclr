using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Extract;

/// <summary>Hand-checked facts about the fixture snapshots: the ABI differences extract exists to record.</summary>
[Collection("libclang")]
public class LayoutTests
{
    private static TypeDecl Type(string fixture, string abi, string name)
        => Assert.Single(FixtureExtractor.Extract(fixture, abi).Types, t => t.Name == name);

    [Fact]
    public void DoviBitfieldsStartInANewUnitOnWindowsAndPackOnSysV()
    {
        var windows = Type("bitfields", Abis.Windows, "bits_video::dovi");
        var sysv = Type("bitfields", Abis.SysV, "bits_video::dovi");

        Assert.True(windows.IsAnonymous);
        Assert.Equal("bits_video", windows.Parent);
        Assert.Equal(8, windows.Size);
        Assert.Equal(32, windows.Fields.Single(f => f.Name == "rpu_type").BitOffset);
        Assert.Equal(4, windows.Fields.Single(f => f.Name == "rpu_type").Offset);

        Assert.Equal(8, sysv.Size);
        Assert.Equal(16, sysv.Fields.Single(f => f.Name == "rpu_type").BitOffset);
        Assert.Equal(2, sysv.Fields.Single(f => f.Name == "rpu_type").Offset);
        Assert.Equal(4, sysv.Fields.Single(f => f.Name == "dv_level").Offset);
        Assert.Equal(6, sysv.Fields.Single(f => f.Name == "dv_level").BitWidth);
    }

    [Fact]
    public void LongIsFourBytesOnWindowsAndEightOnSysV()
    {
        var windows = Type("long", Abis.Windows, "long_mix");
        var sysv = Type("long", Abis.SysV, "long_mix");

        Assert.Equal([0L, 4, 8, 16, 24], windows.Fields.Select(f => f.Offset));
        Assert.Equal([0L, 8, 16, 24, 32], sysv.Fields.Select(f => f.Offset));
        Assert.Equal(32, windows.Size);
        Assert.Equal(40, sysv.Size);
    }

    [Fact]
    public void Uint64KeepsItsTypedefAndDiffersInTheCanonicalBuiltin()
    {
        foreach (var (abi, builtin) in new[] { (Abis.Windows, BuiltinType.ULongLong), (Abis.SysV, BuiltinType.ULong) })
        {
            var tick = Type("size_t", abi, "size_types").Fields.Single(f => f.Name == "tick").Type;
            Assert.Equal(TypeRefKind.Typedef, tick.Kind);
            Assert.Equal("uint64_t", tick.Name);
            Assert.Equal(builtin, tick.Element!.Builtin);
        }
    }

    [Fact]
    public void AnonymousMembersAreNamedByIndexAndChained()
    {
        var snapshot = FixtureExtractor.Extract("anonymous", Abis.SysV);
        var parent = snapshot.Types.Single(t => t.Name == "es_like");

        Assert.Equal(["cat", "", "named", "", "tail"], parent.Fields.Select(f => f.Name));
        Assert.Equal(["es_like::@0", "es_like::@1"], parent.Fields.Where(f => f.IsAnonymousMember).Select(f => f.Type.Name));
        Assert.Equal("es_like::named", parent.Fields.Single(f => f.Name == "named").Type.Name);

        var union = snapshot.Types.Single(t => t.Name == "es_like::@0");
        Assert.Equal(RecordKind.Union, union.Kind);
        Assert.Equal("es_like", union.Parent);
        Assert.Contains(snapshot.Types, t => t is { Name: "es_like::@0::@0", Parent: "es_like::@0", IsAnonymous: true });
        Assert.Contains(snapshot.Types, t => t.Name == "es_like::@0::size");
    }

    [Fact]
    public void TypedefNamedAnonymousRecordTakesTheTypedefName()
    {
        var anon = Type("typedef_chains", Abis.SysV, "anon_t");

        Assert.False(anon.IsAnonymous);
        Assert.Null(anon.Parent);
    }

    [Fact]
    public void PackedRecordsAreMarkedForAttributeAndPragma()
    {
        Assert.True(Type("packed", Abis.Windows, "packed_attr").IsPacked);
        Assert.True(Type("packed", Abis.Windows, "packed_pragma").IsPacked);
        Assert.False(Type("packed", Abis.Windows, "normal_after").IsPacked);
        Assert.Equal(7, Type("packed", Abis.SysV, "packed_attr").Size);
    }

    [Fact]
    public void FlexibleArraysHaveNoCount()
    {
        var payload = Type("flexible_array", Abis.SysV, "block_like").Fields.Single(f => f.Name == "p_payload");

        Assert.Equal(TypeRefKind.Array, payload.Type.Kind);
        Assert.Null(payload.Type.Count);
        Assert.Equal(16, payload.Offset);
    }

    [Fact]
    public void EnumsRecordUnderlyingTypeFixednessAndValues()
    {
        var sysv = FixtureExtractor.Extract("enums", Abis.SysV);

        var fixedEnum = sysv.Enums.Single(e => e.Name == "fixed_e");
        Assert.True(fixedEnum.HasFixedUnderlyingType);
        Assert.Equal(BuiltinType.UChar, fixedEnum.UnderlyingType.Builtin);
        Assert.False(sysv.Enums.Single(e => e.Name == "signed_e").HasFixedUnderlyingType);
        Assert.Equal([-1L, 0, 5], sysv.Enums.Single(e => e.Name == "signed_e").Members.Select(m => m.Value));
        Assert.Equal(1L << 32, sysv.Enums.Single(e => e.Name == "wide_e").Members.Last().Value);
        Assert.False(sysv.Enums.Single(e => e.Name == "typedef_e").IsAnonymous);
        Assert.True(sysv.Enums.Single(e => e.Name == "@ANON_A").IsAnonymous);
    }

    [Fact]
    public void ExportedInlineAndVariadicFunctionsAreTold()
    {
        var functions = FixtureExtractor.Extract("functions", Abis.Windows).Functions.ToDictionary(f => f.Name);

        Assert.True(functions["add"].IsExported);
        Assert.True(functions["exported_nothing"].IsExported);
        Assert.False(functions["log_message"].IsExported);
        Assert.True(functions["log_message"].IsVariadic);
        Assert.True(functions["square"].IsInline);
        Assert.False(functions["add"].IsInline);
        Assert.Equal("a", functions["add"].Parameters[0].Name);
    }

    [Fact]
    public void ConstantsEvaluateWithSignedInterpretation()
    {
        var constants = FixtureExtractor.Extract("constants", Abis.SysV).Constants.ToDictionary(c => c.Name);

        Assert.Equal(-5L, constants["K_NEGATIVE"].Integer!.Value);
        Assert.Equal(-1L, constants["K_UINT64"].Integer!.Value);
        Assert.Equal(BuiltinType.ULongLong, constants["K_UINT64"].Type.Builtin);
        Assert.Equal(0x30323449L, constants["K_CODEC_I420"].Integer!.Value);
        Assert.Equal(ConstantKind.Float, constants["K_FLOAT"].Kind);
        Assert.Equal("hello", constants["K_STRING"].String);
        Assert.Equal("K_FOURCC ( 'I' , '4' , '2' , '0' )", constants["K_CODEC_I420"].Expression);
    }
}
