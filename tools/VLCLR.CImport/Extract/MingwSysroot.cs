namespace VLCLR.CImport.Extract;

/// <summary>
/// Finds a mingw-w64 toolchain (llvm-mingw) for the windows ABI: VLC's
/// Windows builds are made with it, so its C library headers, not MSVC's,
/// give libvlccore's layouts.
/// </summary>
public static class MingwSysroot
{
    public const string EnvironmentVariable = "CIMPORT_MINGW_SYSROOT";

    private static readonly string[] CompilerNames = ["x86_64-w64-mingw32-clang.exe", "x86_64-w64-mingw32-clang"];

    /// <summary>The first usable toolchain, or null.</summary>
    public static string? Find()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && IsSysroot(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!CompilerNames.Any(name => File.Exists(Path.Combine(dir, name))))
                continue;
            var root = Path.GetDirectoryName(Path.GetFullPath(dir));
            if (root is not null && IsSysroot(root))
                return root;
        }
        return null;
    }

    /// <summary>True when <paramref name="dir"/> holds the mingw-w64 headers, as clang's mingw driver looks for them.</summary>
    public static bool IsSysroot(string dir)
        => File.Exists(Path.Combine(dir, "include", "_mingw.h"))
           || File.Exists(Path.Combine(dir, "x86_64-w64-mingw32", "include", "_mingw.h"));
}
