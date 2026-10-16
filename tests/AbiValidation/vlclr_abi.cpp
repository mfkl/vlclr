#include <cstddef>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <sys/types.h>
#ifdef _MSC_VER
#include <BaseTsd.h>

typedef SSIZE_T ssize_t;
#endif
#define restrict __restrict

#include <vlc_common.h>
#include <vlc_es.h>
#include <vlc_filter.h>
#include <vlc_picture.h>
#include <vlc_subpicture.h>
#include <vlc_text_style.h>
#include <vlc_configuration.h>

static_assert(sizeof(void *) == 8, "VLCLR currently targets the 64-bit VLC ABI");

#define VLCLR_SIZE(type, expected) \
    static_assert(sizeof(type) == expected, "VLCLR size mismatch: " #type)
#define VLCLR_OFFSET(type, field, expected) \
    static_assert(offsetof(type, field) == expected, "VLCLR offset mismatch: " #type "." #field)

#ifdef _WIN32
#define VLCLR_ABI(windows, unix) windows
#else
#define VLCLR_ABI(windows, unix) unix
#endif
#define VLCLR_SIZE_ABI(type, windows, unix) VLCLR_SIZE(type, VLCLR_ABI(windows, unix))
#define VLCLR_OFFSET_ABI(type, field, windows, unix) \
    VLCLR_OFFSET(type, field, VLCLR_ABI(windows, unix))

VLCLR_SIZE(vlc_object_t, 24);

VLCLR_SIZE(plane_t, 32);
VLCLR_OFFSET(plane_t, p_pixels, 0);
VLCLR_OFFSET(plane_t, i_lines, 8);
VLCLR_OFFSET(plane_t, i_pitch, 12);
VLCLR_OFFSET(plane_t, i_pixel_pitch, 16);
VLCLR_OFFSET(plane_t, i_visible_lines, 20);
VLCLR_OFFSET(plane_t, i_visible_pitch, 24);

VLCLR_SIZE_ABI(video_format_t, 152, 144);
VLCLR_OFFSET(video_format_t, i_chroma, 0);
VLCLR_OFFSET(video_format_t, i_visible_width, 20);
VLCLR_OFFSET(video_format_t, p_palette, 48);
VLCLR_OFFSET(video_format_t, orientation, 56);
VLCLR_OFFSET(video_format_t, projection_mode, 88);
VLCLR_OFFSET(video_format_t, pose, 92);
VLCLR_OFFSET(video_format_t, mastering, 108);
VLCLR_OFFSET(video_format_t, lighting, 132);
VLCLR_OFFSET(video_format_t, dovi, 136);
VLCLR_OFFSET_ABI(video_format_t, i_cubemap_padding, 144, 140);

VLCLR_SIZE_ABI(es_format_t, 240, 232);
VLCLR_OFFSET(es_format_t, i_cat, 0);
VLCLR_OFFSET(es_format_t, psz_language, 24);
VLCLR_OFFSET(es_format_t, p_extra_languages, 48);
VLCLR_OFFSET(es_format_t, video, 56);
VLCLR_OFFSET_ABI(es_format_t, i_bitrate, 208, 200);
VLCLR_OFFSET_ABI(es_format_t, i_profile, 212, 204);
VLCLR_OFFSET_ABI(es_format_t, i_level, 216, 208);
VLCLR_OFFSET_ABI(es_format_t, b_packetized, 220, 212);
VLCLR_OFFSET_ABI(es_format_t, i_extra, 224, 216);
VLCLR_OFFSET_ABI(es_format_t, p_extra, 232, 224);

VLCLR_SIZE(vlc_filter_operations, 48);
VLCLR_OFFSET(vlc_filter_operations, filter_video, 0);
VLCLR_OFFSET(vlc_filter_operations, flush, 16);
VLCLR_OFFSET(vlc_filter_operations, close, 40);

VLCLR_SIZE(filter_owner_t, 24);
VLCLR_OFFSET(filter_owner_t, video, 0);
VLCLR_OFFSET(filter_owner_t, pf_get_attachments, 8);
VLCLR_OFFSET(filter_owner_t, sys, 16);

VLCLR_SIZE_ABI(filter_t, 592, 576);
VLCLR_OFFSET(filter_t, obj, 0);
VLCLR_OFFSET(filter_t, p_module, 24);
VLCLR_OFFSET(filter_t, p_sys, 32);
VLCLR_OFFSET(filter_t, fmt_in, 40);
VLCLR_OFFSET_ABI(filter_t, vctx_in, 280, 272);
VLCLR_OFFSET_ABI(filter_t, fmt_out, 288, 280);
VLCLR_OFFSET_ABI(filter_t, vctx_out, 528, 512);
VLCLR_OFFSET_ABI(filter_t, b_allow_fmt_out_change, 536, 520);
VLCLR_OFFSET_ABI(filter_t, psz_name, 544, 528);
VLCLR_OFFSET_ABI(filter_t, p_cfg, 552, 536);
VLCLR_OFFSET_ABI(filter_t, ops, 560, 544);
VLCLR_OFFSET_ABI(filter_t, owner, 568, 552);

VLCLR_SIZE_ABI(picture_t, 376, 368);
VLCLR_OFFSET(picture_t, format, 0);
VLCLR_OFFSET_ABI(picture_t, p, 152, 144);
VLCLR_OFFSET_ABI(picture_t, p[1], 184, 176);
VLCLR_OFFSET_ABI(picture_t, p[2], 216, 208);
VLCLR_OFFSET_ABI(picture_t, i_planes, 312, 304);
VLCLR_OFFSET_ABI(picture_t, date, 320, 312);
VLCLR_OFFSET_ABI(picture_t, context, 344, 336);
VLCLR_OFFSET_ABI(picture_t, p_sys, 352, 344);
VLCLR_OFFSET_ABI(picture_t, p_next, 360, 352);
VLCLR_OFFSET_ABI(picture_t, refs, 368, 360);

VLCLR_SIZE_ABI(subpicture_region_t, 224, 216);
VLCLR_OFFSET(subpicture_region_t, fmt, 0);
VLCLR_OFFSET_ABI(subpicture_region_t, p_picture, 152, 144);
VLCLR_OFFSET_ABI(subpicture_region_t, b_absolute, 160, 152);
VLCLR_OFFSET_ABI(subpicture_region_t, b_in_window, 161, 153);
VLCLR_OFFSET_ABI(subpicture_region_t, i_x, 164, 156);
VLCLR_OFFSET_ABI(subpicture_region_t, i_y, 168, 160);
VLCLR_OFFSET_ABI(subpicture_region_t, i_align, 172, 164);
VLCLR_OFFSET_ABI(subpicture_region_t, i_alpha, 176, 168);
VLCLR_OFFSET_ABI(subpicture_region_t, p_text, 184, 176);
VLCLR_OFFSET_ABI(subpicture_region_t, text_flags, 192, 184);
VLCLR_OFFSET_ABI(subpicture_region_t, i_max_width, 196, 188);
VLCLR_OFFSET_ABI(subpicture_region_t, i_max_height, 200, 192);
VLCLR_OFFSET_ABI(subpicture_region_t, node, 208, 200);
VLCLR_OFFSET_ABI(subpicture_region_t, node.next, 216, 208);

VLCLR_SIZE(subpicture_t, 96);
VLCLR_OFFSET(subpicture_t, i_channel, 0);
VLCLR_OFFSET(subpicture_t, i_order, 8);
VLCLR_OFFSET(subpicture_t, regions, 24);
VLCLR_OFFSET(subpicture_t, i_start, 40);
VLCLR_OFFSET(subpicture_t, updater, 72);
VLCLR_OFFSET(subpicture_t, p_private, 88);

VLCLR_SIZE(text_style_t, 80);
VLCLR_OFFSET(text_style_t, psz_fontname, 0);
VLCLR_OFFSET(text_style_t, i_features, 16);
VLCLR_OFFSET(text_style_t, f_font_relsize, 20);
VLCLR_OFFSET(text_style_t, i_font_alpha, 32);
VLCLR_OFFSET(text_style_t, i_spacing, 36);
VLCLR_OFFSET(text_style_t, i_outline_color, 40);
VLCLR_OFFSET(text_style_t, i_shadow_color, 52);
VLCLR_OFFSET(text_style_t, i_background_color, 64);
VLCLR_OFFSET(text_style_t, e_wrapinfo, 72);

VLCLR_SIZE(text_segment_t, 32);
VLCLR_OFFSET(text_segment_t, psz_text, 0);
VLCLR_OFFSET(text_segment_t, style, 8);
VLCLR_OFFSET(text_segment_t, p_next, 16);
VLCLR_OFFSET(text_segment_t, p_ruby, 24);

VLCLR_SIZE(text_segment_ruby_t, 24);

VLCLR_SIZE(module_value_t, 8);
VLCLR_OFFSET(module_config_t, i_type, 0);
VLCLR_OFFSET(module_config_t, psz_name, 16);

int main()
{
    return 0;
}
