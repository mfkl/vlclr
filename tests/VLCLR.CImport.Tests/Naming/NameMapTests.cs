using VLCLR.CImport.Manifest;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;
using static VLCLR.CImport.Tests.Naming.NameTestData;

namespace VLCLR.CImport.Tests.Naming;

public class NameMapTests
{
    private static readonly NameMap Vlc = Map(NameTestData.Vlc());

    [Fact]
    public void Namespace_IsTheOneGiven() => Assert.Equal("VLCLR.Native.Interop", Vlc.Namespace);

    // Types

    [Theory]
    [InlineData("filter_t", "Filter")]
    [InlineData("es_format_t", "EsFormat")]
    [InlineData("video_format_t", "VideoFormat")]
    [InlineData("plane_t", "Plane")]
    [InlineData("vlc_filter_operations", "FilterOperations")]
    [InlineData("vlc_object_internals", "ObjectInternals")]
    [InlineData("es_format_category_e", "EsFormatCategory")]
    [InlineData("video_orientation_t", "VideoOrientation")]
    public void Type_StripsSuffixAndVlcPrefix(string c, string expected) => Assert.Equal(expected, Vlc.Type(c));

    [Theory]
    [InlineData("video_format_t::dovi", "VideoFormatDovi")]
    [InlineData("es_format_t::@0", "EsFormatAnon0")]
    [InlineData("es_format_t::@0::@0", "EsFormatAnon0Anon0")]
    public void Type_AnonymousRecordsComposeParentAndField(string c, string expected) => Assert.Equal(expected, Vlc.Type(c));

    [Fact]
    public void Type_TypedefOfRecordOrEnumReturnsThatTypesName()
    {
        Assert.Equal("VideoFormat", Vlc.Type("video_format_t"));
        Assert.Equal("VideoOrientation", Vlc.Type("vlc_orientation_alias_t"));
    }

    [Fact]
    public void Type_OtherTypedefsGetRuleBasedName() => Assert.Equal("Tick", Vlc.Type("vlc_tick_t"));

    [Fact]
    public void Type_TypedefsDoNotTakePartInCollisionChecks()
    {
        var merged = Merged(
            types: [Record("tick")],
            typedefs: [Typedef("tick_t", TypeRef.PointerTo(TypeRef.Of(BuiltinType.Void)))]);

        Assert.Equal("Tick", Map(merged).Type("tick"));
        Assert.Equal("Tick", Map(merged).Type("tick_t"));
    }

    // Fields

    [Theory]
    [InlineData("filter_t", "p_module", "Module")]
    [InlineData("filter_t", "psz_name", "Name")]
    [InlineData("filter_t", "pf_get_attachments", "GetAttachments")]
    [InlineData("filter_t", "p_sys", "Sys")]
    [InlineData("filter_t", "fmt_in", "FmtIn")]
    [InlineData("video_format_t", "i_chroma", "Chroma")]
    [InlineData("video_format_t", "i_cubemap_padding", "CubemapPadding")]
    [InlineData("es_format_t", "psz_language", "Language")]
    [InlineData("vlc_filter_operations", "close", "Close")]
    public void Field_StripsHungarianPrefixThenPascal(string type, string field, string expected)
        => Assert.Equal(expected, Vlc.Field(type, field));

    [Fact]
    public void Field_PrefixIsKeptWhenRemainderIsEmptyOrStartsWithDigit()
    {
        var merged = Merged(types: [Record("x_t", "vlc_common.h", Field("i_"), Field("p_3d"), Field("e_"))]);
        var map = Map(merged);

        Assert.Equal("I", map.Field("x_t", "i_"));
        Assert.Equal("P3d", map.Field("x_t", "p_3d"));
    }

    [Fact]
    public void Field_FlattenedAnonymousMembersAreFoundByDeclaringRecord()
    {
        Assert.Equal("Video", Vlc.Field("es_format_t::@0", "video"));
        Assert.Equal("CodecData", Vlc.Field("es_format_t::@0", "i_codec_data"));
        Assert.Equal("Nested", Vlc.Field("es_format_t::@0::@0", "p_nested"));
        Assert.Equal("Profile", Vlc.Field("video_format_t::dovi", "profile"));
    }

