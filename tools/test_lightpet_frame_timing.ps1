param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LightPet.App\bin\Release\net10.0-windows\LightPet.exe'),
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\frame-timing-report.json')
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$reportDirectory = Split-Path $Report -Parent
New-Item -ItemType Directory -Force $reportDirectory | Out-Null

function Run-Trace {
    param(
        [string] $Name,
        [string] $Action,
        [bool] $Loop,
        [int] $ExitMilliseconds,
        [string] $Placement,
        [int] $Size = 260
    )
    $tracePath = Join-Path $reportDirectory "$Name-trace.json"
    Remove-Item -LiteralPath $tracePath -ErrorAction SilentlyContinue
    $env:LIGHTPET_QA_WINDOW = '1'
    $env:LIGHTPET_QA_ACTION = $Action
    $env:LIGHTPET_QA_PLACEMENT = $Placement
    $env:LIGHTPET_QA_TRACE = $tracePath
    $env:LIGHTPET_QA_EXIT_MS = $ExitMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:LIGHTPET_QA_SIZE = $Size.ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($Loop) { $env:LIGHTPET_QA_LOOP = '1' }
    try {
        $process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
    } finally {
        Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_ACTION, Env:LIGHTPET_QA_PLACEMENT, Env:LIGHTPET_QA_TRACE, Env:LIGHTPET_QA_EXIT_MS, Env:LIGHTPET_QA_SIZE, Env:LIGHTPET_QA_LOOP -ErrorAction SilentlyContinue
    }
    try {
        Wait-Process -Id $process.Id -Timeout ([math]::Ceiling($ExitMilliseconds / 1000) + 10)
        $process.Refresh()
        if ($process.ExitCode -ne 0) {
            throw "$Name timing process exited with code $($process.ExitCode)."
        }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
    if (-not (Test-Path -LiteralPath $tracePath)) {
        throw "$Name did not produce a frame trace."
    }

    $trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
    $candidate = @($trace.entries | Where-Object action -eq $Action | Group-Object operationId | Sort-Object Count -Descending)[0]
    $entries = @($candidate.Group | Sort-Object presentedAtMs)
    $errors = @()
    for ($index = 0; $index -lt $entries.Count - 1; $index++) {
        $actual = $entries[$index + 1].presentedAtMs - $entries[$index].presentedAtMs
        $errors += [math]::Round($actual - $entries[$index].expectedDurationMs, 3)
    }
    if ($errors.Count -lt 4) {
        throw "$Name produced too few same-operation intervals: $($errors.Count)."
    }
    $absolute = @($errors | ForEach-Object { [math]::Abs($_) } | Sort-Object)
    $median = if ($absolute.Count % 2) {
        $absolute[[math]::Floor($absolute.Count / 2)]
    } else {
        ($absolute[$absolute.Count / 2 - 1] + $absolute[$absolute.Count / 2]) / 2
    }
    $p95Index = [math]::Min($absolute.Count - 1, [math]::Ceiling($absolute.Count * 0.95) - 1)
    $expectedOperationDuration = ($entries | Measure-Object expectedDurationMs -Sum).Sum
    $nextOperationFrame = @($trace.entries | Where-Object {
        $_.presentedAtMs -gt $entries[-1].presentedAtMs -and $_.operationId -ne [int]$candidate.Name
    } | Sort-Object presentedAtMs | Select-Object -First 1)
    $completionError = $null
    if ($nextOperationFrame.Count -eq 1) {
        $actualOperationDuration = $nextOperationFrame[0].presentedAtMs - $entries[0].presentedAtMs
        $completionError = [math]::Round($actualOperationDuration - $expectedOperationDuration, 3)
    }
    $meanSigned = ($errors | Measure-Object -Average).Average
    $result = [pscustomobject]@{
        name = $Name
        action = $Action
        size = $Size
        operationId = [int]$candidate.Name
        frames = $entries.Count
        intervals = $errors.Count
        medianAbsoluteErrorMs = [math]::Round($median, 3)
        p95AbsoluteErrorMs = [math]::Round($absolute[$p95Index], 3)
        maximumAbsoluteErrorMs = [math]::Round($absolute[-1], 3)
        meanSignedErrorMs = [math]::Round($meanSigned, 3)
        operationCompletionErrorMs = $completionError
        passed = $median -le 15 -and $absolute[$p95Index] -le 30 -and $absolute[-1] -le 60 -and [math]::Abs($meanSigned) -le 3 -and ($null -eq $completionError -or [math]::Abs($completionError) -le 30)
        trace = $tracePath
    }
    return $result
}

$existing = @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable })
if ($existing.Count -gt 0) {
    throw 'Close the running LightPet instance before the isolated frame-timing test.'
}

$results = @(
    Run-Trace -Name 'idle' -Action 'idle' -Loop $true -ExitMilliseconds 7500 -Placement 'bottom-right'
    Run-Trace -Name 'walk-right' -Action 'walk-right' -Loop $false -ExitMilliseconds 5000 -Placement 'top-left'
    Run-Trace -Name 'walk-right-maximum' -Action 'walk-right' -Loop $false -ExitMilliseconds 5000 -Placement 'top-left' -Size 400
)
$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    executable = $Executable
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    thresholds = [pscustomobject]@{
        medianAbsoluteErrorMs = 15
        p95AbsoluteErrorMs = 30
        maximumAbsoluteErrorMs = 60
        absoluteMeanSignedErrorMs = 3
        operationCompletionErrorMs = 30
    }
    results = $results
}
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Format-Table name, action, size, frames, intervals, medianAbsoluteErrorMs, p95AbsoluteErrorMs, maximumAbsoluteErrorMs, meanSignedErrorMs, operationCompletionErrorMs, passed -AutoSize
if (-not $payload.passed) { exit 1 }
