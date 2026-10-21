using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;
using VLCLR.CImport.Naming;
using static VLCLR.CImport.Tests.Emit.CSharp.Cases.Ir;

namespace VLCLR.CImport.Tests.Emit.CSharp.Cases;

/// <summary>
/// Hand-written IR pairs (windows, sysv) shaped like the VLC headers. Each case
/// is merged with the real <see cref="AbiMerger"/>, so the emitter sees what
/// the pipeline would give it.
/// </summary>
internal static class CSharpCases
{
    private static readonly Dictionary<string, Func<bool, Snapshot>> Builders = new(StringComparer.Ordinal)
    {
        ["identical"] = Identical,
        ["divergent"] = Divergent,
        ["bitfields"] = Bitfields,
        ["unions"] = Unions,
        ["arrays"] = Arrays,
        ["function_pointers"] = FunctionPointers,
        ["enums"] = Enums,
        ["constants"] = Constants,
        ["functions"] = Functions,
        ["opaque"] = OpaqueRecords,
        ["unsupported"] = Unsupported,
        ["clong"] = CLongs,
    };

    public static IEnumerable<string> Names => Builders.Keys.Order(StringComparer.Ordinal);

    public static (Snapshot Windows, Snapshot SysV) Snapshots(string name)
        => (Builders[name](true), Builders[name](false));

    public static MergedSnapshot Merged(string name)
    {
        var (windows, sysv) = Snapshots(name);
        return AbiMerger.Merge(windows, sysv, TestAbis.DarwinLike(sysv));
    }

    public static INameMap NameMap(string name) => name switch
    {
        "constants" => new TestNameMap(constantOverrides: new Dictionary<string, QualifiedName> { ["POINT_MAX"] = new("Point", "Max") }),
        _ => new TestNameMap(),
    };

    private static T W<T>(bool windows, T onWindows, T onSysV) => windows ? onWindows : onSysV;

    // ---- identical layout ------------------------------------------------------------------------

    private static Snapshot Identical(bool windows) => Snapshot(windows,
        types:
        [
            Struct("point_t", "vlc_point.h", 48, 8,
                F("i_x", Int, 0), F("i_y", Int, 4), F("d_w", CDouble, 8),
                F("psz_name", Ptr(CChar), 16), F("p_opaque", Ptr(CVoid), 24),
                F("b_enabled", Bool, 32), F("b_flag", U8, 33), F("s_mid", Short, 34),
                F("f_scale", Float, 36), F("i_len", SizeT(windows), 40)),
            Struct("rect_t", "vlc_rect.h", 104, 8,
                F("origin", Rec("point_t"), 0), F("corner", Rec("point_t"), 48), F("p_next", Ptr(Rec("point_t")), 96)),
        ]);

    // ---- divergent offsets: dovi -> video_format_t -> es_format_t -> filter_t ---------------------

    private static Snapshot Divergent(bool windows) => Snapshot(windows,
        types:
        [
            Opaque("video_palette_t", "vlc_es.h"),
            Opaque("picture_t", "vlc_picture.h"),
            Anonymous(RecordKind.Struct, "video_format_t::dovi", "video_format_t", "vlc_es.h", W(windows, 8, 4), 4,
                F("version_major", U8, 0), F("version_minor", U8, 1),
                Bits("profile", UInt, W(windows, 32, 16), 7),
                Bits("level", UInt, W(windows, 39, 23), 6),
                Bits("bl_present", UInt, W(windows, 45, 29), 1)),
            Struct("video_format_t", "vlc_es.h", W(windows, 40, 32), 8,
                F("i_chroma", Fourcc, 0),
                F("dovi", Rec("video_format_t::dovi"), 4),
                F("i_width", UInt, W(windows, 12, 8)),
                F("i_height", UInt, W(windows, 16, 12)),
                F("p_palette", Ptr(Rec("video_palette_t")), W(windows, 24, 16)),
                F("orientation", Enum("video_orientation_t"), W(windows, 32, 24))),
            Anonymous(RecordKind.Union, "es_format_t::@0", "es_format_t", "vlc_es.h", W(windows, 40, 32), 8,
                F("video", Rec("video_format_t"), 0), F("i_raw", Arr(U32, 4), 0)),
            Struct("es_format_t", "vlc_es.h", W(windows, 72, 64), 8,
                F("i_cat", Enum("es_format_category_e"), 0),
                F("i_codec", Fourcc, 4),
                AnonMember("es_format_t::@0", 8),
                F("p_extra", Ptr(CVoid), W(windows, 48, 40)),
                F("i_extra", SizeT(windows), W(windows, 56, 48)),
                F("psz_language", Ptr(CharT), W(windows, 64, 56))),
            Struct("vlc_filter_operations", "vlc_filter.h", 16, 8,
                F("close", FnPtr(CVoid, Ptr(Rec("filter_t"))), 0),
                F("filter_video", FnPtr(Ptr(Rec("picture_t")), Ptr(Rec("filter_t")), Ptr(Rec("picture_t"))), 8)),
            Struct("filter_t", "vlc_filter.h", W(windows, 168, 152), 8,
                F("psz_name", Ptr(CharT), 0),
                F("fmt_in", Rec("es_format_t"), 8),
                F("fmt_out", Rec("es_format_t"), W(windows, 80, 72)),
                F("p_sys", Ptr(CVoid), W(windows, 152, 136)),
                F("ops", Ptr(Rec("vlc_filter_operations")), W(windows, 160, 144))),
        ],
        enums:
        [
            Enumeration("es_format_category_e", "vlc_es.h", windows ? BuiltinType.Int : BuiltinType.UInt,
                ("UNKNOWN_ES", 0), ("VIDEO_ES", 1), ("AUDIO_ES", 2)),
            Enumeration("video_orientation_t", "vlc_es.h", BuiltinType.UInt,
                ("ORIENT_TOP_LEFT", 0), ("ORIENT_TOP_RIGHT", 1), ("ORIENT_BOTTOM_LEFT", 2)),
        ]);

