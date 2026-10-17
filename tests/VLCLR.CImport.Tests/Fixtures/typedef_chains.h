/* Fixture: typedef_chains.h
 * typedef of a typedef of a struct, typedef struct x x;, typedef of a pointer, and uint64_t-style typedefs whose canonical builtin differs per ABI.
 * Golden snapshots: typedef_chains.windows.json, typedef_chains.sysv.json */
#include "fixture_types.h"

struct base {
    int v;
};
typedef struct base base_t;
typedef base_t base_alias_t;

typedef struct tagged tagged;
struct tagged {
    int v;
};

typedef struct {
    int a;
    uint8_t b;
} anon_t;
typedef anon_t *anon_ptr_t;

typedef uint64_t tick_t;
typedef tick_t duration_t;

struct uses_typedefs {
    tick_t when;
    duration_t span;
    base_alias_t base;
    anon_ptr_t anon;
    const base_t *const_base;
    tagged *self;
};
