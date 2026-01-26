/**
 * Test Harness
 * Standalone test program to verify the .NET plugin bridge works.
 * This simulates what VLC does when loading a plugin.
 */

#include <stdio.h>
#include <windows.h>

/* VLC module property IDs (from vlc_plugin.h) */
#define VLC_MODULE_CREATE 0
#define VLC_MODULE_NAME 0x107
#define VLC_MODULE_SHORTNAME 0x108
#define VLC_MODULE_DESCRIPTION 0x109
#define VLC_MODULE_CAPABILITY 0x102
#define VLC_MODULE_SCORE 0x103
#define VLC_MODULE_CB_OPEN 0x104
#define VLC_MODULE_CB_CLOSE 0x105

/* Simplified vlc_set callback function */
typedef int (*vlc_set_cb)(void *opaque, void *target, int property, ...);

/* Plugin entry point signature */
typedef int (*vlc_entry_fn)(vlc_set_cb vlc_set, void *opaque);

/* Callback function pointers stored during module initialization */
static int (*stored_open)(void*) = NULL;
static void (*stored_close)(void*) = NULL;
static const char* stored_name = NULL;
static const char* stored_capability = NULL;
static int stored_score = 0;

/* Simulated vlc_set function */
static int test_vlc_set(void *opaque, void *target, int property, ...)
{
    va_list args;
    va_start(args, property);

    switch (property)
    {
        case VLC_MODULE_CREATE:
        {
            void **module_ptr = va_arg(args, void**);
            *module_ptr = (void*)0x12345678; /* Fake module pointer */
            printf("[test] VLC_MODULE_CREATE\n");
            break;
        }
        case VLC_MODULE_NAME:
        {
            const char *name = va_arg(args, const char*);
            stored_name = name;
            printf("[test] VLC_MODULE_NAME: %s\n", name);
            break;
        }
        case VLC_MODULE_SHORTNAME:
        {
            const char *name = va_arg(args, const char*);
            printf("[test] VLC_MODULE_SHORTNAME: %s\n", name);
            break;
        }
        case VLC_MODULE_DESCRIPTION:
        {
            const char *desc = va_arg(args, const char*);
            printf("[test] VLC_MODULE_DESCRIPTION: %s\n", desc);
            break;
        }
        case VLC_MODULE_CAPABILITY:
        {
            const char *cap = va_arg(args, const char*);
            stored_capability = cap;
            printf("[test] VLC_MODULE_CAPABILITY: %s\n", cap);
            break;
        }
        case VLC_MODULE_SCORE:
        {
            int score = va_arg(args, int);
            stored_score = score;
            printf("[test] VLC_MODULE_SCORE: %d\n", score);
            break;
        }
        case VLC_MODULE_CB_OPEN:
        {
            const char *name = va_arg(args, const char*);
            void *fn = va_arg(args, void*);
            stored_open = (int (*)(void*))fn;
            printf("[test] VLC_MODULE_CB_OPEN: %s at %p\n", name, fn);
            break;
        }
        case VLC_MODULE_CB_CLOSE:
        {
            const char *name = va_arg(args, const char*);
            void *fn = va_arg(args, void*);
            stored_close = (void (*)(void*))fn;
            printf("[test] VLC_MODULE_CB_CLOSE: %s at %p\n", name, fn);
            break;
        }
        default:
            printf("[test] Unknown property: 0x%x\n", property);
            break;
    }

    va_end(args);
    return 0;
}

int main(int argc, char **argv)
{
    printf("=== VLC .NET Plugin Test Harness ===\n\n");

    /* Load the C glue plugin DLL */
    printf("[1] Loading libdotnet_bridge_plugin.dll...\n");
    HMODULE glue = LoadLibraryA("libdotnet_bridge_plugin.dll");
    if (!glue)
    {
        printf("ERROR: Failed to load libdotnet_bridge_plugin.dll (error %lu)\n", GetLastError());
        return 1;
    }
    printf("    Loaded at %p\n", glue);

    /* Get the vlc_entry function */
    printf("\n[2] Resolving vlc_entry...\n");
    vlc_entry_fn entry = (vlc_entry_fn)GetProcAddress(glue, "vlc_entry");
    if (!entry)
    {
        printf("ERROR: Failed to find vlc_entry\n");
        FreeLibrary(glue);
        return 1;
    }
    printf("    Found at %p\n", entry);

    /* Call vlc_entry to initialize module */
    printf("\n[3] Calling vlc_entry to initialize module...\n");
    int result = entry(test_vlc_set, NULL);
    if (result != 0)
    {
        printf("ERROR: vlc_entry returned %d\n", result);
        FreeLibrary(glue);
        return 1;
    }
    printf("    Module initialized successfully\n");

    /* Verify we got the callbacks */
    printf("\n[4] Verifying callbacks...\n");
    if (!stored_open || !stored_close)
    {
        printf("ERROR: Open or Close callback not registered\n");
        FreeLibrary(glue);
        return 1;
    }
    printf("    Open callback: %p\n", stored_open);
    printf("    Close callback: %p\n", stored_close);

    /* Create a fake VLC object for testing */
    void *fake_vlc_obj = (void*)0xDEADBEEF;

    /* Call Open (this will load VlcPlugin.dll and call .NET) */
    printf("\n[5] Calling Open callback...\n");
    result = stored_open(fake_vlc_obj);
    printf("    Open returned: %d\n", result);
    if (result != 0)
    {
        printf("WARNING: Open failed, but continuing to test Close\n");
    }

    /* Call Close */
    printf("\n[6] Calling Close callback...\n");
    stored_close(fake_vlc_obj);
    printf("    Close completed\n");

    /* Cleanup */
    printf("\n[7] Unloading plugin DLL...\n");
    FreeLibrary(glue);

    printf("\n=== Test Complete ===\n");
    return 0;
}
