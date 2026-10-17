/* Fixture: constants.h
 * Object-like macros: decimal, hex, negative, unsigned and 64-bit integers, shifts and ORs, a VLC_FOURCC-style expression, a float, a string literal, a macro referring to another macro, and a function-like macro that must be skipped.
 * Golden snapshots: constants.windows.json, constants.sysv.json */
#include "fixture_types.h"

#define K_DECIMAL 42
#define K_HEX 0xFF
#define K_NEGATIVE (-5)
#define K_UNSIGNED 4000000000u
#define K_INT64 0x7FFFFFFFFFFFFFFFLL
#define K_UINT64 0xFFFFFFFFFFFFFFFFull
#define K_SHIFT (1 << 5)
#define K_OR (K_SHIFT | 0x3)
#define K_FOURCC(a, b, c, d) (((uint32_t)(a)) | (((uint32_t)(b)) << 8) | (((uint32_t)(c)) << 16) | (((uint32_t)(d)) << 24))
#define K_CODEC_I420 K_FOURCC('I', '4', '2', '0')
#define K_FLOAT 1.5
#define K_FLOAT_SINGLE 2.5f
#define K_STRING "hello"
#define K_ALIAS K_DECIMAL
#define K_TICKS(x) ((x) * 1000)
#define K_EMPTY
#define K_NOT_A_VALUE int
