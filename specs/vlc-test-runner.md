# VLC Test Runner Specification

## Overview

This spec defines a reliable method to run VLC.exe for testing video filter plugins with:
- Live stdout/stderr streaming (not post-hoc log files)
- Automatic timeout to kill VLC after N seconds
- Proper command-line arguments for automated testing
- Cross-platform support (Windows focus)

## Problem Statement

Current testing issues:
1. VLC Qt interface doesn't auto-play files from command line
2. Git Bash mangles Windows paths when passed to VLC
3. Checking log files after VLC exits misses real-time issues
4. Manual testing requires Ctrl+C to stop VLC

## Solution: PowerShell Test Script

Use PowerShell for reliable Windows process management with:
- Process spawning with stdout/stderr redirection
- Timeout via `Wait-Process -Timeout`
- Proper path handling (no Git Bash path mangling)
- Console output in real-time

## Script: `test-vlc-filter.ps1`

```powershell
param(
    [string]$VideoPath = "$env:USERPROFILE\Videos\BigBuckBunny.mp4",
    [int]$TimeoutSeconds = 10,
    [string]$Filter = "dotnet_overlay"
)

$vlcExe = "$PSScriptRoot\vlc-binaries\vlc-4.0.0-dev\vlc.exe"
$pluginPath = "$PSScriptRoot\vlc-binaries\vlc-4.0.0-dev\plugins"

# Convert to file:// URL for VLC
$videoUrl = "file:///" + $VideoPath.Replace("\", "/")

$args = @(
    "-vvv",                           # Verbose logging
    "--no-hw-dec",                    # Software decoding (CPU-accessible frames)
    "--video-filter=$Filter",         # Our filter
    "--play-and-exit",                # Exit when done
    "--no-interact",                  # No user prompts
    $videoUrl
)

Write-Host "Starting VLC with filter: $Filter"
Write-Host "Video: $videoUrl"
Write-Host "Timeout: ${TimeoutSeconds}s"
Write-Host "---"

$process = Start-Process -FilePath $vlcExe -ArgumentList $args -PassThru -NoNewWindow -RedirectStandardError "vlc_stderr.txt"

$exited = $process.WaitForExit($TimeoutSeconds * 1000)

if (-not $exited) {
    Write-Host "---"
    Write-Host "Timeout reached, stopping VLC..."
    $process.Kill()
}

Write-Host "---"
Write-Host "VLC exit code: $($process.ExitCode)"

# Show stderr output
if (Test-Path "vlc_stderr.txt") {
    Write-Host "--- stderr output ---"
    Get-Content "vlc_stderr.txt" | Select-Object -Last 50
}
```

## Alternative: Batch Script with timeout

For simpler cases, use Windows `timeout` command:

```batch
@echo off
setlocal

set VLC=vlc-binaries\vlc-4.0.0-dev\vlc.exe
set VIDEO=file:///%USERPROFILE:\=/%/Videos/BigBuckBunny.mp4

echo Starting VLC with dotnet_overlay filter...
start /wait /b "" "%VLC%" -vvv --no-hw-dec --video-filter=dotnet_overlay --play-and-exit %VIDEO%

echo VLC exited
```

## VLC Command-Line Arguments

### Required for Automated Testing

| Argument | Purpose |
|----------|---------|
| `-vvv` | Maximum verbosity (shows filter loading) |
| `--no-hw-dec` | Disable hardware decoding (CPU-accessible pixels) |
| `--video-filter=dotnet_overlay` | Enable our filter |
| `--play-and-exit` | Exit when playback ends |
| `--no-interact` | No user interaction prompts |
| `file:///path` | URL format for reliable path handling |

### Optional for Debugging

| Argument | Purpose |
|----------|---------|
| `--verbose-objects=+dotnet_overlay` | Extra verbosity for our module |
| `--file-logging` | Write logs to file |
| `--logfile=vlc.log` | Specify log file path |
| `--qt-start-minimized` | Start Qt window minimized |
| `--no-video-title-show` | Hide title overlay |

## Expected Output

### Successful Filter Load

```
[00000001] main video output: looking for video filter module matching "dotnet_overlay": 1 candidates
[00000001] main video output: using video filter module "dotnet_overlay"
[00000001] dotnet_overlay video filter: .NET Overlay: Open called, format I420 1920x1080
[00000001] dotnet_overlay video filter: .NET Overlay: Filter opened successfully
[00000001] dotnet_overlay video filter: .NET Overlay: Frame 100
[00000001] dotnet_overlay video filter: .NET Overlay: Frame 200
```

### stderr from C# (via Console.Error.WriteLine)

```
[VlcPlugin] FilterState initialized: 1920x1080
[VlcPlugin] Chroma fourcc: I420
[VlcPlugin] Saved debug overlay to: overlay_test.png
```

## Debug Files Generated

After successful run:
- `dotnet_filter_open.txt` - Proof that C Open() was called
- `dotnet_filter_frame.txt` - First frame info
- `overlay_test.png` - ImageSharp rendered overlay

## Acceptance Criteria

- [ ] VLC starts and plays video automatically
- [ ] stdout/stderr visible in real-time
- [ ] VLC exits automatically after timeout or playback end
- [ ] Filter messages visible in output
- [ ] Debug files created in working directory
- [ ] No path mangling issues
