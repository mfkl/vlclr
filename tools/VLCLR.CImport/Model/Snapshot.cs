namespace VLCLR.CImport.Model;

/// <summary>
/// Everything extract learned from the VLC headers for one ABI. Serialised as
/// abi/windows.json and abi/sysv.json. Top-level lists are kept in canonical
/// order (see <see cref="Canonicalize"/>); lists inside a declaration (fields,
/// parameters, enum members) keep their declaration order.
/// </summary>
public sealed record Snapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary><see cref="Abis.Windows"/> or <see cref="Abis.SysV"/>.</summary>
    public required string Abi { get; init; }

    /// <summary>Clang target triple the headers were parsed for, e.g. x86_64-w64-windows-gnu.</summary>
    public required string Target { get; init; }

    /// <summary>VLC commit the headers come from (manifest <c>vlc.commit</c>).</summary>
    public required string VlcCommit { get; init; }

    public required int PointerSize { get; init; }

    /// <summary>Headers the translation unit included, in order (the manifest's, with <c>*</c> expanded).</summary>
    public IReadOnlyList<string> Headers { get; init; } = [];

    public IReadOnlyList<TypeDecl> Types { get; init; } = [];
    public IReadOnlyList<EnumDecl> Enums { get; init; } = [];
    public IReadOnlyList<TypedefDecl> Typedefs { get; init; } = [];
    public IReadOnlyList<FunctionDecl> Functions { get; init; } = [];
    public IReadOnlyList<ConstantDecl> Constants { get; init; } = [];

    /// <summary>Selected symbols extract could not represent (macros that do not evaluate, unsupported types, ...).</summary>
    public IReadOnlyList<SkippedDecl> Skipped { get; init; } = [];

    /// <summary>
    /// Returns a copy with every top-level list sorted by name (ordinal).
    /// Declaration-internal lists are left as declared.
    /// </summary>
    public Snapshot Canonicalize() => this with
    {
        Types = [.. Types.OrderBy(t => t.Name, StringComparer.Ordinal)],
        Enums = [.. Enums.OrderBy(e => e.Name, StringComparer.Ordinal)],
        Typedefs = [.. Typedefs.OrderBy(t => t.Name, StringComparer.Ordinal)],
        Functions = [.. Functions.OrderBy(f => f.Name, StringComparer.Ordinal)],
        Constants = [.. Constants.OrderBy(c => c.Name, StringComparer.Ordinal)],
        Skipped = [.. Skipped.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Kind, StringComparer.Ordinal)],
    };
}

/// <summary>ABI names used in snapshots, file names and the merged model.</summary>
public static class Abis
{
    public const string Windows = "windows";

    /// <summary>Linux (x86-64 System V).</summary>
    public const string SysV = "sysv";

    /// <summary>macOS: System V too, but with its own C library (BSD sockaddr, errno values).</summary>
    public const string Darwin = "darwin";

    /// <summary>Order of ABIs in <see cref="Merge.PerAbi{T}"/> and in generated code.</summary>
    public static IReadOnlyList<string> All { get; } = [Windows, SysV, Darwin];

    /// <summary>The .NET platform name of an ABI, for <c>[SupportedOSPlatform]</c>.</summary>
    public static string Platform(string abi) => abi switch
    {
        Windows => "windows",
        SysV => "linux",
        Darwin => "macos",
        _ => throw new ArgumentException($"Unknown ABI '{abi}'.", nameof(abi)),
    };

    /// <summary>The C# test that the running platform is the ABI's.</summary>
    public static string CSharpTest(string abi) => abi switch
    {
        Windows => "OperatingSystem.IsWindows()",
        SysV => "OperatingSystem.IsLinux()",
        Darwin => "OperatingSystem.IsMacOS()",
        _ => throw new ArgumentException($"Unknown ABI '{abi}'.", nameof(abi)),
    };

    /// <summary>The C preprocessor test that the compiler targets the ABI.</summary>
    public static string CTest(string abi) => abi switch
    {
        Windows => "defined(_WIN32)",
        SysV => "(!defined(_WIN32) && !defined(__APPLE__))",
        Darwin => "defined(__APPLE__)",
        _ => throw new ArgumentException($"Unknown ABI '{abi}'.", nameof(abi)),
    };
}
