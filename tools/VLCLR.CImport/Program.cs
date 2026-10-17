using VLCLR.CImport.Emit;
using VLCLR.CImport.Extract;

namespace VLCLR.CImport;

internal static class Program
{
    private const string Usage = """
        vlclr-cimport extract --abi <windows|sysv> [--manifest <vlc.cimport.json>] [--include <dir>] --out <snapshot.json>
        vlclr-cimport emit [--manifest <vlc.cimport.json>]
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
