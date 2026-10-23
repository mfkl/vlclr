# McpRemote Sample

A VLC 4.x interface plugin written in C# that lets AI agents control VLC
through the [Model Context Protocol](https://modelcontextprotocol.io) (MCP).
While VLC runs, the plugin serves VLC's player as MCP tools on
`http://127.0.0.1:8765/mcp`. Claude Code, Claude Desktop, or any other MCP
client can then check what is playing, pause, seek, and change items.

## What It Does

- Registers a VLC **interface** module (`dotnet_mcp`), the kind of module
  VLC's Qt interface, HTTP interface, and hotkeys are
- Runs Kestrel and the official MCP C# SDK
  (`ModelContextProtocol.AspNetCore`) inside VLC, compiled with Native AOT
  with no AOT or trim warnings
- Controls VLC through VLCLR's `VLCPlayer` and `VLCPlaylist` wrappers

## Tools

| Tool | Does |
|---|---|
| `status` | State, media name, time and length, volume, position in the playlist |
| `play` | Starts playback, or resumes it when paused |
| `pause` | Pauses |
| `seek` | Jumps to a time in seconds, from the start or (`relative`) from the current time |
| `next`, `previous` | Plays the next or previous playlist item |
| `set_volume` | Sets the volume, 0 to 200 percent |

## Building

```bash
dotnet publish samples/McpRemote -c Release -r win-x64

# Output
samples/McpRemote/bin/Release/net10.0/win-x64/publish/libdotnet_mcp_plugin.dll
```

The plugin is about 13 MB: most of it is Kestrel.

## Running

Put the DLL in a folder VLC loads plugins from, then start VLC with the
interface as an extra interface:

```bash
set VLC_PLUGIN_PATH=C:\path\to\folder\with\the\dll
vlc --extraintf=dotnet_mcp video.mp4
```

VLC's log shows `[McpRemote] MCP server on http://127.0.0.1:8765/mcp`.
`--mcp-port=<port>` changes the port.

To connect Claude Code:

```bash
claude mcp add --transport http vlc http://127.0.0.1:8765/mcp
```

Then ask things like "what is VLC playing?", "go back 30 seconds", or "pause
the video".

## Security

The server listens on 127.0.0.1 only. A web page could still reach it
through DNS rebinding, so it also rejects any request whose `Host` header is
not `127.0.0.1` or `localhost`, and any request from a web page that is not
local (`Origin` header). It has no authentication: any program on the machine
can control VLC while the interface runs.

## Files

| File | Purpose |
|---|---|
| `PluginEntry.cs` | `vlc_entry`, module registration, open and close |
| `McpServerHost.cs` | Kestrel and the MCP server, started and stopped with the interface |
| `PlayerTools.cs` | The MCP tools |
