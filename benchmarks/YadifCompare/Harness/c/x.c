/* RenderX of modules/video_filter/deinterlace/algo_x.c (compiled from a VLC
 * checkout, unchanged, as a separate unit), the algorithm VLC's yadif renders
 * its first picture with, on planes laid out by the harness.
 *
 * algo_x.c is LGPL-2.1-or-later. */

#include <vlc_common.h>
#include <vlc_picture.h>

#include "algo_x.h"

__declspec(dllexport) void x_render(int planes, plane_t *dst, const plane_t *src)
{
    picture_t out = { 0 }, in = { 0 };
    out.i_planes = in.i_planes = planes;
    for (int n = 0; n < planes; n++)
    {
        out.p[n] = dst[n];
        in.p[n] = src[n];
    }
    RenderX(NULL, &out, &in);
}
