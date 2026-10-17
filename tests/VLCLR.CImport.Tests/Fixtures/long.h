/* Fixture: long.h
 * C long and unsigned long fields (4 bytes on Windows, 8 on SysV), shifting later offsets.
 * Golden snapshots: long.windows.json, long.sysv.json */
#include "fixture_types.h"

struct long_mix {
    char a;
    long b;
    unsigned long c;
    long long d;
    int e;
};
