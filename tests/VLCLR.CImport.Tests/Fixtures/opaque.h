/* Fixture: opaque.h
 * Forward-declared struct that is never defined, used through pointers (vlc_object_internals pattern).
 * Golden snapshots: opaque.windows.json, opaque.sysv.json */
#include "fixture_types.h"

struct hidden;
typedef struct hidden hidden_t;

struct holder {
    struct hidden *direct;
    hidden_t *through_typedef;
    int count;
};

/* Defined, but holder_of_visible reaches it only through a pointer. */
struct visible {
    int a;
};

struct holder_of_visible {
    struct visible *only_by_pointer;
};
