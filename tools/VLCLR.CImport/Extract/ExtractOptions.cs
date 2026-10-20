using VLCLR.CImport.Model;

namespace VLCLR.CImport.Extract;

/// <summary>Everything one extraction run needs; the manifest-driven command builds it from vlc.cimport.json.</summary>
public sealed record ExtractOptions
{
    /// <summary><see cref="Abis.Windows"/> or <see cref="Abis.SysV"/>; stored in the snapshot.</summary>
    public required string Abi { get; init; }

    /// <summary>Clang target triple; defaults to <see cref="AbiTargets.TripleFor"/>.</summary>
    public string? Target { get; init; }

    /// <summary>Recorded in <see cref="Snapshot.VlcCommit"/>.</summary>
    public string VlcCommit { get; init; } = "";

    /// <summary>Headers <c>#include</c>d, in order, by the single translation unit (names relative to an include dir).</summary>
    public required IReadOnlyList<string> Headers { get; init; }

    /// <summary>
    /// Include directories (<c>-I</c>). Declarations located under them are the
    /// only ones extract reports; everything else is typedef sugar at most.
    /// </summary>
    public required IReadOnlyList<string> IncludeDirs { get; init; }

    /// <summary><c>NAME</c> or <c>NAME=VALUE</c>, passed as <c>-D</c>.</summary>
    public IReadOnlyList<string> Defines { get; init; } = [];

    /// <summary>Source lines placed before the includes.</summary>
    public IReadOnlyList<string> Prelude { get; init; } = [];

    /// <summary>Extra clang arguments (for fixtures: <c>-nostdinc</c>, <c>-ffreestanding</c>).</summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    /// <summary>Clang resource directory holding the builtin headers (<c>include/stddef.h</c> ...). Null: let clang decide.</summary>
    public string? ResourceDir { get; init; }

    /// <summary>
    /// System root of the C library headers (<c>--sysroot</c>): a mingw-w64
    /// toolchain for <see cref="Abis.Windows"/>. Null: the host's.
    /// </summary>
    public string? Sysroot { get; init; }

    /// <summary>
    /// Symbols to keep (C names, trailing <c>*</c> allowed). Null keeps every
    /// symbol declared in <see cref="Headers"/>.
    /// </summary>
    public IReadOnlyList<string>? Roots { get; init; }

    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>Names libvlccore exports. Null: every <c>VLC_API</c> function counts as exported.</summary>
    public IReadOnlySet<string>? ExportedSymbols { get; init; }
}

/// <summary>Clang triples used per ABI.</summary>
public static class AbiTargets
{
    /// <summary>
    /// mingw-w64, as VLC's Windows builds are made (llvm-mingw): libvlccore's
    /// layouts come from its headers, not MSVC's.
    /// </summary>
    public const string WindowsTriple = "x86_64-w64-windows-gnu";
    public const string SysVTriple = "x86_64-pc-linux-gnu";

    public static string TripleFor(string abi) => abi switch
    {
        Abis.Windows => WindowsTriple,
        Abis.SysV => SysVTriple,
        _ => throw new CommandException($"Unknown ABI '{abi}'. Known: {string.Join(", ", Abis.All)}."),
    };
}
