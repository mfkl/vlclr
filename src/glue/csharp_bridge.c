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
    int result = var_GetChecked((vlc_object_t*)vlc_object, name, VLC_VAR_STRING, &val);
    if (result != 0 || val.psz_string == NULL)
        return NULL;

    /* VLC uses msvcrt allocator, but we use UCRT. They're incompatible.
     * Copy the string to our heap so C# can free it with our allocator. */
    char *copy = _strdup(val.psz_string);

    /* Free VLC's string using msvcrt-compatible free.
     * Since we're linking against VLC, we should use the same free it uses.
     * But for now, we'll just skip freeing it (small memory leak).
     * TODO: Use VLC's allocator directly if available. */
    /* free(val.psz_string);  -- This would cause heap corruption */

    return copy;
}

BRIDGE_API void csharp_bridge_free_string(char* str)
{
    if (str != NULL)
        free(str);  /* This is safe now - str was allocated with our _strdup */
}

/*
 * Player Events Implementation
 */

/* Forward declarations for VLC types */
typedef struct intf_thread_t intf_thread_t;
typedef struct vlc_playlist_t vlc_playlist_t;
typedef struct vlc_player_t vlc_player_t;
typedef struct vlc_player_listener_id vlc_player_listener_id;
typedef struct input_item_t input_item_t;
typedef int64_t vlc_tick_t;

/* VLC player state enum - matches vlc_player.h */
enum vlc_player_state
{
    VLC_PLAYER_STATE_STOPPED = 0,
    VLC_PLAYER_STATE_STARTED,
    VLC_PLAYER_STATE_PLAYING,
    VLC_PLAYER_STATE_PAUSED,
    VLC_PLAYER_STATE_STOPPING,
};

/* VLC player callbacks structure - must match vlc_player_cbs in vlc_player.h */
struct vlc_player_cbs
{
    void (*on_current_media_changed)(vlc_player_t *player, input_item_t *new_media, void *data);
    void (*on_state_changed)(vlc_player_t *player, enum vlc_player_state new_state, void *data);
    /* We only need a subset of callbacks - the rest can be NULL */
    void (*on_error_changed)(vlc_player_t *player, int error, void *data);
    void (*on_buffering_changed)(vlc_player_t *player, float new_buffering, void *data);
    void (*on_rate_changed)(vlc_player_t *player, float new_rate, void *data);
    void (*on_capabilities_changed)(vlc_player_t *player, int old_caps, int new_caps, void *data);
    void (*on_position_changed)(vlc_player_t *player, vlc_tick_t new_time, double new_pos, void *data);
    void (*on_length_changed)(vlc_player_t *player, vlc_tick_t new_length, void *data);
    /* ... rest of callbacks we don't use ... */
};

/* VLC function declarations */
extern vlc_playlist_t* vlc_intf_GetMainPlaylist(intf_thread_t *intf);
extern vlc_player_t* vlc_playlist_GetPlayer(vlc_playlist_t *playlist);
extern void vlc_player_Lock(vlc_player_t *player);
extern void vlc_player_Unlock(vlc_player_t *player);
extern vlc_player_listener_id* vlc_player_AddListener(vlc_player_t *player, const struct vlc_player_cbs *cbs, void *cbs_data);
extern void vlc_player_RemoveListener(vlc_player_t *player, vlc_player_listener_id *listener_id);
extern enum vlc_player_state vlc_player_GetState(vlc_player_t *player);

/* Context for our listener - holds C# callbacks */
typedef struct {
    csharp_player_callbacks_t csharp_cbs;
    struct vlc_player_cbs vlc_cbs;
} listener_context_t;

/* Handle structure to track both listener_id and context */
typedef struct {
    vlc_player_listener_id *listener_id;
    listener_context_t *context;
} listener_handle_t;

