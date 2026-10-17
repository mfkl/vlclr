/*
 * Test-only C library for the VLCLR inline-parity tests. It is compiled at
 * test time (never shipped, never committed as a binary) against the pinned VLC
 * headers and does two jobs:
 *
 *  1. It exports one plain wrapper per VLC `static inline` function or
 *     function-like macro that VLCLR reimplements in C#, under a `parity_`
 *     prefix. The wrapper runs the real C original from the VLC header.
 *
 *  2. It defines stand-ins for the libvlccore exports those inlines call
 *     (var_SetChecked, picture_Destroy, vlc_object_Log, ...). A stand-in
 *     records its arguments in `parity_log` and returns the results scripted in
 *     `parity_script`. The managed side is pointed at this same library as
 *     "libvlccore", so the C# code and the C wrapper hit the same stand-ins.
 */

/* Portability prelude, same as the generated ABI probe (tests/AbiValidation). */
#ifdef _WIN32
typedef long long ssize_t;
#define _OFF_T_DEFINED
typedef long long _off_t;
typedef long long off_t;
#endif

#include <stddef.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#ifdef _MSC_VER
#include <BaseTsd.h>
typedef SSIZE_T ssize_t;
#endif
#ifndef restrict
#define restrict __restrict
#endif

#include <vlc_common.h>
#include <vlc_config.h>
#include <vlc_tick.h>
#include <vlc_threads.h>
#include <vlc_objects.h>
#include <vlc_messages.h>
#include <vlc_variables.h>
#include <vlc_es.h>
#include <vlc_fourcc.h>
#include <vlc_frame.h>
#include <vlc_picture.h>
#include <vlc_filter.h>
#include <vlc_interface.h>
#include <vlc_playlist.h>
#include <vlc_player.h>

#define PARITY_API VLC_EXPORT

const char vlc_module_name[] = "parity";

/* ------------------------------------------------------------------------ *
 * Recorded calls and scripted results, shared with the C# tests.
 * Keep this layout in sync with tests/InlineParity/Native/ParityStructs.cs.
 * ------------------------------------------------------------------------ */

#define NAME_MAX_LEN 128
#define STR_MAX_LEN 512

typedef struct parity_log
{
    int32_t set_calls;
    int32_t set_type;
    int64_t set_int;                 /* raw 8 bytes of the vlc_value_t passed */
    int32_t set_str_is_null;
    char set_name[NAME_MAX_LEN];
    char set_str[STR_MAX_LEN];       /* copy of the string when type is STRING */
    void *set_obj;

    int32_t get_calls;
    int32_t get_type;
    char get_name[NAME_MAX_LEN];
    void *get_obj;

    int32_t inherit_calls;
    int32_t inherit_type;
    char inherit_name[NAME_MAX_LEN];
    void *inherit_obj;

    int32_t create_calls;
    int32_t create_type;
    char create_name[NAME_MAX_LEN];
    void *create_obj;

    int32_t destroy_calls;           /* picture_Destroy */
    void *destroy_arg;

    int32_t newfmt_calls;            /* picture_NewFromFormat */
    const void *newfmt_arg;

    int32_t log_calls;               /* vlc_object_Log */
    int32_t log_type;
    void *log_obj;
    char log_message[STR_MAX_LEN];

    int32_t buffer_new_calls;        /* fake owner.video->buffer_new */
    void *buffer_new_arg;

    int32_t caps_calls;              /* vlc_player_GetCapabilities */
    void *caps_arg;
    int32_t lock_calls;
    int32_t unlock_calls;

    int32_t seek_time_calls;         /* vlc_player_SeekByTime */
    int64_t seek_time;
    int32_t seek_speed;
    int32_t seek_whence;

    int32_t parent_calls;            /* vlc_object_parent */
    int32_t typename_calls;          /* vlc_object_typename */
} parity_log;

typedef struct parity_script
{
    int32_t set_result;
    int32_t get_result;
    int64_t get_value;               /* raw 8 bytes handed back as vlc_value_t */
    int32_t get_string_is_null;
    char get_string[STR_MAX_LEN];    /* strdup'ed to the caller when type is STRING */
    int32_t inherit_result;
    int64_t inherit_value;
    int32_t inherit_string_is_null;
    char inherit_string[STR_MAX_LEN];
    int32_t create_result;
    int32_t pnff_null;               /* picture_NewFromFormat returns NULL */
    int32_t capabilities;
    int64_t player_time;
    int64_t player_length;
} parity_script;

