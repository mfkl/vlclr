/* Fixture: aligned.h
 * Struct with an over-aligned member (alignas(16) / __attribute__((aligned(16)))) and trailing padding.
 * Golden snapshots: aligned.windows.json, aligned.sysv.json */
#include "fixture_types.h"

struct aligned_block {
    char tag;
    _Alignas(16) int value;
    char tail;
};

struct attr_aligned {
    char a;
    int b __attribute__((aligned(16)));
};

struct holds_aligned {
    int id;
    struct aligned_block block;
};
