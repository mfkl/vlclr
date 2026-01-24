/**
 * VLC Core Stub
 * Minimal stubs for linking the C glue layer without real VLC libraries.
 * This allows the plugin DLL to be built and tested in isolation.
 */

#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Stub for vlc_object_Log - the VLC logging function */
void vlc_object_Log(void *obj, int type, const char *module, const char *file,
                    unsigned line, const char *func, const char *format, ...)
{
    va_list args;
    va_start(args, format);
    fprintf(stderr, "[%s] ", module ? module : "vlc");
    vfprintf(stderr, format, args);
    fprintf(stderr, "\n");
    va_end(args);
}

/* Simple in-memory variable storage for testing */
#define MAX_STUB_VARS 32

typedef struct {
    char name[128];
    int type;
    union {
        long long i_int;
        char *psz_string;
    } value;
    int in_use;
} stub_var_t;

static stub_var_t stub_vars[MAX_STUB_VARS] = {0};

static stub_var_t* find_stub_var(const char *name)
{
    for (int i = 0; i < MAX_STUB_VARS; i++) {
        if (stub_vars[i].in_use && strcmp(stub_vars[i].name, name) == 0) {
            return &stub_vars[i];
        }
    }
    return NULL;
}

static stub_var_t* alloc_stub_var(const char *name)
{
    for (int i = 0; i < MAX_STUB_VARS; i++) {
        if (!stub_vars[i].in_use) {
            stub_vars[i].in_use = 1;
            strncpy(stub_vars[i].name, name, sizeof(stub_vars[i].name) - 1);
            stub_vars[i].name[sizeof(stub_vars[i].name) - 1] = '\0';
            return &stub_vars[i];
        }
    }
    return NULL;
}

/* VLC variable type constants */
#define VLC_VAR_INTEGER   0x0030
#define VLC_VAR_STRING    0x0040

/* VLC value union - matches vlc_value_t from vlc_variables.h */
typedef union
{
    int64_t         i_int;
    int             b_bool;
    float           f_float;
    char *          psz_string;
    void *          p_address;
    struct { int32_t x; int32_t y; } coords;
} vlc_value_t;

/* Stub for var_Create */
int var_Create(void *obj, const char *name, int type)
{
    (void)obj;
    fprintf(stderr, "[vlccore_stub] var_Create: %s (type=0x%04x)\n", name, type);

    stub_var_t *var = find_stub_var(name);
    if (var != NULL) {
        /* Already exists, increment refcount (in real VLC) - we just return success */
        return 0;
    }

    var = alloc_stub_var(name);
    if (var == NULL) {
        fprintf(stderr, "[vlccore_stub] var_Create: out of variable slots\n");
        return -1;
    }

    var->type = type & 0x00ff; /* Extract base type */
    var->value.i_int = 0;
    return 0;
}

/* Stub for var_Destroy */
void var_Destroy(void *obj, const char *name)
{
    (void)obj;
    fprintf(stderr, "[vlccore_stub] var_Destroy: %s\n", name);

    stub_var_t *var = find_stub_var(name);
    if (var != NULL) {
        if ((var->type & 0x00ff) == VLC_VAR_STRING && var->value.psz_string != NULL) {
            free(var->value.psz_string);
        }
        var->in_use = 0;
    }
}

/* Stub for var_SetChecked - uses real vlc_value_t signature */
int var_SetChecked(void *obj, const char *name, int type, vlc_value_t val)
{
    (void)obj;
    int base_type = type & 0x00ff;

    stub_var_t *var = find_stub_var(name);
    if (var == NULL) {
        fprintf(stderr, "[vlccore_stub] var_SetChecked: variable not found: %s\n", name);
        return -1;
    }

    if (base_type == VLC_VAR_INTEGER) {
        fprintf(stderr, "[vlccore_stub] var_SetChecked: %s = %lld\n", name, (long long)val.i_int);
        var->value.i_int = val.i_int;
    } else if (base_type == VLC_VAR_STRING) {
        fprintf(stderr, "[vlccore_stub] var_SetChecked: %s = \"%s\"\n", name, val.psz_string ? val.psz_string : "(null)");
        /* Free old string if present */
        if (var->value.psz_string != NULL) {
            free(var->value.psz_string);
        }
        var->value.psz_string = val.psz_string ? strdup(val.psz_string) : NULL;
    }
    return 0;
}

/* Stub for var_GetChecked - uses real vlc_value_t signature */
int var_GetChecked(void *obj, const char *name, int type, vlc_value_t *valp)
{
    (void)obj;
    int base_type = type & 0x00ff;

    stub_var_t *var = find_stub_var(name);
    if (var == NULL) {
        fprintf(stderr, "[vlccore_stub] var_GetChecked: variable not found: %s\n", name);
        if (base_type == VLC_VAR_INTEGER)
            valp->i_int = 0;
        else if (base_type == VLC_VAR_STRING)
            valp->psz_string = NULL;
        return -1;
    }

    if (base_type == VLC_VAR_INTEGER) {
        valp->i_int = var->value.i_int;
        fprintf(stderr, "[vlccore_stub] var_GetChecked: %s = %lld\n", name, (long long)valp->i_int);
    } else if (base_type == VLC_VAR_STRING) {
        valp->psz_string = var->value.psz_string ? strdup(var->value.psz_string) : NULL;
        fprintf(stderr, "[vlccore_stub] var_GetChecked: %s = \"%s\"\n", name, valp->psz_string ? valp->psz_string : "(null)");
    }
    return 0;
}
