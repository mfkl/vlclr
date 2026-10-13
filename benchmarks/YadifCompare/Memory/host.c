/* Memory cost of a yadif DLL: loads the DLL given on the command line, runs
 * yadif_render on three 1080p 8-bit frames the way the harness does, and
 * prints the process's memory at each step. The same host runs VLC's C
 * (cyadif.dll from the harness) and the C# filter (YadifNative.dll, Native
 * AOT, same export), so the difference is the filter and what it brings.
 *
 *   host <dll> <frames-8bit.yuv> <impl> <frames>
 */

#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <psapi.h>
#include <tlhelp32.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct
{
    uint8_t *p_pixels;
    int i_lines;
    int i_pitch;
    int i_pixel_pitch;
    int i_visible_lines;
    int i_visible_pitch;
} plane_t;

typedef void (*render_fn)(int impl, int planes, plane_t *dst, const plane_t *prev,
                          const plane_t *cur, const plane_t *next, int field, int parity);
typedef void (*gc_info_fn)(int64_t *allocated, int *collections, int64_t *heap);

enum { W = 1920, H = 1080, SPARE_LINES = 8 };

static int Threads(void)
{
    DWORD pid = GetCurrentProcessId();
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    THREADENTRY32 te = { .dwSize = sizeof(te) };
    int n = 0;
    if (Thread32First(snap, &te))
        do { if (te.th32OwnerProcessID == pid) n++; } while (Thread32Next(snap, &te));
    CloseHandle(snap);
    return n;
}

static void Report(const char *step)
{
    PROCESS_MEMORY_COUNTERS_EX pmc = { .cb = sizeof(pmc) };
    GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS *)&pmc, sizeof(pmc));
    printf("%s\t%zu\t%zu\t%zu\t%d\n", step, pmc.PrivateUsage / 1024, pmc.WorkingSetSize / 1024,
           pmc.PeakWorkingSetSize / 1024, Threads());
}

/* A 4:2:0 picture laid out like the harness's: pitch rounded up to 64,
 * spare lines below. */
static void NewPicture(plane_t planes[3])
{
    for (int i = 0; i < 3; i++)
    {
        int w = i ? W / 2 : W, h = i ? H / 2 : H;
        int pitch = (w + 63) & ~63;
        size_t size = (size_t)pitch * (h + SPARE_LINES);
        planes[i].p_pixels = _aligned_malloc(size, 64);
        memset(planes[i].p_pixels, 0, size);
        planes[i].i_lines = h + SPARE_LINES;
        planes[i].i_pitch = pitch;
        planes[i].i_pixel_pitch = 1;
        planes[i].i_visible_lines = h;
        planes[i].i_visible_pitch = w;
    }
}

int main(int argc, char **argv)
{
    if (argc < 5)
    {
        fprintf(stderr, "host <dll> <frames-8bit.yuv> <impl> <frames>\n");
        return 2;
    }
    int impl = atoi(argv[3]), count = atoi(argv[4]);
    printf("step\tprivate KiB\tworking set KiB\tpeak working set KiB\tthreads\n");
    Report("start");

    plane_t pictures[4][3];
    for (int p = 0; p < 4; p++)
        NewPicture(pictures[p]);
    FILE *f = fopen(argv[2], "rb");
    if (!f) { perror(argv[2]); return 1; }
    for (int p = 0; p < 3; p++)
        for (int i = 0; i < 3; i++)
            for (int y = 0; y < pictures[p][i].i_visible_lines; y++)
                if (fread(pictures[p][i].p_pixels + (size_t)y * pictures[p][i].i_pitch, 1,
                          pictures[p][i].i_visible_pitch, f) != (size_t)pictures[p][i].i_visible_pitch)
                { fprintf(stderr, "short read\n"); return 1; }
    fclose(f);
    Report("pictures allocated");

    HMODULE dll = LoadLibraryA(argv[1]);
    if (!dll) { fprintf(stderr, "LoadLibrary failed: %lu\n", GetLastError()); return 1; }
    render_fn render = (render_fn)GetProcAddress(dll, "yadif_render");
    gc_info_fn gc_info = (gc_info_fn)GetProcAddress(dll, "yadif_gc_info");
    if (!render) { fprintf(stderr, "no yadif_render\n"); return 1; }
    Report("dll loaded");

    render(impl, 3, pictures[3], pictures[0], pictures[1], pictures[2], 0, 1);
    Report("first frame");
    int64_t allocated_first = 0, heap;
    int collections;
    if (gc_info)
        gc_info(&allocated_first, &collections, &heap);

    for (int n = 1; n < count; n++)
        render(impl, 3, pictures[3], pictures[0], pictures[1], pictures[2], n & 1, (n & 1) ^ 1);
    char step[64];
    snprintf(step, sizeof(step), "after %d frames", count);
    Report(step);

    if (gc_info)
    {
        int64_t allocated;
        gc_info(&allocated, &collections, &heap);
        printf("gc\tallocated %lld bytes by the first frame, %lld more in the next %d\tcollections %d\theap %lld bytes\n",
               (long long)allocated_first, (long long)(allocated - allocated_first), count - 1, collections, (long long)heap);
    }
    return 0;
}
