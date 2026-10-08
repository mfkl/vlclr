using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Builds a C <c>va_list</c> over 8-byte argument slots for VLC's fixed-signature
/// <c>*_vaControl</c> exports.
/// </summary>
/// <remarks>
/// Each slot holds one argument as a variadic call would pass it: an integer
/// widened to 64 bits, a pointer, or the bits of a double. On Win64 and Apple
/// arm64 a <c>va_list</c> is a pointer to those slots. On System V x86-64 it is a
/// pointer to a <c>__va_list_tag</c>; marking both register save areas as
/// consumed makes <c>va_arg</c> read every argument, including doubles, from the
/// overflow area. The slots and tag must stay alive until the native call returns.
/// </remarks>
public static unsafe class VLCVaList
{
    // Six general-purpose registers (6 * 8) and eight vector registers (8 * 16).
    private const uint SystemVGeneralRegistersConsumed = 48;
    private const uint SystemVVectorRegistersConsumed = 176;

    /// <summary>The System V x86-64 <c>__va_list_tag</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemVTag
    {
        public uint GeneralOffset;
        public uint VectorOffset;
        public nint OverflowArea;
        public nint RegisterSaveArea;
    }

    /// <summary>Returns the <c>va_list</c> value describing <paramref name="slots"/>.</summary>
    /// <param name="slots">Caller-owned argument slots.</param>
    /// <param name="tag">Caller-owned storage, used on System V targets.</param>
    public static nint Create(long* slots, SystemVTag* tag)
    {
        VLCPlatform.EnsureSupported(nameof(VLCVaList));
        if (!UsesSystemVTag)
            return (nint)slots;
        *tag = new SystemVTag
        {
            GeneralOffset = SystemVGeneralRegistersConsumed,
            VectorOffset = SystemVVectorRegistersConsumed,
            OverflowArea = (nint)slots,
        };
        return (nint)tag;
    }

    /// <summary>
    /// Reads the first argument of a <c>va_list</c> VLC passed in, such as the
    /// configuration pointer of a video output control query, without consuming it.
    /// </summary>
    public static nint PeekPointer(nint vaList)
    {
        VLCPlatform.EnsureSupported(nameof(VLCVaList));
        if (!UsesSystemVTag)
            return *(nint*)vaList;
        SystemVTag* tag = (SystemVTag*)vaList;
        return tag->GeneralOffset < SystemVGeneralRegistersConsumed
            ? *(nint*)(tag->RegisterSaveArea + (nint)tag->GeneralOffset)
            : *(nint*)tag->OverflowArea;
    }

    // Windows x64 and Apple arm64 use a plain pointer to the argument slots.
    private static bool UsesSystemVTag =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64 && !OperatingSystem.IsWindows();

    /// <summary>The slot value of a double argument.</summary>
    public static long Double(double value) => BitConverter.DoubleToInt64Bits(value);
}
