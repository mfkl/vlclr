namespace VLCLR.CImport.Extract;

/// <summary><c>extract --abi &lt;windows|sysv&gt; [--manifest m] [--include dir] --out snapshot.json</c></summary>
public static class ExtractCommand
{
    public static int Run(IReadOnlyList<string> args)
        => throw new CommandException("extract is not implemented yet.");
}
