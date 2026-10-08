using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VLCLR.Native;

/// <summary>The 64-bit VLC 3 targets whose native calling conventions VLCLR implements.</summary>
public static class VLCPlatform
{
    /// <summary>True on Windows x64 and Linux x64.</summary>
    [SupportedOSPlatformGuard("windows")]
    [SupportedOSPlatformGuard("linux")]
    public static bool IsSupported =>
        (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64;

    internal static void EnsureSupported(string component)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException($"{component} requires the VLC 3 ABI on Windows x64 or Linux x64.");
    }
}
