using System.Diagnostics;

namespace VLCLR.CImport.Extract;

/// <summary>
/// Finds the macOS SDK for the darwin ABI: its C library headers (BSD
/// sockaddr, errno values) are where macOS differs from Linux. Apple does not
/// distribute it apart from Xcode, so the darwin snapshot is extracted on macOS.
/// </summary>
public static class MacSdk
{
    public const string EnvironmentVariable = "CIMPORT_MACOS_SDK";

    /// <summary>The SDK of <c>$CIMPORT_MACOS_SDK</c>, else <c>xcrun --sdk macosx --show-sdk-path</c>, or null.</summary>
    public static string? Find()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && IsSdk(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);
        if (!OperatingSystem.IsMacOS())
            return null;

        try
        {
            using var xcrun = Process.Start(new ProcessStartInfo("xcrun", ["--sdk", "macosx", "--show-sdk-path"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (xcrun is null)
                return null;
            var path = xcrun.StandardOutput.ReadToEnd().Trim();
            xcrun.WaitForExit();
            return xcrun.ExitCode == 0 && IsSdk(path) ? path : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="dir"/> holds an SDK's C library headers.</summary>
    public static bool IsSdk(string dir) => File.Exists(Path.Combine(dir, "usr", "include", "stdio.h"));
}
