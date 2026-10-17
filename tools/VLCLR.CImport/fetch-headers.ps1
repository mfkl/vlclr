#Requires -Version 7
<#
.SYNOPSIS
    Fetches the VLC headers pinned in vlc.cimport.json.

.DESCRIPTION
    Reads vlc.repository, vlc.commit and vlc.includeDir from the manifest,
    fetches only the include/ directory of that exact commit (shallow,
    blobless fetch plus sparse checkout, kept in a .vlc-src directory next to
    the include directory) and copies it to the include directory, so that
    <includeDir>/vlc_common.h exists.

    Re-running is cheap: nothing happens when the include directory is already
    at the pinned commit, and a different pinned commit is fetched into the
    same .vlc-src directory.

.PARAMETER Destination
    Include directory to fill instead of vlc.includeDir (relative paths are
    relative to the current directory).

.PARAMETER Manifest
    Path of the manifest. Defaults to vlc.cimport.json next to this script.

.EXAMPLE
    ./fetch-headers.ps1

.EXAMPLE
    ./fetch-headers.ps1 -Destination C:\vlc\include
#>
[CmdletBinding()]
param(
    [string]$Destination,
    [string]$Manifest = (Join-Path $PSScriptRoot 'vlc.cimport.json')
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string]$WorkDir, [string[]]$GitArgs)
    $output = & git -C $WorkDir @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE):`n$($output -join "`n")"
    }
    $output
}

try {
    if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) {
        throw "Manifest not found: $Manifest"
    }
    $manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
    $manifestDir = Split-Path -Parent $manifestPath

    # ConvertFrom-Json in PowerShell 7 accepts // and /* */ comments.
    $json = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $vlc = $json.vlc
    foreach ($key in 'repository', 'commit', 'includeDir') {
        if (-not $vlc -or [string]::IsNullOrWhiteSpace($vlc.$key)) {
            throw "$manifestPath has no vlc.$key."
        }
    }
    $repository = [string]$vlc.repository
    $commit = ([string]$vlc.commit).ToLowerInvariant()
    if ($commit -notmatch '^[0-9a-f]{8,40}$') {
        throw "vlc.commit '$commit' is not a hexadecimal commit id."
    }

    if ($Destination) {
        $includeDir = [IO.Path]::GetFullPath($Destination, (Get-Location).ProviderPath)
    } else {
        $includeDir = [IO.Path]::GetFullPath([string]$vlc.includeDir, $manifestDir)
    }
    $includeDir = $includeDir.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $stamp = Join-Path $includeDir '.vlc-commit'

    if ((Test-Path -LiteralPath $stamp) -and (Test-Path -LiteralPath (Join-Path $includeDir 'vlc_common.h'))) {
        $current = (Get-Content -Raw -LiteralPath $stamp).Trim()
        if ($current.StartsWith($commit)) {
            Write-Host "fetch-headers: $includeDir is already at $current"
            exit 0
        }
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git is not on PATH.'
    }
    if ($commit.Length -lt 40) {
        Write-Warning "vlc.commit '$commit' is abbreviated; most servers only serve a fetch by the full 40-character id."
    }

    # Never wipe a directory this script did not fill.
    if ((Test-Path -LiteralPath $includeDir) -and -not (Test-Path -LiteralPath $stamp) `
            -and (Get-ChildItem -LiteralPath $includeDir -Force | Select-Object -First 1)) {
        throw "$includeDir exists, is not empty and was not created by fetch-headers; refusing to replace it."
    }

    $srcDir = Join-Path (Split-Path -Parent $includeDir) '.vlc-src'
    New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $srcDir '.git'))) {
        Invoke-Git $srcDir @('init', '--quiet') | Out-Null
    }
    $remotes = @(Invoke-Git $srcDir @('remote'))
    if ($remotes -contains 'origin') {
        Invoke-Git $srcDir @('remote', 'set-url', 'origin', $repository) | Out-Null
    } else {
        Invoke-Git $srcDir @('remote', 'add', 'origin', $repository) | Out-Null
    }
    Invoke-Git $srcDir @('sparse-checkout', 'set', '--cone', 'include') | Out-Null

    Write-Host "fetch-headers: fetching include/ of $commit from $repository"
    Invoke-Git $srcDir @('fetch', '--quiet', '--depth', '1', '--filter=blob:none', 'origin', $commit) | Out-Null
    Invoke-Git $srcDir @('checkout', '--quiet', '--force', '--detach', 'FETCH_HEAD') | Out-Null

    $head = (Invoke-Git $srcDir @('rev-parse', 'HEAD') | Select-Object -First 1).ToString().Trim()
    if (-not $head.StartsWith($commit)) {
        throw "Checked out $head, expected $commit."
    }
    $srcInclude = Join-Path $srcDir 'include'
    if (-not (Test-Path -LiteralPath (Join-Path $srcInclude 'vlc_common.h'))) {
        throw "Commit $head has no include/vlc_common.h; is vlc.repository a VLC repository?"
    }

    if (Test-Path -LiteralPath $includeDir) {
        Remove-Item -LiteralPath $includeDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $includeDir | Out-Null
    Copy-Item -Path (Join-Path $srcInclude '*') -Destination $includeDir -Recurse -Force
    Set-Content -LiteralPath $stamp -Value $head -NoNewline

    $count = (Get-ChildItem -LiteralPath $includeDir -Recurse -File).Count
    Write-Host "fetch-headers: VLC $head, $count files in $includeDir"
    exit 0
} catch {
    [Console]::Error.WriteLine("fetch-headers: $($_.Exception.Message)")
    exit 1
}
