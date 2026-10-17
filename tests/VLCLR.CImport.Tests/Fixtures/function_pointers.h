/* Fixture: function_pointers.h
 * Function pointer fields (plain and via typedef), a struct of callbacks like vlc_filter_operations, and a callback taking and returning pointers.
 * Golden snapshots: function_pointers.windows.json, function_pointers.sysv.json */
#include "fixture_types.h"

typedef void (*callback_t)(void *opaque, int value);
typedef int fn_type(int);

struct ops {
    void (*close)(struct ops *self);
    int (*process)(struct ops *self, const char *name, ...);
    callback_t on_event;
    callback_t handlers[2];
    char *(*lookup)(const char *key);
    fn_type *transform;
};
