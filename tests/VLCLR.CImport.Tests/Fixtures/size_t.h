/* Fixture: size_t.h
 * size_t, ssize_t, ptrdiff_t, intptr_t and uintptr_t fields.
 * Golden snapshots: size_t.windows.json, size_t.sysv.json */
#include "fixture_types.h"

struct size_types {
    size_t size;
    ssize_t signed_size;
    ptrdiff_t diff;
    intptr_t iptr;
    uintptr_t uptr;
    uint64_t tick;
};