/* VLC callback implementations that forward to C# */
static void on_state_changed_cb(vlc_player_t *player, enum vlc_player_state new_state, void *data)
{
    (void)player;
    listener_context_t *ctx = (listener_context_t*)data;
    if (ctx && ctx->csharp_cbs.on_state_changed)
    {
        ctx->csharp_cbs.on_state_changed((int)new_state, ctx->csharp_cbs.user_data);
    }
}

static void on_position_changed_cb(vlc_player_t *player, vlc_tick_t new_time, double new_pos, void *data)
{
    (void)player;
    listener_context_t *ctx = (listener_context_t*)data;
    if (ctx && ctx->csharp_cbs.on_position_changed)
    {
        ctx->csharp_cbs.on_position_changed((long long)new_time, new_pos, ctx->csharp_cbs.user_data);
    }
}

static void on_media_changed_cb(vlc_player_t *player, input_item_t *new_media, void *data)
{
    (void)player;
    listener_context_t *ctx = (listener_context_t*)data;
    if (ctx && ctx->csharp_cbs.on_media_changed)
    {
        ctx->csharp_cbs.on_media_changed((void*)new_media, ctx->csharp_cbs.user_data);
    }
}

BRIDGE_API void* csharp_bridge_get_player(void* intf)
{
    if (intf == NULL)
        return NULL;

    vlc_playlist_t *playlist = vlc_intf_GetMainPlaylist((intf_thread_t*)intf);
    if (playlist == NULL)
        return NULL;

    return (void*)vlc_playlist_GetPlayer(playlist);
}

BRIDGE_API int csharp_bridge_player_get_state(void* player)
{
    if (player == NULL)
        return 0; /* VLC_PLAYER_STATE_STOPPED */

    vlc_player_Lock((vlc_player_t*)player);
    enum vlc_player_state state = vlc_player_GetState((vlc_player_t*)player);
    vlc_player_Unlock((vlc_player_t*)player);

    return (int)state;
}

BRIDGE_API void* csharp_bridge_player_add_listener(void* player, csharp_player_callbacks_t* callbacks)
{
    if (player == NULL || callbacks == NULL)
        return NULL;

    /* Allocate context to hold C# callbacks */
    listener_context_t *ctx = (listener_context_t*)malloc(sizeof(listener_context_t));
    if (ctx == NULL)
        return NULL;

    /* Copy C# callbacks */
    ctx->csharp_cbs = *callbacks;

    /* Initialize VLC callbacks - all to NULL first */
    memset(&ctx->vlc_cbs, 0, sizeof(ctx->vlc_cbs));

    /* Set callbacks we care about */
    ctx->vlc_cbs.on_current_media_changed = on_media_changed_cb;
    ctx->vlc_cbs.on_state_changed = on_state_changed_cb;
    ctx->vlc_cbs.on_position_changed = on_position_changed_cb;

    /* Register with VLC */
    vlc_player_Lock((vlc_player_t*)player);
    vlc_player_listener_id *listener_id = vlc_player_AddListener((vlc_player_t*)player, &ctx->vlc_cbs, ctx);
    vlc_player_Unlock((vlc_player_t*)player);

    if (listener_id == NULL)
    {
        free(ctx);
        return NULL;
    }

    /* Allocate handle to track both listener_id and context */
    listener_handle_t *handle = (listener_handle_t*)malloc(sizeof(listener_handle_t));
    if (handle == NULL)
    {
        vlc_player_Lock((vlc_player_t*)player);
        vlc_player_RemoveListener((vlc_player_t*)player, listener_id);
        vlc_player_Unlock((vlc_player_t*)player);
        free(ctx);
        return NULL;
    }

    handle->listener_id = listener_id;
    handle->context = ctx;

    return (void*)handle;
}

BRIDGE_API void csharp_bridge_player_remove_listener(void* player, void* listener_handle)
{
    if (player == NULL || listener_handle == NULL)
        return;

    listener_handle_t *handle = (listener_handle_t*)listener_handle;

    vlc_player_Lock((vlc_player_t*)player);
    vlc_player_RemoveListener((vlc_player_t*)player, handle->listener_id);
    vlc_player_Unlock((vlc_player_t*)player);

    free(handle->context);
    free(handle);
}

