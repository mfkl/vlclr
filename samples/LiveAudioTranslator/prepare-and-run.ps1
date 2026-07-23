[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$VideoPath,
    [Parameter(Mandatory)]
    [string]$VlcDirectory,
    [string]$WhisperModelPath,
    [string]$TranslationModelDirectory,
    [ValidateRange(1, 8)]
    [int]$WhisperThreads = 2,
    [ValidateRange(1, 8)]
    [int]$TranslationThreads = 1,
    [ValidateRange(5, 120)]
    [int]$MinimumLeadSeconds = 15,
    [Parameter(Position = 2, ValueFromRemainingArguments = $true)]
    [string[]]$ExtraVlcArguments = @()
)

$ErrorActionPreference = "Stop"
$forwardedArgumentCount = 0
if ([int]::TryParse(
    [Environment]::GetEnvironmentVariable("VLCLR_EXTRA_VLC_ARGUMENT_COUNT"),
    [ref]$forwardedArgumentCount)) {
    for ($argumentIndex = 0; $argumentIndex -lt $forwardedArgumentCount; $argumentIndex++) {
        $ExtraVlcArguments += [Environment]::GetEnvironmentVariable(
            "VLCLR_EXTRA_VLC_ARGUMENT_$argumentIndex")
    }
}
$video = [System.IO.Path]::GetFullPath($VideoPath)
$vlcRoot = [System.IO.Path]::GetFullPath($VlcDirectory)
$vlc = Join-Path $vlcRoot "vlc.exe"
$helperRoot = Join-Path $vlcRoot "helpers/live-audio-translator"
$helper = Join-Path $helperRoot "LiveAudioTranslator.Prepare.exe"
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) {
    $helper = Join-Path $helperRoot "LiveAudioTranslator.Prepare.dll"
}
if ([string]::IsNullOrWhiteSpace($WhisperModelPath)) {
    $WhisperModelPath = Join-Path $vlcRoot "models/whisper/ggml-tiny.bin"
}
if ([string]::IsNullOrWhiteSpace($TranslationModelDirectory)) {
    $TranslationModelDirectory = Join-Path $vlcRoot "models/opus-mt-en-fr"
}

foreach ($required in @($video, $vlc, $helper, $WhisperModelPath, $TranslationModelDirectory)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required synchronized-translation input not found: $required"
    }
}

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$runDirectory = Join-Path $tempRoot ("vlclr-live-audio-" + [Guid]::NewGuid().ToString("N"))
$audioPath = Join-Path $runDirectory "audio-16k-mono.wav"
$cuePath = Join-Path $runDirectory "timeline.jsonl"
$extractionLog = Join-Path $runDirectory "audio-extraction.log"
$extraction = $null
$preparation = $null
$player = $null
$success = $false
New-Item -ItemType Directory -Path $runDirectory | Out-Null

function Start-ProcessWithArguments {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [bool]$Redirect = $false
    )
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    if ($Redirect) {
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "Could not start child process: $FilePath"
    }
    return $process
}

