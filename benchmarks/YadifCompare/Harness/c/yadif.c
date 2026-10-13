/* VLC's yadif as the deinterlace plugin runs it: the line kernels of
 * modules/video_filter/deinterlace/yadif.h (included from a VLC checkout,
 * unchanged) and yadif_x86.asm (assembled separately), driven per plane and
 * per line by a copy of the loop of RenderYadif in algo_yadif.c.
 *
 * yadif.h is GPL-2.0-or-later (FFmpeg); algo_yadif.c is LGPL-2.1-or-later. */

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

/* common.h of the deinterlace module */
#define FFMAX(a,b)      __MAX(a,b)
#define FFMAX3(a,b,c)   FFMAX(FFMAX(a,b),c)
#define FFMIN(a,b)      __MIN(a,b)
#define FFMIN3(a,b,c)   FFMIN(FFMIN(a,b),c)
/* vlc_common.h */
#define __MAX(a, b)   ( ((a) > (b)) ? (a) : (b) )
#define __MIN(a, b)   ( ((a) < (b)) ? (a) : (b) )

#include "yadif.h"

#define EXPORT __declspec(dllexport)

/* plane_t of vlc_picture.h */
typedef struct
{
    uint8_t *p_pixels;
    int i_lines;
    int i_pitch;
    int i_pixel_pitch;
    int i_visible_lines;
    int i_visible_pitch;
} plane_t;

typedef void (*filter_fn)(uint8_t *dst, uint8_t *prev, uint8_t *cur, uint8_t *next,
                          int w, int prefs, int mrefs, int parity, int mode);

enum
{
    IMPL_C,
    IMPL_SSE2,
    IMPL_SSSE3,
    IMPL_C_16BIT,
};

static filter_fn Filter(int impl)
{
    switch (impl)
    {
        case IMPL_SSE2:    return vlcpriv_yadif_filter_line_sse2;
        case IMPL_SSSE3:   return vlcpriv_yadif_filter_line_ssse3;
        case IMPL_C_16BIT: return yadif_filter_line_c_16bit;
        default:           return yadif_filter_line_c;
    }
}

/* The body of RenderYadif once p_prev, p_cur and p_next are all there,
 * with the kernel chosen by the caller instead of by CPU detection. */
EXPORT void yadif_render(int impl, int planes, plane_t *dst, const plane_t *prev,
                         const plane_t *cur, const plane_t *next, int i_field, int yadif_parity)
{
    filter_fn filter = Filter(impl);

    for( int n = 0; n < planes; n++ )
    {
        const plane_t *prevp = &prev[n];
        const plane_t *curp  = &cur[n];
        const plane_t *nextp = &next[n];
        plane_t *dstp        = &dst[n];

        for( int y = 1; y < dstp->i_visible_lines - 1; y++ )
        {
            if( (y % 2) == i_field  ||  yadif_parity == 2 )
            {
                memcpy( &dstp->p_pixels[y * dstp->i_pitch],
                            &curp->p_pixels[y * curp->i_pitch], dstp->i_visible_pitch );
            }
            else
            {
                int mode;
                /* Spatial checks only when enough data */
                mode = (y >= 2 && y < dstp->i_visible_lines - 2) ? 0 : 2;

                filter( &dstp->p_pixels[y * dstp->i_pitch],
                        &prevp->p_pixels[y * prevp->i_pitch],
                        &curp->p_pixels[y * curp->i_pitch],
                        &nextp->p_pixels[y * nextp->i_pitch],
                        dstp->i_visible_pitch,
                        y < dstp->i_visible_lines - 2  ? curp->i_pitch : -curp->i_pitch,
                        y  - 1  ?  -curp->i_pitch : curp->i_pitch,
                        yadif_parity,
                        mode );
            }

            /* We duplicate the first and last lines */
            if( y == 1 )
                memcpy(&dstp->p_pixels[(y-1) * dstp->i_pitch],
                           &dstp->p_pixels[ y    * dstp->i_pitch],
                           dstp->i_pitch);
            else if( y == dstp->i_visible_lines - 2 )
                memcpy(&dstp->p_pixels[(y+1) * dstp->i_pitch],
                           &dstp->p_pixels[ y    * dstp->i_pitch],
                           dstp->i_pitch);
        }
    }
}
