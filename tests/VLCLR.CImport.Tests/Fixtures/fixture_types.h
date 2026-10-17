/* Fixture: fixture_types.h
 * Shared by every fixture so both ABIs can be extracted on any host without system headers
 * (fixtures are parsed with -nostdinc -ffreestanding). The standard typedefs come from clang's
 * predefined macros, so they are target-correct: uint64_t is unsigned long long on windows and
 * unsigned long on sysv. */
#ifndef FIXTURE_TYPES_H
#define FIXTURE_TYPES_H

typedef __INT8_TYPE__ int8_t;
typedef __UINT8_TYPE__ uint8_t;
typedef __INT16_TYPE__ int16_t;
typedef __UINT16_TYPE__ uint16_t;
typedef __INT32_TYPE__ int32_t;
typedef __UINT32_TYPE__ uint32_t;
typedef __INT64_TYPE__ int64_t;
typedef __UINT64_TYPE__ uint64_t;
typedef __INTPTR_TYPE__ intptr_t;
typedef __UINTPTR_TYPE__ uintptr_t;
typedef __SIZE_TYPE__ size_t;
typedef __PTRDIFF_TYPE__ ptrdiff_t;
typedef __PTRDIFF_TYPE__ ssize_t;
typedef __WCHAR_TYPE__ wchar_t;
typedef _Bool bool;

#endif
