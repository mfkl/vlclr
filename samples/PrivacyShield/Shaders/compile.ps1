param(
    [string] $FxcPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($FxcPath)) {
    $windowsKitsBin = Join-Path `
        ${env:ProgramFiles(x86)} `
        'Windows Kits\10\bin'
    $FxcPath = Get-ChildItem `
            -LiteralPath $windowsKitsBin `
            -Filter fxc.exe `
            -Recurse |
        Where-Object { $_.DirectoryName.EndsWith('\x64') } |
        Sort-Object { [version] $_.Directory.Parent.Name } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if ([string]::IsNullOrWhiteSpace($FxcPath) -or
    -not (Test-Path -LiteralPath $FxcPath -PathType Leaf)) {
    throw 'Could not find the Windows SDK x64 fxc.exe. Pass -FxcPath explicitly.'
}

$shaderSource = Join-Path $PSScriptRoot 'PrivacyOverlay.hlsl'
$vertexOutput = Join-Path $PSScriptRoot 'PrivacyOverlayVS.cso'
$pixelOutput = Join-Path $PSScriptRoot 'PrivacyOverlayPS.cso'

& $FxcPath /nologo /O3 /T vs_5_0 /E VSMain /Fo $vertexOutput $shaderSource
if ($LASTEXITCODE -ne 0) {
    throw "Vertex shader compilation failed with exit code $LASTEXITCODE."
}

& $FxcPath /nologo /O3 /T ps_5_0 /E PSMain /Fo $pixelOutput $shaderSource
if ($LASTEXITCODE -ne 0) {
    throw "Pixel shader compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Compiled $vertexOutput"
Write-Host "Compiled $pixelOutput"
