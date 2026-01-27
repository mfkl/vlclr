/**
 * VLC Video Filter Plugin Entry Point
 * C glue that bridges VLC's video filter system to .NET Native AOT code.
 *
 * This is a separate plugin from the interface plugin (plugin_entry.c).
 * Both plugins share the same VlcPlugin.dll for .NET code.
 */

/* VLC requires MODULE_NAME and VLC_DYNAMIC_PLUGIN before headers */
#define MODULE_NAME dotnet_overlay
#define VLC_DYNAMIC_PLUGIN

/* LGPL 2.1+ license for VLC compatibility */
#define VLC_MODULE_LICENSE VLC_LICENSE_LGPL_2_1_PLUS

/* POSIX types needed by VLC headers on Windows */
#ifdef _WIN32
#include <sys/types.h>
#include <basetsd.h>
typedef SSIZE_T ssize_t;
#ifndef _OFF_T_DEFINED
typedef long off_t;
#define _OFF_T_DEFINED
#endif
#endif

#include <vlc_common.h>
#include <vlc_plugin.h>
#include <vlc_filter.h>
#include <vlc_picture.h>

#include <stdint.h>
#include <stdio.h>
#include <string.h>

#ifdef _WIN32
#include <windows.h>
#define FILTER_HANDLE HMODULE
#define FILTER_LOAD(path) LoadLibraryA(path)
#define FILTER_SYMBOL(h, name) GetProcAddress(h, name)
#define FILTER_UNLOAD(h) FreeLibrary(h)
#else
#include <dlfcn.h>
#define FILTER_HANDLE void*
#define FILTER_LOAD(path) dlopen(path, RTLD_NOW | RTLD_LOCAL)
#define FILTER_SYMBOL(h, name) dlsym(h, name)
#define FILTER_UNLOAD(h) dlclose(h)
#endif

/* Module name for logging */
static const char filter_module_name[] = "dotnet_overlay";

/* .NET filter function pointer types */
typedef int (*dotnet_filter_open_fn)(void* filter, int width, int height, uint32_t chroma);
typedef void (*dotnet_filter_close_fn)(void* filter);
typedef void (*dotnet_filter_frame_fn)(void* filter,
    uint8_t* pixels, int pitch, int visible_pitch, int visible_lines,
    uint32_t chroma);

/* Static function pointers resolved from VlcPlugin.dll */
static FILTER_HANDLE filter_dotnet_dll = NULL;
static dotnet_filter_open_fn dotnet_filter_open = NULL;
static dotnet_filter_close_fn dotnet_filter_close = NULL;
static dotnet_filter_frame_fn dotnet_filter_frame = NULL;

/* Filter private data */
typedef struct {
    int initialized;
    int frame_count;
} filter_sys_t;

/* Get the directory path of this DLL */
#ifdef _WIN32
static int get_filter_plugin_directory(char *buf, size_t bufsize)
{
    HMODULE hModule = NULL;

    /* Get handle to this DLL by using a function address inside it */
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            (LPCSTR)&get_filter_plugin_directory, &hModule))
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