    [Fact]
    public void Field_NamedLikeItsEnclosingTypeGetsValueSuffix()
    {
        var map = Map(Merged(types: [Record("foo_t", "vlc_common.h", Field("foo"), Field("i_bar"))]));

        Assert.Equal("FooValue", map.Field("foo_t", "foo"));
        Assert.Equal("Bar", map.Field("foo_t", "i_bar"));
    }

    [Fact]
    public void Field_ValueSuffixUsesNearestNamedAncestorForFlattenedMembers()
    {
        var merged = Merged(types:
        [
            Record("foo_t", "vlc_common.h", Anonymous("foo_t::@0")),
            AnonRecord("foo_t::@0", "foo_t", "vlc_common.h", Field("foo")),
        ]);

        Assert.Equal("FooValue", Map(merged).Field("foo_t::@0", "foo"));
    }

    [Fact]
    public void Field_ValueSuffixUsesAnonymousTypeNameForNamedFieldRecords()
    {
        var merged = Merged(types:
        [
            Record("foo_t", "vlc_common.h", Field("x", TypeRef.Record("foo_t::x"))),
            AnonRecord("foo_t::x", "foo_t", "vlc_common.h", Field("foo_x")),
        ]);

        Assert.Equal("FooXValue", Map(merged).Field("foo_t::x", "foo_x"));
    }

    // Enum members

    [Theory]
    [InlineData("video_orientation_t", "ORIENT_TOP_LEFT", "TopLeft")]
    [InlineData("video_orientation_t", "ORIENT_BOTTOM_LEFT", "BottomLeft")]
    [InlineData("es_format_category_e", "VIDEO_ES", "VideoEs")]
    public void EnumMember_StripsCommonPrefixAndPascals(string e, string member, string expected)
        => Assert.Equal(expected, Vlc.EnumMember(e, member));

    [Fact]
    public void EnumMember_SingleMemberKeepsItsPrefix()
    {
        var map = Map(Merged(enums: [Enum("one_e", "vlc_common.h", "ONE_MEMBER")]));

        Assert.Equal("OneMember", map.EnumMember("one_e", "ONE_MEMBER"));
    }

    [Fact]
    public void EnumMember_BacksOffWhenRemainderWouldStartWithDigit()
    {
        var map = Map(Merged(enums: [Enum("fmt_e", "vlc_common.h", "FMT_A_1", "FMT_A_2", "FMT_A_3")]));

        // "FMT_A_" leaves "1", "2", "3"; "FMT_" leaves "A_1", ...
        Assert.Equal("A1", map.EnumMember("fmt_e", "FMT_A_1"));
        Assert.Equal("A3", map.EnumMember("fmt_e", "FMT_A_3"));
    }

    [Fact]
    public void EnumMember_BacksOffWhenAMemberEqualsThePrefix()
    {
        var map = Map(Merged(enums: [Enum("fmt_e", "vlc_common.h", "FMT_A_", "FMT_A_B")]));

        Assert.Equal("A", map.EnumMember("fmt_e", "FMT_A_"));
        Assert.Equal("AB", map.EnumMember("fmt_e", "FMT_A_B"));
    }

    [Fact]
    public void EnumMember_NoSharedPrefixKeepsWholeName()
    {
        var map = Map(Merged(enums: [Enum("mixed_e", "vlc_common.h", "ALPHA", "BETA_ONE")]));

        Assert.Equal("Alpha", map.EnumMember("mixed_e", "ALPHA"));
        Assert.Equal("BetaOne", map.EnumMember("mixed_e", "BETA_ONE"));
    }

    // Functions

    [Theory]
    [InlineData("vlc_object_Log", "VlcObject", "Log")]
    [InlineData("var_Create", "Var", "Create")]
    [InlineData("picture_NewFromFormat", "Picture", "NewFromFormat")]
    [InlineData("vlc_player_GetTime", "VlcPlayer", "GetTime")]
    public void Function_SplitsAtFirstUnderscoreBeforeUppercase(string c, string cls, string member)
        => Assert.Equal(new QualifiedName(cls, member), Vlc.Function(c));

    [Fact]
    public void Function_WithoutSplitUsesHeaderClassAndStripsStem()
    {
        Assert.Equal(new QualifiedName("VlcTick", "Now"), Vlc.Function("vlc_tick_now"));
        Assert.Equal(new QualifiedName("VlcCommon", "VlcAlloc"), Vlc.Function("vlc_alloc"));
    }

