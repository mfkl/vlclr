[CmdletBinding()]
param(
    [string]$PublishDirectory = (
        Join-Path $PSScriptRoot "../samples/LiveAudioTranslator/bin/Release/net10.0/win-x64/publish")
)

$ErrorActionPreference = "Stop"
$publishRoot = [System.IO.Path]::GetFullPath($PublishDirectory)
$pluginPath = Join-Path $publishRoot "libdotnet_live_audio_translator_plugin.dll"
if (-not (Test-Path -LiteralPath $pluginPath -PathType Leaf)) {
    throw "Native AOT plugin not found: $pluginPath"
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw "vswhere.exe not found: $vswhere"
}

$visualStudio = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) {
    throw "A Visual Studio installation with C++ tools was not found."
}

$dumpbin = Get-ChildItem -LiteralPath (Join-Path $visualStudio "VC/Tools/MSVC") `
    -Recurse -Filter dumpbin.exe |
    Where-Object { $_.FullName -like "*\bin\Hostx64\x64\dumpbin.exe" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($dumpbin)) {
    throw "dumpbin.exe was not found below $visualStudio."
}

$exports = (& $dumpbin /exports $pluginPath 2>&1) -join "`n"
foreach ($symbol in @("vlc_entry", "vlc_entry_api_version", "vlc_entry_copyright")) {
    if ($exports -notmatch "(?m)\s$([regex]::Escape($symbol))\s") {
        throw "Required VLC export is missing: $symbol"
    }
}

$imports = (& $dumpbin /imports $pluginPath 2>&1) -join "`n"
if ($imports -notmatch "(?i)libvlccore\.dll") {
    throw "Native AOT plugin does not import libvlccore.dll."
}

Write-Host "Live audio translator Native AOT smoke passed."
Write-Host "  Plugin: $pluginPath"
Write-Host "  dumpbin: $dumpbin"
