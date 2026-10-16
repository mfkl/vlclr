// Calls to variadic C functions such as vlc_set
// VLC Version: 4.0.6

using System.Runtime.InteropServices;

namespace VLCLR.Module;

/// <summary>
/// Calls a C function that is variadic after three named arguments, such as
/// VLC's <c>vlc_set(opaque, target, property, ...)</c>, with up to two
/// variable arguments.
/// </summary>
/// <remarks>
/// C# calls function pointers with fixed signatures, and a variadic callee
/// reads its variable arguments differently on each ABI: Apple arm64 reads
/// all of them from 8-byte stack slots, Windows reads floating-point ones from
/// integer registers, and the others read them like named arguments. System V
/// x64 also needs AL to hold the number of vector registers used, which C#
/// cannot set, so <see cref="CallDouble"/> is unreliable there
/// (<see cref="PassesDoublesReliably"/>). The ABI checks are constants under
/// Native AOT.
/// </remarks>
internal static unsafe class VLCVariadic
{
    internal static bool IsAppleArm64 =>
        (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS())
        && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>
    /// False on System V x64, where a variadic callee may ignore the vector
    /// registers that carry <see cref="CallDouble"/>'s arguments.
    /// </summary>
    internal static bool PassesDoublesReliably =>
        OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64;

    /// <summary>
    /// Calls <paramref name="function"/> with integer or pointer variable
    /// arguments (a C <c>int</c> reads the low 32 bits of its slot).
    /// </summary>
    public static int Call(nint function, nint arg0, nint arg1, nint arg2, long first = 0, long second = 0)
    {
        if (IsAppleArm64)
        {
            return ((delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, nint, nint, nint, long, long, int>)function)(
                arg0, arg1, arg2, 0, 0, 0, 0, 0, first, second);
        }
        return ((delegate* unmanaged[Cdecl]<nint, nint, nint, long, long, int>)function)(arg0, arg1, arg2, first, second);
    }

    /// <summary>
    /// Calls <paramref name="function"/> with <c>double</c> variable arguments.
    /// </summary>
    public static int CallDouble(nint function, nint arg0, nint arg1, nint arg2, double first, double second = 0)
    {
        if (OperatingSystem.IsWindows() || IsAppleArm64)
        {
            return Call(function, arg0, arg1, arg2,
                BitConverter.DoubleToInt64Bits(first), BitConverter.DoubleToInt64Bits(second));
        }
        return ((delegate* unmanaged[Cdecl]<nint, nint, nint, double, double, int>)function)(arg0, arg1, arg2, first, second);
    }
}
