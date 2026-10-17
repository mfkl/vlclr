/* Fixture: functions.h
 * Exported function (VLC_API-like), a static inline function, a variadic function, functions taking pointers to records and function pointers.
 * Golden snapshots: functions.windows.json, functions.sysv.json */
#include "fixture_types.h"

/* Like VLC_API for consumers: expands to nothing, so exports are found through the macro expansion. */
#define VLC_API

struct point {
    int x;
    int y;
};

VLC_API int add(int a, int b);
VLC_API struct point *point_new(int x, int y);
VLC_API void exported_nothing(void);

static inline int square(int value)
{
    return value * value;
}

int log_message(void *object, const char *format, ...);

void for_each(const struct point *points, size_t count, void (*callback)(struct point *, void *), void *data);