    // ---- bitfields -------------------------------------------------------------------------------

    private static Snapshot Bitfields(bool windows) => Snapshot(windows,
        types:
        [
            Struct("bits_t", "vlc_bits.h", 16, 4,
                F("i_head", U32, 0),
                Bits("a", UInt, 32, 3),
                Bits("b", UInt, 35, 5),
                Bits("c", Int, 40, 4),
                Bits("d", Bool, 44, 1),
                Bits("e", Enum("mode_e"), 45, 2),
                Bits("f", U32, 47, 17),
                Bits("g", ULong, 64, 9),
                Bits("h", Long, 73, 7),
                F("i_tail", Int, 12)),
        ],
        enums: [Enumeration("mode_e", "vlc_bits.h", BuiltinType.UInt, ("MODE_OFF", 0), ("MODE_ON", 1), ("MODE_AUTO", 2), ("MODE_X", 3))]);

    // ---- unions and flattened anonymous members ---------------------------------------------------

    private static Snapshot Unions(bool windows) => Snapshot(windows,
        types:
        [
            Union("value_t", "vlc_value.h", 8, 8,
                F("i_int", Int, 0), F("i_ll", LongLong, 0), F("f_float", Float, 0), F("psz_string", Ptr(CharT), 0)),
            Anonymous(RecordKind.Struct, "rec_t::@0::@0", "rec_t::@0", "vlc_value.h", 4, 2,
                F("s_lo", Short, 0), F("s_hi", Short, 2)),
            Anonymous(RecordKind.Union, "rec_t::@0", "rec_t", "vlc_value.h", 4, 4,
                F("i_a", Int, 0), F("f_a", Float, 0), AnonMember("rec_t::@0::@0", 0)),
            Anonymous(RecordKind.Struct, "rec_t::stats", "rec_t", "vlc_value.h", 8, 4,
                F("i_count", Int, 0), F("i_sum", Int, 4)),
            Struct("rec_t", "vlc_value.h", 24, 8,
                F("i_tag", Int, 0),
                AnonMember("rec_t::@0", 4),
                F("stats", Rec("rec_t::stats"), 8),
                F("val", Rec("value_t"), 16)),
        ]);

    // ---- inline arrays ---------------------------------------------------------------------------