    // Constants

    [Theory]
    [InlineData("VLC_CODEC_I420", "VlcFourcc", "CodecI420")]
    [InlineData("VLC_CODEC_RGBA", "VlcFourcc", "CodecRgba")]
    [InlineData("DIR_SEP", "VlcCommon", "DirSep")]
    [InlineData("VLC_SUCCESS", "VlcCommon", "Success")]
    [InlineData("VLC_EGENERIC", "VlcCommon", "Egeneric")]
    public void Constant_StripsVlcPrefixAndPascals(string c, string cls, string member)
        => Assert.Equal(new QualifiedName(cls, member), Vlc.Constant(c));

    [Fact]
    public void Constant_LowerSnakeFallsBackToPascal()
    {
        var map = Map(Merged(constants: [Constant("vlc_some_value", "vlc_common.h")]));

        Assert.Equal(new QualifiedName("VlcCommon", "VlcSomeValue"), map.Constant("vlc_some_value"));
    }

    // Header classes

    [Theory]
    [InlineData("vlc_es.h", "VlcEs")]
    [InlineData("vlc_fourcc.h", "VlcFourcc")]
    [InlineData("vlc_common.h", "VlcCommon")]
    public void HeaderClass_IsPascalOfStem(string header, string expected) => Assert.Equal(expected, Vlc.HeaderClass(header));

    [Fact]
    public void HeaderClass_SubdirectoriesJoinTheirParts()
    {
        var map = Map(Merged(constants: [Constant("A_B", "sub/dir/vlc_x.h")]));

        Assert.Equal("SubDirVlcX", map.HeaderClass("sub/dir/vlc_x.h"));
    }

    // Identifier safety

    [Fact]
    public void Escape_LeadingDigitGetsUnderscore()
    {
        var map = Map(Merged(constants: [Constant("VLC_3D", "vlc_common.h")]));

        Assert.Equal("_3d", map.Constant("VLC_3D").Member);
    }

    [Fact]
    public void Escape_KeywordsAreEscapedWithAt()
    {
        var overrides = new NameOverrides
        {
            Fields = new Dictionary<string, string> { ["filter_t.p_sys"] = "object" },
            Types = new Dictionary<string, string> { ["plane_t"] = "event" },
            Functions = new Dictionary<string, string> { ["var_Create"] = "static.new" },
        };
        var map = Map(NameTestData.Vlc(), overrides);

        Assert.Equal("@object", map.Field("filter_t", "p_sys"));
        Assert.Equal("@event", map.Type("plane_t"));
        Assert.Equal(new QualifiedName("@static", "@new"), map.Function("var_Create"));
    }

    [Fact]
    public void EmptyName_IsAnError()
    {
        var merged = Merged(constants: [Constant("VLC_", "vlc_common.h")]);

        // "VLC_" is not stripped (nothing would remain); its Pascal form is "Vlc".
        Assert.Equal("Vlc", Map(merged).Constant("VLC_").Member);

        var empty = Merged(types: [Record("_")]);
        Assert.Throws<InvalidOperationException>(() => Map(empty));
    }

    // Overrides

    [Fact]
    public void Overrides_WinOverRulesForEveryKind()
    {
        var overrides = new NameOverrides
        {
            Types = new Dictionary<string, string> { ["filter_t"] = "VideoFilter", ["video_orientation_t"] = "Orientation" },
            Fields = new Dictionary<string, string> { ["filter_t.p_sys"] = "Opaque" },
            EnumMembers = new Dictionary<string, string> { ["video_orientation_t.ORIENT_TOP_LEFT"] = "Normal" },
            Functions = new Dictionary<string, string> { ["vlc_object_Log"] = "Logger.Write" },
            Constants = new Dictionary<string, string> { ["VLC_CODEC_I420"] = "Codecs.I420" },
            Headers = new Dictionary<string, string> { ["vlc_es.h"] = "Es" },
        };
        var map = Map(NameTestData.Vlc(), overrides);

        Assert.Equal("VideoFilter", map.Type("filter_t"));
        Assert.Equal("Orientation", map.Type("video_orientation_t"));
        Assert.Equal("Opaque", map.Field("filter_t", "p_sys"));
        Assert.Equal("Normal", map.EnumMember("video_orientation_t", "ORIENT_TOP_LEFT"));
        Assert.Equal(new QualifiedName("Logger", "Write"), map.Function("vlc_object_Log"));
        Assert.Equal(new QualifiedName("Codecs", "I420"), map.Constant("VLC_CODEC_I420"));
        Assert.Equal("Es", map.HeaderClass("vlc_es.h"));
    }

