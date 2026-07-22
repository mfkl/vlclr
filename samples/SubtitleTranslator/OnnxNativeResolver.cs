using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SubtitleTranslator;

/// <summary>
/// Ensures onnxruntime native DLL is loadable when running as a VLC plugin.
/// The system may have a different onnxruntime.dll (e.g., in System32), so we
/// must load from the correct path before any ONNX Runtime P/Invoke runs.
/// </summary>
internal static class OnnxNativeResolver
{
    private static bool _initialized;
    private static nint _onnxruntimeHandle;
    private static string? _loadedFrom;

    public static string? LoadedFrom => _loadedFrom;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetModuleFileNameW(nint hModule, StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddDllDirectory(string newDirectory);

    /// <summary>
    /// Load onnxruntime.dll from the correct location. Must be called before
    /// any Microsoft.ML.OnnxRuntime types are used.
    /// </summary>
    public static string EnsureLoaded()
    {
        if (_initialized)
            return $"Already loaded from: {_loadedFrom ?? "unknown"}";
        _initialized = true;

        var diag = new StringBuilder();

        // Try loading from known paths using explicit full paths
        foreach (var dir in GetSearchPaths(diag))
        {
            var dllPath = Path.Combine(dir, "onnxruntime.dll");
            if (!File.Exists(dllPath))
                continue;

            diag.AppendLine($"Trying: {dllPath}");

            // Add directory so transitive deps are found
            try { AddDllDirectory(dir); } catch { }

            var handle = LoadLibraryW(dllPath);
            if (handle != 0)
            {
                // Verify it has the right export
                var proc = GetProcAddress(handle, "OrtGetApiBase");
                if (proc != 0)
                {
                    _onnxruntimeHandle = handle;
                    _loadedFrom = dllPath;
                    diag.AppendLine($"Loaded OK, OrtGetApiBase at 0x{proc:X}");
                    RegisterResolver();
                    return diag.ToString();
                }
                diag.AppendLine($"Loaded but OrtGetApiBase missing - wrong DLL version");
            }
            else
            {
                diag.AppendLine($"LoadLibraryW failed: error {Marshal.GetLastWin32Error()}");
            }
        }

        diag.AppendLine("FAILED: Could not load onnxruntime.dll with OrtGetApiBase export");
        return diag.ToString();
    }

    private static void RegisterResolver()
    {
        try
        {
            var onnxAssembly = typeof(Microsoft.ML.OnnxRuntime.SessionOptions).Assembly;
            NativeLibrary.SetDllImportResolver(onnxAssembly, ResolveDllImport);
        }
        catch { }

        try
        {
            var selfAssembly = typeof(OnnxNativeResolver).Assembly;
            NativeLibrary.SetDllImportResolver(selfAssembly, ResolveDllImport);
        }
        catch { }
    }

    private static nint ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == "onnxruntime" && _onnxruntimeHandle != 0)
            return _onnxruntimeHandle;
        return 0;
    }

    private static IEnumerable<string> GetSearchPaths(StringBuilder diag)
    {
        // 1. Environment variable override
        var envPath = Environment.GetEnvironmentVariable("ONNXRUNTIME_PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            if (File.Exists(envPath))
                yield return Path.GetDirectoryName(envPath)!;
            else if (Directory.Exists(envPath))
                yield return envPath;
        }

        // 2. Same directory as our own plugin DLL (using GetModuleFileName)
        var pluginDir = GetPluginDirectory(diag);
        if (pluginDir != null)
            yield return pluginDir;

        // 3. AppContext.BaseDirectory (works for non-plugin scenarios)
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            yield return baseDir;
            // Parent directories (plugins/ and VLC root)
            var trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(trimmed);
            if (parent != null)
            {
                yield return parent;
                var grandparent = Path.GetDirectoryName(parent);
                if (grandparent != null)
                    yield return grandparent;
            }
        }

        // 4. Current working directory
        yield return Directory.GetCurrentDirectory();

        // 5. Model directory (onnxruntime.dll might be placed alongside models)
        var modelPath = Environment.GetEnvironmentVariable("SUBTITLE_TRANSLATOR_MODEL_PATH");
        if (!string.IsNullOrEmpty(modelPath) && Directory.Exists(modelPath))
            yield return modelPath;
    }

    /// <summary>
    /// Find the directory of our plugin DLL using Win32 GetModuleFileName.
    /// </summary>
    private static string? GetPluginDirectory(StringBuilder diag)
    {
        try
        {
            // Get handle to our own DLL
            var handle = GetModuleHandleW("libdotnet_subtitle_translator_plugin");
            if (handle == 0)
            {
                // Try with .dll extension
                handle = GetModuleHandleW("libdotnet_subtitle_translator_plugin.dll");
            }

            if (handle != 0)
            {
                var sb = new StringBuilder(1024);
                var len = GetModuleFileNameW(handle, sb, sb.Capacity);
                if (len > 0)
                {
                    var pluginPath = sb.ToString();
                    var dir = Path.GetDirectoryName(pluginPath);
                    diag.AppendLine($"Plugin DLL at: {pluginPath}");
                    return dir;
                }
            }
            else
            {
                diag.AppendLine("GetModuleHandleW for plugin DLL returned null");
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"GetPluginDirectory error: {ex.Message}");
        }
        return null;
    }
}
