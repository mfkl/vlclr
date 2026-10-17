using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VLCLR.Native;

namespace InlineParity.Native;

/// <summary>
/// Compiles native/inline_parity.c against the pinned VLC headers once per test
/// run, loads it, and makes it answer for "libvlccore" in the VLCLR assembly so
/// the managed reimplementations and the C originals hit the same stand-ins.
/// </summary>
internal static class ParityLibrary
{
    public const string Name = "inline_parity";

    private static readonly Lazy<Result> s_result = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record Result(nint Handle, string? SkipReason, string? BuildError, string? Path);

    /// <summary>Handle of the compiled library, or 0 when it is not available.</summary>
    public static nint Handle => s_result.Value.Handle;

    /// <summary>Why the tests cannot run here (no compiler, no headers), or null.</summary>
    public static string? SkipReason => s_result.Value.SkipReason;

    /// <summary>Path of the compiled library (for diagnostics).</summary>
    public static string? Path => s_result.Value.Path;

    /// <summary>
    /// Skips the calling test when the environment cannot build the library and
    /// fails it when the library should have built but did not.
    /// </summary>
    public static void Require()
    {
        Result r = s_result.Value;
        if (r.BuildError is not null)
        {
            throw new InvalidOperationException("Could not build the parity library:" + Environment.NewLine + r.BuildError);
        }

        Xunit.Skip.If(r.SkipReason is not null, r.SkipReason);
    }

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(VLCCore).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(ParityLibrary).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        switch (libraryName)
        {
            case "libvlccore":
            case Name:
                return s_result.Value.Handle;

            // VLCCore.Free imports "free" from msvcrt. Memory handed out by the
            // C library comes from the C runtime it links, so answer with that.
            case "msvcrt":
                return LoadCRuntime();

            default:
                return 0;
        }
    }

    private static nint LoadCRuntime()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? ["ucrtbase.dll"]
            : OperatingSystem.IsMacOS()
                ? ["libSystem.dylib"]
                : ["libc.so.6", "libc"];
        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out nint handle))
            {
                return handle;
            }
        }

        return 0;
    }

    private static Result Build()
    {
        string? includeDir = FindIncludeDirectory();
        if (includeDir is null)
        {
            return Skipped("VLC headers not found. Set VLC_INCLUDE to the pinned include directory, "
                + "or run tools/VLCLR.CImport/fetch-headers to populate artifacts/vlc-headers/include.");
        }

        string source = System.IO.Path.Combine(AppContext.BaseDirectory, "native", "inline_parity.c");
        if (!File.Exists(source))
        {
            return Skipped($"native source not found at {source}");
        }

        string outDir = System.IO.Path.Combine(AppContext.BaseDirectory, "native-build", Environment.ProcessId.ToString());
        Directory.CreateDirectory(outDir);

        string libFile = System.IO.Path.Combine(outDir, OperatingSystem.IsWindows() ? "inline_parity.dll"
            : OperatingSystem.IsMacOS() ? "libinline_parity.dylib" : "libinline_parity.so");

        string? error;
        if (OperatingSystem.IsWindows())
        {
            string? vcvars = FindVcvars64();
            if (vcvars is null)
            {
                return Skipped("MSVC (vcvars64.bat) not found; install the Visual Studio C++ x64 tools.");
            }

            error = CompileWithMsvc(vcvars, includeDir, source, outDir, libFile);
        }
        else
        {
            string? cc = FindUnixCompiler();
            if (cc is null)
            {
                return Skipped("No C compiler found (looked for cc, gcc, clang).");
            }

            error = CompileWithUnixCompiler(cc, includeDir, source, libFile);
        }

        if (error is not null)
        {
            return new Result(0, null, error, null);
        }

        nint handle = NativeLibrary.Load(libFile);
        return new Result(handle, null, null, libFile);
    }

    private static Result Skipped(string reason) => new(0, reason, null, null);

    private static string? FindIncludeDirectory()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("VLC_INCLUDE");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return File.Exists(System.IO.Path.Combine(fromEnv, "vlc_common.h")) ? System.IO.Path.GetFullPath(fromEnv) : null;
        }

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "vlclr.sln")))
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "artifacts", "vlc-headers", "include");
                return File.Exists(System.IO.Path.Combine(candidate, "vlc_common.h")) ? candidate : null;
            }
        }

        return null;
    }

    private static string? FindVcvars64()
    {
        string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (programFilesX86 is null)
        {
            return null;
        }

        string vswhere = System.IO.Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        (int code, string output) = Run(vswhere,
            ["-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"]);
        string install = output.Trim();
        if (code != 0 || install.Length == 0)
        {
            return null;
        }

        string vcvars = System.IO.Path.Combine(install.Split('\n')[0].Trim(), "VC", "Auxiliary", "Build", "vcvars64.bat");
        return File.Exists(vcvars) ? vcvars : null;
    }

    private static string? CompileWithMsvc(string vcvars, string includeDir, string source, string outDir, string libFile)
    {
        // Same warnings the VLC headers trigger everywhere under MSVC are muted; the
        // C11 atomics switch is what vlc_atomic.h needs in C mode.
        string batch = System.IO.Path.Combine(outDir, "build.cmd");
        File.WriteAllText(batch,
            "@echo off\r\n" +
            $"call \"{vcvars}\" >nul\r\n" +
            "if errorlevel 1 exit /b 1\r\n" +
            $"cd /d \"{outDir}\"\r\n" +
            $"cl /nologo /LD /std:c11 /experimental:c11atomics /W3 /wd4267 /wd4244 /wd4996 /I\"{includeDir}\" \"{source}\" /Fe:\"{libFile}\" /Fo:\"{outDir}\\\\\"\r\n" +
            "exit /b %errorlevel%\r\n");

        (int code, string output) = Run(batch, []);
        return code == 0 && File.Exists(libFile) ? null : $"cl exited with {code}:{Environment.NewLine}{output}";
    }

    private static string? FindUnixCompiler()
    {
        foreach (string? candidate in new[] { Environment.GetEnvironmentVariable("CC"), "cc", "gcc", "clang" })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                (int code, _) = Run(candidate, ["--version"]);
                if (code == 0)
                {
                    return candidate;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return null;
    }

    private static string? CompileWithUnixCompiler(string cc, string includeDir, string source, string libFile)
    {
        (int code, string output) = Run(cc,
            ["-shared", "-fPIC", "-O1", "-std=gnu11", "-w", "-I" + includeDir, source, "-o", libFile]);
        return code == 0 && File.Exists(libFile) ? null : $"{cc} exited with {code}:{Environment.NewLine}{output}";
    }

    private static (int ExitCode, string Output) Run(string file, IEnumerable<string> args)
    {
        ProcessStartInfo info = new(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        StringBuilder text = new();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (text) { text.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (text) { text.AppendLine(e.Data); } } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        process.WaitForExit(); // flush the async readers
        return (process.ExitCode, text.ToString());
    }
}