static parity_log g_log;
static parity_script g_script;
static picture_t g_fake_pnff_picture;   /* handed out by the picture_NewFromFormat stand-in */
static picture_t g_fake_buffer_picture; /* handed out by the fake buffer_new callback */

PARITY_API parity_log *parity_log_ptr(void) { return &g_log; }
PARITY_API parity_script *parity_script_ptr(void) { return &g_script; }

PARITY_API void parity_reset(void)
{
    memset(&g_log, 0, sizeof g_log);
    memset(&g_script, 0, sizeof g_script);
}

PARITY_API picture_t *parity_fake_pnff_picture(void) { return &g_fake_pnff_picture; }
PARITY_API picture_t *parity_fake_buffer_picture(void) { return &g_fake_buffer_picture; }

static void copy_name(char *dst, const char *src)
{
    snprintf(dst, NAME_MAX_LEN, "%s", src ? src : "");
}

static char *dup_str(const char *s)
{
    size_t n = strlen(s) + 1;
    char *p = malloc(n);
    if (p != NULL)
        memcpy(p, s, n);
    return p;
}

/* ------------------------------------------------------------------------ *
 * Stand-ins for the libvlccore exports.
 * ------------------------------------------------------------------------ */

int (var_Create)(vlc_object_t *obj, const char *name, int type)
{
    g_log.create_calls++;
    g_log.create_obj = obj;
    g_log.create_type = type;
    copy_name(g_log.create_name, name);
    return g_script.create_result;
}

int (var_SetChecked)(vlc_object_t *obj, const char *name, int type, vlc_value_t val)
{
    g_log.set_calls++;
    g_log.set_obj = obj;
    g_log.set_type = type;
    copy_name(g_log.set_name, name);
    /* A string value is a pointer: its bits mean nothing to compare, the text is recorded below. */
    g_log.set_int = 0;
    if ((type & VLC_VAR_CLASS) != VLC_VAR_STRING)
        memcpy(&g_log.set_int, &val, sizeof g_log.set_int);
    g_log.set_str_is_null = 1;
    g_log.set_str[0] = '\0';
    if ((type & VLC_VAR_CLASS) == VLC_VAR_STRING && val.psz_string != NULL)
    {
        g_log.set_str_is_null = 0;
        snprintf(g_log.set_str, STR_MAX_LEN, "%s", val.psz_string);
    }
    return g_script.set_result;
}

int (var_GetChecked)(vlc_object_t *obj, const char *name, int type, vlc_value_t *val)
{
    g_log.get_calls++;
    g_log.get_obj = obj;
    g_log.get_type = type;
    copy_name(g_log.get_name, name);
    if (g_script.get_result != 0)
        return g_script.get_result;
    if ((type & VLC_VAR_CLASS) == VLC_VAR_STRING)
        val->psz_string = g_script.get_string_is_null ? NULL : dup_str(g_script.get_string);
    else
        memcpy(val, &g_script.get_value, sizeof g_script.get_value);
    return 0;
}

int (var_Inherit)(vlc_object_t *obj, const char *name, int type, vlc_value_t *val)
{
    g_log.inherit_calls++;
    g_log.inherit_obj = obj;
    g_log.inherit_type = type;
    copy_name(g_log.inherit_name, name);
    if (g_script.inherit_result != 0)
        return g_script.inherit_result;
    if ((type & VLC_VAR_CLASS) == VLC_VAR_STRING)
        val->psz_string = g_script.inherit_string_is_null ? NULL : dup_str(g_script.inherit_string);
    else
        memcpy(val, &g_script.inherit_value, sizeof g_script.inherit_value);
    return 0;
}

void picture_Destroy(picture_t *picture)
{
    g_log.destroy_calls++;
    g_log.destroy_arg = picture;
}

picture_t *picture_NewFromFormat(const video_format_t *fmt)
{
    g_log.newfmt_calls++;
    g_log.newfmt_arg = fmt;
    return g_script.pnff_null ? NULL : &g_fake_pnff_picture;
}

void vlc_object_Log(vlc_object_t *obj, int type, const char *module,
                    const char *file, unsigned line, const char *func,
                    const char *format, ...)
{
    va_list ap;
    (void)module; (void)file; (void)line; (void)func;
    g_log.log_calls++;
    g_log.log_obj = obj;
    g_log.log_type = type;
    va_start(ap, format);
    vsnprintf(g_log.log_message, STR_MAX_LEN, format, ap);
    va_end(ap);
}

