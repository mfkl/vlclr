# build-and-test.ps1
# Builds the dotnet VLC plugin, deploys it, and validates VLC can load it

param(
    [switch]$SkipBuild,
    [switch]$SkipCacheRegen,
    [string]$VideoPath = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4",
    [string]$Filter = "dotnet_overlay",
    [int]$TestTimeout = 15,
    [string]$VlcBinaryPath  # Override VLC location (for CI)
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot

# Paths
$projectDir = Join-Path $scriptDir "samples\VideoOverlay"
$projectFile = Join-Path $projectDir "VideoOverlay.csproj"
$nativeOutputDir = Join-Path $projectDir "bin\Release\net10.0\win-x64\native"
$pluginSource = Join-Path $nativeOutputDir "libdotnet_overlay_plugin.dll"

# Use VlcBinaryPath if provided, otherwise default
if ($VlcBinaryPath) {
    $vlcDir = $VlcBinaryPath
} else {
    $vlcDir = Join-Path $scriptDir "vlc-binaries\vlc-4.0.0-dev"
}

$pluginDir = Join-Path $vlcDir "plugins\video_filter"
$pluginDest = Join-Path $pluginDir "libdotnet_overlay_plugin.dll"
$oldPluginDir = Join-Path $vlcDir "plugins\interface"
$oldPluginDest = Join-Path $oldPluginDir "libdotnet_overlay_plugin.dll"
$vlcExe = Join-Path $vlcDir "vlc.exe"
$cacheGenExe = Join-Path $vlcDir "vlc-cache-gen.exe"
$pluginsDir = Join-Path $vlcDir "plugins"

# Ensure vswhere.exe is in PATH for Native AOT linking
$vsWherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if (Test-Path $vsWherePath) {
    if ($env:PATH -notlike "*$vsWherePath*") {
        $env:PATH = "$vsWherePath;$env:PATH"
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " VLC C# Plugin Build & Test Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the plugin
if (-not $SkipBuild) {
    Write-Host "[1/4] Building dotnet plugin (Native AOT)..." -ForegroundColor Yellow
    Write-Host "      Project: $projectFile"

    $buildStart = Get-Date

    # Clean and publish with Native AOT
    $publishResult = dotnet publish $projectFile -c Release -r win-x64 --self-contained 2>&1
    $buildExitCode = $LASTEXITCODE

    $buildDuration = (Get-Date) - $buildStart

    if ($buildExitCode -ne 0) {
        Write-Host ""
        Write-Host "BUILD FAILED!" -ForegroundColor Red
        Write-Host $publishResult
        exit 1
    }

    Write-Host "      Build completed in $($buildDuration.TotalSeconds.ToString('F1'))s" -ForegroundColor Green

    # Verify output exists
    if (-not (Test-Path $pluginSource)) {
        Write-Host "      ERROR: Native AOT output not found at:" -ForegroundColor Red
        Write-Host "      $pluginSource" -ForegroundColor Red
        exit 1
    }

    $pluginSize = (Get-Item $pluginSource).Length / 1MB
    Write-Host "      Output: libdotnet_overlay_plugin.dll ($($pluginSize.ToString('F1')) MB)" -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping build (--SkipBuild specified)" -ForegroundColor DarkGray

    if (-not (Test-Path $pluginSource)) {
        Write-Host "      ERROR: No existing build found at:" -ForegroundColor Red
        Write-Host "      $pluginSource" -ForegroundColor Red
        exit 1
    }
    $pluginSize = (Get-Item $pluginSource).Length / 1MB
}

Write-Host ""

# Step 2: Clear old plugin and copy new one
Write-Host "[2/4] Deploying plugin to VLC..." -ForegroundColor Yellow
Write-Host "      Target: $pluginDest"

# Remove old plugin from interface folder (legacy location)
if (Test-Path $oldPluginDest) {
    try {
        Remove-Item $oldPluginDest -Force
        Write-Host "      Removed legacy plugin from interface folder" -ForegroundColor DarkGray
    } catch {
        Write-Host "      WARNING: Could not remove legacy plugin from interface folder" -ForegroundColor DarkYellow
    }
}

# Remove old plugin if it exists
if (Test-Path $pluginDest) {
    try {
        Remove-Item $pluginDest -Force
        Write-Host "      Removed old plugin" -ForegroundColor DarkGray
    } catch {
        Write-Host "      ERROR: Could not remove old plugin. Is VLC running?" -ForegroundColor Red
        Write-Host "      $_" -ForegroundColor Red
        exit 1
    }
}

# Copy new plugin
try {
    Copy-Item $pluginSource $pluginDest -Force
    Write-Host "      Copied new plugin" -ForegroundColor Green
} catch {
    Write-Host "      ERROR: Could not copy plugin" -ForegroundColor Red
    Write-Host "      $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 3: Regenerate plugin cache immediately after deployment
Write-Host "[3/5] Regenerating VLC plugin cache..." -ForegroundColor Yellow

# Delete old cache first
$cacheFile = Join-Path $pluginsDir "plugins.dat"
if (Test-Path $cacheFile) {
    Remove-Item $cacheFile -Force -ErrorAction SilentlyContinue
    Write-Host "      Removed old plugin cache" -ForegroundColor DarkGray
}

# Run cache-gen
if (Test-Path $cacheGenExe) {
    $cacheResult = & $cacheGenExe $pluginsDir 2>&1 | Out-String
    Start-Sleep -Seconds 1
    Write-Host "      Plugin cache regenerated" -ForegroundColor Green
}

# Step 3b: Test plugin loading with headless VLC
$headlessTestFailed = $false
$headlessTestError = ""
Write-Host "[3b/5] Testing plugin load with headless VLC..." -ForegroundColor Yellow

# Run VLC headless with verbose logging to see plugin loading
$headlessArgs = @(
    "-vvv",
    "--intf", "dummy",
    "--no-video",
    "--no-audio",
    "--no-playlist-autostart",
    "--play-and-exit",
    "--run-time=1"
)

$tempStderr = Join-Path $env:TEMP "vlc_headless_stderr.txt"
$tempStdout = Join-Path $env:TEMP "vlc_headless_stdout.txt"

Write-Host "      Running: vlc.exe $($headlessArgs -join ' ')" -ForegroundColor DarkGray

try {
    $vlcHeadless = Start-Process -FilePath $vlcExe -ArgumentList $headlessArgs -PassThru -NoNewWindow -RedirectStandardError $tempStderr -RedirectStandardOutput $tempStdout -ErrorAction Stop

    $completed = $vlcHeadless.WaitForExit(15000)
    if (-not $completed) {
        $vlcHeadless.Kill()
        Write-Host "      VLC timed out after 15s (killed)" -ForegroundColor DarkGray
    }

    $vlcExitCode = $vlcHeadless.ExitCode
    Write-Host "      VLC exit code: $vlcExitCode" -ForegroundColor DarkGray

    # Read stderr for analysis
    $headlessStderr = ""
    if (Test-Path $tempStderr) {
        $headlessStderr = Get-Content $tempStderr -Raw -ErrorAction SilentlyContinue
    }

    # Check for assertion failure or crash
    if ($headlessStderr -match "Assertion failed:([^,]+)") {
        $headlessTestFailed = $true
        $headlessTestError = "Assertion: $($Matches[1].Trim())"
        Write-Host "      ASSERTION FAILURE detected" -ForegroundColor Red
        Write-Host "      $headlessTestError" -ForegroundColor Red
    } elseif ($vlcExitCode -ne 0 -and $vlcExitCode -ne $null) {
        # Check if it's a crash vs normal exit
        if ($headlessStderr -match "dotnet_plugin.*error|error.*dotnet_plugin|cannot load.*libdotnet") {
            $headlessTestFailed = $true
            $headlessTestError = "Plugin load error (exit code $vlcExitCode)"
            Write-Host "      Plugin loading error detected" -ForegroundColor Red
        } else {
            Write-Host "      VLC exited with code $vlcExitCode (may be normal)" -ForegroundColor DarkGray
        }
    }

    # Check if our plugin was mentioned in logs
    if ($headlessStderr -match "dotnet_plugin|dotnet_overlay") {
        Write-Host "      Plugin 'dotnet_plugin' found in VLC logs" -ForegroundColor Green
    } else {
        Write-Host "      Plugin not mentioned in VLC logs" -ForegroundColor DarkYellow
    }

} catch {
    $headlessTestFailed = $true
    $headlessTestError = $_.Exception.Message
    Write-Host "      ERROR: Failed to run VLC: $headlessTestError" -ForegroundColor Red
}

Write-Host ""

# Step 4: Regenerate plugin cache
$cacheGenFailed = $false
$cacheGenError = ""
if (-not $SkipCacheRegen) {
    Write-Host "[4/5] Regenerating VLC plugin cache..." -ForegroundColor Yellow

    if (Test-Path $cacheGenExe) {
        try {
            # Run cache-gen and wait for filesystem to sync
            $cacheResult = & $cacheGenExe $pluginsDir 2>&1 | Out-String
            Start-Sleep -Seconds 2
            if ($LASTEXITCODE -eq 0) {
                Write-Host "      Plugin cache updated" -ForegroundColor Green
            } else {
                $cacheGenFailed = $true
                $cacheGenError = $cacheResult
                Write-Host "      WARNING: Cache generation failed (exit code $LASTEXITCODE)" -ForegroundColor DarkYellow
                # Extract assertion info if present
                if ($cacheResult -match "Assertion failed:([^,]+)") {
                    Write-Host "      Assertion: $($Matches[1].Trim())" -ForegroundColor Red
                }
            }
        } catch {
            $cacheGenFailed = $true
            $cacheGenError = $_.Exception.Message
            Write-Host "      WARNING: Cache generation threw an exception" -ForegroundColor DarkYellow
        }
    } else {
        Write-Host "      WARNING: vlc-cache-gen.exe not found, skipping" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "[4/5] Skipping cache regeneration (--SkipCacheRegen specified)" -ForegroundColor DarkGray
}

Write-Host ""

# Step 5: Test plugin loading via --list
Write-Host "[5/5] Testing VLC module list..." -ForegroundColor Yellow

# First, test with --list to see if VLC recognizes the module
Write-Host "      Checking module registration..." -ForegroundColor DarkGray

$listStdout = Join-Path $env:TEMP "vlc_list_stdout.txt"
$listStderr = Join-Path $env:TEMP "vlc_list_stderr.txt"

$listProcess = Start-Process -FilePath $vlcExe -ArgumentList "--list" -PassThru -NoNewWindow -RedirectStandardOutput $listStdout -RedirectStandardError $listStderr
$listCompleted = $listProcess.WaitForExit(10000)
if (-not $listCompleted) {
    $listProcess.Kill()
    Write-Host "      VLC --list timed out after 10s" -ForegroundColor DarkYellow
}

$listOutput = ""
if (Test-Path $listStdout) {
    $listOutput = Get-Content $listStdout -Raw -ErrorAction SilentlyContinue
}

$moduleFound = $false
$filterFound = $false

if ($listOutput -match "dotnet_plugin|dotnet_overlay") {
    $moduleFound = $true
    Write-Host "      Module 'dotnet_plugin' registered" -ForegroundColor Green
}

if ($listOutput -match "dotnet_overlay|dotnet|netoverlay") {
    $filterFound = $true
    Write-Host "      Video filter 'dotnet_overlay' available" -ForegroundColor Green
}

if (-not $moduleFound) {
    Write-Host "      WARNING: Module not found in VLC module list" -ForegroundColor DarkYellow
}

# Read and analyze logs from headless test
$stderrContent = ""
if (Test-Path $tempStderr) {
    $stderrContent = Get-Content $tempStderr -Raw -ErrorAction SilentlyContinue
}

$loadSuccess = $false
$loadError = $false
$errorMessage = ""

# Check for successful plugin loading indicators
if ($stderrContent -match "dotnet_plugin") {
    $loadSuccess = $true
}

# Check for common loading errors
if ($stderrContent -match "cannot load.*libdotnet_overlay_plugin") {
    $loadError = $true
    $errorMessage = "Plugin DLL could not be loaded"
}
if ($stderrContent -match "vlc_entry.*failed|cannot find.*vlc_entry") {
    $loadError = $true
    $errorMessage = "vlc_entry function not found or failed"
}
if ($stderrContent -match "libvlccore.*not found|missing.*libvlccore") {
    $loadError = $true
    $errorMessage = "libvlccore dependency issue"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Preliminary Check Results" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check preliminary results - fail early if plugin not found
if ($headlessTestFailed) {
    Write-Host "PLUGIN LOAD: FAILED (headless VLC test)" -ForegroundColor Red
    Write-Host "Error: $headlessTestError" -ForegroundColor Red
    exit 1
} elseif ($cacheGenFailed) {
    Write-Host "PLUGIN LOAD: FAILED (cache-gen crashed)" -ForegroundColor Red
    exit 1
} elseif ($loadError) {
    Write-Host "PLUGIN LOAD: FAILED - $errorMessage" -ForegroundColor Red
    exit 1
} elseif (-not $moduleFound -and -not $loadSuccess) {
    Write-Host "PLUGIN LOAD: FAILED - Module not found in VLC" -ForegroundColor Red
    Write-Host ""
    Write-Host "The plugin DLL was deployed but VLC does not recognize it." -ForegroundColor Yellow
    Write-Host "This can happen if:" -ForegroundColor Yellow
    Write-Host "  - vlc-cache-gen failed to register the plugin" -ForegroundColor Gray
    Write-Host "  - The plugin doesn't export vlc_entry correctly" -ForegroundColor Gray
    Write-Host "  - VLC commands are timing out in CI environment" -ForegroundColor Gray
    exit 1
} else {
    Write-Host "Plugin load check: SUCCESS" -ForegroundColor Green
}

Write-Host ""
Write-Host "Plugin Details:" -ForegroundColor White
Write-Host "  - Module: dotnet_plugin" -ForegroundColor Gray
Write-Host "  - Filter: dotnet_overlay" -ForegroundColor Gray
Write-Host "  - Size:   $($pluginSize.ToString('F1')) MB" -ForegroundColor Gray
Write-Host ""

# Step 6: Test video filter with actual video playback (integration test)
    Write-Host "[6/6] Running filter integration test..." -ForegroundColor Yellow
    Write-Host "      Video: $VideoPath" -ForegroundColor DarkGray

    $filterTestStderr = Join-Path $PSScriptRoot "vlc_filter_test_stderr.txt"
    $filterTestStdout = Join-Path $PSScriptRoot "vlc_filter_test_stdout.txt"

    # Build video path - support both URLs and local files
    if ($VideoPath -match "^https?://") {
        $videoArg = $VideoPath
    } else {
        $videoArg = "file:///$($VideoPath -replace '\\','/' -replace ' ','%20')"
    }

    $filterArgs = @(
        "--video-filter=dotnet_overlay",
        "--no-hw-dec",
        "--start-time=30",
        "--run-time=5",
        "--play-and-exit",
        "--no-audio",
        "-vvv",
        $videoArg
    )

    Write-Host "      Running: vlc.exe $($filterArgs -join ' ')" -ForegroundColor DarkGray

    $integrationTestPassed = $true
    $integrationTestError = ""

    try {
        $filterProcess = Start-Process -FilePath $vlcExe -ArgumentList $filterArgs -PassThru -NoNewWindow -RedirectStandardError $filterTestStderr -RedirectStandardOutput $filterTestStdout -ErrorAction Stop

        $filterCompleted = $filterProcess.WaitForExit($TestTimeout * 1000)
        if (-not $filterCompleted) {
            $filterProcess.Kill()
            Write-Host "      VLC playback timed out after ${TestTimeout}s (killed)" -ForegroundColor DarkGray
        }

        # Read and analyze filter test logs
        $filterLogs = ""
        if (Test-Path $filterTestStderr) {
            $filterLogs = Get-Content $filterTestStderr -Raw -ErrorAction SilentlyContinue
        }

        # Look for our plugin's log output
        $pluginLogs = $filterLogs -split "`n" | Where-Object { $_ -match "\[VideoOverlay\]" }

        Write-Host ""
        Write-Host "      Filter Plugin Logs:" -ForegroundColor Cyan
        if ($pluginLogs.Count -gt 0) {
            $pluginLogs | Select-Object -First 15 | ForEach-Object {
                Write-Host "        $_" -ForegroundColor White
            }
            if ($pluginLogs.Count -gt 15) {
                Write-Host "        ... ($($pluginLogs.Count - 15) more lines)" -ForegroundColor DarkGray
            }
        } else {
            Write-Host "        (no plugin logs captured)" -ForegroundColor DarkGray
        }

        Write-Host ""
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host " Filter Integration Test Assertions" -ForegroundColor Cyan
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host ""

        # Required log patterns - ALL must be present to prove callbacks are invoked
        $assertions = @(
            @{ Name = "FilterOpen called"; Pattern = "\[VideoOverlay\] FilterOpen:" },
            @{ Name = "FilterOpen succeeded"; Pattern = "\[VideoOverlay\] FilterOpen completed successfully" },
            @{ Name = "FilterState initialized"; Pattern = "\[VideoOverlay\] FilterState initialized:" },
            @{ Name = "FilterClose called"; Pattern = "\[VideoOverlay\] FilterClose called" },
            @{ Name = "Frames processed"; Pattern = "\[VideoOverlay\] FilterState cleanup, processed (\d+) frames" }
        )

        foreach ($assertion in $assertions) {
            if ($filterLogs -match $assertion.Pattern) {
                Write-Host "[PASS] $($assertion.Name)" -ForegroundColor Green

                # Special check for frame count
                if ($assertion.Name -eq "Frames processed") {
                    $frameCount = [int]$Matches[1]
                    if ($frameCount -eq 0) {
                        Write-Host "       Frame count is 0 - filter callbacks not processing frames" -ForegroundColor Red
                        $integrationTestPassed = $false
                        $integrationTestError = "Frame count is 0"
                    } else {
                        Write-Host "       Processed $frameCount frames" -ForegroundColor Cyan
                    }
                }
            } else {
                Write-Host "[FAIL] $($assertion.Name)" -ForegroundColor Red
                $integrationTestPassed = $false
                if (-not $integrationTestError) {
                    $integrationTestError = "Missing: $($assertion.Name)"
                }
            }
        }

        Write-Host ""

    } catch {
        Write-Host "      ERROR: Failed to run filter test: $_" -ForegroundColor Red
        $integrationTestPassed = $false
        $integrationTestError = $_.Exception.Message
    }

    if ($integrationTestPassed) {
        Write-Host "FILTER INTEGRATION TEST: PASSED" -ForegroundColor Green
        Write-Host ""
        exit 0
    } else {
        Write-Host "FILTER INTEGRATION TEST: FAILED" -ForegroundColor Red
        Write-Host "Error: $integrationTestError" -ForegroundColor Red
        Write-Host ""
        exit 1
    }
