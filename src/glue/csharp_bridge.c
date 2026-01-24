/**
 * C# Bridge Implementation
 * Dynamically loads VlcPlugin.dll (C# Native AOT) and resolves exports.
 */

#include "csharp_bridge.h"
#include <stdio.h>

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

static BRIDGE_HANDLE csharp_dll = NULL;

/* Exported function pointers */
csharp_open_fn csharp_plugin_open = NULL;
csharp_close_fn csharp_plugin_close = NULL;

int csharp_bridge_init(void)
{
    if (csharp_dll != NULL)
    {
        /* Already initialized */
        return 0;
    }

    /* Load C# Native AOT DLL */
    csharp_dll = BRIDGE_LOAD("VlcPlugin.dll");
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