/* Fake object tree: vlc_object_t first, so a fake pointer is a vlc_object_t pointer. */
typedef struct fake_object
{
    vlc_object_t obj;
    struct fake_object *parent;
    const char *type_name;
} fake_object;

/* type_name must outlive the object; the caller owns it. */
PARITY_API vlc_object_t *parity_object_new(vlc_object_t *parent, const char *type_name)
{
    fake_object *o = calloc(1, sizeof *o);
    o->parent = (fake_object *)parent;
    o->type_name = type_name;
    return &o->obj;
}

PARITY_API void parity_object_free(vlc_object_t *obj) { free(obj); }

vlc_object_t *(vlc_object_parent)(vlc_object_t *obj)
{
    g_log.parent_calls++;
    return obj == NULL ? NULL : &((fake_object *)obj)->parent->obj;
}

const char *vlc_object_typename(const vlc_object_t *obj)
{
    g_log.typename_calls++;
    return ((const fake_object *)obj)->type_name;
}

/* Fake player/playlist/interface. */
int vlc_player_GetCapabilities(vlc_player_t *player)
{
    g_log.caps_calls++;
    g_log.caps_arg = player;
    return g_script.capabilities;
}

void vlc_player_Lock(vlc_player_t *player) { (void)player; g_log.lock_calls++; }
void vlc_player_Unlock(vlc_player_t *player) { (void)player; g_log.unlock_calls++; }
vlc_tick_t vlc_player_GetTime(vlc_player_t *player) { (void)player; return g_script.player_time; }
vlc_tick_t vlc_player_GetLength(vlc_player_t *player) { (void)player; return g_script.player_length; }

void vlc_player_SeekByTime(vlc_player_t *player, vlc_tick_t time,
                           enum vlc_player_seek_speed speed,
                           enum vlc_player_whence whence)
{
    (void)player;
    g_log.seek_time_calls++;
    g_log.seek_time = time;
    g_log.seek_speed = (int32_t)speed;
    g_log.seek_whence = (int32_t)whence;
}

static char g_fake_player_storage[16];
static char g_fake_playlist_storage[16];

vlc_playlist_t *vlc_intf_GetMainPlaylist(intf_thread_t *intf)
{
    (void)intf;
    return (vlc_playlist_t *)g_fake_playlist_storage;
}

vlc_player_t *vlc_playlist_GetPlayer(vlc_playlist_t *playlist)
{
    (void)playlist;
    return (vlc_player_t *)g_fake_player_storage;
}

PARITY_API vlc_player_t *parity_fake_player(void) { return (vlc_player_t *)g_fake_player_storage; }
PARITY_API intf_thread_t *parity_fake_intf(void) { return (intf_thread_t *)g_fake_playlist_storage; }

/* ------------------------------------------------------------------------ *
 * Fake pictures and filters.
 * ------------------------------------------------------------------------ */

PARITY_API picture_t *parity_picture_new(uintptr_t refs)
{
    picture_t *p = calloc(1, sizeof *p);
    atomic_store(&p->refs.refs, refs);
    return p;
}

PARITY_API uintptr_t parity_picture_refs(picture_t *p) { return atomic_load(&p->refs.refs); }
PARITY_API void parity_picture_set_refs(picture_t *p, uintptr_t refs) { atomic_store(&p->refs.refs, refs); }
PARITY_API void parity_picture_free(picture_t *p) { free(p); }

static picture_t *fake_buffer_new(filter_t *filter)
{
    g_log.buffer_new_calls++;
    g_log.buffer_new_arg = filter;
    return &g_fake_buffer_picture;
}

static picture_t *fake_buffer_new_null(filter_t *filter)
{
    g_log.buffer_new_calls++;
    g_log.buffer_new_arg = filter;
    return NULL;
}

static const struct filter_video_callbacks g_cb_returning = { fake_buffer_new, NULL };
static const struct filter_video_callbacks g_cb_null_result = { fake_buffer_new_null, NULL };
static const struct filter_video_callbacks g_cb_no_buffer_new = { NULL, NULL };

/* mode 0: owner.video == NULL            1: buffer_new == NULL
 *      2: buffer_new returns a picture   3: buffer_new returns NULL */
