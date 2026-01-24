/**
 * C# Bridge Implementation
 * Dynamically loads VlcPlugin.dll (C# Native AOT) and resolves exports.
 */

#include "csharp_bridge.h"
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#ifdef _WIN32
#include <windows.h>
#define BRIDGE_HANDLE HMODULE
#define BRIDGE_LOAD(path) LoadLibraryA(path)
#define BRIDGE_SYMBOL(h, name) GetProcAddress(h, name)
#define BRIDGE_UNLOAD(h) FreeLibrary(h)
#else
#include <dlfcn.h>
#define BRIDGE_HANDLE void*
#define BRIDGE_LOAD(path) dlopen(path, RTLD_NOW | RTLD_LOCAL)
#define BRIDGE_SYMBOL(h, name) dlsym(h, name)
#define BRIDGE_UNLOAD(h) dlclose(h)
#endif

/* VLC types forward declarations for logging */
struct vlc_object_t;
typedef struct vlc_object_t vlc_object_t;

/* VLC logging function declaration (defined in vlccore_stub.c or libvlccore) */
extern void vlc_object_Log(vlc_object_t *obj, int type, const char *module,
                           const char *file, unsigned line, const char *func,
                           const char *format, ...);

/* Module name for logging */
static const char vlc_module_name[] = "hello_csharp";

static BRIDGE_HANDLE csharp_dll = NULL;

/* Exported function pointers */
csharp_open_fn csharp_plugin_open = NULL;
csharp_close_fn csharp_plugin_close = NULL;

/* VLC log type constants matching vlc_messages.h */
#define VLC_MSG_INFO 0
#define VLC_MSG_ERR  1
#define VLC_MSG_WARN 2
#define VLC_MSG_DBG  3

BRIDGE_API void csharp_bridge_log(void* vlc_object, int type, const char* message)
{
    if (vlc_object == NULL || message == NULL)
    {
        fprintf(stderr, "[VlcPlugin] (null object) %s\n", message ? message : "(null)");
        return;
    }

    vlc_object_Log((vlc_object_t*)vlc_object, type, vlc_module_name,
                   NULL, 0, NULL, "%s", message);
}

/* Get the directory path of this DLL (the C glue plugin) */
#ifdef _WIN32
static int get_plugin_directory(char *buf, size_t bufsize)
{
    HMODULE hModule = NULL;

    /* Get handle to this DLL by using a function address inside it */
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            (LPCSTR)&csharp_bridge_init, &hModule))
    {
        return -1;
    }

    /* Get the full path of this DLL */
    if (GetModuleFileNameA(hModule, buf, (DWORD)bufsize) == 0)
    {
        return -1;
    }

    /* Strip the filename to get directory path */
    char *lastSlash = strrchr(buf, '\\');
    if (lastSlash == NULL)
        lastSlash = strrchr(buf, '/');
    if (lastSlash != NULL)
        *lastSlash = '\0';

    return 0;
}
#endif

int csharp_bridge_init(void)
{
    if (csharp_dll != NULL)
    {
        /* Already initialized */
        return 0;
    }

#ifdef _WIN32
    /* Build path to VlcPlugin.dll in the same directory as this DLL */
    char plugin_dir[MAX_PATH];
    char plugin_path[MAX_PATH];

    if (get_plugin_directory(plugin_dir, sizeof(plugin_dir)) == 0)
    {
        snprintf(plugin_path, sizeof(plugin_path), "%s\\VlcPlugin.dll", plugin_dir);
        fprintf(stderr, "[csharp_bridge] Loading C# DLL from: %s\n", plugin_path);
        csharp_dll = LoadLibraryA(plugin_path);
    }

    /* Fallback to just the filename if path-based loading failed */
    if (!csharp_dll)
    {
        fprintf(stderr, "[csharp_bridge] Path-based loading failed, trying direct load\n");
        csharp_dll = LoadLibraryA("VlcPlugin.dll");
    }
#else
    /* On non-Windows, try relative path first, then direct */
    csharp_dll = BRIDGE_LOAD("./VlcPlugin.dll");
    if (!csharp_dll)
        csharp_dll = BRIDGE_LOAD("VlcPlugin.dll");
#endif

    if (!csharp_dll)
    {
        fprintf(stderr, "[csharp_bridge] Failed to load VlcPlugin.dll\n");
        return -1;
    }

    /* Resolve exported functions */
    csharp_plugin_open = (csharp_open_fn)BRIDGE_SYMBOL(csharp_dll, "CSharpPluginOpen");
    csharp_plugin_close = (csharp_close_fn)BRIDGE_SYMBOL(csharp_dll, "CSharpPluginClose");

    if (!csharp_plugin_open || !csharp_plugin_close)
    {
        fprintf(stderr, "[csharp_bridge] Failed to resolve C# exports: open=%p close=%p\n",
                (void*)csharp_plugin_open, (void*)csharp_plugin_close);
        BRIDGE_UNLOAD(csharp_dll);
        csharp_dll = NULL;
        return -1;
    }

    fprintf(stderr, "[csharp_bridge] Successfully loaded VlcPlugin.dll\n");
    return 0;
}

