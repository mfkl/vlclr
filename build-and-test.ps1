# build-and-test.ps1
# Builds the .NET VLC plugin and runs the integration test

param(
    [switch]$SkipBuild,
    [string]$VideoPath = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4",
    [int]$TestTimeout = 10,
    [string]$VlcSdkPath
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

# Paths
$projectFile = Join-Path $scriptDir "samples\VideoOverlay\VideoOverlay.csproj"
$pluginSource = Join-Path $scriptDir "samples\VideoOverlay\bin\Release\net10.0\win-x64\native\libdotnet_overlay_plugin.dll"
$integrationTestProject = Join-Path $scriptDir "tests\IntegrationTest"

# VLC SDK location
if ($VlcSdkPath) {
    $vlcDir = $VlcSdkPath
} else {
    $vlcDir = Join-Path $scriptDir "vlc-binaries\vlc-4.0.0-dev"
}

$pluginDir = Join-Path $vlcDir "plugins\video_filter"
$pluginDest = Join-Path $pluginDir "libdotnet_overlay_plugin.dll"

# Ensure vswhere.exe is in PATH for Native AOT linking
$vsWherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path $vsWherePath) -and ($env:PATH -notlike "*$vsWherePath*")) {
    $env:PATH = "$vsWherePath;$env:PATH"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " .NET VLC Plugin Build & Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the plugin
if (-not $SkipBuild) {
    Write-Host "[1/3] Building plugin (Native AOT)..." -ForegroundColor Yellow

    $publishResult = dotnet publish $projectFile -c Release -r win-x64 --self-contained 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED!" -ForegroundColor Red
        Write-Host $publishResult
        exit 1
    }

    if (-not (Test-Path $pluginSource)) {
        Write-Host "ERROR: Plugin not found at: $pluginSource" -ForegroundColor Red
        exit 1
    }

    $pluginSize = (Get-Item $pluginSource).Length / 1MB
    Write-Host "      Built: libdotnet_overlay_plugin.dll ($($pluginSize.ToString('F1')) MB)" -ForegroundColor Green
} else {
    Write-Host "[1/3] Skipping build" -ForegroundColor DarkGray
    if (-not (Test-Path $pluginSource)) {
        Write-Host "ERROR: No existing build found" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""

# Step 2: Deploy plugin
Write-Host "[2/3] Deploying plugin..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $pluginSource $pluginDest -Force
Write-Host "      Deployed to: $pluginDest" -ForegroundColor Green

Write-Host ""

# Step 3: Run integration test
Write-Host "[3/3] Running integration test..." -ForegroundColor Yellow
Write-Host "      Video: $VideoPath" -ForegroundColor DarkGray
Write-Host ""

dotnet run --project $integrationTestProject -- $vlcDir $VideoPath $TestTimeout
$testExitCode = $LASTEXITCODE

Write-Host ""
if ($testExitCode -eq 0) {
    Write-Host "BUILD AND TEST: PASSED" -ForegroundColor Green
} else {
    Write-Host "BUILD AND TEST: FAILED" -ForegroundColor Red
}

exit $testExitCode
