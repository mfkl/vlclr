#!/usr/bin/env python3
"""Drive the McpRemote plugin (dotnet_mcp) as an MCP client would (stdlib only).

  mcp-check.py media DIR [--amplitude A]
      Write DIR/tone.wav, a 10 s 440 Hz tone, and DIR/tone.srt next to it.
  mcp-check.py check URL DIR [--volume required|optional]
      With VLC started with --extraintf=dotnet_mcp and an empty playlist,
      call every tool on DIR/tone.wav and check the answers. The volume
      checks need an audio output with a volume, such as ALSA's (on the null
      device too); with "optional", they are skipped when VLC has none.

Exit status 0 when every check passes.
"""

import argparse
import json
import math
import os
import re
import struct
import sys
import time
import urllib.error
import urllib.request
import wave

RATE = 48000
SECONDS = 10

SUBTITLES = """1
00:00:01,000 --> 00:00:03,000
Good morning, everyone.

2
00:00:04,500 --> 00:00:07,000
<i>Why do you always leave</i>
the dishes in the sink?

3
00:00:07,200 --> 00:00:09,000
We had this argument in the kitchen yesterday!
"""

TOOLS = {"open", "search", "status", "play", "pause", "seek", "next", "previous", "set_volume"}


def write_media(directory, amplitude):
    os.makedirs(directory, exist_ok=True)
    with wave.open(os.path.join(directory, "tone.wav"), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        peak = amplitude * 32767
        out.writeframes(b"".join(
            struct.pack("<h", round(peak * math.sin(2 * math.pi * 440 * i / RATE)))
            for i in range(RATE * SECONDS)))
    with open(os.path.join(directory, "tone.srt"), "w", encoding="utf-8", newline="\n") as out:
        out.write(SUBTITLES)


class Client:
    def __init__(self, url):
        self.url = url
        self.id = 0

    def call(self, method, params=None):
        self.id += 1
        body = json.dumps({"jsonrpc": "2.0", "id": self.id, "method": method, "params": params or {}}).encode()
        request = urllib.request.Request(self.url, body, {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
        })
        with urllib.request.urlopen(request, timeout=30) as response:
            text = response.read().decode()
        # The answer is one server-sent event: "data: {...}".
        data = next(line[5:] for line in text.splitlines() if line.startswith("data:"))
        return json.loads(data)

    def tool(self, name, **arguments):
        result = self.call("tools/call", {"name": name, "arguments": arguments})
        text = result["result"]["content"][0]["text"]
        print(f"   {name} {json.dumps(arguments)} -> {text.replace(chr(10), ' | ')}")
        return text


def check(url, directory, volume):
    client = Client(url)
    failures = []

    def expect(condition, what):
        if not condition:
            failures.append(what)
            print(f"FAIL: {what}")

    deadline = time.monotonic() + 30
    while True:
        try:
            info = client.call("initialize", {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "clientInfo": {"name": "mcp-check", "version": "1"},
            })
            break
        except (urllib.error.URLError, ConnectionError):
            if time.monotonic() > deadline:
                print(f"FAIL: no MCP server at {url}")
                return 1
            time.sleep(0.25)
    print(f"   server {info['result']['serverInfo']['name']}, protocol {info['result']['protocolVersion']}")

    tools = {tool["name"] for tool in client.call("tools/list")["result"]["tools"]}
    expect(tools == TOOLS, f"tools are {sorted(tools)}")

    tone = os.path.abspath(os.path.join(directory, "tone.wav"))
    expect(client.tool("search", query="kitchen").startswith("Nothing is playing"), "search with no media")
    expect(client.tool("open", location=os.path.join(os.path.abspath(directory), "missing.wav"))
           .startswith("There is no file"), "open of a missing file")
    expect(client.tool("open", location="tone.wav").startswith("Give an absolute path"), "open of a relative path")
    expect(client.tool("open", location=tone).startswith("Playing tone.wav"), "open did not play the tone")

    status = client.tool("status")
    expect("state: playing" in status, "status after open: not playing")
    expect("media: tone.wav" in status, "status after open: wrong media")
    # Not necessarily 100 %: Windows keeps the volume each program last set.
    has_volume = "volume: no audio output" not in status
    if has_volume or volume == "required":
        expect(re.search(r"volume: \d+%", status), "status after open: no volume (no audio output?)")
    else:
        print("SKIP: VLC has no audio output with a volume here: volume not checked")

    expect(client.tool("search", query="the kitchen argument").startswith("0:07.2,"), "search for the kitchen")
    expect(client.tool("search", query="argue in the kitchen", count=1).startswith("0:07.2,"),
           "search by word stems")
    expect(client.tool("search", query="spaceship").startswith("No moment matches"), "search with no match")

    # Seeks while playing: when paused, VLC updates the time of an audio-only
    # media only once it plays again.
    # Past what a TimeSpan holds: the start of the media.
    expect(client.tool("seek", seconds=-1e300, relative=True).startswith("Now at 0:0"), "seek back 1e300 s")
    expect(client.tool("seek", seconds=7.2).startswith("Now at 0:07."), "seek to 7.2 s")
    expect(client.tool("seek", seconds=-6, relative=True).startswith("Now at 0:01."), "seek back 6 s")
    expect(client.tool("pause").startswith("Paused at 0:01."), "pause")
    expect("state: paused" in client.tool("status"), "status after pause: not paused")
    expect(client.tool("play").startswith("Playing tone.wav, at 0:01."), "play after pause")

    if has_volume or volume == "required":
        for percent in (80, 50):
            expect(client.tool("set_volume", percent=percent) == f"Volume set to {percent}%.", f"set_volume {percent}")
            expect(f"volume: {percent}%" in client.tool("status"), f"status after set_volume: volume is not {percent}%")
        client.tool("set_volume", percent=100)
    else:
        expect(client.tool("set_volume", percent=50) == "There is no audio output.", "set_volume with no output")
    expect(client.tool("next") == "There is no next item.", "next with one item")

    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    media = commands.add_parser("media")
    media.add_argument("dir")
    media.add_argument("--amplitude", type=float, default=0.5)
    run = commands.add_parser("check")
    run.add_argument("url")
    run.add_argument("dir")
    run.add_argument("--volume", choices=["required", "optional"], default="required")
    args = parser.parse_args()

    if args.command == "media":
        write_media(args.dir, args.amplitude)
        return 0
    return check(args.url, args.dir, args.volume)


if __name__ == "__main__":
    sys.exit(main())
