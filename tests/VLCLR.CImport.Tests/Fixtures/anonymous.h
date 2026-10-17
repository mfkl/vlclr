/* Fixture: anonymous.h
 * C11 anonymous struct and union members (es_format_t pattern: union { struct { a; b; }; c; d; }), a named field of anonymous struct type, and nesting of both.
 * Golden snapshots: anonymous.windows.json, anonymous.sysv.json */
#include "fixture_types.h"

struct es_like {
    int cat;
    union {
        struct {
            int width;
            int height;
        } size;
        struct {
            int rate;
            int channels;
        };
        long long raw;
    };
    struct {
        int quality;
    } named;
    union {
        int as_int;
        float as_float;
    };
    int tail;
};
