[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [string]$Ffmpeg = "ffmpeg"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "live-sync-speech.mp4"
}
$output = [System.IO.Path]::GetFullPath($OutputPath)
$directory = [System.IO.Path]::GetDirectoryName($output)
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$wave = Join-Path ([System.IO.Path]::GetTempPath()) (
    "vlclr-live-sync-speech-" + [Guid]::NewGuid().ToString("N") + ".wav")

try {
    Add-Type -AssemblyName System.Speech
    $synthesizer = [System.Speech.Synthesis.SpeechSynthesizer]::new()
    try {
        $synthesizer.Rate = -1
        $synthesizer.SetOutputToWaveFile($wave)
        $synthesizer.Speak(
            "This is the V L C R live translation fixture. " +
            "The worker keeps this clearly spoken sentence synchronized with the video.")
    }
    finally {
        $synthesizer.Dispose()
    }

    & $Ffmpeg -hide_banner -loglevel error -y `
        -f lavfi -i "color=c=0x204060:s=1280x720:r=30:d=35" `
        -stream_loop -1 -i $wave `
        -t 35 -shortest `
        -c:v libx264 -pix_fmt yuv420p -preset veryfast `
        -c:a aac -b:a 128k `
        -metadata:s:v:0 rotate=0 `
        $output
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output -PathType Leaf)) {
        throw "ffmpeg did not create the live-sync fixture."
    }
    Write-Host "Created normal-orientation English speech fixture: $output"
}
finally {
    if (Test-Path -LiteralPath $wave) {
        Remove-Item -LiteralPath $wave -Force
    }
}