/* Load VlcPlugin.dll and resolve filter exports */
static int filter_load_dotnet(vlc_object_t *obj)
{
    if (filter_dotnet_dll != NULL)
    {
        /* Already loaded */
        return 0;
    }

#ifdef _WIN32
    /* First try: Look in same directory as this filter plugin (video_filter/) */
    char plugin_dir[MAX_PATH];
    char plugin_path[MAX_PATH];

    if (get_filter_plugin_directory(plugin_dir, sizeof(plugin_dir)) == 0)
    {
        snprintf(plugin_path, sizeof(plugin_path), "%s\\VlcPlugin.dll", plugin_dir);
        msg_Dbg(obj, "Trying to load .NET DLL from: %s", plugin_path);
        filter_dotnet_dll = LoadLibraryA(plugin_path);
    }

    /* Second try: Look in control/ directory (where interface plugin lives) */
    if (!filter_dotnet_dll && get_filter_plugin_directory(plugin_dir, sizeof(plugin_dir)) == 0)
    {
        /* Go up one level from video_filter/ to plugins/, then into control/ */
        char *lastSlash = strrchr(plugin_dir, '\\');
        if (lastSlash == NULL)
            lastSlash = strrchr(plugin_dir, '/');
        if (lastSlash != NULL)
        {
            *lastSlash = '\0';
            snprintf(plugin_path, sizeof(plugin_path), "%s\\control\\VlcPlugin.dll", plugin_dir);
            msg_Dbg(obj, "Trying to load .NET DLL from: %s", plugin_path);
            filter_dotnet_dll = LoadLibraryA(plugin_path);
        }
    }

    /* Fallback to just the filename */
    if (!filter_dotnet_dll)
    {
        msg_Dbg(obj, "Path-based loading failed, trying direct load");
        filter_dotnet_dll = LoadLibraryA("VlcPlugin.dll");
    }
#else
    filter_dotnet_dll = FILTER_LOAD("./VlcPlugin.dll");
    if (!filter_dotnet_dll)
        filter_dotnet_dll = FILTER_LOAD("VlcPlugin.dll");
#endif

    if (!filter_dotnet_dll)
    {
        msg_Err(obj, "Failed to load VlcPlugin.dll for video filter");
        return -1;
    }

    /* Resolve filter exports */
    dotnet_filter_open = (dotnet_filter_open_fn)FILTER_SYMBOL(filter_dotnet_dll, "DotNetFilterOpen");
    dotnet_filter_close = (dotnet_filter_close_fn)FILTER_SYMBOL(filter_dotnet_dll, "DotNetFilterClose");
    dotnet_filter_frame = (dotnet_filter_frame_fn)FILTER_SYMBOL(filter_dotnet_dll, "DotNetFilterFrame");

    if (!dotnet_filter_open || !dotnet_filter_close || !dotnet_filter_frame)
    {
        msg_Err(obj, "Failed to resolve .NET filter exports: open=%p close=%p frame=%p",
                (void*)dotnet_filter_open, (void*)dotnet_filter_close, (void*)dotnet_filter_frame);
        FILTER_UNLOAD(filter_dotnet_dll);
        filter_dotnet_dll = NULL;
        return -1;
    }

    msg_Info(obj, "Successfully loaded VlcPlugin.dll for video filter");
    return 0;
}

/* Video filter callback - processes each frame */
static picture_t *Filter(filter_t *filter, picture_t *pic)
{
    if (pic == NULL)
        return NULL;

    filter_sys_t *sys = filter->p_sys;
    if (sys == NULL || !sys->initialized)
    {
        return pic;  /* Pass through if not initialized */
    }

    /* Log every 100 frames */
    sys->frame_count++;
    if (sys->frame_count % 100 == 0) {
        msg_Info(VLC_OBJECT(filter), ".NET Overlay: Frame %d", sys->frame_count);
    }

    /* Get frame info */
    video_format_t *fmt = &filter->fmt_in.video;
    uint32_t chroma = fmt->i_chroma;

    /* Write first frame info to debug file */
    if (sys->frame_count == 1) {
        FILE *debug_file = fopen("dotnet_filter_frame.txt", "w");
        if (debug_file) {
            fprintf(debug_file, "First frame processed!\n");
            fprintf(debug_file, "chroma=0x%08X (%c%c%c%c)\n",
                    chroma,
                    (char)(chroma & 0xFF),
                    (char)((chroma >> 8) & 0xFF),
                    (char)((chroma >> 16) & 0xFF),
                    (char)((chroma >> 24) & 0xFF));
            fprintf(debug_file, "planes=%d pitch=%d visible_pitch=%d visible_lines=%d\n",
                    pic->i_planes,
                    pic->i_planes > 0 ? pic->p[0].i_pitch : 0,
                    pic->i_planes > 0 ? pic->p[0].i_visible_pitch : 0,
                    pic->i_planes > 0 ? pic->p[0].i_visible_lines : 0);
            fclose(debug_file);
        }
    }

    /* Check if we have accessible planes */
    if (pic->i_planes == 0) {
        /* GPU opaque format (like DX11) - cannot modify directly */
        if (sys->frame_count == 1) {
            msg_Warn(VLC_OBJECT(filter), ".NET Overlay: Opaque format (0 planes), cannot draw overlay");
        }
        return pic;  /* Pass through unmodified */
    }

    /* Get output picture - we'll modify it */
    picture_t *outpic = filter_NewPicture(filter);
    if (outpic == NULL)
    {
        picture_Release(pic);
        return NULL;
    }

    /* Copy input to output */
    picture_Copy(outpic, pic);

    /* Call .NET/ImageSharp for frame processing */
    if (outpic->i_planes > 0 && outpic->p[0].p_pixels != NULL && dotnet_filter_frame)
    {
        plane_t *plane = &outpic->p[0];
        dotnet_filter_frame(filter,
            plane->p_pixels,
            plane->i_pitch,
            plane->i_visible_pitch,
            plane->i_visible_lines,
            chroma);
    }

    /* Copy properties and release input */
    picture_CopyProperties(outpic, pic);
    picture_Release(pic);

    return outpic;
}

