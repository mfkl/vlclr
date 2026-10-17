/* Fixture: unions.h
 * Named union type, a union field inside a struct, and a struct inside a union.
 * Golden snapshots: unions.windows.json, unions.sysv.json */
#include "fixture_types.h"

union named_union {
    int i;
    float f;
    char bytes[4];
};

struct with_union {
    int kind;
    union named_union u;
    union {
        int a;
        double b;
    } inline_union;
};

union with_struct {
    struct {
        int x;
        int y;
    } pair;
    long long whole;
};
