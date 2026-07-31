[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "models/open-model-zoo")
)

$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $PSScriptRoot "models/open-model-zoo/model-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Model manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.formatVersion -ne 1) {
    throw "Unsupported model manifest format: $($manifest.formatVersion)"
}

$repository = $manifest.source.repository.TrimEnd('/')
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

foreach ($model in $manifest.models) {
    foreach ($file in $model.files) {
        $outputPath = Join-Path $resolvedOutput $file.fileName
        $temporaryPath = "$outputPath.download"
        $isValid = $false

        if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
            $existing = Get-Item -LiteralPath $outputPath
            if ($existing.Length -eq [long]$file.size) {
                $existingHash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash
                $isValid = $existingHash.Equals($file.sha256, [StringComparison]::OrdinalIgnoreCase)
            }
        }

        if ($isValid) {
            Write-Host "Verified existing file: $($file.fileName)"
            continue
        }

        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }

        $downloadUrl = "$repository/$($file.sourcePath)"
        Write-Host "Downloading $($file.fileName)..."
        try {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $temporaryPath
            $download = Get-Item -LiteralPath $temporaryPath
            if ($download.Length -ne [long]$file.size) {
                throw "Size mismatch for $($file.fileName): got $($download.Length), expected $($file.size)."
            }

            $downloadHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
            if (-not $downloadHash.Equals($file.sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "SHA-256 mismatch for $($file.fileName): got $downloadHash, expected $($file.sha256)."
            }

            Move-Item -LiteralPath $temporaryPath -Destination $outputPath -Force
            Write-Host "Verified: $($file.fileName) ($($download.Length) bytes)"
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }
}

Write-Host "Sensitive-object model bundle ready: $resolvedOutput"
