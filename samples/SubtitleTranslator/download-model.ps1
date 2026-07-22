# Downloads INT8 quantized OPUS-MT en->fr from Hugging Face
# Run once before first use: pwsh samples/SubtitleTranslator/download-model.ps1

$repo = "onnx-community/opus-mt-en-fr"
$baseUrl = "https://huggingface.co/$repo/resolve/main/onnx"
$outDir = Join-Path $PSScriptRoot "models/opus-mt-en-fr"

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# ONNX model files (INT8 quantized)
$onnxFiles = @(
    "encoder_model_quantized.onnx",
    "decoder_model_merged_quantized.onnx"
)

# Tokenizer files from repo root
$rootFiles = @("tokenizer.json", "source.spm", "target.spm")

foreach ($f in $onnxFiles) {
    $outPath = Join-Path $outDir $f
    if (Test-Path $outPath) {
        Write-Host "Already exists: $f"
        continue
    }
    Write-Host "Downloading $f..."
    Invoke-WebRequest -Uri "$baseUrl/$f" -OutFile $outPath
}

foreach ($f in $rootFiles) {
    $outPath = Join-Path $outDir $f
    if (Test-Path $outPath) {
        Write-Host "Already exists: $f"
        continue
    }
    Write-Host "Downloading $f..."
    Invoke-WebRequest -Uri "https://huggingface.co/$repo/resolve/main/$f" -OutFile $outPath
}

# Verify downloads
$allFiles = $onnxFiles + $rootFiles
$missing = @()
foreach ($f in $allFiles) {
    $path = Join-Path $outDir $f
    if (-not (Test-Path $path)) {
        $missing += $f
    } else {
        $size = (Get-Item $path).Length
        Write-Host "  OK: $f ($([math]::Round($size / 1MB, 1)) MB)"
    }
}

if ($missing.Count -gt 0) {
    Write-Host "`nMISSING FILES:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "`nDone. All model files downloaded to: $outDir"
