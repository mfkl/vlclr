/* Hand-written header that ProbeSampleIr describes. Compiled only as C++17 by the probe tests. */
#ifndef PROBE_SAMPLE_H
#define PROBE_SAMPLE_H

#include <stdint.h>

/* Uniform record. */
typedef struct plane_t {
    uint8_t *p_pixels;
    int i_lines;
    int i_pitch;
} plane_t;

/* dovi: Windows starts the unsigned bitfields in a new 4-byte unit after the
 * uint8_t members, SysV packs them right behind. */
typedef struct video_format_t {
    uint32_t i_chroma;
    struct {
        uint8_t version_major;
        uint8_t version_minor;
        unsigned profile : 7;
        unsigned level : 6;
    } dovi;
    uint32_t i_cubemap_padding;
} video_format_t;

/* Array plus an anonymous union member. */
typedef struct es_format_t {
    int i_cat;
    int i_array[3];
    union {
        video_format_t video;
        char i_codec_data[4];
    };
    int i_bitrate;
} es_format_t;

typedef union probe_value_t {
    int i;
    double d;
    char c[3];
} probe_value_t;

/* long is 4 bytes on Windows and 8 on SysV. */
typedef struct probe_long_t {
    long l;
    int i;
    uint64_t u;
} probe_long_t;

typedef struct probe_bits_t {
    unsigned a : 3;
    unsigned b : 5;
    int c;
} probe_bits_t;

struct probe_opaque_t;

enum probe_color_e {
    PROBE_RED = 1,
    PROBE_GREEN = 2,
    PROBE_NEG = -2
};

enum probe_flags_e {
    PROBE_FLAG_ONE = 1,
    PROBE_FLAG_BIG = 0xFFFFFFFFFFFFFFFFull
};

enum {
    PROBE_ANON_A = 7,
    PROBE_ANON_B = 8
};

#define PROBE_MAX 100
#define PROBE_NEGATIVE (-5)
#define PROBE_MASK 0xFFFFFFFFu
#define PROBE_TOP_BIT 0x8000000000000000ull

#endif
