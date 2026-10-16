using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;

namespace VLCLR.Tests;

/// <summary>
/// Checks VLCLR's interop structs against VLC's native layout for the ABI the
/// tests run on. The table mirrors tests/AbiValidation/vlclr_abi.cpp, which
/// asserts the same numbers against the real VLC headers with the platform's
/// C compiler. Windows compilers lay out video_format_t 8 bytes larger than
/// GCC and Clang do, so every struct embedding it has two columns.
/// </summary>
public unsafe class VLCAbiLayoutTests
{
    public static TheoryData<string, int, int> Offsets => new()
    {
        { "plane_t.p_pixels",                         0,    0 },
        { "plane_t.i_lines",                          8,    8 },
        { "plane_t.i_pitch",                         12,   12 },
        { "plane_t.i_pixel_pitch",                   16,   16 },
        { "plane_t.i_visible_lines",                 20,   20 },
        { "plane_t.i_visible_pitch",                 24,   24 },

        { "video_format_t.i_chroma",                  0,    0 },
        { "video_format_t.i_visible_width",          20,   20 },
        { "video_format_t.p_palette",                48,   48 },
        { "video_format_t.orientation",              56,   56 },
        { "video_format_t.projection_mode",          88,   88 },
        { "video_format_t.pose",                     92,   92 },
        { "video_format_t.mastering",               108,  108 },
        { "video_format_t.lighting",                132,  132 },
        { "video_format_t.dovi",                    136,  136 },

        { "es_format_t.i_cat",                        0,    0 },
        { "es_format_t.psz_language",                24,   24 },
        { "es_format_t.p_extra_languages",           48,   48 },
        { "es_format_t.video",                       56,   56 },
        { "es_format_t.i_bitrate",                  208,  200 },
        { "es_format_t.i_profile",                    212,  204 },
        { "es_format_t.i_level",                      216,  208 },
        { "es_format_t.b_packetized",               220,  212 },
        { "es_format_t.i_extra",                    224,  216 },
        { "es_format_t.p_extra",                    232,  224 },

        { "vlc_filter_operations.filter_video",       0,    0 },
        { "vlc_filter_operations.flush",             16,   16 },
        { "vlc_filter_operations.close",             40,   40 },

        { "filter_owner_t.video",                     0,    0 },
        { "filter_owner_t.pf_get_attachments",        8,    8 },
        { "filter_owner_t.sys",                      16,   16 },

        { "filter_t.obj",                             0,    0 },
        { "filter_t.p_module",                       24,   24 },
        { "filter_t.p_sys",                          32,   32 },
        { "filter_t.fmt_in",                         40,   40 },
        { "filter_t.vctx_in",                       280,  272 },
        { "filter_t.fmt_out",                       288,  280 },
        { "filter_t.vctx_out",                      528,  512 },
        { "filter_t.b_allow_fmt_out_change",        536,  520 },
        { "filter_t.psz_name",                      544,  528 },
        { "filter_t.p_cfg",                         552,  536 },
        { "filter_t.ops",                           560,  544 },
        { "filter_t.owner",                         568,  552 },

        { "picture_t.format",                         0,    0 },
        { "picture_t.p",                            152,  144 },
        { "picture_t.p[1]",                           184,  176 },
        { "picture_t.p[2]",                           216,  208 },
        { "picture_t.i_planes",                     312,  304 },
        { "picture_t.date",                         320,  312 },
        { "picture_t.context",                      344,  336 },
        { "picture_t.p_sys",                        352,  344 },
        { "picture_t.p_next",                       360,  352 },
        { "picture_t.refs",                         368,  360 },

        { "subpicture_region_t.fmt",                  0,    0 },
        { "subpicture_region_t.p_picture",          152,  144 },
        { "subpicture_region_t.b_absolute",         160,  152 },
        { "subpicture_region_t.b_in_window",          161,  153 },
        { "subpicture_region_t.i_x",                164,  156 },
        { "subpicture_region_t.i_y",                  168,  160 },
        { "subpicture_region_t.i_align",              172,  164 },
        { "subpicture_region_t.i_alpha",              176,  168 },
        { "subpicture_region_t.p_text",             184,  176 },
        { "subpicture_region_t.text_flags",         192,  184 },
        { "subpicture_region_t.i_max_width",          196,  188 },
        { "subpicture_region_t.i_max_height",         200,  192 },
        { "subpicture_region_t.node",               208,  200 },
        { "subpicture_region_t.node.next",            216,  208 },

        { "vlc_chroma_description_t.fcc",               0,    0 },
        { "vlc_chroma_description_t.subtype",           4,    4 },
        { "vlc_chroma_description_t.plane_count",       8,    8 },
        { "vlc_chroma_description_t.p",                12,   12 },
        { "vlc_chroma_description_t.p[1].h",           36,   36 },
        { "vlc_chroma_description_t.pixel_size",       76,   76 },
        { "vlc_chroma_description_t.pixel_bits",       80,   80 },
        { "vlc_chroma_description_t.color_bits",       84,   84 },
    };

