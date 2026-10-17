namespace VLCLR.CImport;

/// <summary>A user error on the command line or in the inputs; printed without a stack trace.</summary>
public sealed class CommandException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary><c>--name value</c> options.</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);

    public CommandLine(IReadOnlyList<string> args, params IReadOnlyList<string> known)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal) || !known.Contains(name[2..]))
                throw new CommandException($"Unknown option '{name}'. Known: {string.Join(", ", known.Select(k => "--" + k))}.");
            if (i + 1 >= args.Count)
                throw new CommandException($"Option '{name}' needs a value.");
            _options[name[2..]] = args[++i];
        }
    }

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public string Require(string name) => Get(name) ?? throw new CommandException($"Option '--{name}' is required.");

    /// <summary>The manifest next to the tool unless <c>--manifest</c> is given.</summary>
    public string ManifestPath => Get("manifest") ?? Path.Combine(AppContext.BaseDirectory, "vlc.cimport.json");
}