PARITY_API filter_t *parity_filter_new(int mode)
{
    filter_t *f = calloc(1, sizeof *f);
    switch (mode)
    {
        case 1: f->owner.video = &g_cb_no_buffer_new; break;
        case 2: f->owner.video = &g_cb_returning; break;
        case 3: f->owner.video = &g_cb_null_result; break;
        default: f->owner.video = NULL; break;
    }
    return f;
}

PARITY_API void parity_filter_free(filter_t *f) { free(f); }
PARITY_API const video_format_t *parity_filter_fmt_out_video(filter_t *f) { return &f->fmt_out.video; }

/* ------------------------------------------------------------------------ *
 * Wrappers around the real inline functions and macros.
 * ------------------------------------------------------------------------ */

PARITY_API void parity_vlc_fourcc_to_char(uint32_t fcc, char *out4) { vlc_fourcc_to_char(fcc, out4); }

PARITY_API void parity_picture_Release(picture_t *p) { picture_Release(p); }
PARITY_API picture_t *parity_filter_NewPicture(filter_t *f) { return filter_NewPicture(f); }

PARITY_API int parity_var_SetInteger(vlc_object_t *o, const char *n, int64_t i) { return var_SetInteger(o, n, i); }
PARITY_API int64_t parity_var_GetInteger(vlc_object_t *o, const char *n) { return var_GetInteger(o, n); }
PARITY_API int parity_var_SetString(vlc_object_t *o, const char *n, const char *s) { return var_SetString(o, n, s); }
PARITY_API char *parity_var_GetString(vlc_object_t *o, const char *n) { return var_GetString(o, n); }
PARITY_API int parity_var_Create(vlc_object_t *o, const char *n, int t) { return var_Create(o, n, t); }
PARITY_API void parity_free(void *p) { free(p); }

PARITY_API int parity_var_InheritBool(vlc_object_t *o, const char *n) { return var_InheritBool(o, n); }
PARITY_API int64_t parity_var_InheritInteger(vlc_object_t *o, const char *n) { return var_InheritInteger(o, n); }
PARITY_API float parity_var_InheritFloat(vlc_object_t *o, const char *n) { return var_InheritFloat(o, n); }
PARITY_API char *parity_var_InheritString(vlc_object_t *o, const char *n) { return var_InheritString(o, n); }

PARITY_API int parity_vlc_player_CanSeek(vlc_player_t *p) { return vlc_player_CanSeek(p); }
PARITY_API int parity_vlc_player_CanPause(vlc_player_t *p) { return vlc_player_CanPause(p); }

PARITY_API int64_t parity_VLC_TICK_FROM_US(int64_t v) { return VLC_TICK_FROM_US(v); }
PARITY_API int64_t parity_US_FROM_VLC_TICK(int64_t v) { return US_FROM_VLC_TICK(v); }
PARITY_API int64_t parity_VLC_TICK_FROM_MS(int64_t v) { return VLC_TICK_FROM_MS(v); }
PARITY_API int64_t parity_MS_FROM_VLC_TICK(int64_t v) { return MS_FROM_VLC_TICK(v); }
PARITY_API int64_t parity_VLC_TICK_FROM_SEC(int64_t v) { return VLC_TICK_FROM_SEC(v); }
PARITY_API int64_t parity_SEC_FROM_VLC_TICK(int64_t v) { return SEC_FROM_VLC_TICK(v); }
PARITY_API int64_t parity_VLC_TICK_FROM_NS(int64_t v) { return VLC_TICK_FROM_NS(v); }
PARITY_API int64_t parity_NS_FROM_VLC_TICK(int64_t v) { return NS_FROM_VLC_TICK(v); }

PARITY_API int64_t parity_CLOCK_FREQ(void) { return CLOCK_FREQ; }
PARITY_API int64_t parity_VLC_TICK_INVALID(void) { return VLC_TICK_INVALID; }
PARITY_API int64_t parity_VLC_TICK_0(void) { return VLC_TICK_0; }

PARITY_API vlc_object_t *parity_vlc_object_instance(vlc_object_t *o) { return (vlc_object_t *)vlc_object_instance(o); }

PARITY_API int parity_VLC_PLAYER_CAP_SEEK(void) { return VLC_PLAYER_CAP_SEEK; }
PARITY_API int parity_VLC_PLAYER_CAP_PAUSE(void) { return VLC_PLAYER_CAP_PAUSE; }
PARITY_API int parity_VLC_MSG_WARN(void) { return VLC_MSG_WARN; }