    [Theory]
    [MemberData(nameof(Offsets))]
    public void Offset_MatchesNativeLayout(string member, int windows, int unix)
    {
        Assert.Equal(OperatingSystem.IsWindows() ? windows : unix, MeasureOffset(member));
    }

    public static TheoryData<string, int, int> ContainerSizes => new()
    {
        { nameof(VLCEsFormat),         240, 232 },
        { nameof(VLCFilter),           592, 576 },
        { nameof(VLCPicture),          376, 368 },
        { nameof(VLCSubpictureRegion), 224, 216 },
    };

    /// <summary>
    /// Structs that embed video_format_t only ever live in VLC memory, so the
    /// managed struct may be larger than the native one but never smaller:
    /// a copy must still hold every field.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContainerSizes))]
    public void Container_CoversNativeSize(string type, int windows, int unix)
    {
        int managed = type switch
        {
            nameof(VLCEsFormat) => sizeof(VLCEsFormat),
            nameof(VLCFilter) => sizeof(VLCFilter),
            nameof(VLCPicture) => sizeof(VLCPicture),
            nameof(VLCSubpictureRegion) => sizeof(VLCSubpictureRegion),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        Assert.True(managed >= (OperatingSystem.IsWindows() ? windows : unix),
            $"{type} is {managed} bytes");
    }

    /// <summary>
    /// Plugins copy whole video formats (fmt_out.video = fmt_in.video), so
    /// VLCVideoFormat holds only the 144 bytes both ABIs share: a copy into
    /// VLC memory can never write past the native struct.
    /// </summary>
    [Fact]
    public void VideoFormat_IsThePrefixBothAbisShare()
    {
        Assert.Equal(144, sizeof(VLCVideoFormat));
    }

    [Fact]
    public void ChromaDescription_MatchesNativeSize()
    {
        Assert.Equal(88, sizeof(VLCChromaDescription));
    }

    private static int MeasureOffset(string member)
    {
        byte* native = (byte*)NativeMemory.AllocZeroed(1024);
        try
        {
            var plane = (VLCPlane*)native;
            var video = (VLCVideoFormat*)native;
            var es = (VLCEsFormat*)native;
            var ops = (VLCFilterOperations*)native;
            var owner = (VLCFilterOwner*)native;
            var filter = (VLCFilter*)native;
            var picture = (VLCPicture*)native;
            var region = (VLCSubpictureRegion*)native;
            var chroma = (VLCChromaDescription*)native;

            return member switch
            {
                "plane_t.p_pixels" => At(native, ref plane->Pixels),
                "plane_t.i_lines" => At(native, ref plane->Lines),
                "plane_t.i_pitch" => At(native, ref plane->Pitch),
                "plane_t.i_pixel_pitch" => At(native, ref plane->PixelPitch),
                "plane_t.i_visible_lines" => At(native, ref plane->VisibleLines),
                "plane_t.i_visible_pitch" => At(native, ref plane->VisiblePitch),

                "video_format_t.i_chroma" => At(native, ref video->Chroma),
                "video_format_t.i_visible_width" => At(native, ref video->VisibleWidth),
                "video_format_t.p_palette" => At(native, ref video->Palette),
                "video_format_t.orientation" => At(native, ref video->Orientation),
                "video_format_t.projection_mode" => At(native, ref video->ProjectionMode),
                "video_format_t.pose" => At(native, ref video->PoseYaw),
                "video_format_t.mastering" => At(native, ref video->MasteringPrimariesGX),
                "video_format_t.lighting" => At(native, ref video->LightingMaxCLL),
                "video_format_t.dovi" => At(native, ref video->DoviVersionMajor),

                "es_format_t.i_cat" => At(native, ref es->Category),
                "es_format_t.psz_language" => At(native, ref es->Language),
                "es_format_t.p_extra_languages" => At(native, ref es->ExtraLanguages),
                "es_format_t.video" => At(native, ref es->Video),
                "es_format_t.i_bitrate" => At(native, ref es->Bitrate),
                "es_format_t.i_profile" => At(native, ref es->Profile),
                "es_format_t.i_level" => At(native, ref es->Level),
                "es_format_t.b_packetized" => At(native, ref es->Packetized),
                "es_format_t.i_extra" => At(native, ref es->ExtraSize),
                "es_format_t.p_extra" => At(native, ref es->Extra),

                "vlc_filter_operations.filter_video" => At(native, ref ops->FilterVideo),
                "vlc_filter_operations.flush" => At(native, ref ops->Flush),
                "vlc_filter_operations.close" => At(native, ref ops->Close),

                "filter_owner_t.video" => At(native, ref owner->Callbacks),
                "filter_owner_t.pf_get_attachments" => At(native, ref owner->GetAttachments),
                "filter_owner_t.sys" => At(native, ref owner->Sys),

                "filter_t.obj" => At(native, ref filter->Obj),
                "filter_t.p_module" => At(native, ref filter->Module),
                "filter_t.p_sys" => At(native, ref filter->Sys),
                "filter_t.fmt_in" => At(native, ref filter->FormatIn),
                "filter_t.vctx_in" => At(native, ref filter->VideoContextIn),
                "filter_t.fmt_out" => At(native, ref filter->FormatOut),
                "filter_t.vctx_out" => At(native, ref filter->VideoContextOut),
                "filter_t.b_allow_fmt_out_change" => At(native, ref filter->AllowFormatOutChange),
                "filter_t.psz_name" => At(native, ref filter->Name),
                "filter_t.p_cfg" => At(native, ref filter->Config),
                "filter_t.ops" => At(native, ref filter->Operations),
                "filter_t.owner" => At(native, ref filter->Owner),

                "picture_t.format" => At(native, ref picture->Format),
                "picture_t.p" => At(native, ref picture->Plane0),
                "picture_t.p[1]" => At(native, ref picture->Plane1),
                "picture_t.p[2]" => At(native, ref picture->Plane2),
                "picture_t.i_planes" => At(native, ref picture->PlaneCount),
                "picture_t.date" => At(native, ref picture->Date),
                "picture_t.context" => At(native, ref picture->Context),
                "picture_t.p_sys" => At(native, ref picture->Sys),
                "picture_t.p_next" => At(native, ref picture->Next),
                "picture_t.refs" => At(native, ref picture->Refs),

                "subpicture_region_t.fmt" => At(native, ref region->Format),
                "subpicture_region_t.p_picture" => At(native, ref region->Picture),
                "subpicture_region_t.b_absolute" => At(native, ref region->IsAbsolute),
                "subpicture_region_t.b_in_window" => At(native, ref region->IsInWindow),
                "subpicture_region_t.i_x" => At(native, ref region->X),
                "subpicture_region_t.i_y" => At(native, ref region->Y),
                "subpicture_region_t.i_align" => At(native, ref region->Align),
                "subpicture_region_t.i_alpha" => At(native, ref region->Alpha),
                "subpicture_region_t.p_text" => At(native, ref region->Text),
                "subpicture_region_t.text_flags" => At(native, ref region->TextFlags),
                "subpicture_region_t.i_max_width" => At(native, ref region->MaxWidth),
                "subpicture_region_t.i_max_height" => At(native, ref region->MaxHeight),
                "subpicture_region_t.node" => At(native, ref region->NodePrev),
                "subpicture_region_t.node.next" => At(native, ref region->NodeNext),

                "vlc_chroma_description_t.fcc" => At(native, ref chroma->Fourcc),
                "vlc_chroma_description_t.subtype" => At(native, ref chroma->Subtype),
                "vlc_chroma_description_t.plane_count" => At(native, ref chroma->PlaneCount),
                "vlc_chroma_description_t.p" => At(native, ref chroma->Plane0),
                "vlc_chroma_description_t.p[1].h" => At(native, ref chroma->Plane1.Height),
                "vlc_chroma_description_t.pixel_size" => At(native, ref chroma->PixelSize),
                "vlc_chroma_description_t.pixel_bits" => At(native, ref chroma->PixelBits),
                "vlc_chroma_description_t.color_bits" => At(native, ref chroma->ColorBits),

                _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
            };
        }
        finally
        {
            NativeMemory.Free(native);
        }
    }

    private static int At<T>(byte* owner, ref T member)
        => (int)((byte*)Unsafe.AsPointer(ref member) - owner);
}
