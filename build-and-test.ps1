# build-and-test.ps1
# Builds the .NET VLC plugins and runs the integration tests

param(
    [switch]$SkipBuild,
    [string]$VideoPath = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4",
    [int]$TestTimeout = 10,
    [string]$VlcSdkPath,
    [string]$SubtitleFile,
    [switch]$VideoOverlayOnly,
    [switch]$SubtitleRendererOnly
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

# Paths - VideoOverlay
$videoOverlayProject = Join-Path $scriptDir "samples\VideoOverlay\VideoOverlay.csproj"
$videoOverlayPlugin = Join-Path $scriptDir "samples\VideoOverlay\bin\Release\net10.0\win-x64\native\libdotnet_overlay_plugin.dll"
$videoOverlayTestProject = Join-Path $scriptDir "tests\IntegrationTest"

# Paths - SubtitleRenderer
$subtitleRendererProject = Join-Path $scriptDir "samples\SubtitleRenderer\SubtitleRenderer.csproj"
$subtitleRendererPlugin = Join-Path $scriptDir "samples\SubtitleRenderer\bin\Release\net10.0\win-x64\native\libdotnet_subtitle_plugin.dll"
$subtitleRendererTestProject = Join-Path $scriptDir "tests\SubtitleRendererTest"
$defaultSubtitleFile = Join-Path $scriptDir "tests\SubtitleRendererTest\fixtures\test.srt"

# VLC SDK location
if ($VlcSdkPath) {
    $vlcDir = $VlcSdkPath
} else {
    $vlcDir = Join-Path $scriptDir "vlc-binaries\vlc-4.0.0-dev"
}

$videoFilterDir = Join-Path $vlcDir "plugins\video_filter"
$spuDir = Join-Path $vlcDir "plugins\spu"
$videoOverlayDest = Join-Path $videoFilterDir "libdotnet_overlay_plugin.dll"
$subtitleRendererDest = Join-Path $spuDir "libdotnet_subtitle_plugin.dll"

# Determine which tests to run
$runVideoOverlay = (-not $SubtitleRendererOnly)
$runSubtitleRenderer = (-not $VideoOverlayOnly)

# Ensure vswhere.exe is in PATH for Native AOT linking
$vsWherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path $vsWherePath) -and ($env:PATH -notlike "*$vsWherePath*")) {
    $env:PATH = "$vsWherePath;$env:PATH"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " .NET VLC Plugin Build & Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build plugins
if (-not $SkipBuild) {
    Write-Host "[1/4] Building plugins (Native AOT)..." -ForegroundColor Yellow

    # Build VideoOverlay if requested
    if ($runVideoOverlay) {
        Write-Host "      Building VideoOverlay..." -ForegroundColor DarkGray
        $publishResult = dotnet publish $videoOverlayProject -c Release -r win-x64 --self-contained 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "VideoOverlay BUILD FAILED!" -ForegroundColor Red
            Write-Host $publishResult
            exit 1
        }

        if (-not (Test-Path $videoOverlayPlugin)) {
            Write-Host "ERROR: VideoOverlay plugin not found at: $videoOverlayPlugin" -ForegroundColor Red
            exit 1
        }

        $pluginSize = (Get-Item $videoOverlayPlugin).Length / 1MB
        Write-Host "      Built: libdotnet_overlay_plugin.dll ($($pluginSize.ToString('F1')) MB)" -ForegroundColor Green
    }

    # Build SubtitleRenderer if requested
    if ($runSubtitleRenderer) {
        Write-Host "      Building SubtitleRenderer..." -ForegroundColor DarkGray
        $publishResult = dotnet publish $subtitleRendererProject -c Release -r win-x64 --self-contained 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "SubtitleRenderer BUILD FAILED!" -ForegroundColor Red
            Write-Host $publishResult
            exit 1
        }

        if (-not (Test-Path $subtitleRendererPlugin)) {
            Write-Host "ERROR: SubtitleRenderer plugin not found at: $subtitleRendererPlugin" -ForegroundColor Red
            exit 1
        }

        $pluginSize = (Get-Item $subtitleRendererPlugin).Length / 1MB
        Write-Host "      Built: libdotnet_subtitle_plugin.dll ($($pluginSize.ToString('F1')) MB)" -ForegroundColor Green
    }
} else {
    Write-Host "[1/4] Skipping build" -ForegroundColor DarkGray
    if ($runVideoOverlay -and -not (Test-Path $videoOverlayPlugin)) {
        Write-Host "ERROR: No existing VideoOverlay build found" -ForegroundColor Red
        exit 1
    }
    if ($runSubtitleRenderer -and -not (Test-Path $subtitleRendererPlugin)) {
        Write-Host "ERROR: No existing SubtitleRenderer build found" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""

# Step 2: Deploy plugins
Write-Host "[2/4] Deploying plugins..." -ForegroundColor Yellow

if ($runVideoOverlay) {
    New-Item -ItemType Directory -Force -Path $videoFilterDir | Out-Null
    Copy-Item $videoOverlayPlugin $videoOverlayDest -Force
    Write-Host "      Deployed VideoOverlay to: $videoOverlayDest" -ForegroundColor Green
}

if ($runSubtitleRenderer) {
    New-Item -ItemType Directory -Force -Path $spuDir | Out-Null
    Copy-Item $subtitleRendererPlugin $subtitleRendererDest -Force
    Write-Host "      Deployed SubtitleRenderer to: $subtitleRendererDest" -ForegroundColor Green
}

Write-Host ""

$allTestsPassed = $true

# Step 3: Run VideoOverlay integration test
if ($runVideoOverlay) {
    Write-Host "[3/4] Running VideoOverlay integration test..." -ForegroundColor Yellow
    Write-Host "      Video: $VideoPath" -ForegroundColor DarkGray
    Write-Host ""

    dotnet run --project $videoOverlayTestProject -- $vlcDir $VideoPath $TestTimeout
    if ($LASTEXITCODE -ne 0) {
        $allTestsPassed = $false
    }
    Write-Host ""
} else {
    Write-Host "[3/4] Skipping VideoOverlay test" -ForegroundColor DarkGray
    Write-Host ""
}

# Step 4: Run SubtitleRenderer integration test
if ($runSubtitleRenderer) {
    Write-Host "[4/4] Running SubtitleRenderer integration test..." -ForegroundColor Yellow
    Write-Host "      Video: $VideoPath" -ForegroundColor DarkGray

    # Use provided subtitle file or default
    $subtitleArg = if ($SubtitleFile) { $SubtitleFile } else { $defaultSubtitleFile }
    Write-Host "      Subtitles: $subtitleArg" -ForegroundColor DarkGray
    Write-Host ""

    dotnet run --project $subtitleRendererTestProject -- $vlcDir $VideoPath $subtitleArg $TestTimeout
    if ($LASTEXITCODE -ne 0) {
        $allTestsPassed = $false
    }
    Write-Host ""
} else {
    Write-Host "[4/4] Skipping SubtitleRenderer test" -ForegroundColor DarkGray
    Write-Host ""
}

Write-Host ""
if ($allTestsPassed) {
    Write-Host "BUILD AND TEST: PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "BUILD AND TEST: FAILED" -ForegroundColor Red
    exit 1
}
