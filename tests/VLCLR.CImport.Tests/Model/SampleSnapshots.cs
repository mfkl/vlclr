using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Model;

/// <summary>
/// Hand-written snapshots shaped like video_format_t / es_format_t, covering
/// every IR feature. Windows and SysV differ in the dovi bitfields only.
/// </summary>
internal static class SampleSnapshots
{
    private static readonly TypeRef UInt8 = TypeRef.Typedef("uint8_t", TypeRef.Of(BuiltinType.UChar));
    private static readonly TypeRef UInt32 = TypeRef.Typedef("uint32_t", TypeRef.Of(BuiltinType.UInt));

    private static TypeRef UInt64(string abi)
        => TypeRef.Typedef("uint64_t", TypeRef.Of(abi == Abis.Windows ? BuiltinType.ULongLong : BuiltinType.ULong));

    public static Snapshot Windows => Create(Abis.Windows);

    public static Snapshot SysV => Create(Abis.SysV);

    public static Snapshot Create(string abi)
    {
        var windows = abi == Abis.Windows;
        return new Snapshot
        {
            Abi = abi,
            Target = windows ? "x86_64-pc-windows-msvc" : "x86_64-pc-linux-gnu",
            VlcCommit = "dc69496e",
            PointerSize = 8,
            Types =
            [
                // Deliberately out of order: Canonicalize sorts.
                new TypeDecl
                {
                    Name = "video_format_t::dovi",
                    Kind = RecordKind.Struct,
                    Header = "vlc_es.h",
                    IsAnonymous = true,
                    Parent = "video_format_t",
                    Size = windows ? 8 : 4,
                    Alignment = windows ? 4 : 4,
                    Fields =
                    [
                        new FieldDecl { Name = "version_major", Type = UInt8, Offset = 0 },
                        new FieldDecl { Name = "version_minor", Type = UInt8, Offset = 1 },
                        new FieldDecl { Name = "profile", Type = TypeRef.Of(BuiltinType.UInt), Offset = windows ? 4 : 2, BitOffset = windows ? 32 : 16, BitWidth = 7 },
                        new FieldDecl { Name = "level", Type = TypeRef.Of(BuiltinType.UInt), Offset = windows ? 4 : 2, BitOffset = windows ? 39 : 23, BitWidth = 6 },
                    ],
                },
                new TypeDecl
                {
                    Name = "video_format_t",
                    Kind = RecordKind.Struct,
                    Header = "vlc_es.h",
                    Size = windows ? 24 : 16,
                    Alignment = 8,
                    Fields =
                    [
                        new FieldDecl { Name = "i_chroma", Type = TypeRef.Typedef("vlc_fourcc_t", UInt32), Offset = 0 },
                        new FieldDecl { Name = "dovi", Type = TypeRef.Record("video_format_t::dovi"), Offset = 4 },
                        new FieldDecl { Name = "i_cubemap_padding", Type = UInt32, Offset = windows ? 12 : 8 },
                        new FieldDecl { Name = "i_size", Type = UInt64(abi), Offset = windows ? 16 : 8 },
                    ],
                },
                new TypeDecl
                {
                    Name = "es_format_t::@0",
                    Kind = RecordKind.Union,
                    Header = "vlc_es.h",
                    IsAnonymous = true,
                    Parent = "es_format_t",
                    Size = windows ? 24 : 16,
                    Alignment = 8,
                    Fields =
                    [
                        new FieldDecl { Name = "video", Type = TypeRef.Record("video_format_t"), Offset = 0 },
                        new FieldDecl { Name = "i_codec_data", Type = TypeRef.ArrayOf(TypeRef.Of(BuiltinType.Char), 4), Offset = 0 },
                    ],
                },
                new TypeDecl
                {
                    Name = "es_format_t",
                    Kind = RecordKind.Struct,
                    Header = "vlc_es.h",
                    Size = windows ? 40 : 32,
                    Alignment = 8,
                    Fields =
                    [
                        new FieldDecl { Name = "i_cat", Type = TypeRef.Enum("es_format_category_e"), Offset = 0 },
                        new FieldDecl { Name = "", Type = TypeRef.Record("es_format_t::@0"), Offset = 8, IsAnonymousMember = true },
                        new FieldDecl { Name = "p_extra", Type = TypeRef.PointerTo(TypeRef.Of(BuiltinType.Void)), Offset = windows ? 32 : 24 },
                    ],
                },
                new TypeDecl
                {
                    Name = "vlc_object_internals",
                    Kind = RecordKind.Struct,
                    Header = "vlc_objects.h",
                    IsOpaque = true,
                },
                new TypeDecl
                {
                    Name = "vlc_filter_operations",
                    Kind = RecordKind.Struct,
                    Header = "vlc_filter.h",
                    Size = 8,
                    Alignment = 8,
                    Fields =
                    [
                        new FieldDecl
                        {
                            Name = "close",
                            Type = TypeRef.PointerTo(TypeRef.FunctionOf(new FunctionType
                            {
                                ReturnType = TypeRef.Of(BuiltinType.Void),
                                Parameters = [new ParameterDecl("", TypeRef.PointerTo(TypeRef.Typedef("filter_t", TypeRef.Record("filter_t"))))],
                            })),
                            Offset = 0,
                        },
                    ],
                },
            ],
            Enums =
            [
                new EnumDecl
                {
                    Name = "es_format_category_e",
                    Header = "vlc_es.h",
                    UnderlyingType = TypeRef.Of(windows ? BuiltinType.Int : BuiltinType.UInt),
                    Members = [new EnumMember("UNKNOWN_ES", 0), new EnumMember("VIDEO_ES", 1), new EnumMember("NEGATIVE_ES", -1)],
                },
                new EnumDecl
                {
                    Name = "@VLC_VAR_TYPE",
                    Header = "vlc_variables.h",
                    IsAnonymous = true,
                    UnderlyingType = TypeRef.Of(BuiltinType.Int),
                    Members = [new EnumMember("VLC_VAR_TYPE", 0xff)],
                },
                new EnumDecl
                {
                    Name = "wide_e",
                    Header = "vlc_es.h",
                    HasFixedUnderlyingType = true,
                    UnderlyingType = TypeRef.Of(BuiltinType.ULongLong),
                    Members = [new EnumMember("WIDE_MAX", unchecked((long)ulong.MaxValue))],
                },
            ],
            Typedefs =
            [
                new TypedefDecl { Name = "video_format_t", Header = "vlc_common.h", Target = TypeRef.Record("video_format_t") },
                new TypedefDecl { Name = "vlc_tick_t", Header = "vlc_common.h", Target = TypeRef.Typedef("int64_t", TypeRef.Of(windows ? BuiltinType.LongLong : BuiltinType.Long)) },
            ],
            Functions =
            [
                new FunctionDecl
                {
                    Name = "vlc_object_Log",
                    Header = "vlc_messages.h",
                    ReturnType = TypeRef.Of(BuiltinType.Void),
                    Parameters =
                    [
                        new ParameterDecl("obj", TypeRef.PointerTo(TypeRef.Typedef("vlc_object_t", TypeRef.Record("vlc_object_t")))),
                        new ParameterDecl("format", TypeRef.PointerTo(TypeRef.Of(BuiltinType.Char) with { IsConst = true })),
                    ],
                    IsVariadic = true,
                    IsExported = true,
                },
                new FunctionDecl
                {
                    Name = "vlc_tick_now",
                    Header = "vlc_tick.h",
                    ReturnType = TypeRef.Typedef("vlc_tick_t", TypeRef.Typedef("int64_t", TypeRef.Of(windows ? BuiltinType.LongLong : BuiltinType.Long))),
                    IsExported = true,
                },
                new FunctionDecl
                {
                    Name = "es_format_IsSimilar",
                    Header = "vlc_es.h",
                    ReturnType = TypeRef.Of(BuiltinType.Bool),
                    IsInline = true,
                },
            ],
            Skipped = windows
                ? [new SkippedDecl { Name = "VLC_TICK_FROM_SEC", Header = "vlc_tick.h", Kind = "constant", Reason = "function-like macro" }]
                : [new SkippedDecl { Name = "VLC_TICK_FROM_SEC", Header = "vlc_tick.h", Kind = "constant", Reason = "function-like macro" },
                   new SkippedDecl { Name = "vlc_va_list_fn", Header = "vlc_common.h", Kind = "typedef", Reason = "va_list parameter" }],
            Constants =
            [
                new ConstantDecl
                {
                    Name = "VLC_CODEC_I420",
                    Header = "vlc_fourcc.h",
                    Kind = ConstantKind.Integer,
                    Type = TypeRef.Of(BuiltinType.UInt),
                    Integer = 0x30323449,
                    Expression = "VLC_FOURCC ( 'I' , '4' , '2' , '0' )",
                },
                new ConstantDecl
                {
                    Name = "VLC_TICK_RATE",
                    Header = "vlc_tick.h",
                    Kind = ConstantKind.Float,
                    Type = TypeRef.Of(BuiltinType.Double),
                    Float = 1e6,
                    Expression = "1e6",
                },
                new ConstantDecl
                {
                    Name = "DIR_SEP",
                    Header = "vlc_common.h",
                    Kind = ConstantKind.String,
                    Type = TypeRef.PointerTo(TypeRef.Of(BuiltinType.Char) with { IsConst = true }),
                    String = windows ? "\\" : "/",
                    Expression = windows ? "\"\\\\\"" : "\"/\"",
                },
            ],
        };
    }
}
