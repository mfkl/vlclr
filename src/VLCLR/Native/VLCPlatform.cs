using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCLR.Native;

/// <summary>The 64-bit VLC 3 targets whose native calling conventions VLCLR implements.</summary>
public static class VLCPlatform
{
    /// <summary>True on Windows x64, Linux x64, and macOS on x64 or arm64.</summary>
    [SupportedOSPlatformGuard("windows")]
    [SupportedOSPlatformGuard("linux")]
    [SupportedOSPlatformGuard("macos")]
    public static bool IsSupported => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
        Architecture.Arm64 => OperatingSystem.IsMacOS(),
        _ => false,
    };

    internal static void EnsureSupported(string component)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException($"{component} requires the VLC 3 ABI on Windows x64, Linux x64, or macOS.");
    }
}
