using System.Runtime.InteropServices;

namespace VLCLR.Native;

/// <summary>
/// Calls C variadic functions such as <c>vlc_set</c> through fixed-signature
/// function pointers.
/// </summary>
/// <remarks>
/// On Win64 and System V x86-64 a variadic callee finds integer and pointer
/// arguments where a fixed-signature call puts them. Apple arm64 differs: fixed
/// arguments travel in x0-x7, but every variadic argument goes on the stack in
/// its own 8-byte slot. Padding the fixed-signature call with zeros up to x7
/// moves the remaining arguments to the stack, and passing each as a 64-bit
/// integer gives them exactly those slots.
/// </remarks>
public static unsafe class VLCVariadic
{
    /// <summary>True where variadic arguments go on the stack (Apple arm64).</summary>
    public static bool PassesOnStack =>
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>
    /// Calls <c>int f(a0, a1, a2, ...)</c> with up to three integer or pointer
    /// variadic arguments. Unused slots are passed as zero and ignored by the
    /// callee. On Apple arm64 a slot may also hold the bits of a double
    /// (<see cref="VLCVaList.Double"/>); elsewhere doubles need a signature that
    /// passes them in vector registers.
    /// </summary>
    public static int Call(nint function, nint a0, nint a1, nint a2, long v0 = 0, long v1 = 0, long v2 = 0)
    {
        VLCPlatform.EnsureSupported(nameof(VLCVariadic));
        if (PassesOnStack)
            return ((delegate* unmanaged[Cdecl]<nint, nint, nint, long, long, long, long, long, long, long, long, int>)function)(
                a0, a1, a2, 0, 0, 0, 0, 0, v0, v1, v2);
        return ((delegate* unmanaged[Cdecl]<nint, nint, nint, long, long, long, int>)function)(a0, a1, a2, v0, v1, v2);
    }
}
