# Memory of VLC's yadif (C + asm) against the C# filter, in the same host:
# builds host.exe and YadifNative.dll, runs each DLL in fresh processes on
# the harness's frames, and prints the host's memory at each step.
# Needs the harness built first (../Harness/build.ps1).
#
#   ./run.ps1 [-Frames 300] [-Runs 3]
param(
    [int] $Frames = 300,
    [int] $Runs = 3
)
$ErrorActionPreference = 'Stop'
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
$llvm = "$env:ProgramFiles\LLVM\bin"
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

& "$llvm\clang.exe" -O2 (Join-Path $PSScriptRoot 'host.c') -o (Join-Path $out 'host.exe') -lpsapi
if ($LASTEXITCODE) { throw 'clang failed' }
dotnet publish (Join-Path $PSScriptRoot 'YadifNative') -c Release -o (Join-Path $out 'csharp') --nologo -v q
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

$harness = Join-Path $PSScriptRoot '..\Harness\bin'
$frameFile = Join-Path $harness 'frames-8bit.yuv'
$variants = @(
    @{ Name = "VLC's C + SSSE3 asm (cyadif.dll)"; Dll = Join-Path $harness 'x86-64-v2\cyadif.dll'; Impl = 2 }
    @{ Name = 'C# Native AOT (YadifNative.dll)'; Dll = Join-Path $out 'csharp\YadifNative.dll'; Impl = 0 }
)

foreach ($v in $variants) {
    $size = (Get-Item $v.Dll).Length
    "## $($v.Name), $([math]::Round($size / 1KB)) KiB on disk"
    for ($r = 1; $r -le $Runs; $r++) {
        ''
        "Run $r"
        ''
        & (Join-Path $out 'host.exe') $v.Dll $frameFile $v.Impl $Frames | ForEach-Object {
            $cells = $_ -split "`t"
            '| ' + ($cells -join ' | ') + ' |'
            if ($cells[0] -eq 'step') { '|' + ('---|' * $cells.Count) }
        }
        if ($LASTEXITCODE) { throw "host failed for $($v.Name)" }
    }
    ''
}
