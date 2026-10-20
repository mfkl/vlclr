/* Minimal config.h to compile VLC module sources outside VLC's build (clang,
 * MSVC target), after VLC's config.h.meson: for asmlab's disassembly, and
 * for the harness's DLL. Every replacement function in vlc_fixups.h is
 * declared present, so a module that calls one fails to link: none of the
 * code the harness runs does. */
#define PACKAGE "vlc"
#define PACKAGE_NAME "vlc"
#define VERSION "4.0.0-dev"
#define PACKAGE_VERSION "4.0.0-dev"
#define _WIN32_WINNT 0x0601
#define _FILE_OFFSET_BITS 64
#define ssize_t ptrdiff_t
#define HAVE_ATOF 1
#define HAVE_ATOLL 1
#define HAVE_CLOCK_GETRES 1
#define HAVE_CLOCK_GETTIME 1
#define HAVE_CLOCK_NANOSLEEP 1
#define HAVE_DIRFD 1
#define HAVE_FDOPENDIR 1
#define HAVE_FLOCKFILE 1
#define HAVE_FORK 1
#define HAVE_FSYNC 1
#define HAVE_GETDELIM 1
#define HAVE_GETENV 1
#define HAVE_GETPID 1
#define HAVE_GMTIME_R 1
#define HAVE_IF_NAMEINDEX 1
#define HAVE_IF_NAMETOINDEX 1
#define HAVE_INET_PTON 1
#define HAVE_LLDIV 1
#define HAVE_LOCALTIME_R 1
#define HAVE_MAX_ALIGN_T 1
#define HAVE_MEMRCHR 1
#define HAVE_NANF 1
#define HAVE_NEWLOCALE 1
#define HAVE_NRAND48 1
#define HAVE_POLL 1
#define HAVE_POSIX_MEMALIGN 1
#define HAVE_QSORT_R 1
#define HAVE_READV 1
#define HAVE_REALPATH 1
#define HAVE_RECVMSG 1
#define HAVE_REWIND 1
#define HAVE_SENDMSG 1
#define HAVE_SETENV 1
#define HAVE_SINCOS 1
#define HAVE_STRCASESTR 1
#define HAVE_STRDUP 1
#define HAVE_STRLCPY 1
#define HAVE_STRNDUP 1
#define HAVE_STRNLEN 1
#define HAVE_STRNSTR 1
#define HAVE_STRSEP 1
#define HAVE_STRTOF 1
#define HAVE_STRTOLL 1
#define HAVE_STRUCT_TIMESPEC 1
#define HAVE_STRVERSCMP 1
#define HAVE_SWAB 1
#define HAVE_TDESTROY 1
#define HAVE_TFIND 1
#define HAVE_THREAD_LOCAL 1
#define HAVE_TIMEGM 1
#define HAVE_TIMESPEC_GET 1
#define HAVE_USELOCALE 1
#define HAVE_WRITEV 1
#include <sys/types.h>
#include <vlc_fixups.h>