void csharp_bridge_cleanup(void)
{
    if (csharp_dll)
    {
        BRIDGE_UNLOAD(csharp_dll);
        csharp_dll = NULL;
    }
    csharp_plugin_open = NULL;
    csharp_plugin_close = NULL;
}

/* VLC value union - matches vlc_value_t from vlc_variables.h */
typedef union
{
    int64_t         i_int;
    int             b_bool;  /* bool in C99 */
    float           f_float;
    char *          psz_string;
    void *          p_address;
    struct { int32_t x; int32_t y; } coords;
} vlc_value_t;

/* VLC variable functions - forward declarations matching real libvlccore */
extern int var_Create(vlc_object_t *obj, const char *name, int type);
extern void var_Destroy(vlc_object_t *obj, const char *name);
extern int var_SetChecked(vlc_object_t *obj, const char *name, int type, vlc_value_t val);
extern int var_GetChecked(vlc_object_t *obj, const char *name, int type, vlc_value_t *valp);

/* VLC variable type constants */
#define VLC_VAR_INTEGER   0x0030
#define VLC_VAR_STRING    0x0040

BRIDGE_API int csharp_bridge_var_create(void* vlc_object, const char* name, int type)
{
    if (vlc_object == NULL || name == NULL)
        return -1;
    return var_Create((vlc_object_t*)vlc_object, name, type);
}

BRIDGE_API void csharp_bridge_var_destroy(void* vlc_object, const char* name)
{
    if (vlc_object == NULL || name == NULL)
        return;
    var_Destroy((vlc_object_t*)vlc_object, name);
}

BRIDGE_API int csharp_bridge_var_set_integer(void* vlc_object, const char* name, long long value)
{
    if (vlc_object == NULL || name == NULL)
        return -1;
    vlc_value_t val;
    val.i_int = value;
    return var_SetChecked((vlc_object_t*)vlc_object, name, VLC_VAR_INTEGER, val);
}

BRIDGE_API long long csharp_bridge_var_get_integer(void* vlc_object, const char* name)
{
    if (vlc_object == NULL || name == NULL)
        return 0;
    vlc_value_t val;
    val.i_int = 0;
    if (var_GetChecked((vlc_object_t*)vlc_object, name, VLC_VAR_INTEGER, &val) == 0)
        return val.i_int;
    return 0;
}

BRIDGE_API int csharp_bridge_var_set_string(void* vlc_object, const char* name, const char* value)
{
    if (vlc_object == NULL || name == NULL)
        return -1;
    vlc_value_t val;
    val.psz_string = (char*)value;
    return var_SetChecked((vlc_object_t*)vlc_object, name, VLC_VAR_STRING, val);
}

BRIDGE_API char* csharp_bridge_var_get_string(void* vlc_object, const char* name)
{
    if (vlc_object == NULL || name == NULL)
        return NULL;
    vlc_value_t val;
    val.psz_string = NULL;
    if (var_GetChecked((vlc_object_t*)vlc_object, name, VLC_VAR_STRING, &val) == 0)
        return val.psz_string;
    return NULL;
}

BRIDGE_API void csharp_bridge_free_string(char* str)
{
    if (str != NULL)
        free(str);
}