    [Fact]
    public void Overrides_AnonymousRecordAndFlattenedFieldKeys()
    {
        var overrides = new NameOverrides
        {
            Types = new Dictionary<string, string> { ["es_format_t::@0"] = "EsFormatUnion", ["video_format_t::dovi"] = "DolbyVision" },
            Fields = new Dictionary<string, string> { ["es_format_t::@0.video"] = "VideoFormat" },
        };
        var map = Map(NameTestData.Vlc(), overrides);

        Assert.Equal("EsFormatUnion", map.Type("es_format_t::@0"));
        Assert.Equal("DolbyVision", map.Type("video_format_t::dovi"));
        Assert.Equal("VideoFormat", map.Field("es_format_t::@0", "video"));
    }

    [Fact]
    public void Overrides_HeaderOverrideChangesFallbackClass()
    {
        var overrides = new NameOverrides { Headers = new Dictionary<string, string> { ["vlc_tick.h"] = "Tick" } };

        Assert.Equal(new QualifiedName("Tick", "Now"), Map(NameTestData.Vlc(), overrides).Function("vlc_tick_now"));
    }

    [Fact]
    public void Overrides_TakePartInCollisionChecks()
    {
        var overrides = new NameOverrides { Fields = new Dictionary<string, string> { ["filter_t.p_sys"] = "Name" } };

        var e = Assert.Throws<NameCollisionException>(() => Map(NameTestData.Vlc(), overrides));
        Assert.Contains("filter_t.p_sys", e.Message);
        Assert.Contains("filter_t.psz_name", e.Message);
    }

    [Theory]
    [InlineData("types")]
    [InlineData("fields")]
    [InlineData("enumMembers")]
    [InlineData("functions")]
    [InlineData("constants")]
    [InlineData("headers")]
    public void Overrides_StaleKeyIsAnError(string section)
    {
        var stale = new Dictionary<string, string> { ["no_such_symbol"] = "Whatever" };
        var overrides = section switch
        {
            "types" => new NameOverrides { Types = stale },
            "fields" => new NameOverrides { Fields = new Dictionary<string, string> { ["filter_t.no_such"] = "X" } },
            "enumMembers" => new NameOverrides { EnumMembers = new Dictionary<string, string> { ["video_orientation_t.NO_SUCH"] = "X" } },
            "functions" => new NameOverrides { Functions = stale },
            "constants" => new NameOverrides { Constants = stale },
            _ => new NameOverrides { Headers = stale },
        };

        var e = Assert.Throws<StaleNameOverrideException>(() => Map(NameTestData.Vlc(), overrides));
        Assert.Equal(section, e.Section);
        Assert.Contains(e.Key, e.Message);
    }

    [Fact]
    public void Overrides_MalformedQualifiedNameIsAnError()
    {
        var overrides = new NameOverrides { Functions = new Dictionary<string, string> { ["var_Create"] = "NoDot" } };

        Assert.Throws<InvalidOperationException>(() => Map(NameTestData.Vlc(), overrides));
    }

    // Collisions

