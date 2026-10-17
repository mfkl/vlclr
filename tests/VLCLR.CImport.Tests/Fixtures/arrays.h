/* Fixture: arrays.h
 * Fixed-size arrays of scalars, of structs, of pointers, and a two-dimensional array.
 * Golden snapshots: arrays.windows.json, arrays.sysv.json */
#include "fixture_types.h"

struct pair {
    int a;
    char b;
};

struct arrays {
    char name[16];
    struct pair pairs[3];
    void *ptrs[4];
    int grid[2][3];
    const char *const labels[2];
    uint8_t bytes[5];
    uint64_t wide[2];
};