/* Close callback */
static void Close(filter_t *filter)
{
    filter_sys_t *sys = filter->p_sys;
    int total_frames = sys ? sys->frame_count : 0;

    msg_Info(VLC_OBJECT(filter), ".NET Overlay: Closing after %d frames", total_frames);

    if (dotnet_filter_close)
    {
        dotnet_filter_close(filter);
    }

    if (sys)
    {
        free(sys);
        filter->p_sys = NULL;
    }

    /* Note: We don't unload the DLL here because other filters may use it */
}

/* Filter operations structure */
static const struct vlc_filter_operations filter_ops = {
    .filter_video = Filter,
    .close = Close,
};

/* Open callback - called when VLC activates the filter */
static int Open(filter_t *filter)
{
    video_format_t *fmt = &filter->fmt_in.video;
    uint32_t chroma = fmt->i_chroma;

    /* FIRST THING: File-based debug proof that Open() is being called */
    FILE *debug_file = fopen("dotnet_filter_open.txt", "w");
    if (debug_file) {
        fprintf(debug_file, "Open called at filter=%p\n", (void*)filter);
        fprintf(debug_file, "chroma=0x%08X (%c%c%c%c)\n",
                chroma,
                (char)(chroma & 0xFF),
                (char)((chroma >> 8) & 0xFF),
                (char)((chroma >> 16) & 0xFF),
                (char)((chroma >> 24) & 0xFF));
        fprintf(debug_file, "size=%dx%d\n", fmt->i_width, fmt->i_height);
        fclose(debug_file);
    }

    msg_Info(VLC_OBJECT(filter), ".NET Overlay: Open called, format %c%c%c%c %dx%d",
             (char)(chroma & 0xFF),
             (char)((chroma >> 8) & 0xFF),
             (char)((chroma >> 16) & 0xFF),
             (char)((chroma >> 24) & 0xFF),
             fmt->i_width, fmt->i_height);

    /* Validate chroma format using VLC's fourcc description */
    const vlc_chroma_description_t *chroma_desc = vlc_fourcc_GetChromaDescription(chroma);
    if (chroma_desc == NULL) {
        msg_Warn(VLC_OBJECT(filter), ".NET Overlay: Unknown chroma format 0x%08X, proceeding anyway", chroma);
        /* Don't reject - let's try to handle it */
    } else if (chroma_desc->plane_count == 0) {
        msg_Warn(VLC_OBJECT(filter), ".NET Overlay: Chroma has no planes, proceeding anyway");
        /* Don't reject - opaque formats might still work */
    } else {
        msg_Info(VLC_OBJECT(filter), ".NET Overlay: Chroma has %u planes, pixel_size=%u, pixel_bits=%u",
                 chroma_desc->plane_count, chroma_desc->pixel_size, chroma_desc->pixel_bits);
    }

    /* Load the .NET DLL */
    if (filter_load_dotnet(VLC_OBJECT(filter)) != 0)
    {
        msg_Err(VLC_OBJECT(filter), ".NET Overlay: Failed to load VlcPlugin.dll");
        return VLC_EGENERIC;
    }

    /* Allocate private data */
    filter_sys_t *sys = calloc(1, sizeof(filter_sys_t));
    if (sys == NULL)
    {
        return VLC_ENOMEM;
    }

    filter->p_sys = sys;

    /* Output format is same as input (we modify in place) */
    filter->fmt_out = filter->fmt_in;

    /* Initialize .NET filter */
    if (dotnet_filter_open)
    {
        int result = dotnet_filter_open(filter, fmt->i_width, fmt->i_height, chroma);
        if (result != 0)
        {
            msg_Err(VLC_OBJECT(filter), ".NET Overlay: .NET filter init failed: %d", result);
            free(sys);
            return VLC_EGENERIC;
        }
    }

    sys->initialized = 1;
    filter->ops = &filter_ops;

    msg_Info(VLC_OBJECT(filter), ".NET Overlay: Filter opened successfully");
    return VLC_SUCCESS;
}

/* VLC module descriptor */
vlc_module_begin()
    set_shortname(".NET Overlay")
    set_description(".NET Native AOT Video Filter Overlay")
    set_subcategory(SUBCAT_VIDEO_VFILTER)
    add_shortcut("dotnet_overlay", "dotnet", "netoverlay")
    set_callback_video_filter(Open)
vlc_module_end()
