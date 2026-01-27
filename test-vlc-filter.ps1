# test-vlc-filter.ps1
# Simple VLC video filter test

param(
    [string]$VideoPath = "$env:USERPROFILE\Videos\BigBuckBunny.mp4",
    [string]$Filter = "dotnet_overlay"
)

$vlcExe = Join-Path $PSScriptRoot "vlc-binaries\vlc-4.0.0-dev\vlc.exe"
$videoUrl = "file:///" + $VideoPath.Replace("\", "/")

Write-Host "Filter: $Filter"
Write-Host "Video:  $videoUrl"
Write-Host "VLC:    $vlcExe"
Write-Host ""

& "$vlcExe" -vvv --no-hw-dec "--video-filter=$Filter" --play-and-exit "$videoUrl" 2>&1