/* ------------------------------------------------------------------------ *
 * Chroma descriptions. vlc_fourcc_GetChromaBPP is an inline over the
 * libvlccore export vlc_fourcc_GetChromaDescription; this stand-in answers
 * for the chromas VLCLR knows, with the geometry of the real VLC table.
 * ------------------------------------------------------------------------ */

#define R1 {1, 1}
#define R2 {1, 2}
static const vlc_chroma_description_t g_chromas[] = {
    { VLC_CODEC_I420,  VLC_CHROMA_SUBTYPE_YUV420, 3, {{R1, R1}, {R2, R2}, {R2, R2}}, 1, 8, 8.f },
    { VLC_CODEC_YV12,  VLC_CHROMA_SUBTYPE_YUV420, 3, {{R1, R1}, {R2, R2}, {R2, R2}}, 1, 8, 8.f },
    { VLC_CODEC_XRGB,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 24, 8.f },
    { VLC_CODEC_RGBX,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 24, 8.f },
    { VLC_CODEC_BGRX,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 24, 8.f },
    { VLC_CODEC_XBGR,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 24, 8.f },
    { VLC_CODEC_RGB24, VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 3, 24, 8.f },
    { VLC_CODEC_BGR24, VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 3, 24, 8.f },
    { VLC_CODEC_RGBA,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 32, 8.f },
    { VLC_CODEC_ARGB,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 32, 8.f },
    { VLC_CODEC_BGRA,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 32, 8.f },
    { VLC_CODEC_ABGR,  VLC_CHROMA_SUBTYPE_RGB,    1, {{R1, R1}}, 4, 32, 8.f },
};

const vlc_chroma_description_t *vlc_fourcc_GetChromaDescription(vlc_fourcc_t fourcc)
{
    size_t i;
    for (i = 0; i < sizeof g_chromas / sizeof g_chromas[0]; i++)
        if (g_chromas[i].fcc == fourcc)
            return &g_chromas[i];
    return NULL;
}

PARITY_API unsigned parity_vlc_fourcc_GetChromaBPP(uint32_t fcc) { return vlc_fourcc_GetChromaBPP(fcc); }

/* Bytes per pixel of the first plane, or -1 for a chroma without a description. */
PARITY_API int parity_chroma_pixel_size(uint32_t fcc)
{
    const vlc_chroma_description_t *d = vlc_fourcc_GetChromaDescription(fcc);
    return d == NULL ? -1 : (int)d->pixel_size;
}

/* ------------------------------------------------------------------------ *
 * Codec constants VLCLR mirrors as C# consts.
 * ------------------------------------------------------------------------ */

#define CODEC(n) { #n, VLC_CODEC_##n }
static const struct { const char *name; uint32_t value; } g_codecs[] = {
    CODEC(XRGB), CODEC(RGB24), CODEC(RGBX), CODEC(BGRX), CODEC(XBGR), CODEC(BGR24),
    CODEC(ARGB), CODEC(ABGR), CODEC(RGBA), CODEC(BGRA), CODEC(I420), CODEC(YV12),
    CODEC(D3D11_OPAQUE), CODEC(D3D11_OPAQUE_10B), CODEC(D3D11_OPAQUE_RGBA),
    CODEC(D3D11_OPAQUE_BGRA), CODEC(D3D11_OPAQUE_ALPHA), CODEC(F32L), CODEC(S16L),
};

PARITY_API int parity_codec_count(void) { return (int)(sizeof g_codecs / sizeof g_codecs[0]); }
PARITY_API const char *parity_codec_name(int i) { return g_codecs[i].name; }
PARITY_API uint32_t parity_codec_value(int i) { return g_codecs[i].value; }

PARITY_API int parity_log_size(void) { return (int)sizeof(parity_log); }
PARITY_API int parity_script_size(void) { return (int)sizeof(parity_script); }

/* Reference composition for VLCVariable.CreateInteger / CreateString: VLC has no
 * single helper, callers do var_Create then var_Set*. */
PARITY_API int parity_create_integer(vlc_object_t *o, const char *n, int64_t init)
{
    int r = var_Create(o, n, VLC_VAR_INTEGER);
    if (r == 0)
        var_SetInteger(o, n, init);
    return r;
}

PARITY_API int parity_create_string(vlc_object_t *o, const char *n, const char *init)
{
    int r = var_Create(o, n, VLC_VAR_STRING);
    if (r == 0 && init != NULL)
        var_SetString(o, n, init);
    return r;
}
