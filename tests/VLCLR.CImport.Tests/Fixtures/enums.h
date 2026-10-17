/* Fixture: enums.h
 * Enums with negative values, a 64-bit value, a fixed underlying type (enum e : uint8_t), a typedef'd anonymous enum, and a truly anonymous enum of constants.
 * Golden snapshots: enums.windows.json, enums.sysv.json */
#include "fixture_types.h"

enum signed_e { SIGNED_NEG = -1, SIGNED_ZERO, SIGNED_POS = 5 };

enum unsigned_e { UNSIGNED_BIG = 0xFFFFFFFFu };

/* MSVC enums are int: for the windows target clang truncates WIDE_BIG to 0, sysv keeps the 64-bit value. */
enum wide_e { WIDE_SMALL = 1, WIDE_BIG = 0x100000000LL };

enum fixed_e : uint8_t { FIXED_A = 1, FIXED_B = 200 };

typedef enum { TYPEDEF_ONE = 1, TYPEDEF_TWO } typedef_e;

enum { ANON_A = 10, ANON_B, ANON_C = 0x20 };

struct uses_enums {
    enum signed_e s;
    typedef_e t;
    enum fixed_e f;
};
