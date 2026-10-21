using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests;

internal static class TestAbis
{
    /// <summary>A darwin snapshot with the layouts of <paramref name="sysv"/>: macOS shares nearly all of Linux's.</summary>
    public static Snapshot DarwinLike(Snapshot sysv) => sysv with { Abi = Abis.Darwin, Target = "x86_64-apple-darwin" };
}