    private static Snapshot Arrays(bool windows) => Snapshot(windows,
        types:
        [
            Struct("vec_t", "vlc_arrays.h", 8, 4, F("x", Int, 0), F("y", Int, 4)),
            Struct("dv_t", "vlc_arrays.h", W(windows, 4, 8), W(windows, 4, 8), F("l", Long, 0)),
            Struct("dv_holder_t", "vlc_arrays.h", W(windows, 8, 16), W(windows, 4, 8), F("dvs", Arr(Rec("dv_t"), 2), 0)),
            Struct("arrays_t", "vlc_arrays.h", 160, 8,
                F("data", Arr(U8, 16), 0),
                F("name", Arr(CharT, 8), 16),
                F("pts", Arr(Rec("vec_t"), 4), 24),
                F("names", Arr(Ptr(CharT), 3), 56),
                F("cbs", Arr(FnPtr(CVoid, Ptr(CVoid)), 2), 80),
                F("m", Arr(Arr(Int, 3), 2), 96),
                F("sizes", Arr(SizeT(windows), 2), 120),
                F("rgb", TypeRef.Typedef("rgb_t", Arr(U8, 3)), 136),
                F("modes", Arr(Enum("mode_e"), 2), 140),
                F("flex", Arr(U8, null), 160)),
            Struct("darr_t", "vlc_arrays.h", W(windows, 24, 40), W(windows, 4, 8),
                F("l_before", Long, 0),
                F("i_arr", Arr(Int, 3), W(windows, 4, 8)),
                F("longs", Arr(Long, 2), W(windows, 16, 24))),
        ],
        enums: [Enumeration("mode_e", "vlc_arrays.h", BuiltinType.UInt, ("M_A", 0), ("M_B", 1))]);

    // ---- function pointers -----------------------------------------------------------------------

    private static Snapshot FunctionPointers(bool windows) => Snapshot(windows,
        types:
        [
            Opaque("obj_t", "vlc_cb.h"),
            Struct("vec_t", "vlc_cb.h", 8, 4, F("x", Int, 0), F("y", Int, 4)),
            Struct("dv_t", "vlc_cb.h", W(windows, 4, 8), W(windows, 4, 8), F("l", Long, 0)),
            Struct("callbacks_t", "vlc_cb.h", 96, 8,
                F("pf_close", FnPtr(CVoid, Ptr(Rec("obj_t"))), 0),
                F("pf_open", FnPtr(Int, Ptr(Rec("obj_t")), Ptr(CChar), U64(windows)), 8),
                F("pf_alloc", FnPtr(Ptr(CVoid), SizeT(windows)), 16),
                F("pf_ok", FnPtr(Bool, Ptr(Rec("obj_t")), Bool), 24),
                F("pf_mode", FnPtr(Enum("mode_e"), Enum("mode_e")), 32),
                F("pf_vec", FnPtr(Rec("vec_t"), Rec("vec_t")), 40),
                F("ppf", Ptr(FnPtr(CVoid)), 48),
                F("pf_va", Ptr(TypeRef.FunctionOf(new FunctionType { ReturnType = CVoid, Parameters = [new("", Ptr(CChar))], IsVariadic = true })), 56),
                F("pf_dv", FnPtr(Rec("dv_t"), Rec("dv_t")), 64),
                F("pf_std", Ptr(TypeRef.FunctionOf(new FunctionType { ReturnType = CVoid, Parameters = [new("", Int)], CallingConvention = VLCLR.CImport.Model.CallingConvention.Stdcall })), 72),
                F("pf_long", FnPtr(Long, Long), 80),
                F("pf_get", FnPtr(FnPtr(CVoid, Int)), 88)),
        ],
        enums: [Enumeration("mode_e", "vlc_cb.h", BuiltinType.UInt, ("MODE_A", 0), ("MODE_B", 1))]);

    // ---- enums -----------------------------------------------------------------------------------

    private static Snapshot Enums(bool windows) => Snapshot(windows,
        types:
        [
            Struct("holder_t", "vlc_enums.h", 32, 8,
                F("n", Enum("neg_e"), 0),
                F("s", Enum("sign_e"), 4),
                F("b", Enum("big_e"), 8),
                F("y", Enum("width_e"), 16),
                F("kind", Enum("@FLAG_A"), 20),
                F("sz", Enum("size_e"), 24)),
        ],
        enums:
        [
            Enumeration("neg_e", "vlc_enums.h", BuiltinType.Int, ("A_NEG", -1), ("A_ZERO", 0), ("A_ONE", 1)),
            Enumeration("big_e", "vlc_enums.h", BuiltinType.ULongLong, ("BIG_ONE", 1), ("BIG_MAX", -1)),
            Enumeration("width_e", "vlc_enums.h", BuiltinType.UChar, ("WIDTH_A", 1), ("WIDTH_B", 255)) with { HasFixedUnderlyingType = true },
            Enumeration("sign_e", "vlc_enums.h", windows ? BuiltinType.Int : BuiltinType.UInt, ("SIGN_A", 0), ("SIGN_B", 1)),
            Enumeration("sign_big_e", "vlc_enums.h", windows ? BuiltinType.Int : BuiltinType.UInt, ("SB_LOW", 0), ("SB_HIGH", 2147483648)),
            Enumeration("div_val_e", "vlc_enums.h", BuiltinType.Int, ("DV_SAME", 1), ("DV_DIFF", windows ? 2 : 3)),
            Enumeration("size_e", "vlc_enums.h", BuiltinType.Long, ("SIZE_A", 1)),
            Enumeration("@FLAG_A", "vlc_enums.h", BuiltinType.Int, ("FLAG_A", 1), ("FLAG_B", 2), ("FLAG_MASK", 0xff)) with { IsAnonymous = true },
            Enumeration("@BIG_LIMIT", "vlc_enums.h", BuiltinType.UInt, ("BIG_LIMIT", 4294967295)) with { IsAnonymous = true },
        ]);

