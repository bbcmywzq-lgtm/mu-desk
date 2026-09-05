param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LightPet.App\bin\Release\net10.0-windows\LightPet.exe'),
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\action-tour-report.json')
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$applicationDirectory = Split-Path $Executable -Parent
$packRoot = Join-Path $applicationDirectory 'pets\violet-alex-benchmark'
$manifest = Get-Content (Join-Path $packRoot 'pet.json') -Raw | ConvertFrom-Json
$actions = @($manifest.actions | Where-Object id -ne 'idle' | ForEach-Object id)
$reportDirectory = Split-Path $Report -Parent
New-Item -ItemType Directory -Force $reportDirectory | Out-Null
$tracePath = Join-Path $reportDirectory 'action-tour-trace.json'
Remove-Item -LiteralPath $tracePath -ErrorAction SilentlyContinue

$existing = @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable })
if ($existing.Count -gt 0) {
    throw 'Close the running LightPet instance before the isolated action tour.'
}

$env:LIGHTPET_QA_WINDOW = '1'
$env:LIGHTPET_QA_PLACEMENT = 'center'
$env:LIGHTPET_QA_PLAYLIST = $actions -join ','
$env:LIGHTPET_QA_EXIT_ON_PLAYLIST = '1'
$env:LIGHTPET_QA_TRACE = $tracePath
try {
    $process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
} finally {
    Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_PLACEMENT, Env:LIGHTPET_QA_PLAYLIST, Env:LIGHTPET_QA_EXIT_ON_PLAYLIST, Env:LIGHTPET_QA_TRACE -ErrorAction SilentlyContinue
}
try {
    Wait-Process -Id $process.Id -Timeout 90
    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        throw "Action tour exited with code $($process.ExitCode)."
    }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw 'Action tour did not produce a frame trace.'
}

function Read-Duration([string] $Path) {
    if ((Split-Path $Path -Leaf) -notmatch '_(\d+)\.png$') {
        throw "Frame has no duration suffix: $Path"
    }
    return [int]$Matches[1]
}

$trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
$results = @()
foreach ($actionId in $actions) {
    $definition = $manifest.actions | Where-Object id -eq $actionId | Select-Object -First 1
    $expectedDurations = @()
    foreach ($phase in $definition.phases) {
        $files = @(Get-ChildItem -LiteralPath (Join-Path $packRoot $phase.folder) -Filter '*.png' | Sort-Object Name)
        $repeats = if ($phase.kind -eq 'loop' -and $actionId -in @('walk-left', 'walk-right', 'sleep')) { 2 } else { 1 }
        for ($repeat = 0; $repeat -lt $repeats; $repeat++) {
            $expectedDurations += @($files | ForEach-Object { Read-Duration $_.FullName })
        }
    }

    $candidate = @($trace.entries | Where-Object action -eq $actionId | Group-Object operationId | Sort-Object Count -Descending | Select-Object -First 1)
    if ($candidate.Count -ne 1) {
        $results += [pscustomobject]@{ action = $actionId; passed = $false; message = 'action was not presented' }
        continue
    }
    $entries = @($candidate[0].Group | Sort-Object presentedAtMs)
    $intervalErrors = @()
    for ($index = 0; $index -lt $entries.Count - 1; $index++) {
        $intervalErrors += ($entries[$index + 1].presentedAtMs - $entries[$index].presentedAtMs) - $entries[$index].expectedDurationMs
    }
    $meanSigned = if ($intervalErrors.Count) { ($intervalErrors | Measure-Object -Average).Average } else { 0 }
    $nextFrame = @($trace.entries | Where-Object {
        $_.presentedAtMs -gt $entries[-1].presentedAtMs -and $_.operationId -ne [int]$candidate[0].Name
    } | Sort-Object presentedAtMs | Select-Object -First 1)
    $completionError = $null
    if ($nextFrame.Count -eq 1) {
        $actualDuration = $nextFrame[0].presentedAtMs - $entries[0].presentedAtMs
        $completionError = $actualDuration - ($expectedDurations | Measure-Object -Sum).Sum
    }
    $frameNamesMatch = $entries.Count -eq $expectedDurations.Count
    if ($frameNamesMatch) {
        for ($index = 0; $index -lt $entries.Count; $index++) {
            if ($entries[$index].expectedDurationMs -ne $expectedDurations[$index]) {
                $frameNamesMatch = $false
                break
            }
        }
    }
    $passed = $frameNamesMatch -and [math]::Abs($meanSigned) -le 5 -and $null -ne $completionError -and [math]::Abs($completionError) -le 35
    $results += [pscustomobject]@{
        action = $actionId
        expectedFrames = $expectedDurations.Count
        actualFrames = $entries.Count
        expectedDurationMs = ($expectedDurations | Measure-Object -Sum).Sum
        meanSignedFrameErrorMs = [math]::Round($meanSigned, 3)
        completionErrorMs = if ($null -eq $completionError) { $null } else { [math]::Round($completionError, 3) }
        passed = $passed
        message = if ($passed) { 'ok' } else { 'frame count, duration order, pacing, or completion mismatch' }
    }
}

$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    executable = $Executable
    actionCount = $actions.Count
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    thresholds = [pscustomobject]@{
        absoluteMeanSignedFrameErrorMs = 5
        absoluteCompletionErrorMs = 35
    }
    results = $results
}
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Format-Table action, expectedFrames, actualFrames, expectedDurationMs, meanSignedFrameErrorMs, completionErrorMs, passed -AutoSize
if (-not $payload.passed) { exit 1 }