try {
    $mediaUri = ([Uri]$video).AbsoluteUri
    $sout = "#transcode{acodec=s16l,channels=1,samplerate=16000}:std{access=file,mux=wav,dst='$audioPath'}"
    Write-Host "Extracting 16-kHz mono audio with bundled VLC..."
    $extraction = Start-ProcessWithArguments -FilePath $vlc -Redirect $true -Arguments @(
        "-I", "dummy",
        $mediaUri,
        "--no-video",
        "--no-sout-video",
        "--sout=$sout",
        "--play-and-exit"
    )
    $extractOut = $extraction.StandardOutput.ReadToEndAsync()
    $extractError = $extraction.StandardError.ReadToEndAsync()
    $extraction.WaitForExit()
    $combinedExtractionLog = $extractOut.GetAwaiter().GetResult() + [Environment]::NewLine +
        $extractError.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($extractionLog, $combinedExtractionLog)
    $extractExitCode = $extraction.ExitCode
    $extraction.Dispose()
    $extraction = $null
    if ($extractExitCode -ne 0) {
        throw "VLC audio extraction failed with exit code $extractExitCode."
    }
    if (-not (Test-Path -LiteralPath $audioPath -PathType Leaf) -or
        (Get-Item -LiteralPath $audioPath).Length -le 44) {
        throw "VLC returned without producing a non-empty WAV file."
    }

    $runtimePath = Join-Path $helperRoot "runtimes/win-x64/whisper.dll"
    $helperArguments = @(
        "--wave", $audioPath,
        "--cue-file", $cuePath,
        "--media", $mediaUri,
        "--whisper-model", ([System.IO.Path]::GetFullPath($WhisperModelPath)),
        "--translation-model", ([System.IO.Path]::GetFullPath($TranslationModelDirectory)),
        "--whisper-threads", $WhisperThreads.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--translation-threads", $TranslationThreads.ToString([Globalization.CultureInfo]::InvariantCulture)
    )
    if (Test-Path -LiteralPath $runtimePath -PathType Leaf) {
        $helperArguments += @("--whisper-runtime", $runtimePath)
    }
    $helperExecutable = $helper
    if ([System.IO.Path]::GetExtension($helper).Equals(".dll", [StringComparison]::OrdinalIgnoreCase)) {
        $helperArguments = @($helper) + $helperArguments
        $helperExecutable = "dotnet"
    }

    Write-Host "Preparing timestamped French cues..."
    $preparation = Start-ProcessWithArguments -FilePath $helperExecutable -Arguments $helperArguments
    $progressPath = "$cuePath.progress.json"
    $launch = $false
    $lastReport = [DateTime]::MinValue
    while (-not $launch) {
        if ($preparation.HasExited -and $preparation.ExitCode -ne 0) {
            throw "Cue preparation failed with exit code $($preparation.ExitCode)."
        }

        if (Test-Path -LiteralPath $progressPath -PathType Leaf) {
            try {
                $progress = [System.IO.File]::ReadAllText($progressPath) | ConvertFrom-Json
                $processedTicks = [long]$progress.processedAudioTicks
                $durationTicks = [long]$progress.audioDurationTicks
                $cueCount = [long]$progress.cueCount
                $complete = [bool]$progress.complete
                $rtf = if ($processedTicks -gt 0) {
                    [long]$progress.processingWallMilliseconds * 1000.0 / $processedTicks
                } else {
                    [double]::PositiveInfinity
                }
                $minimumTicks = $MinimumLeadSeconds * 1000000L
                $remainingTicks = [Math]::Max(0L, $durationTicks - $processedTicks)
                if ($complete) {
                    $requiredTicks = [Math]::Min($durationTicks, $minimumTicks)
                    $launch = $cueCount -gt 0
                } elseif ($rtf -lt 1.0) {
                    $pressure = [Math]::Clamp(($rtf - 0.67) / 0.33, 0.0, 1.0)
                    $safetyTicks = [long]($remainingTicks * $pressure * 0.15)
                    $requiredTicks = [Math]::Max($minimumTicks, $minimumTicks + $safetyTicks)
                    $launch = $requiredTicks -le 120000000L -and
                        $processedTicks -ge $requiredTicks -and $cueCount -gt 0
                } else {
                    $requiredTicks = $durationTicks
                }

                if (([DateTime]::UtcNow - $lastReport).TotalSeconds -ge 2) {
                    Write-Host ("Prepared {0:F1}s/{1:F1}s, RTF {2:F3}, cues {3}, required lead {4:F1}s" -f `
                        ($processedTicks / 1000000.0), ($durationTicks / 1000000.0), $rtf, $cueCount,
                        ($requiredTicks / 1000000.0))
                    $lastReport = [DateTime]::UtcNow
                }
                if ($complete -and $cueCount -eq 0) {
                    throw "Preparation completed without a subtitle cue; the GUI was not started."
                }
            }
            catch [System.Management.Automation.RuntimeException] {
                if ($_.Exception.Message.StartsWith("Preparation completed", [StringComparison]::Ordinal)) {
                    throw
                }
                # Atomic replacement can briefly race a file open. Retry.
            }
        }

        if (-not $launch) {
            if ($preparation.HasExited -and -not (Test-Path -LiteralPath $progressPath)) {
                throw "Cue preparation exited without publishing progress."
            }
            Start-Sleep -Milliseconds 250
        }
    }

    if (-not (Test-Path -LiteralPath $cuePath -PathType Leaf) -or
        (Get-Item -LiteralPath $cuePath).Length -eq 0) {
        throw "Prepared cue timeline is missing or empty."
    }

    Write-Host "Starting synchronized VLC playback with hardware decoding available..."
    $playerArguments = @(
        "--live-translator-mode=sync",
        "--live-translator-cue-file=$cuePath",
        "--audio-filter=dotnet_audio_translator",
        "--sub-source=dotnet_live_subtitles",
        "--no-video-title-show"
    ) + $ExtraVlcArguments + @($mediaUri)
    $player = Start-ProcessWithArguments -FilePath $vlc -Arguments $playerArguments
    while (-not $player.HasExited) {
        if ($preparation.HasExited -and $preparation.ExitCode -ne 0) {
            $player.Kill($true)
            $player.WaitForExit()
            throw "Cue preparation failed during playback with exit code $($preparation.ExitCode)."
        }
        Start-Sleep -Milliseconds 250
    }
    if ($player.ExitCode -ne 0) {
        throw "VLC exited with code $($player.ExitCode)."
    }

    if (-not $preparation.HasExited) {
        $preparation.Kill($true)
        $preparation.WaitForExit()
    }
    $success = $true
}
finally {
    foreach ($child in @($player, $preparation, $extraction)) {
        if ($null -ne $child -and -not $child.HasExited) {
            try {
                $child.Kill($true)
                $child.WaitForExit()
            }
            catch {
            }
        }
        if ($null -ne $child) {
            $child.Dispose()
        }
    }

    if ($success) {
        $resolvedRunDirectory = [System.IO.Path]::GetFullPath($runDirectory)
        $expectedParent = [System.IO.Path]::TrimEndingDirectorySeparator($tempRoot)
        $actualParent = [System.IO.Path]::TrimEndingDirectorySeparator(
            [System.IO.Path]::GetDirectoryName($resolvedRunDirectory))
        if (-not $actualParent.Equals($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
            -not [System.IO.Path]::GetFileName($resolvedRunDirectory).StartsWith(
                "vlclr-live-audio-", [StringComparison]::Ordinal)) {
            throw "Refusing to clean unexpected run directory: $resolvedRunDirectory"
        }
        Remove-Item -LiteralPath $resolvedRunDirectory -Recurse -Force
    } else {
        Write-Error "Synchronized playback failed. Diagnostic files retained at: $runDirectory" `
            -ErrorAction Continue
    }
}
