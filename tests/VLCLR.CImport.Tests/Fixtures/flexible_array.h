/* Fixture: flexible_array.h
 * Struct ending in a flexible array member (block_t-like payload).
 * Golden snapshots: flexible_array.windows.json, flexible_array.sysv.json */
#include "fixture_types.h"

struct block_like {
    struct block_like *p_next;
    size_t i_buffer;
    uint8_t p_payload[];
};

struct counted_values {
    int count;
    int values[];
};
