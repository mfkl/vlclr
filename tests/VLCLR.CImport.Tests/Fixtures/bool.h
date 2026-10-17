/* Fixture: bool.h
 * _Bool / stdbool bool fields mixed with other members.
 * Golden snapshots: bool.windows.json, bool.sysv.json */
#include "fixture_types.h"

struct bool_mix {
    bool a;
    int b;
    bool c;
    bool d;
    double e;
    _Bool f;
};
