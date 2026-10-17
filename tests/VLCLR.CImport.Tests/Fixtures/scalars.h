/* Fixture: scalars.h
 * Struct of every C integer and floating type (char, signed/unsigned char, short, int, long long, float, double, int8_t..uint64_t), checking padding between them.
 * Golden snapshots: scalars.windows.json, scalars.sysv.json */
#include "fixture_types.h"

struct scalars {
    char c;
    signed char sc;
    unsigned char uc;
    short s;
    unsigned short us;
    int i;
    unsigned int ui;
    long long ll;
    unsigned long long ull;
    float f;
    double d;
    long double ld;
    int8_t i8;
    uint8_t u8;
    int16_t i16;
    uint16_t u16;
    int32_t i32;
    uint32_t u32;
    int64_t i64;
    uint64_t u64;
    wchar_t wc;
};
