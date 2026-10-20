# Builds the harness for each instruction set into bin/<isa>, with VLC's
# yadif next to it:
#   cyadif.dll      yadif.h and yadif_x86.asm as VLC's release builds compile
#                   them: clang -O3 -funroll-loops -fomit-frame-pointer for
#                   baseline x86-64, NASM -f win64; and algo_x.c, which
#                   renders yadif's first picture
#   cyadif-isa.dll  the same C for the harness's instruction set, to see what
#                   clang makes of it with AVX2 / AVX-512
# and decodes the test frames (frames-8bit.yuv, frames-10bit.yuv) once.
param(
    [string[]] $Isa = @('x86-64-v2', 'x86-64-v3', 'x86-64-v4'),
    [string] $VlcSource = (Join-Path $PSScriptRoot '..\..\..\..\vlc'),
    [string] $Clip = 'C:\Users\Martin\Videos\vlclr-demo\yadif-source\football-1080i.mp4',
    # Start of the three consecutive frames: the camera pans at about 6 s.
    [double] $At = 6.0
)

$ErrorActionPreference = 'Stop'
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
$llvm = "$env:ProgramFiles\LLVM\bin"
# winget installs NASM per user, without putting it on PATH.
$nasm = @((Get-Command nasm -ErrorAction Ignore).Source, "$env:LOCALAPPDATA\bin\NASM\nasm.exe", "$env:ProgramFiles\NASM\nasm.exe") |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $nasm) { throw 'NASM not found: winget install NASM.NASM' }
$deinterlace = Join-Path (Resolve-Path $VlcSource) 'modules\video_filter\deinterlace'
$x86inc = Join-Path (Resolve-Path $VlcSource) 'extras\include\x86'
$vlcInclude = Join-Path (Resolve-Path $VlcSource) 'include'

$obj = Join-Path $PSScriptRoot 'obj\native'
New-Item -ItemType Directory -Force $obj | Out-Null
$asmObj = Join-Path $obj 'yadif_x86.obj'
& $nasm -f win64 -DARCH_X86_64=1 "-I$x86inc\" (Join-Path $deinterlace 'yadif_x86.asm') -o $asmObj
if ($LASTEXITCODE) { throw 'nasm failed' }

function Build-C([string] $march, [string] $dll) {
    # algo_x.c and its caller need VLC's headers, and a config.h for them.
    $xObjs = foreach ($source in @((Join-Path $PSScriptRoot 'c\x.c'), (Join-Path $deinterlace 'algo_x.c'))) {
        $o = Join-Path $obj "$([IO.Path]::GetFileNameWithoutExtension($source))-$march.o"
        & "$llvm\clang.exe" -c -O3 -funroll-loops -fomit-frame-pointer "-march=$march" -Wno-deprecated-declarations `
            -include (Join-Path $PSScriptRoot 'c\config.h') "-I$vlcInclude" "-I$deinterlace" '-DMODULE_STRING="deinterlace"' $source -o $o
        if ($LASTEXITCODE) { throw "clang failed for $source, $march" }
        $o
    }
    & "$llvm\clang.exe" -O3 -funroll-loops -fomit-frame-pointer "-march=$march" -shared -fuse-ld=lld `
        "-I$deinterlace" (Join-Path $PSScriptRoot 'c\yadif.c') $asmObj @xObjs -o $dll
    if ($LASTEXITCODE) { throw "clang failed for $march" }
}

foreach ($i in $Isa) {
    $out = Join-Path $PSScriptRoot "bin\$i"
    New-Item -ItemType Directory -Force $out | Out-Null
    dotnet publish $PSScriptRoot -c Release "-p:Isa=$i" -o $out --nologo -v q
    if ($LASTEXITCODE) { throw "dotnet publish failed for $i" }
    Build-C 'x86-64' (Join-Path $out 'cyadif.dll')
    Build-C $i (Join-Path $out 'cyadif-isa.dll')
}

$frames = Join-Path $PSScriptRoot 'bin'
foreach ($f in @(@{ Name = 'frames-8bit.yuv'; Format = 'yuv420p' }, @{ Name = 'frames-10bit.yuv'; Format = 'yuv420p10le' })) {
    $file = Join-Path $frames $f.Name
    if (-not (Test-Path $file)) {
        ffmpeg -hide_banner -loglevel error -y -ss $At -i $Clip -frames:v 3 -pix_fmt $f.Format -f rawvideo $file
        if ($LASTEXITCODE) { throw "ffmpeg failed for $($f.Name)" }
    }
}