    [Fact]
    public void Collision_NamespaceTypes()
    {
        var merged = Merged(types: [Record("vlc_foo"), Record("foo_t")]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Equal("Foo", e.CSharpName);
        Assert.Contains("vlc_foo", e.Message);
        Assert.Contains("foo_t", e.Message);
    }

    [Fact]
    public void Collision_RecordAndEnumInNamespace()
    {
        var merged = Merged(types: [Record("foo_t")], enums: [Enum("foo_e", "vlc_common.h", "A", "B")]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Contains("foo_t", e.Message);
        Assert.Contains("foo_e", e.Message);
    }

    [Fact]
    public void Collision_AnonymousRecordUsedByNamedFieldIsEmitted()
    {
        var merged = Merged(types:
        [
            Record("a_t", "vlc_common.h", Field("b", TypeRef.Record("a_t::b"))),
            AnonRecord("a_t::b", "a_t"),
            Record("a_b_t"),
        ]);

        // a_t::b and a_b_t are both "AB".
        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Contains("a_t::b", e.Message);
        Assert.Contains("a_b_t", e.Message);
    }

    [Fact]
    public void NoCollision_UnreferencedAnonymousRecordIsNotEmitted()
    {
        var merged = Merged(types: [Record("a_t", "vlc_common.h", Anonymous("a_t::@0")), AnonRecord("a_t::@0", "a_t"), Record("a_anon0_t")]);

        Assert.Equal("AAnon0", Map(merged).Type("a_anon0_t"));
    }

    [Fact]
    public void Collision_FunctionClassAndEnumInNamespace()
    {
        var merged = Merged(
            enums: [Enum("var_t", "vlc_common.h", "A", "B")],
            functions: [Function("var_Create", "vlc_variables.h")]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Contains("var_t", e.Message);
        Assert.Contains("var_Create", e.Message);
    }

    [Fact]
    public void Collision_HeaderClassAndEnumInNamespace()
    {
        var merged = Merged(
            enums: [Enum("vlc_tick_e", "vlc_common.h", "A", "B")],
            functions: [Function("vlc_tick_now", "vlc_tick.h")]);

        // VlcTick (header class) vs enum vlc_tick_e = Tick: no clash; rename via override to provoke it.
        var overrides = new NameOverrides { Types = new Dictionary<string, string> { ["vlc_tick_e"] = "VlcTick" } };
        var e = Assert.Throws<NameCollisionException>(() => Map(merged, overrides));
        Assert.Contains("vlc_tick_e", e.Message);
        Assert.Contains("vlc_tick_now", e.Message);
    }

    [Fact]
    public void NoCollision_FunctionClassMayEqualHeaderClassOrRecord()
    {
        var merged = Merged(
            types: [Record("es_format_t", "vlc_es.h")],
            functions: [Function("es_format_Init", "vlc_es.h"), Function("es_format_Copy", "vlc_es.h")],
            constants: [Constant("ES_FORMAT_X", "vlc_es.h")]);
        var map = Map(merged);

        Assert.Equal("EsFormat", map.Function("es_format_Init").Class);
        Assert.Equal("EsFormat", map.Type("es_format_t"));
        Assert.Equal("VlcEs", map.Constant("ES_FORMAT_X").Class);
    }

    [Fact]
    public void Collision_StructFields()
    {
        var merged = Merged(types: [Record("foo_t", "vlc_common.h", Field("psz_x"), Field("p_x"))]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Equal("X", e.CSharpName);
        Assert.Contains("foo_t.psz_x", e.Message);
        Assert.Contains("foo_t.p_x", e.Message);
    }

    [Fact]
    public void CountAndItems_TheCountGetsCount()
    {
        var merged = Merged(types: [Record("foo_t", "vlc_common.h", Field("i_slaves"), Field("pp_slaves"), Field("i_other"))]);

        var map = Map(merged);
        Assert.Equal("SlavesCount", map.Field("foo_t", "i_slaves"));
        Assert.Equal("Slaves", map.Field("foo_t", "pp_slaves"));
        Assert.Equal("Other", map.Field("foo_t", "i_other"));
    }

    [Fact]
    public void CountAndItems_OverrideWins()
    {
        var merged = Merged(types: [Record("foo_t", "vlc_common.h", Field("i_x"), Field("p_x"))]);
        var overrides = new NameOverrides { Fields = new Dictionary<string, string> { ["foo_t.i_x"] = "XSize" } };

        Assert.Equal("XSize", Map(merged, overrides).Field("foo_t", "i_x"));
    }

    [Fact]
    public void Collisions_AreReportedTogether()
    {
        var merged = Merged(types:
        [
            Record("foo_t", "vlc_common.h", Field("psz_x"), Field("p_x")),
            Record("bar_t", "vlc_common.h", Field("psz_y"), Field("p_y")),
        ]);

        var e = Assert.Throws<NameCollisionsException>(() => Map(merged));
        Assert.Equal(2, e.Collisions.Count);
        Assert.Contains("foo_t.p_x", e.Message);
        Assert.Contains("bar_t.p_y", e.Message);
    }

    [Fact]
    public void Collision_FlattenedMemberAgainstDirectField()
    {
        var merged = Merged(types:
        [
            Record("foo_t", "vlc_common.h", Field("psz_x"), Anonymous("foo_t::@0")),
            AnonRecord("foo_t::@0", "foo_t", "vlc_common.h", Field("p_x")),
        ]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Contains("foo_t.psz_x", e.Message);
        Assert.Contains("foo_t::@0.p_x", e.Message);
    }

    [Fact]
    public void FieldNamedLikeAnObjectMember_GetsValue()
    {
        var merged = Merged(types: [Record("stream_operations", "vlc_stream.h", Field("get_type"), Field("to_string"))]);

        var map = Map(merged);
        Assert.Equal("GetTypeValue", map.Field("stream_operations", "get_type"));
        Assert.Equal("ToStringValue", map.Field("stream_operations", "to_string"));
    }

    [Fact]
    public void NoCollision_SameFieldNameInDifferentStructs()
    {
        var merged = Merged(types: [Record("a_t", "vlc_common.h", Field("i_x")), Record("b_t", "vlc_common.h", Field("i_x"))]);

        Assert.Equal("X", Map(merged).Field("b_t", "i_x"));
    }

    [Fact]
    public void Collision_EnumMembers()
    {
        var merged = Merged(enums: [Enum("foo_e", "vlc_common.h", "FOO_A_B", "FOO_A__B")]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Equal("B", e.CSharpName);
        Assert.Contains("FOO_A_B", e.Message);
        Assert.Contains("FOO_A__B", e.Message);
    }

    [Fact]
    public void Collision_StaticClassMembers()
    {
        var merged = Merged(
            functions: [Function("var_Create", "vlc_variables.h")],
            constants: [Constant("VAR_CREATE", "vlc_variables.h")]);

        // var_Create is Var.Create, VAR_CREATE is VlcVariables.VarCreate: no clash. Override to provoke it.
        var overrides = new NameOverrides { Constants = new Dictionary<string, string> { ["VAR_CREATE"] = "Var.Create" } };
        var e = Assert.Throws<NameCollisionException>(() => Map(merged, overrides));
        Assert.Contains("var_Create", e.Message);
        Assert.Contains("VAR_CREATE", e.Message);
    }

    [Fact]
    public void Collision_FunctionAgainstStructFieldInMergedPartialStruct()
    {
        var merged = Merged(
            types: [Record("es_format_t", "vlc_es.h", Field("i_init"))],
            functions: [Function("es_format_Init", "vlc_es.h")]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Contains("es_format_t.i_init", e.Message);
        Assert.Contains("es_format_Init", e.Message);
    }

    [Fact]
    public void MemberNamedLikeItsClass_IsAnErrorUnlessOverridden()
    {
        var merged = Merged(functions: [Function("var_Var", "vlc_variables.h")]);

        var e = Assert.Throws<NameCollisionException>(() => Map(merged));
        Assert.Contains("var_Var", e.Message);

        var overrides = new NameOverrides { Functions = new Dictionary<string, string> { ["var_Var"] = "Var.Get" } };
        Assert.Equal(new QualifiedName("Var", "Get"), Map(merged, overrides).Function("var_Var"));
    }

    [Fact]
    public void FunctionsNeverBound_TakePartInNoCollision()
    {
        // static inline: the C# emitter skips it, so it cannot clash with the field.
        var merged = Merged(
            types: [Record("video_splitter_t", "vlc_splitter.h", Field("pf_filter"))],
            functions: [Function("video_splitter_Filter", "vlc_splitter.h") with { IsInline = true, IsExported = false }]);

        Assert.Equal("Filter", Map(merged).Field("video_splitter_t", "pf_filter"));
    }

    // Unknown names

    [Fact]
    public void UnknownNames_ThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Vlc.Type("nope_t"));
        Assert.Throws<ArgumentException>(() => Vlc.Field("filter_t", "nope"));
        Assert.Throws<ArgumentException>(() => Vlc.Field("nope_t", "p_sys"));
        Assert.Throws<ArgumentException>(() => Vlc.EnumMember("video_orientation_t", "NOPE"));
        Assert.Throws<ArgumentException>(() => Vlc.EnumMember("nope_e", "A"));
        Assert.Throws<ArgumentException>(() => Vlc.Function("nope"));
        Assert.Throws<ArgumentException>(() => Vlc.Constant("NOPE"));
        Assert.Throws<ArgumentException>(() => Vlc.HeaderClass("nope.h"));
    }
}
