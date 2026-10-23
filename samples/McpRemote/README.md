# McpRemote Sample

A VLC 4.x interface plugin written in C# that lets AI agents control VLC
through the [Model Context Protocol](https://modelcontextprotocol.io) (MCP).
While VLC runs, the plugin serves VLC's player as MCP tools on
`http://127.0.0.1:8765/mcp`. Claude Code, Claude Desktop, or any other MCP
client can then open media, find a moment, and jump to it: "find where they
argue in the kitchen and play it".

## What It Does

- Registers a VLC **interface** module (`dotnet_mcp`), the kind of module
  VLC's Qt interface, HTTP interface, and hotkeys are, by deriving from
  `VLCInterfaceBase`: the source generator writes the entry point and the
  open and close callbacks
- Runs Kestrel and the official MCP C# SDK
  (`ModelContextProtocol.AspNetCore`) inside VLC, compiled with Native AOT
  with no AOT or trim warnings
- Controls VLC through VLCLR's `VLCPlayer` and `VLCPlaylist` wrappers
- Finds moments through a search service it is given, or in the media's
  subtitle file

## Tools

| Tool | Does |
|---|---|
| `open` | Adds a local file or a URL to the playlist, and plays it |
| `search` | Finds the moments of the current media that match a description, with their times |
| `status` | State, media name, time and length, volume, position in the playlist |
| `play` | Starts playback, or resumes it when paused |
| `pause` | Pauses |
| `seek` | Jumps to a time in seconds, from the start or (`relative`) from the current time |
| `next`, `previous` | Plays the next or previous playlist item |
| `set_volume` | Sets the volume, 0 to 200 percent |

An agent answers "play the part where they argue in the kitchen" with
`search`, then `seek` to the best result.

## Search

By default, `search` reads the SubRip file next to the media (`movie.srt`
or `movie.en.srt` for `movie.mp4`) and finds the lines that contain the
query's words: it searches what is said, with no model.

With `--mcp-search-url=<url>`, it asks a local search service instead, such
as one that indexes what is shown. The contract is one POST of JSON:

```json
{ "media": "file:///C:/Videos/movie.mp4", "query": "a red truck", "count": 5 }
```

answered with the best moments first, `time` in seconds and `text` optional:

```json
{ "moments": [ { "time": 754.5, "score": 0.31, "text": "a red truck" } ] }
```

## Building

```bash
dotnet publish samples/McpRemote -c Release -r win-x64

# Output
samples/McpRemote/bin/Release/net10.0/win-x64/publish/libdotnet_mcp_plugin.dll
```

The plugin is about 15 MB: most of it is Kestrel.

## Running

Put the DLL in a folder VLC loads plugins from, then start VLC with the
interface as an extra interface:

```bash
set VLC_PLUGIN_PATH=C:\path\to\folder\with\the\dll
vlc --extraintf=dotnet_mcp
```

VLC's log shows `[McpRemote] MCP server on http://127.0.0.1:8765/mcp`.
`--mcp-port=<port>` changes the port.

To connect Claude Code:

```bash
claude mcp add --transport http vlc http://127.0.0.1:8765/mcp
```

Then ask things like "play C:\Videos\movie.mp4", "what is VLC playing?",
"go back 30 seconds", or "jump to where they talk about the beach".

## Security

The server listens on 127.0.0.1 only. A web page could still reach it
through DNS rebinding, so it also rejects any request whose `Host` header is
not `127.0.0.1` or `localhost`, and any request from a web page that is not
local (`Origin` header). It has no authentication: any program on the machine
can control VLC, and open any file or URL in it, while the interface runs.

## Files

| File | Purpose |
|---|---|
| `McpInterface.cs` | The interface module: options, open and close |
| `McpServerHost.cs` | Kestrel and the MCP server, started and stopped with the interface |
| `PlayerTools.cs` | The MCP tools |
| `MomentSearch.cs` | The search contract, and the client for a search service |
| `SubtitleSearch.cs` | Search in the subtitle file next to the media |
