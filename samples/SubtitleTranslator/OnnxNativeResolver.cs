using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SubtitleTranslator;

/// <summary>
/// Loads the exact ONNX Runtime native library before the managed wrapper makes
/// its first P/Invoke. Failed attempts are retryable.
/// </summary>
internal static class OnnxNativeResolver
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    private static readonly object Sync = new();
    private static nint _onnxruntimeHandle;
    private static string? _loadedFrom;
    private static string? _loadedVersion;

    public static string? LoadedFrom
    {
        get { lock (Sync) return _loadedFrom; }
    }

    public static string? LoadedVersion
    {
        get { lock (Sync) return _loadedVersion; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string fileName, nint file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetModuleFileNameW(nint module, StringBuilder filename, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint module, [MarshalAs(UnmanagedType.LPStr)] string procedureName);

    public static string EnsureLoaded(string? modelDirectory = null) =>
        EnsureLoadedResult(modelDirectory).Diagnostics;

    public static NativeLoadResult EnsureLoadedResult(string? modelDirectory = null)
    {
        lock (Sync)
        {
            if (_onnxruntimeHandle != 0)
            {
                return new NativeLoadResult(
                    true,
                    _loadedFrom,
                    _loadedVersion,
                    $"Already loaded ONNX Runtime {_loadedVersion ?? "unknown"} from: {_loadedFrom}");
            }

            var diagnostics = new StringBuilder();
            string expectedVersion = GetManagedRuntimeVersion();
            diagnostics.AppendLine($"Managed ONNX Runtime version: {expectedVersion}");

            foreach (string directory in GetSearchPaths(modelDirectory, diagnostics))
            {
                string dllPath = Path.Combine(directory, "onnxruntime.dll");
                if (!File.Exists(dllPath))
                    continue;

                string canonicalPath;
                try
                {
                    canonicalPath = Path.GetFullPath(dllPath);
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine($"Ignoring invalid path '{dllPath}': {ex.Message}");
                    continue;
                }

                string? nativeFileVersion = GetNativeFileVersion(canonicalPath);
                diagnostics.AppendLine($"Trying: {canonicalPath} (file version {nativeFileVersion ?? "unknown"})");
                if (nativeFileVersion != null && !MajorMinorVersionsMatch(expectedVersion, nativeFileVersion))
                {
                    diagnostics.AppendLine(
                        $"Rejected version mismatch: managed {expectedVersion}, native file {nativeFileVersion}.");
                    continue;
                }

                nint handle = LoadLibraryExW(
                    canonicalPath,
                    0,
                    LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
                if (handle == 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    diagnostics.AppendLine(
                        $"LoadLibraryExW failed with Win32 error {error}. " +
                        "Install the matching Microsoft Visual C++ runtime and verify DLL architecture.");
                    continue;
                }

                nint apiBase = GetProcAddress(handle, "OrtGetApiBase");
                if (apiBase == 0)
                {
                    diagnostics.AppendLine("Rejected DLL because OrtGetApiBase is missing.");
                    _ = FreeLibrary(handle);
                    continue;
                }

                string? nativeVersion = GetNativeRuntimeVersion(apiBase);
                if (nativeVersion != null && !SemanticVersionsMatch(expectedVersion, nativeVersion))
                {
                    diagnostics.AppendLine(
                        $"Rejected runtime API version mismatch: managed {expectedVersion}, native {nativeVersion}.");
                    _ = FreeLibrary(handle);
                    continue;
                }

                _onnxruntimeHandle = handle;
                _loadedFrom = canonicalPath;
                _loadedVersion = nativeVersion;
                RegisterResolver();
                diagnostics.AppendLine($"Loaded ONNX Runtime from {canonicalPath}.");
                return new NativeLoadResult(true, canonicalPath, nativeVersion, diagnostics.ToString());
            }

            diagnostics.AppendLine(
                "ONNX Runtime could not be loaded. Place the matching onnxruntime.dll in the VLC root " +
                "or set ONNXRUNTIME_PATH to an explicit file/directory. A later call may retry.");
            return new NativeLoadResult(false, null, null, diagnostics.ToString());
        }
    }

    /// <summary>Returns the VLC root inferred from plugins/&lt;capability&gt;/&lt;plugin&gt;.dll.</summary>
    public static string? GetHostRootDirectory()
    {
        var diagnostics = new StringBuilder();
        string? pluginDirectory = GetPluginDirectory(diagnostics);
        string? pluginsDirectory = pluginDirectory == null ? null : Path.GetDirectoryName(pluginDirectory);
        return pluginsDirectory == null ? null : Path.GetDirectoryName(pluginsDirectory);
    }

    private static void RegisterResolver()
    {
        TryRegisterResolver(typeof(Microsoft.ML.OnnxRuntime.SessionOptions).Assembly);
        TryRegisterResolver(typeof(OnnxNativeResolver).Assembly);
    }

    private static void TryRegisterResolver(Assembly assembly)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, ResolveDllImport);
        }
        catch (InvalidOperationException)
        {
            // A resolver may already be registered by the hosting application.
        }
    }

    private static nint ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        string.Equals(libraryName, "onnxruntime", StringComparison.OrdinalIgnoreCase)
            ? _onnxruntimeHandle
            : 0;

    private static IEnumerable<string> GetSearchPaths(string? modelDirectory, StringBuilder diagnostics)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in CandidatePaths(modelDirectory, diagnostics))
        {
            string? directory = candidate;
            try
            {
                if (File.Exists(candidate))
                    directory = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    continue;
                directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            }
            catch
            {
                continue;
            }

            if (unique.Add(directory))
                yield return directory;
        }
    }

    private static IEnumerable<string> CandidatePaths(string? modelDirectory, StringBuilder diagnostics)
    {
        string? explicitPath = Environment.GetEnvironmentVariable("ONNXRUNTIME_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            yield return explicitPath;

        string? pluginDirectory = GetPluginDirectory(diagnostics);
        if (pluginDirectory != null)
        {
            yield return pluginDirectory;
            string? pluginsDirectory = Path.GetDirectoryName(pluginDirectory);
            if (pluginsDirectory != null)
            {
                yield return pluginsDirectory;
                string? vlcRoot = Path.GetDirectoryName(pluginsDirectory);
                if (vlcRoot != null)
                    yield return vlcRoot;
            }
        }

        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(modelDirectory))
        {
            yield return modelDirectory;
            string? modelsDirectory = Path.GetDirectoryName(Path.GetFullPath(modelDirectory));
            if (modelsDirectory != null)
            {
                yield return modelsDirectory;
                string? root = Path.GetDirectoryName(modelsDirectory);
                if (root != null)
                    yield return root;
            }
        }
    }

    private static string? GetPluginDirectory(StringBuilder diagnostics)
    {
        try
        {
            string assemblyName = typeof(OnnxNativeResolver).Assembly.GetName().Name ?? "";
            if (assemblyName.Length == 0)
                return null;

            nint handle = GetModuleHandleW($"{assemblyName}.dll");
            if (handle == 0)
                return null;

            var buffer = new StringBuilder(1024);
            int length = GetModuleFileNameW(handle, buffer, buffer.Capacity);
            if (length <= 0)
                return null;

            string pluginPath = buffer.ToString();
            diagnostics.AppendLine($"Plugin DLL: {pluginPath}");
            return Path.GetDirectoryName(pluginPath);
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"Could not resolve plugin path: {ex.Message}");
            return null;
        }
    }

    private static string GetManagedRuntimeVersion()
    {
        Version? version = typeof(Microsoft.ML.OnnxRuntime.SessionOptions).Assembly.GetName().Version;
        return version == null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string? GetNativeFileVersion(string path)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? null : info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static unsafe string? GetNativeRuntimeVersion(nint ortGetApiBaseAddress)
    {
        try
        {
            var getApiBase = (delegate* unmanaged<nint>)ortGetApiBaseAddress;
            nint apiBase = getApiBase();
            if (apiBase == 0)
                return null;

            nint getVersionStringAddress = Marshal.ReadIntPtr(apiBase, IntPtr.Size);
            if (getVersionStringAddress == 0)
                return null;

            var getVersionString = (delegate* unmanaged<nint>)getVersionStringAddress;
            return Marshal.PtrToStringUTF8(getVersionString());
        }
        catch
        {
            return null;
        }
    }

    private static bool SemanticVersionsMatch(string managed, string native) =>
        managed == "unknown" || string.Equals(
            NormalizeSemanticVersion(managed),
            NormalizeSemanticVersion(native),
            StringComparison.Ordinal);

    private static bool MajorMinorVersionsMatch(string managed, string native)
    {
        string[] managedParts = NormalizeSemanticVersion(managed).Split('.');
        string[] nativeParts = NormalizeSemanticVersion(native).Split('.');
        return managed == "unknown" ||
            (managedParts.Length >= 2 && nativeParts.Length >= 2 &&
             managedParts[0] == nativeParts[0] && managedParts[1] == nativeParts[1]);
    }

    private static string NormalizeSemanticVersion(string value)
    {
        string numericPrefix = new(value.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
        string[] components = numericPrefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('.', components.Take(3));
    }
}

internal sealed record NativeLoadResult(
    bool Success,
    string? LoadedFrom,
    string? Version,
    string Diagnostics);