    // ---- constants -------------------------------------------------------------------------------

    private static ConstantDecl IntConstant(string name, string header, BuiltinType type, long value)
        => new() { Name = name, Header = header, Kind = ConstantKind.Integer, Type = TypeRef.Of(type), Integer = value, Expression = value.ToString() };

    private static Snapshot Constants(bool windows) => Snapshot(windows,
        types: [Struct("point_t", "vlc_const.h", 8, 4, F("x", Int, 0), F("y", Int, 4))],
        constants:
        [
            IntConstant("INT_C", "vlc_const.h", BuiltinType.Int, 42),
            IntConstant("NEG_C", "vlc_const.h", BuiltinType.Int, -1),
            IntConstant("UINT_C", "vlc_const.h", BuiltinType.UInt, 4294967295),
            IntConstant("LL_C", "vlc_const.h", BuiltinType.LongLong, 1L << 40),
            IntConstant("ULL_C", "vlc_const.h", BuiltinType.ULongLong, -1),
            IntConstant("MIN_C", "vlc_const.h", BuiltinType.LongLong, long.MinValue),
            IntConstant("DIV_INT", "vlc_const.h", BuiltinType.Int, W(windows, 1, 2)),
            IntConstant("DIV_ULL", "vlc_const.h", BuiltinType.ULongLong, W(windows, 10L, 20L)),
            IntConstant("TOO_BIG", "vlc_const.h", BuiltinType.Int, 4294967296),
            IntConstant("POINT_MAX", "vlc_const2.h", BuiltinType.Int, 1000),
            new ConstantDecl { Name = "FLT_C", Header = "vlc_const.h", Kind = ConstantKind.Float, Type = Float, Float = 1.5, Expression = "1.5f" },
            new ConstantDecl { Name = "DBL_C", Header = "vlc_const.h", Kind = ConstantKind.Float, Type = CDouble, Float = 1e6, Expression = "1e6" },
            new ConstantDecl { Name = "DBL_FRAC", Header = "vlc_const.h", Kind = ConstantKind.Float, Type = CDouble, Float = 0.1, Expression = "0.1" },
            new ConstantDecl { Name = "STR_C", Header = "vlc_const.h", Kind = ConstantKind.String, Type = Ptr(CChar), String = "a\\b\"c\né", Expression = "\"a\\\\b\\\"c\\n\\u00e9\"" },
            new ConstantDecl { Name = "DIV_STR", Header = "vlc_const.h", Kind = ConstantKind.String, Type = Ptr(CChar), String = W(windows, "\\", "/"), Expression = "DIR_SEP" },
        ]);

    // ---- functions -------------------------------------------------------------------------------

