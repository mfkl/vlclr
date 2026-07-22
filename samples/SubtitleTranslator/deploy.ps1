[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$VlcDirectory,
    [string]$ModelDirectory = (Join-Path $PSScriptRoot "models/opus-mt-en-fr"),
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$vlcRoot = [System.IO.Path]::GetFullPath($VlcDirectory)
$modelRoot = [System.IO.Path]::GetFullPath($ModelDirectory)
$projectPath = Join-Path $PSScriptRoot "SubtitleTranslator.csproj"
$publishDirectory = Join-Path $PSScriptRoot "bin/$Configuration/net10.0/$RuntimeIdentifier/publish"

if (-not (Test-Path -LiteralPath $vlcRoot -PathType Container)) {
    throw "VLC directory not found: $vlcRoot"
}

if (-not $SkipPublish) {
    dotnet publish $projectPath -c $Configuration -r $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

$pluginPath = Join-Path $publishDirectory "libdotnet_subtitle_translator_plugin.dll"
$runtimePath = Join-Path $publishDirectory "onnxruntime.dll"
foreach ($requiredPath in @($pluginPath, $runtimePath, (Join-Path $modelRoot "model-manifest.json"))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required deployment file not found: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $modelRoot "model-manifest.json") -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $sourcePath = Join-Path $modelRoot $file.fileName
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

$pluginDirectory = Join-Path $vlcRoot "plugins/spu"
$deployedModelDirectory = Join-Path $vlcRoot "models/opus-mt-$($manifest.sourceLanguage)-$($manifest.targetLanguage)"
New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $deployedModelDirectory | Out-Null

Copy-Item -LiteralPath $pluginPath -Destination (Join-Path $pluginDirectory (Split-Path $pluginPath -Leaf)) -Force
Copy-Item -LiteralPath $runtimePath -Destination (Join-Path $vlcRoot "onnxruntime.dll") -Force
Copy-Item -LiteralPath (Join-Path $modelRoot "model-manifest.json") -Destination $deployedModelDirectory -Force
foreach ($file in $manifest.files) {
    Copy-Item -LiteralPath (Join-Path $modelRoot $file.fileName) -Destination $deployedModelDirectory -Force
}

Write-Host "Deployed plugin: $pluginDirectory"
Write-Host "Deployed ONNX Runtime: $(Join-Path $vlcRoot 'onnxruntime.dll')"
Write-Host "Deployed model: $deployedModelDirectory"
