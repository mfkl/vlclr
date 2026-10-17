using System.Diagnostics;
using System.Text;

namespace VLCLR.CImport.Tests.Emit;

/// <summary>Golden file comparison. Set CIMPORT_UPDATE_GOLDEN=1 to rewrite the goldens in the source tree.</summary>
internal static class GoldenFile
{
    /// <summary>
    /// The test project's source directory when the build output sits under it,
    /// else the build output (which holds copies of Golden/ and the probe
    /// headers), e.g. with <c>dotnet test --artifacts-path</c>.
    /// </summary>
    public static string TestProjectDirectory { get; } = FindTestProjectDirectory() ?? AppContext.BaseDirectory;

    public static void Verify(string relativePath, string actual)
    {
        if (Environment.GetEnvironmentVariable("CIMPORT_UPDATE_GOLDEN") == "1")
        {
            var source = FindTestProjectDirectory()
                ?? throw new InvalidOperationException("CIMPORT_UPDATE_GOLDEN needs the build output under the VLCLR.CImport.Tests source directory.");
            var target = Path.Combine(source, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        var path = Path.Combine(TestProjectDirectory, relativePath);
        Assert.True(File.Exists(path), $"Golden file {relativePath} is missing. Run with CIMPORT_UPDATE_GOLDEN=1 to create it.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual);
    }

    private static string? FindTestProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VLCLR.CImport.Tests.csproj")))
                return dir.FullName;
        }
        return null;
    }
}

/// <summary>Result of a native compiler run.</summary>
internal sealed record CompileResult(bool Succeeded, string Output);

/// <summary>Locates MSVC (via vswhere) and g++ in WSL. Each Compile returns null when the toolchain is unavailable.</summary>
internal static class NativeToolchains
{
    private const string WslDistro = "Ubuntu-24.04";

    private static readonly Lazy<string?> Vcvars = new(FindVcvars);
    private static readonly Lazy<bool> Wsl = new(ProbeWsl);

    public static CompileResult? CompileWithMsvc(string source, string includeDir)
    {
        if (Vcvars.Value is not { } vcvars)
            return null;

        var script = Path.ChangeExtension(source, ".cmd");
        var obj = Path.ChangeExtension(source, ".obj");
        File.WriteAllText(script,
            $"@echo off\r\ncall \"{vcvars}\" >nul\r\nif errorlevel 1 exit /b 1\r\n" +
            $"cl /nologo /std:c++17 /TP /W0 /c /I\"{includeDir}\" \"{source}\" /Fo\"{obj}\"\r\n");
        return Run("cmd.exe", ["/c", script], Path.GetTempPath());
    }

    public static CompileResult? CompileWithGcc(string source, string includeDir)
    {
        if (!Wsl.Value)
            return null;

        return Run("wsl.exe", ["-d", WslDistro, "--", "g++", "-std=c++17", "-c", "-o", "/dev/null", $"-I{ToWslPath(includeDir)}", ToWslPath(source)], Path.GetTempPath());
    }

    private static string ToWslPath(string path)
    {
        var full = Path.GetFullPath(path);
        return $"/mnt/{char.ToLowerInvariant(full[0])}{full[2..].Replace('\\', '/')}";
    }

    private static string? FindVcvars()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
            return null;

        var result = Run(vswhere, ["-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], Path.GetTempPath());
        if (!result.Succeeded || result.Output.Trim() is not { Length: > 0 } installation)
            return null;
        var vcvars = Path.Combine(installation.Split('\n')[0].Trim(), "VC", "Auxiliary", "Build", "vcvars64.bat");
        return File.Exists(vcvars) ? vcvars : null;
    }

    private static bool ProbeWsl()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            return Run("wsl.exe", ["-d", WslDistro, "--", "g++", "--version"], Path.GetTempPath()).Succeeded;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static CompileResult Run(string fileName, string[] arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            return new CompileResult(false, "timed out");
        }
        // WSL prints UTF-16 for its own errors; stripping NULs keeps those readable.
        var output = (stdout.Result + stderr.Result).Replace("\0", "");
        return new CompileResult(process.ExitCode == 0, output);
    }
}
