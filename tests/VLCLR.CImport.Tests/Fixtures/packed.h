/* Fixture: packed.h
 * Struct with __attribute__((packed)) and one under #pragma pack(1).
 * Golden snapshots: packed.windows.json, packed.sysv.json */
#include "fixture_types.h"

struct __attribute__((packed)) packed_attr {
    char a;
    int b;
    short c;
};

#pragma pack(push, 1)
struct packed_pragma {
    char a;
    int b;
};
#pragma pack(pop)

struct normal_after {
    char a;
    int b;
};
