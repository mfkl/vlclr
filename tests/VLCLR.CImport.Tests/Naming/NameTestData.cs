using VLCLR.CImport.Manifest;
using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;

namespace VLCLR.CImport.Tests.Naming;

/// <summary>Small builders for merged snapshots used by the naming tests.</summary>
internal static class NameTestData
{
    private static readonly TypeRef Int = TypeRef.Of(BuiltinType.Int);

    public static FieldDecl Field(string name, TypeRef? type = null, long offset = 0)
        => new() { Name = name, Type = type ?? Int, Offset = offset };

    public static FieldDecl Anonymous(string recordName, long offset = 0)
        => new() { Name = "", Type = TypeRef.Record(recordName), Offset = offset, IsAnonymousMember = true };

    public static TypeDecl Record(string name, string header = "vlc_common.h", params FieldDecl[] fields)
        => new() { Name = name, Kind = RecordKind.Struct, Header = header, Size = 8, Alignment = 8, Fields = fields };

    public static TypeDecl AnonRecord(string name, string parent, string header = "vlc_common.h", params FieldDecl[] fields)
        => new() { Name = name, Kind = RecordKind.Struct, Header = header, IsAnonymous = true, Parent = parent, Size = 8, Alignment = 8, Fields = fields };

    public static EnumDecl Enum(string name, string header = "vlc_common.h", params string[] members)
        => new()
        {
            Name = name,
            Header = header,
            IsAnonymous = name.StartsWith('@'),
            UnderlyingType = Int,
            Members = [.. members.Select((m, i) => new EnumMember(m, i))],
        };

    public static FunctionDecl Function(string name, string header = "vlc_common.h")
        => new() { Name = name, Header = header, ReturnType = TypeRef.Of(BuiltinType.Void), IsExported = true };

    public static ConstantDecl Constant(string name, string header = "vlc_common.h")
        => new() { Name = name, Header = header, Kind = ConstantKind.Integer, Type = Int, Integer = 1, Expression = "1" };

    public static TypedefDecl Typedef(string name, TypeRef target, string header = "vlc_common.h")
        => new() { Name = name, Header = header, Target = target };

    public static MergedSnapshot Merged(
        IEnumerable<TypeDecl>? types = null,
        IEnumerable<EnumDecl>? enums = null,
        IEnumerable<TypedefDecl>? typedefs = null,
        IEnumerable<FunctionDecl>? functions = null,
        IEnumerable<ConstantDecl>? constants = null)
    {
        var windows = new Snapshot
        {
            Abi = Abis.Windows,
            Target = "x86_64-pc-windows-msvc",
            VlcCommit = "dc69496e",
            PointerSize = 8,
            Types = [.. types ?? []],
            Enums = [.. enums ?? []],
            Typedefs = [.. typedefs ?? []],
            Functions = [.. functions ?? []],
            Constants = [.. constants ?? []],
        };
        var merged = AbiMerger.Merge(windows, windows with { Abi = Abis.SysV, Target = "x86_64-pc-linux-gnu" });
        Assert.DoesNotContain(merged.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return merged;
    }

    public static NameMap Map(MergedSnapshot merged, NameOverrides? overrides = null)
        => NameMap.Create(merged, overrides ?? new NameOverrides(), "VLCLR.Native.Interop");

    /// <summary>Real-looking names from es_format_t, video_format_t, filters, orientation and fourcc.</summary>
    public static MergedSnapshot Vlc() => Merged(
        types:
        [
            Record("filter_t", "vlc_filter.h", Field("p_module"), Field("psz_name"), Field("pf_get_attachments"), Field("p_sys"), Field("fmt_in")),
            Record("vlc_filter_operations", "vlc_filter.h", Field("close")),
            Record("plane_t", "vlc_picture.h", Field("p_pixels"), Field("i_lines")),
            Record("video_format_t", "vlc_es.h", Field("i_chroma"), Field("dovi", TypeRef.Record("video_format_t::dovi")), Field("i_cubemap_padding")),
            AnonRecord("video_format_t::dovi", "video_format_t", "vlc_es.h", Field("version_major"), Field("profile")),
            Record("es_format_t", "vlc_es.h", Field("i_cat"), Anonymous("es_format_t::@0"), Field("psz_language")),
            AnonRecord("es_format_t::@0", "es_format_t", "vlc_es.h", Field("video", TypeRef.Record("video_format_t")), Field("i_codec_data"), Anonymous("es_format_t::@0::@0")),
            AnonRecord("es_format_t::@0::@0", "es_format_t::@0", "vlc_es.h", Field("p_nested")),
            Record("vlc_object_internals", "vlc_objects.h"),
        ],
        enums:
        [
            Enum("es_format_category_e", "vlc_es.h", "UNKNOWN_ES", "VIDEO_ES", "AUDIO_ES"),
            Enum("video_orientation_t", "vlc_es.h", "ORIENT_TOP_LEFT", "ORIENT_TOP_RIGHT", "ORIENT_BOTTOM_LEFT"),
            Enum("@VLC_SUCCESS", "vlc_common.h", "VLC_SUCCESS", "VLC_EGENERIC"),
        ],
        typedefs:
        [
            Typedef("video_format_t", TypeRef.Record("video_format_t"), "vlc_es.h"),
            Typedef("vlc_orientation_alias_t", TypeRef.Typedef("video_orientation_t", TypeRef.Enum("video_orientation_t"))),
            Typedef("vlc_tick_t", TypeRef.Typedef("int64_t", TypeRef.Of(BuiltinType.LongLong)), "vlc_tick.h"),
        ],
        functions:
        [
            Function("vlc_object_Log", "vlc_messages.h"),
            Function("var_Create", "vlc_variables.h"),
            Function("picture_NewFromFormat", "vlc_picture.h"),
            Function("vlc_player_GetTime", "vlc_player.h"),
            Function("vlc_tick_now", "vlc_tick.h"),
            Function("vlc_alloc", "vlc_common.h"),
        ],
        constants:
        [
            Constant("VLC_CODEC_I420", "vlc_fourcc.h"),
            Constant("VLC_CODEC_RGBA", "vlc_fourcc.h"),
            Constant("DIR_SEP", "vlc_common.h"),
        ]);
}
