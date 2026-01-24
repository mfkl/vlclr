/**
 * VLC Core Stub
 * Minimal stubs for linking the C glue layer without real VLC libraries.
 * This allows the plugin DLL to be built and tested in isolation.
 */

#include <stdarg.h>
#include <stdio.h>

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
