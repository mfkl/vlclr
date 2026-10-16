// Platform-dependent parts of the VLC 4 native layout
// Source: vlc/include/vlc_es.h
// VLC Version: 4.0.6

using System.Runtime.CompilerServices;

namespace VLCLR.Native;

/// <summary>
/// Native sizes that differ between the Windows ABI and the Linux/macOS ABI.
/// </summary>
/// <remarks>
/// video_format_t ends with a <c>dovi</c> struct that mixes uint8_t members
/// with unsigned bitfields. Windows compilers (MSVC, MinGW and llvm-mingw)
/// start the bitfields in a new 4-byte unit; GCC and Clang on Linux and macOS
/// pack them after the version bytes. video_format_t is therefore 152 bytes on
/// Windows and 144 elsewhere, and every struct embedding it shifts.
/// <see cref="OperatingSystem.IsWindows"/> is a constant under Native AOT, so
/// these offsets cost nothing in a published plugin.
/// tests/AbiValidation/vlclr_abi.cpp checks the numbers against VLC's headers.
/// </remarks>
internal static class VLCAbi
{
    /// <summary>sizeof(video_format_t)</summary>
    public static int VideoFormatSize => OperatingSystem.IsWindows() ? 152 : 144;

    /// <summary>sizeof(es_format_t): 56 bytes before the format union, 32 after.</summary>
    public static int EsFormatSize => 88 + VideoFormatSize;

    /// <summary>
    /// Returns the field of type <typeparamref name="TField"/> at
    /// <paramref name="offset"/> bytes into <paramref name="owner"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref TField Field<TOwner, TField>(ref TOwner owner, int offset)
        => ref Unsafe.As<byte, TField>(
            ref Unsafe.AddByteOffset(ref Unsafe.As<TOwner, byte>(ref owner), (nint)offset));
}