/*
 * Playlist Control Implementation
 */

/* VLC playlist function declarations */
extern void vlc_playlist_Lock(vlc_playlist_t *playlist);
extern void vlc_playlist_Unlock(vlc_playlist_t *playlist);
extern int vlc_playlist_Start(vlc_playlist_t *playlist);
extern void vlc_playlist_Stop(vlc_playlist_t *playlist);
extern void vlc_playlist_Pause(vlc_playlist_t *playlist);
extern void vlc_playlist_Resume(vlc_playlist_t *playlist);
extern int vlc_playlist_Next(vlc_playlist_t *playlist);
extern int vlc_playlist_Prev(vlc_playlist_t *playlist);
extern int vlc_playlist_HasNext(vlc_playlist_t *playlist);  /* Returns bool, treat as int */
extern int vlc_playlist_HasPrev(vlc_playlist_t *playlist);  /* Returns bool, treat as int */
extern size_t vlc_playlist_Count(vlc_playlist_t *playlist);
extern int64_t vlc_playlist_GetCurrentIndex(vlc_playlist_t *playlist);  /* Returns ssize_t */
extern int vlc_playlist_GoTo(vlc_playlist_t *playlist, int64_t index);  /* Takes ssize_t */

BRIDGE_API void* csharp_bridge_get_playlist(void* intf)
{
    if (intf == NULL)
        return NULL;

    return (void*)vlc_intf_GetMainPlaylist((intf_thread_t*)intf);
}

BRIDGE_API int csharp_bridge_playlist_start(void* playlist)
{
    if (playlist == NULL)
        return -1;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int result = vlc_playlist_Start((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return result;
}

BRIDGE_API void csharp_bridge_playlist_stop(void* playlist)
{
    if (playlist == NULL)
        return;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    vlc_playlist_Stop((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);
}

BRIDGE_API void csharp_bridge_playlist_pause(void* playlist)
{
    if (playlist == NULL)
        return;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    vlc_playlist_Pause((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);
}

BRIDGE_API void csharp_bridge_playlist_resume(void* playlist)
{
    if (playlist == NULL)
        return;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    vlc_playlist_Resume((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);
}

BRIDGE_API int csharp_bridge_playlist_next(void* playlist)
{
    if (playlist == NULL)
        return -1;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int result = vlc_playlist_Next((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return result;
}

BRIDGE_API int csharp_bridge_playlist_prev(void* playlist)
{
    if (playlist == NULL)
        return -1;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int result = vlc_playlist_Prev((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return result;
}

BRIDGE_API int csharp_bridge_playlist_has_next(void* playlist)
{
    if (playlist == NULL)
        return 0;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int result = vlc_playlist_HasNext((vlc_playlist_t*)playlist) ? 1 : 0;
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return result;
}

BRIDGE_API int csharp_bridge_playlist_has_prev(void* playlist)
{
    if (playlist == NULL)
        return 0;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int result = vlc_playlist_HasPrev((vlc_playlist_t*)playlist) ? 1 : 0;
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return result;
}

BRIDGE_API long long csharp_bridge_playlist_count(void* playlist)
{
    if (playlist == NULL)
        return 0;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    size_t count = vlc_playlist_Count((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return (long long)count;
}

BRIDGE_API long long csharp_bridge_playlist_get_current_index(void* playlist)
{
    if (playlist == NULL)
        return -1;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int64_t index = vlc_playlist_GetCurrentIndex((vlc_playlist_t*)playlist);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return (long long)index;
}

BRIDGE_API int csharp_bridge_playlist_goto(void* playlist, long long index)
{
    if (playlist == NULL)
        return -1;

    vlc_playlist_Lock((vlc_playlist_t*)playlist);
    int result = vlc_playlist_GoTo((vlc_playlist_t*)playlist, (int64_t)index);
    vlc_playlist_Unlock((vlc_playlist_t*)playlist);

    return result;
}
