/* Fixture: nested.h
 * Struct embedding another struct by value and by pointer, two levels deep.
 * Golden snapshots: nested.windows.json, nested.sysv.json */
#include "fixture_types.h"

struct inner {
    int a;
    char b;
};

struct middle {
    struct inner by_value;
    struct inner *by_pointer;
};

struct outer {
    char tag;
    struct middle mid;
    struct middle *next;
};
