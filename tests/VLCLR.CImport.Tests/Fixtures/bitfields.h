/* Fixture: bitfields.h
 * Bitfields of several widths and declared types, a zero-width bitfield, and the video_format_t dovi pattern: uint8_t members followed by unsigned bitfields, which Windows and SysV lay out differently.
 * Golden snapshots: bitfields.windows.json, bitfields.sysv.json */
#include "fixture_types.h"

struct bits_mixed {
    unsigned a : 3;
    int b : 5;
    unsigned char c : 2;
    uint32_t d : 7;
    long long e : 4;
    unsigned : 0;
    unsigned f : 1;
    unsigned : 3;
    unsigned g : 2;
};

/* video_format_t pattern: uint8_t members, then unsigned bitfields.
 * Windows starts the bitfields in a new 4-byte unit, SysV packs them right after the bytes. */
struct bits_video {
    uint32_t chroma;
    struct {
        uint8_t version_major;
        uint8_t version_minor;
        unsigned rpu_type : 4;
        unsigned dv_profile : 7;
        unsigned dv_level : 6;
        unsigned rpu_present : 1;
    } dovi;
    uint32_t padding;
};
