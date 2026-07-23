[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$VlcDirectory,
    [string]$WhisperModelDirectory = (Join-Path $PSScriptRoot "models/whisper"),
    [string]$TranslationModelDirectory = (Join-Path $PSScriptRoot "models/opus-mt-en-fr"),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipPublish,
    [switch]$SkipCacheGeneration
)

$ErrorActionPreference = "Stop"
$vlcRoot = [System.IO.Path]::GetFullPath($VlcDirectory)
$whisperRoot = [System.IO.Path]::GetFullPath($WhisperModelDirectory)
$translationRoot = [System.IO.Path]::GetFullPath($TranslationModelDirectory)
$projectPath = Join-Path $PSScriptRoot "LiveAudioTranslator.csproj"
$publishDirectory = Join-Path $PSScriptRoot "bin/$Configuration/net10.0/$RuntimeIdentifier/publish"
$helperProjectPath = Join-Path $PSScriptRoot "../LiveAudioTranslator.Prepare/LiveAudioTranslator.Prepare.csproj"
$helperPublishDirectory = Join-Path $PSScriptRoot "../LiveAudioTranslator.Prepare/bin/$Configuration/net10.0/$RuntimeIdentifier/publish"

if (-not (Test-Path -LiteralPath $vlcRoot -PathType Container)) {
    throw "VLC directory not found: $vlcRoot"
}

if (-not $SkipPublish) {
    dotnet publish $projectPath -c $Configuration -r $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
    dotnet publish $helperProjectPath -c $Configuration -r $RuntimeIdentifier --self-contained false
    if ($LASTEXITCODE -ne 0) {
        throw "Preparation helper publish failed with exit code $LASTEXITCODE."
    }
}

function Test-ModelBundle {
    param([Parameter(Mandatory)][string]$Directory)

    $bundleManifestPath = Join-Path $Directory "model-manifest.json"
    if (-not (Test-Path -LiteralPath $bundleManifestPath -PathType Leaf)) {
        throw "Model manifest not found: $bundleManifestPath"
    }

    $bundleManifest = Get-Content -LiteralPath $bundleManifestPath -Raw | ConvertFrom-Json
    foreach ($file in $bundleManifest.files) {
        $sourcePath = Join-Path $Directory $file.fileName
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required model file not found: $sourcePath"
        }

        $source = Get-Item -LiteralPath $sourcePath
        $hash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        if ($source.Length -ne [long]$file.size -or
            -not $hash.Equals($file.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Model file failed manifest validation: $sourcePath"
        }
    }

    return $bundleManifest
}

$whisperManifest = Test-ModelBundle -Directory $whisperRoot
$translationManifest = Test-ModelBundle -Directory $translationRoot
$pluginPath = Join-Path $publishDirectory "libdotnet_live_audio_translator_plugin.dll"
$onnxRuntimePath = Join-Path $publishDirectory "onnxruntime.dll"
$providersPath = Join-Path $publishDirectory "onnxruntime_providers_shared.dll"
$whisperRuntimeDirectory = Join-Path $publishDirectory "runtimes/win-x64"
$whisperRuntimeNames = @(
    "ggml-base-whisper.dll",
    "ggml-cpu-whisper.dll",
    "ggml-whisper.dll",
    "whisper.dll"
)

foreach ($requiredPath in @($pluginPath, $onnxRuntimePath, $providersPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required deployment file not found: $requiredPath"
    }
}
foreach ($runtimeName in $whisperRuntimeNames) {
    $runtimePath = Join-Path $whisperRuntimeDirectory $runtimeName
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
        throw "Required Whisper runtime not found: $runtimePath"
    }
}
$helperPath = Join-Path $helperPublishDirectory "LiveAudioTranslator.Prepare.dll"
if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "Preparation helper output not found: $helperPath"
}

$pluginDirectory = Join-Path $vlcRoot "plugins/audio_filter"
$deployedWhisperRuntimeDirectory = Join-Path $vlcRoot "runtimes/win-x64"
$deployedWhisperModelDirectory = Join-Path $vlcRoot "models/whisper"
$deployedTranslationModelDirectory = Join-Path $vlcRoot "models/opus-mt-$($translationManifest.sourceLanguage)-$($translationManifest.targetLanguage)"
$deployedHelperDirectory = Join-Path $vlcRoot "helpers/live-audio-translator"
foreach ($directory in @(
    $pluginDirectory,
    $deployedWhisperRuntimeDirectory,
    $deployedWhisperModelDirectory,
    $deployedTranslationModelDirectory,
    $deployedHelperDirectory
)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Get-ChildItem -LiteralPath $helperPublishDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $deployedHelperDirectory -Recurse -Force
}

Copy-Item -LiteralPath $pluginPath -Destination $pluginDirectory -Force
Copy-Item -LiteralPath $onnxRuntimePath -Destination $vlcRoot -Force
Copy-Item -LiteralPath $providersPath -Destination $vlcRoot -Force
foreach ($runtimeName in $whisperRuntimeNames) {
    Copy-Item -LiteralPath (Join-Path $whisperRuntimeDirectory $runtimeName) `
        -Destination $deployedWhisperRuntimeDirectory -Force
}

Copy-Item -LiteralPath (Join-Path $whisperRoot "model-manifest.json") `
    -Destination $deployedWhisperModelDirectory -Force
foreach ($file in $whisperManifest.files) {
    Copy-Item -LiteralPath (Join-Path $whisperRoot $file.fileName) `
        -Destination $deployedWhisperModelDirectory -Force
}

Copy-Item -LiteralPath (Join-Path $translationRoot "model-manifest.json") `
    -Destination $deployedTranslationModelDirectory -Force
foreach ($file in $translationManifest.files) {
    Copy-Item -LiteralPath (Join-Path $translationRoot $file.fileName) `
        -Destination $deployedTranslationModelDirectory -Force
}

if (-not $SkipCacheGeneration) {
    $cacheGenerator = Join-Path $vlcRoot "vlc-cache-gen.exe"
    if (-not (Test-Path -LiteralPath $cacheGenerator -PathType Leaf)) {
        throw "VLC cache generator not found: $cacheGenerator"
    }

    & $cacheGenerator (Join-Path $vlcRoot "plugins")
    if ($LASTEXITCODE -ne 0) {
        throw "vlc-cache-gen failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Deployed plugin modules: dotnet_audio_translator, dotnet_live_subtitles"
Write-Host "Deployed Whisper model: $deployedWhisperModelDirectory"
Write-Host "Deployed translation model: $deployedTranslationModelDirectory"
Write-Host "Deployed preparation helper: $deployedHelperDirectory"
Write-Host "Run from Git Bash: samples/LiveAudioTranslator/run.sh /path/to/video.mp4"