    private static Snapshot Functions(bool windows)
    {
        var obj = Ptr(Rec("obj_t"));
        return Snapshot(windows,
            types:
            [
                Opaque("obj_t", "vlc_fn.h"),
                Struct("vec_t", "vlc_fn.h", 8, 4, F("x", Int, 0), F("y", Int, 4)),
                Struct("dv_t", "vlc_fn.h", W(windows, 4, 8), W(windows, 4, 8), F("l", Long, 0)),
                Struct("video_format_t", "vlc_fn.h", 8, 4, F("i_w", Int, 0), F("i_h", Int, 4)),
            ],
            enums: [Enumeration("mode_e", "vlc_fn.h", BuiltinType.UInt, ("MODE_A", 0), ("MODE_B", 1))],
            functions:
            [
                Function("vlc_tick_now", "vlc_fn.h", Tick(windows)),
                Function("vlc_obj_Hold", "vlc_fn.h", obj, ("obj", obj)),
                Function("vlc_obj_Release", "vlc_fn.h", CVoid, ("obj", obj)),
                Function("vlc_obj_IsOk", "vlc_fn.h", Bool, ("obj", obj), ("strict", Bool)),
                Function("vlc_vec_Add", "vlc_fn.h", Rec("vec_t"), ("a", Rec("vec_t")), ("b", Rec("vec_t"))),
                Function("vlc_mode_Get", "vlc_fn.h", Enum("mode_e"), ("previous", Enum("mode_e"))),
                Function("vlc_str_Set", "vlc_fn.h", CVoid, ("string", Ptr(CChar)), ("default", Int), ("", Int)),
                Function("vlc_cb_Set", "vlc_fn.h", CVoid, ("cb", FnPtr(CVoid, Ptr(CVoid))), ("opaque", Ptr(CVoid))),
                Function("vlc_num_Mix", "vlc_fn.h", SizeT(windows), ("count", SizeT(windows)), ("l", Long), ("ul", ULong), ("ll", LongLong), ("ids", Ptr(U32))),
                Function("vlc_msg_Log", "vlc_fn.h", CVoid, ("obj", obj), ("format", Ptr(CChar))) with { IsVariadic = true },
                Function("vlc_inline_Foo", "vlc_fn.h", Int) with { IsInline = true, IsExported = false },
                Function("vlc_priv_Bar", "vlc_fn.h", Int) with { IsExported = false },
                Function("vlc_diff_Foo", "vlc_fn.h", CVoid, ("v", windows ? Int : Long)),
                Function("vlc_ld_Get", "vlc_fn.h", TypeRef.Of(BuiltinType.LongDouble)),
                Function("vlc_dv_Pass", "vlc_fn.h", CVoid, ("v", Rec("dv_t"))),
                Function("video_format_Copy", "vlc_fn2.h", Int, ("dst", Ptr(Rec("video_format_t"))), ("src", Ptr(Rec("video_format_t")))),
            ]);
    }

    // ---- opaque records --------------------------------------------------------------------------

    private static Snapshot OpaqueRecords(bool windows) => Snapshot(windows,
        types:
        [
            Opaque("vlc_object_t", "vlc_objects.h"),
            Opaque("block_t", "vlc_block.h"),
            Struct("holder_t", "vlc_objects.h", 24, 8,
                F("p_obj", Ptr(Rec("vlc_object_t")), 0),
                F("pp_obj", Ptr(Ptr(Rec("vlc_object_t"))), 8),
                F("obj", Rec("vlc_object_t"), 16)),
        ],
        functions:
        [
            Function("vlc_object_Hold", "vlc_objects.h", Ptr(Rec("vlc_object_t")), ("obj", Ptr(Rec("vlc_object_t")))),
            Function("block_Release", "vlc_block.h", CVoid, ("block", Ptr(Rec("block_t")))),
        ]);

    // ---- unsupported types -----------------------------------------------------------------------

    private static Snapshot Unsupported(bool windows) => Snapshot(windows,
        types:
        [
            Struct("weird_t", "vlc_weird.h", 96, 16,
                F("ld", TypeRef.Of(BuiltinType.LongDouble), 0),
                F("wc", TypeRef.Typedef("wchar_t", TypeRef.Of(windows ? BuiltinType.UShort : BuiltinType.Int)), 16),
                F("wide", TypeRef.Of(BuiltinType.Int128), 32),
                F("c16", TypeRef.Typedef("char16_t", TypeRef.Of(BuiltinType.Char16)), 48),
                F("ok", Int, 52),
                F("p_missing", Ptr(Rec("missing_t")), 56),
                F("missing", Rec("missing_t"), 64),
                F("e_missing", Enum("missing_e"), 72),
                F("diff", windows ? Int : Long, 76),
                F("pp_arr", Ptr(Arr(Int, 4)), 80),
                F("v", CVoid, 88)),
        ]);

    // ---- long, unsigned long, size_t ---------------------------------------------------------------

    private static Snapshot CLongs(bool windows) => Snapshot(windows,
        types:
        [
            Struct("longs_t", "vlc_longs.h", W(windows, 40, 48), 8,
                F("l", Long, 0),
                F("ul", ULong, W(windows, 4, 8)),
                F("ll", LongLong, W(windows, 8, 16)),
                F("size", SizeT(windows), W(windows, 16, 24)),
                F("tick", Tick(windows), W(windows, 24, 32)),
                Bits("bl", Long, W(windows, 256, 320), 5)),
        ]);
}
