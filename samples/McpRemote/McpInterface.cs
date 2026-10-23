using VLCLR.Plugin;

namespace McpRemote;

/// <summary>
/// An interface module: while VLC runs, it serves VLC's player to AI agents
/// over the Model Context Protocol, on 127.0.0.1 only.
/// </summary>
[VLCModule("dotnet_mcp")]
[VLCCapability("interface")]
[VLCDescription("Lets AI agents control playback over the Model Context Protocol")]
[VLCConfig("mcp-port", VLCConfigType.Integer, Default = McpServerHost.DefaultPort, Min = 1, Max = 65535,
    Description = "Port", LongDescription = "Port of the MCP server, which listens on 127.0.0.1 only")]
[VLCConfig("mcp-search-url", VLCConfigType.String, Description = "Search service",
    LongDescription = "URL of a local service that finds moments in a media (see the sample's README). " +
                      "Without one, the search tool searches the subtitle file next to the media.")]
public partial class McpInterface : VLCInterfaceBase
{
    private McpServerHost? _host;
    private IMomentSearch? _search;

    // The plugin is built without the runtime's own messages
    // (UseSystemResourceKeys): what VLC's options get wrong is said here, in words.
    protected override bool OnOpen()
    {
        string? searchUrl = Config.McpSearchUrl;
        Uri? searchUri = null;
        if (!string.IsNullOrEmpty(searchUrl) &&
            (!Uri.TryCreate(searchUrl, UriKind.Absolute, out searchUri) || searchUri.Scheme is not ("http" or "https")))
        {
            Context.Logger.Error($"[McpRemote] --mcp-search-url={searchUrl} is not an http:// or https:// URL");
            return false;
        }
        _search = searchUri is null ? new SubtitleSearch() : new HttpMomentSearch(searchUri);

        int port = (int)Config.McpPort;
        try
        {
            _host = McpServerHost.Start(Player, Playlist, _search, port, Context.Logger);
        }
        catch (IOException)
        {
            Context.Logger.Error($"[McpRemote] Cannot listen on 127.0.0.1:{port}, another program may use it: " +
                                 "--mcp-port changes the port");
            (_search as IDisposable)?.Dispose();
            return false;
        }
        return true;
    }

    protected override void OnClose()
    {
        _host?.Dispose();
        (_search as IDisposable)?.Dispose();
    }
}
