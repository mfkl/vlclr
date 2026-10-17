using VLCLR.CImport.Emit;
using VLCLR.CImport.Extract;

namespace VLCLR.CImport;

internal static class Program
{
    private static readonly string Usage = $"""
        vlclr-cimport {ExtractCommand.Usage.TrimEnd()}
        vlclr-cimport emit [--manifest <vlc.cimport.json>]
          Reads abi/windows.json and abi/sysv.json from output.directory and writes the
          generated C#, cimport-report.md, the C++ probe and the C# layout tests.
        """;

    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "extract" => ExtractCommand.Run(args[1..]),
                "emit" => EmitCommand.Run(args[1..]),
                _ => Fail(Usage),
            };
        }
        catch (CommandException e)
        {
            return Fail(e.Message);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
