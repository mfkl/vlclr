namespace VLCLR.CImport.Emit;

/// <summary><c>emit [--manifest m]</c>: abi/*.json → *.g.cs, C++ probe, C# layout tests, report.</summary>
public static class EmitCommand
{
    public static int Run(IReadOnlyList<string> args)
        => throw new CommandException("emit is not implemented yet.");
}
