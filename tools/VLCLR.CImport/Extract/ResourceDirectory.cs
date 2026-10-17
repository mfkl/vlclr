namespace VLCLR.CImport.Extract;

/// <summary>
/// Finds a clang resource directory (the one containing <c>include/stddef.h</c>
/// and the other compiler-provided headers). The libclang NuGet package ships
/// without them, so real VLC headers need one from an installed LLVM.
/// </summary>
public static class ResourceDirectory
{
    public const string EnvironmentVariable = "CIMPORT_RESOURCE_DIR";

    /// <summary>The first usable resource directory, or null.</summary>
    public static string? Find()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && IsResourceDir(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);

        foreach (var root in SearchRoots())
        {
            var best = Versions(root).FirstOrDefault();
            if (best is not null)
                return best;
        }
        return null;
    }

    /// <summary>True when <paramref name="dir"/> holds clang's builtin headers.</summary>
    public static bool IsResourceDir(string dir) => File.Exists(Path.Combine(dir, "include", "stddef.h"));

    private static IEnumerable<string> SearchRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var programFiles in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                if (string.IsNullOrEmpty(programFiles))
                    continue;
                yield return Path.Combine(programFiles, "LLVM", "lib", "clang");
                foreach (var vs in SafeDirectories(Path.Combine(programFiles, "Microsoft Visual Studio")))
                {
                    foreach (var edition in SafeDirectories(vs).SelectMany(SafeDirectories))
                        yield return Path.Combine(edition, "VC", "Tools", "Llvm", "x64", "lib", "clang");
                }
            }
        }
        else
        {
            foreach (var llvm in SafeDirectories("/usr/lib").Where(d => Path.GetFileName(d).StartsWith("llvm-", StringComparison.Ordinal))
                         .OrderByDescending(d => d, StringComparer.Ordinal))
                yield return Path.Combine(llvm, "lib", "clang");
            yield return "/usr/lib/clang";
            yield return "/usr/local/lib/clang";
        }
    }

    // lib/clang/<version>, highest version first.
    private static IEnumerable<string> Versions(string root)
        => SafeDirectories(root)
            .Where(IsResourceDir)
            .OrderByDescending(d => VersionKey(Path.GetFileName(d)))
            .ThenByDescending(d => d, StringComparer.Ordinal);

    private static long VersionKey(string name)
        => int.TryParse(name.Split('.')[0], out var major) ? major : 0;

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
