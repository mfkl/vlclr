using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VLCLR;

namespace McpRemote;

/// <summary>
/// The MCP server: Kestrel on 127.0.0.1 with the MCP SDK's HTTP transport,
/// started when VLC opens the interface and stopped when VLC closes it.
/// </summary>
internal sealed class McpServerHost : IDisposable
{
    public const int DefaultPort = 8765;

    private readonly WebApplication _app;
    private readonly VLCLogger _logger;

    private McpServerHost(WebApplication app, VLCLogger logger)
    {
        _app = app;
        _logger = logger;
    }

    public static McpServerHost Start(VLCPlayer player, VLCPlaylist playlist, int port, VLCLogger logger)
    {
        // An empty builder reads no appsettings.json, environment variables
        // or command line: VLC's own options configure the plugin.
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(o => o.Listen(IPAddress.Loopback, port));
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton(player).AddSingleton(playlist);
        builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "VLC", Version = "1.0" })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<PlayerTools>();

        WebApplication app = builder.Build();
        app.Use(LocalOnly);
        app.MapMcp("/mcp");
        app.StartAsync().GetAwaiter().GetResult();

        logger.Info($"[McpRemote] MCP server on http://127.0.0.1:{port}/mcp");
        return new McpServerHost(app, logger);
    }

    // A web page can reach a server on 127.0.0.1 through DNS rebinding: accept
    // only requests addressed to the loopback host, and from no page or a
    // local one.
    private static Task LocalOnly(HttpContext context, RequestDelegate next)
    {
        string? origin = context.Request.Headers.Origin;
        bool local = IsLoopback(context.Request.Host.Host) &&
            (origin == null || (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) && IsLoopback(uri.Host)));
        if (local)
            return next(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static bool IsLoopback(string host) => host is "127.0.0.1" or "localhost";

    public void Dispose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        _app.StopAsync(timeout.Token).GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _logger.Info("[McpRemote] MCP server stopped");
    }
}
