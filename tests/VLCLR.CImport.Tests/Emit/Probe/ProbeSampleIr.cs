using VLCLR.CImport.Merge;
using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Emit;

/// <summary>
/// Hand-written snapshots for Headers/probe_sample.h, with the sizes and offsets
/// MSVC (windows) and g++ (sysv) compute for it.
/// </summary>
internal static class ProbeSampleIr
{
    private static readonly TypeRef UInt8 = TypeRef.Typedef("uint8_t", TypeRef.Of(BuiltinType.UChar));
    private static readonly TypeRef UInt32 = TypeRef.Typedef("uint32_t", TypeRef.Of(BuiltinType.UInt));
    private static readonly TypeRef Int = TypeRef.Of(BuiltinType.Int);
    private static readonly TypeRef Char = TypeRef.Of(BuiltinType.Char);

    public static MergedSnapshot Merged => AbiMerger.Merge(Create(Abis.Windows), Create(Abis.SysV));

    public static Snapshot Create(string abi)
    {
        var w = abi == Abis.Windows;
        var uint64 = TypeRef.Typedef("uint64_t", TypeRef.Of(w ? BuiltinType.ULongLong : BuiltinType.ULong));

        return new Snapshot
        {
            Abi = abi,
            Target = w ? "x86_64-w64-windows-gnu" : "x86_64-pc-linux-gnu",
            VlcCommit = "0123abcd",
            PointerSize = 8,
            Types =
            [
                Record("plane_t", RecordKind.Struct, 16, 8,
                    Field("p_pixels", TypeRef.PointerTo(UInt8), 0),
                    Field("i_lines", Int, 8),
                    Field("i_pitch", Int, 12)),
                Record("video_format_t::dovi", RecordKind.Struct, w ? 8 : 4, 4,
                    Field("version_major", UInt8, 0),
                    Field("version_minor", UInt8, 1),
                    Bits("profile", TypeRef.Of(BuiltinType.UInt), w ? 4 : 2, w ? 32 : 16, 7),
                    Bits("level", TypeRef.Of(BuiltinType.UInt), w ? 4 : 2, w ? 39 : 23, 6)) with { IsAnonymous = true, Parent = "video_format_t" },
                Record("video_format_t", RecordKind.Struct, w ? 16 : 12, 4,
                    Field("i_chroma", UInt32, 0),
                    Field("dovi", TypeRef.Record("video_format_t::dovi"), 4),
                    Field("i_cubemap_padding", UInt32, w ? 12 : 8)),
                Record("es_format_t::@0", RecordKind.Union, w ? 16 : 12, 4,
                    Field("video", TypeRef.Record("video_format_t"), 0),
                    Field("i_codec_data", TypeRef.ArrayOf(Char, 4), 0)) with { IsAnonymous = true, Parent = "es_format_t" },
                Record("es_format_t", RecordKind.Struct, w ? 36 : 32, 4,
                    Field("i_cat", Int, 0),
                    Field("i_array", TypeRef.ArrayOf(Int, 3), 4),
                    new FieldDecl { Name = "", Type = TypeRef.Record("es_format_t::@0"), Offset = 16, IsAnonymousMember = true },
                    Field("i_bitrate", Int, w ? 32 : 28)),
                Record("probe_value_t", RecordKind.Union, 8, 8,
                    Field("i", Int, 0),
                    Field("d", TypeRef.Of(BuiltinType.Double), 0),
                    Field("c", TypeRef.ArrayOf(Char, 3), 0)),
                Record("probe_long_t", RecordKind.Struct, w ? 16 : 24, 8,
                    Field("l", TypeRef.Of(BuiltinType.Long), 0),
                    Field("i", Int, w ? 4 : 8),
                    Field("u", uint64, w ? 8 : 16)),
                Record("probe_bits_t", RecordKind.Struct, 8, 4,
                    Bits("a", TypeRef.Of(BuiltinType.UInt), 0, 0, 3),
                    Bits("b", TypeRef.Of(BuiltinType.UInt), 0, 3, 5),
                    Field("c", Int, 4)),
                new TypeDecl { Name = "probe_opaque_t", Kind = RecordKind.Struct, Header = "probe_sample.h", IsOpaque = true },
            ],
            Enums =
            [
                new EnumDecl
                {
                    Name = "probe_color_e",
                    Header = "probe_sample.h",
                    UnderlyingType = Int,
                    Members = [new("PROBE_RED", 1), new("PROBE_GREEN", 2), new("PROBE_NEG", -2)],
                },
                new EnumDecl
                {
                    Name = "probe_flags_e",
                    Header = "probe_sample.h",
                    UnderlyingType = TypeRef.Of(w ? BuiltinType.ULongLong : BuiltinType.ULong),
                    Members = [new("PROBE_FLAG_ONE", 1), new("PROBE_FLAG_BIG", -1)],
                },
                new EnumDecl
                {
                    Name = "@PROBE_ANON_A",
                    Header = "probe_sample.h",
                    IsAnonymous = true,
                    UnderlyingType = TypeRef.Of(BuiltinType.UInt),
                    Members = [new("PROBE_ANON_A", 7), new("PROBE_ANON_B", 8)],
                },
            ],
            Constants =
            [
                Constant("PROBE_MAX", Int, 100, "100"),
                Constant("PROBE_NEGATIVE", Int, -5, "( - 5 )"),
                Constant("PROBE_MASK", TypeRef.Of(BuiltinType.UInt), 0xFFFFFFFFL, "0xFFFFFFFFu"),
                Constant("PROBE_TOP_BIT", TypeRef.Of(BuiltinType.ULongLong), long.MinValue, "0x8000000000000000ull"),
            ],
        };
    }

    private static ConstantDecl Constant(string name, TypeRef type, long value, string expression) => new()
    {
        Name = name,
        Header = "probe_sample.h",
        Kind = ConstantKind.Integer,
        Type = type,
        Integer = value,
        Expression = expression,
    };

    private static TypeDecl Record(string name, RecordKind kind, long size, long alignment, params FieldDecl[] fields) => new()
    {
        Name = name,
        Kind = kind,
        Header = "probe_sample.h",
        Size = size,
        Alignment = alignment,
        Fields = fields,
    };

    private static FieldDecl Field(string name, TypeRef type, long offset) => new() { Name = name, Type = type, Offset = offset };

    private static FieldDecl Bits(string name, TypeRef type, long byteOffset, long bitOffset, int width)
        => new() { Name = name, Type = type, Offset = byteOffset, BitOffset = bitOffset, BitWidth = width };

    /// <summary>The same IR with <paramref name="member"/> of <paramref name="type"/> moved to <paramref name="offset"/> on both ABIs.</summary>
    public static MergedSnapshot WithOffset(MergedSnapshot merged, string type, string member, long offset)
        => merged with
        {
            Types =
            [
                .. merged.Types.Select(t => t.Name != type ? t : t with
                {
                    Members = [.. t.Members.Select(m => m.Name != member ? m : m with { Offset = new([offset, offset]) })],
                }),
            ],
        };
}
