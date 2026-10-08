// VLC 3.x ABI layout probe for VLCLR.
//
// This compile-only probe verifies that the managed layout constants in
// src/VLCLR/Native/VLCNativeLayout.cs match the pinned VLC 3.0.x headers.
// Any size or offset mismatch causes a compile-time static_assert failure.
//
// The probe targets the VLC 3.0.24-beta1 plugin headers under
// vlc-3.0.24-beta1/sdk/include/vlc/plugins/.
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <string_view>
#include <sys/types.h>
#if defined(_WIN32)
#include <BaseTsd.h>
typedef SSIZE_T ssize_t;
struct pollfd;
extern "C" int poll(struct pollfd *, unsigned, int);
#else
#include <unistd.h>
#endif
#define restrict __restrict

#include <vlc_common.h>
#include <vlc_es.h>
#include <vlc_filter.h>
#include <vlc_picture.h>
#include <vlc_plugin.h>
#include <vlc_subpicture.h>
#include <vlc_text_style.h>

static_assert(sizeof(void *) == 8, "VLCLR currently targets the 64-bit VLC ABI");

#define VLCLR_SIZE(type, expected) \
    static_assert(sizeof(type) == expected, "VLCLR size mismatch: " #type)
#define VLCLR_OFFSET(type, field, expected) \
    static_assert(offsetof(type, field) == expected, "VLCLR offset mismatch: " #type "." #field)

// --- Module ABI constants ---
// VLC 3 defines MODULE_SYMBOL 3_0_0f and MODULE_SUFFIX "__3_0_0f" in vlc_plugin.h.
#define VLCLR_STRINGIFY_IMPL(value) #value
#define VLCLR_STRINGIFY(value) VLCLR_STRINGIFY_IMPL(value)
static_assert(std::string_view(VLCLR_STRINGIFY(MODULE_SYMBOL)) == "3_0_0f",
              "MODULE_SYMBOL must be 3_0_0f for the VLC 3.x plugin ABI");
static_assert(std::string_view(MODULE_SUFFIX) == "__3_0_0f",
              "MODULE_SUFFIX must be __3_0_0f for the VLC 3.x plugin ABI");

// vlc_object_t is just VLC_COMMON_MEMBERS (struct vlc_common_members).
// Layout: object_type(8) + header(8) + flags(4) + force(1) + pad(3) + libvlc(8) + parent(8) = 40
VLCLR_SIZE(vlc_object_t, 40);
VLCLR_OFFSET(vlc_object_t, obj.object_type, 0);
VLCLR_OFFSET(vlc_object_t, obj.header, 8);
VLCLR_OFFSET(vlc_object_t, obj.flags, 16);
VLCLR_OFFSET(vlc_object_t, obj.force, 20);
VLCLR_OFFSET(vlc_object_t, obj.libvlc, 24);
VLCLR_OFFSET(vlc_object_t, obj.parent, 32);

// plane_t: p_pixels(8) + i_lines(4) + i_pitch(4) + i_pixel_pitch(4) + i_visible_lines(4) + i_visible_pitch(4) = 28
// With alignment/padding: 32 bytes
VLCLR_SIZE(plane_t, 32);
VLCLR_OFFSET(plane_t, p_pixels, 0);
VLCLR_OFFSET(plane_t, i_lines, 8);
VLCLR_OFFSET(plane_t, i_pitch, 12);
VLCLR_OFFSET(plane_t, i_pixel_pitch, 16);
VLCLR_OFFSET(plane_t, i_visible_lines, 20);
VLCLR_OFFSET(plane_t, i_visible_pitch, 24);

// --- video_format_t ---
// VLC 3 layout (no dovi field, has i_bits_per_pixel, RGB masks/shifts, b_color_range_full):
// i_chroma(4) + i_width(4) + i_height(4) + i_x_offset(4) + i_y_offset(4) +
// i_visible_width(4) + i_visible_height(4) + i_bits_per_pixel(4) + i_sar_num(4) + i_sar_den(4) +
// i_frame_rate(4) + i_frame_rate_base(4) + i_rmask(4) + i_gmask(4) + i_bmask(4) +
// i_rrshift(4) + i_lrshift(4) + i_rgshift(4) + i_lgshift(4) + i_rbshift(4) + i_lbshift(4) +
// p_palette(8) + orientation(4) + primaries(4) + transfer(4) + space(4) + b_color_range_full(1) + pad(3) +
// chroma_location(4) + multiview_mode(4) + projection_mode(4) + pose(12) +
// mastering(24) + lighting(4) + i_cubemap_padding(4)
// Total: 176 bytes
VLCLR_SIZE(video_format_t, 176);
VLCLR_OFFSET(video_format_t, i_chroma, 0);
VLCLR_OFFSET(video_format_t, i_width, 4);
VLCLR_OFFSET(video_format_t, i_visible_width, 20);
VLCLR_OFFSET(video_format_t, i_bits_per_pixel, 28);
VLCLR_OFFSET(video_format_t, i_sar_num, 32);
VLCLR_OFFSET(video_format_t, i_rmask, 48);
VLCLR_OFFSET(video_format_t, i_rrshift, 60);
VLCLR_OFFSET(video_format_t, p_palette, 88);
VLCLR_OFFSET(video_format_t, orientation, 96);
VLCLR_OFFSET(video_format_t, primaries, 100);
VLCLR_OFFSET(video_format_t, transfer, 104);
VLCLR_OFFSET(video_format_t, space, 108);
VLCLR_OFFSET(video_format_t, b_color_range_full, 112);
VLCLR_OFFSET(video_format_t, chroma_location, 116);
VLCLR_OFFSET(video_format_t, multiview_mode, 120);
VLCLR_OFFSET(video_format_t, projection_mode, 124);
VLCLR_OFFSET(video_format_t, pose, 128);
VLCLR_OFFSET(video_format_t, mastering, 144);
VLCLR_OFFSET(video_format_t, lighting, 168);
VLCLR_OFFSET(video_format_t, i_cubemap_padding, 172);

// --- es_format_t ---
// VLC 3 layout (no video_format_t dovi shift):
// i_cat(4) + i_codec(4) + i_original_fourcc(4) + i_id(4) + i_group(4) + i_priority(4) +
// psz_language(8) + psz_description(8) + i_extra_languages(4) + pad(4) + p_extra_languages(8) +
// union: max(audio,audio_replay_gain,video,subs) + i_bitrate(4) + i_profile(4) + i_level(4) +
// b_packetized(1) + pad(3) + i_extra(4) + p_extra(8)
// video union member: video_format_t = 176 bytes
// Total with video: 56(audio part) + 176(video) + tail fields
// Actual size computed by the compiler - let us check what MSVC produces
// The ABI probe will tell us the real size
#if defined(_WIN32)
// Windows x64 MSVC layout - validated against VLC 3.0.24-beta1 SDK
VLCLR_SIZE(es_format_t, 264);
#else
// Linux x64 GCC layout - same struct, no dovi difference
VLCLR_SIZE(es_format_t, 264);
#endif

VLCLR_OFFSET(es_format_t, i_cat, 0);
VLCLR_OFFSET(es_format_t, i_codec, 4);
VLCLR_OFFSET(es_format_t, i_original_fourcc, 8);
VLCLR_OFFSET(es_format_t, i_id, 12);
VLCLR_OFFSET(es_format_t, i_group, 16);
VLCLR_OFFSET(es_format_t, i_priority, 20);
VLCLR_OFFSET(es_format_t, psz_language, 24);
VLCLR_OFFSET(es_format_t, psz_description, 32);
VLCLR_OFFSET(es_format_t, i_extra_languages, 40);
VLCLR_OFFSET(es_format_t, p_extra_languages, 48);
// Union starts at offset 56
VLCLR_OFFSET(es_format_t, video, 56);
// After union (56 + sizeof(video_format_t)=176 = 232)
VLCLR_OFFSET(es_format_t, i_bitrate, 232);
VLCLR_OFFSET(es_format_t, i_profile, 236);
VLCLR_OFFSET(es_format_t, i_level, 240);
VLCLR_OFFSET(es_format_t, b_packetized, 244);
VLCLR_OFFSET(es_format_t, i_extra, 248);
VLCLR_OFFSET(es_format_t, p_extra, 256);

// --- filter_owner_t ---
// VLC 3: sys(8) + union{ video.buffer_new(8) } = 16 bytes
VLCLR_SIZE(filter_owner_t, 16);
VLCLR_OFFSET(filter_owner_t, sys, 0);
VLCLR_OFFSET(filter_owner_t, video.buffer_new, 8);

// --- filter_t ---
// VLC 3 has NO ops pointer; callbacks are inline unions.
// Layout: obj(40) + p_module(8) + p_sys(8) + fmt_in(es_format_t) +
//         fmt_out(es_format_t) + b_allow_fmt_out_change(1) + pad +
//         psz_name(8) + p_cfg(8) + union{pf_video_filter}(8) +
//         union{pf_audio_drain}(8) + pf_flush(8) + pf_change_viewpoint(8) +
//         union{pf_video_mouse}(8) + pf_get_attachments(8) + owner(filter_owner_t)
VLCLR_SIZE(filter_t, 672);

VLCLR_OFFSET(filter_t, obj, 0);
VLCLR_OFFSET(filter_t, p_module, 40);
VLCLR_OFFSET(filter_t, p_sys, 48);
VLCLR_OFFSET(filter_t, fmt_in, 56);
VLCLR_OFFSET(filter_t, fmt_out, 320);
VLCLR_OFFSET(filter_t, b_allow_fmt_out_change, 584);
VLCLR_OFFSET(filter_t, psz_name, 592);
VLCLR_OFFSET(filter_t, p_cfg, 600);
VLCLR_OFFSET(filter_t, pf_video_filter, 608);
VLCLR_OFFSET(filter_t, pf_audio_drain, 616);
VLCLR_OFFSET(filter_t, pf_flush, 624);
VLCLR_OFFSET(filter_t, pf_change_viewpoint, 632);
VLCLR_OFFSET(filter_t, pf_video_mouse, 640);
VLCLR_OFFSET(filter_t, pf_get_attachments, 648);
VLCLR_OFFSET(filter_t, owner, 656);

// --- picture_t ---
// VLC 3: format(video_frame_format_t = video_format_t = 176) + p[5](160) + i_planes(4) + pad(4) +
//        date(8) + b_force(1) + b_progressive(1) + b_top_field_first(1) + pad(1) +
//        i_nb_fields(4) + context(8) + p_sys(8) + p_next(8)
// Total: 384 bytes
VLCLR_SIZE(picture_t, 384);
VLCLR_OFFSET(picture_t, format, 0);
VLCLR_OFFSET(picture_t, p, 176);
VLCLR_OFFSET(picture_t, i_planes, 336);
VLCLR_OFFSET(picture_t, date, 344);
VLCLR_OFFSET(picture_t, b_force, 352);
VLCLR_OFFSET(picture_t, b_progressive, 353);
VLCLR_OFFSET(picture_t, b_top_field_first, 354);
VLCLR_OFFSET(picture_t, i_nb_fields, 356);
VLCLR_OFFSET(picture_t, context, 360);
VLCLR_OFFSET(picture_t, p_sys, 368);
VLCLR_OFFSET(picture_t, p_next, 376);

// --- subpicture_region_t ---
// VLC 3: fmt(176) + p_picture(8) + i_x(4) + i_y(4) + i_align(4) + i_alpha(4) +
//        p_text(8) + i_text_align(4) + b_noregionbg(1) + b_gridmode(1) + b_balanced_text(1) + pad(1) +
//        i_max_width(4) + i_max_height(4) + p_next(8) + p_private(8)
VLCLR_SIZE(subpicture_region_t, 240);
VLCLR_OFFSET(subpicture_region_t, fmt, 0);
VLCLR_OFFSET(subpicture_region_t, p_picture, 176);
VLCLR_OFFSET(subpicture_region_t, i_x, 184);
VLCLR_OFFSET(subpicture_region_t, i_y, 188);
VLCLR_OFFSET(subpicture_region_t, i_align, 192);
VLCLR_OFFSET(subpicture_region_t, i_alpha, 196);
VLCLR_OFFSET(subpicture_region_t, p_text, 200);
VLCLR_OFFSET(subpicture_region_t, i_text_align, 208);
VLCLR_OFFSET(subpicture_region_t, b_noregionbg, 212);
VLCLR_OFFSET(subpicture_region_t, b_gridmode, 213);
VLCLR_OFFSET(subpicture_region_t, b_balanced_text, 214);
VLCLR_OFFSET(subpicture_region_t, i_max_width, 216);
VLCLR_OFFSET(subpicture_region_t, i_max_height, 220);
VLCLR_OFFSET(subpicture_region_t, p_next, 224);
VLCLR_OFFSET(subpicture_region_t, p_private, 232);

// --- subpicture_t ---
// VLC 3: i_channel(4) + pad(4) + i_order(8) + p_next(8) + p_region(8) +
//        i_start(8) + i_stop(8) + b_ephemer(1) + b_fade(1) + b_subtitle(1) + b_absolute(1) +
//        i_original_picture_width(4) + i_original_picture_height(4) + i_alpha(4) +
//        updater(sizeof(subpicture_updater_t)) + p_private(8)
VLCLR_SIZE(subpicture_t, 104);
VLCLR_OFFSET(subpicture_t, i_channel, 0);
VLCLR_OFFSET(subpicture_t, i_order, 8);
VLCLR_OFFSET(subpicture_t, p_next, 16);
VLCLR_OFFSET(subpicture_t, p_region, 24);
VLCLR_OFFSET(subpicture_t, i_start, 32);
VLCLR_OFFSET(subpicture_t, i_stop, 40);
VLCLR_OFFSET(subpicture_t, b_ephemer, 48);
VLCLR_OFFSET(subpicture_t, b_fade, 49);
VLCLR_OFFSET(subpicture_t, b_subtitle, 50);
VLCLR_OFFSET(subpicture_t, b_absolute, 51);
VLCLR_OFFSET(subpicture_t, i_original_picture_width, 52);
VLCLR_OFFSET(subpicture_t, i_original_picture_height, 56);
VLCLR_OFFSET(subpicture_t, i_alpha, 60);
VLCLR_OFFSET(subpicture_t, updater, 64);
VLCLR_OFFSET(subpicture_t, p_private, 96);

// --- text_style_t ---
// VLC 3 has karaoke background color/alpha fields.
// psz_fontname(8) + psz_monofontname(8) + i_features(2) + i_style_flags(2) +
// f_font_relsize(4) + i_font_size(4) + i_font_color(4) + i_font_alpha(1) + pad(3) +
// i_spacing(4) + i_outline_color(4) + i_outline_alpha(1) + pad(3) + i_outline_width(4) +
// i_shadow_color(4) + i_shadow_alpha(1) + pad(3) + i_shadow_width(4) +
// i_background_color(4) + i_background_alpha(1) + pad(3) +
// i_karaoke_background_color(4) + i_karaoke_background_alpha(1) + pad(3) +
// e_wrapinfo(4)
// Total: 88 bytes
VLCLR_SIZE(text_style_t, 88);
VLCLR_OFFSET(text_style_t, psz_fontname, 0);
VLCLR_OFFSET(text_style_t, psz_monofontname, 8);
VLCLR_OFFSET(text_style_t, i_features, 16);
VLCLR_OFFSET(text_style_t, i_style_flags, 18);
VLCLR_OFFSET(text_style_t, f_font_relsize, 20);
VLCLR_OFFSET(text_style_t, i_font_size, 24);
VLCLR_OFFSET(text_style_t, i_font_color, 28);
VLCLR_OFFSET(text_style_t, i_font_alpha, 32);
VLCLR_OFFSET(text_style_t, i_spacing, 36);
VLCLR_OFFSET(text_style_t, i_outline_color, 40);
VLCLR_OFFSET(text_style_t, i_outline_alpha, 44);
VLCLR_OFFSET(text_style_t, i_outline_width, 48);
VLCLR_OFFSET(text_style_t, i_shadow_color, 52);
VLCLR_OFFSET(text_style_t, i_shadow_alpha, 56);
VLCLR_OFFSET(text_style_t, i_shadow_width, 60);
VLCLR_OFFSET(text_style_t, i_background_color, 64);
VLCLR_OFFSET(text_style_t, i_background_alpha, 68);
VLCLR_OFFSET(text_style_t, i_karaoke_background_color, 72);
VLCLR_OFFSET(text_style_t, i_karaoke_background_alpha, 76);
VLCLR_OFFSET(text_style_t, e_wrapinfo, 80);

// --- text_segment_t ---
// VLC 3: psz_text(8) + style(8) + p_next(8) = 24 bytes
// No Ruby fields (VLC 4 only)
VLCLR_SIZE(text_segment_t, 24);
VLCLR_OFFSET(text_segment_t, psz_text, 0);
VLCLR_OFFSET(text_segment_t, style, 8);
VLCLR_OFFSET(text_segment_t, p_next, 16);

int main()
{
    printf("VLC 3.x ABI probe passed.\n");
    printf("  vlc_object_t:            %zu bytes\n", sizeof(vlc_object_t));
    printf("  plane_t:                 %zu bytes\n", sizeof(plane_t));
    printf("  video_format_t:          %zu bytes\n", sizeof(video_format_t));
    printf("  es_format_t:             %zu bytes\n", sizeof(es_format_t));
    printf("  filter_owner_t:          %zu bytes\n", sizeof(filter_owner_t));
    printf("  filter_t:                %zu bytes\n", sizeof(filter_t));
    printf("  picture_t:               %zu bytes\n", sizeof(picture_t));
    printf("  subpicture_region_t:     %zu bytes\n", sizeof(subpicture_region_t));
    printf("  subpicture_t:            %zu bytes\n", sizeof(subpicture_t));
    printf("  text_style_t:            %zu bytes\n", sizeof(text_style_t));
    printf("  text_segment_t:          %zu bytes\n", sizeof(text_segment_t));
    return 0;
}
