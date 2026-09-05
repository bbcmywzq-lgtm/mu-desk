param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\LightPet-win-x64\LightPet.exe'),
    [int] $Runs = 5,
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\cold-start-report.json')
)

$ErrorActionPreference = 'Stop'
if ($Runs -lt 3) { throw 'Cold-start verification requires at least three runs.' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$sourceDirectory = Split-Path $Executable -Parent
$workspace = Split-Path $PSScriptRoot -Parent
$sandboxRoot = Join-Path $workspace 'work\qa-cold-start-isolated'
$sandboxDirectory = Join-Path $sandboxRoot 'LightPet'
$sandboxExecutable = Join-Path $sandboxDirectory 'LightPet.exe'
$resolvedWork = [IO.Path]::GetFullPath((Join-Path $workspace 'work'))
$resolvedSandboxRoot = [IO.Path]::GetFullPath($sandboxRoot)
if (-not $resolvedSandboxRoot.StartsWith(
        $resolvedWork + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a cold-start sandbox outside the workspace work directory.'
}

if (@(Get-Process LightPet -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close every running LightPet instance before the isolated cold-start test.'
}

New-Item -ItemType Directory -Force $sandboxRoot | Out-Null
if (Test-Path -LiteralPath $sandboxDirectory) {
    Remove-Item -LiteralPath $sandboxDirectory -Recurse
}
Copy-Item -LiteralPath $sourceDirectory -Destination $sandboxDirectory -Recurse
$dataDirectory = Join-Path $sandboxDirectory 'data'
if (Test-Path -LiteralPath $dataDirectory) {
    Remove-Item -LiteralPath $dataDirectory -Recurse
}

$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    $env:LIGHTPET_QA_WINDOW = '1'
    $env:LIGHTPET_QA_SIZE = '260'
    $env:LIGHTPET_QA_PLACEMENT = 'center'
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $process = Start-Process -FilePath $sandboxExecutable -WindowStyle Hidden -PassThru
    } finally {
        Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_SIZE, Env:LIGHTPET_QA_PLACEMENT -ErrorAction SilentlyContinue
    }

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(8)
        do {
            Start-Sleep -Milliseconds 10
            $process.Refresh()
        } while (-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
        if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "Cold-start run $run did not expose a window."
        }
        $windowMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
        Start-Sleep -Milliseconds 400
        $process.Refresh()
        $runThreshold = if ($run -eq 1) { 5000 } else { 2000 }
        $results += [pscustomobject]@{
            run = $run
            windowMilliseconds = $windowMilliseconds
            thresholdMilliseconds = $runThreshold
            workingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 1)
            privateMemoryMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
            responding = $process.Responding
            passed = $windowMilliseconds -le $runThreshold -and $process.Responding
        }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        Start-Sleep -Milliseconds 350
    }
}

$windowTimes = @($results | ForEach-Object windowMilliseconds | Sort-Object)
$median = if ($windowTimes.Count % 2 -eq 1) {
    $windowTimes[[int][math]::Floor($windowTimes.Count / 2)]
} else {
    ($windowTimes[$windowTimes.Count / 2 - 1] + $windowTimes[$windowTimes.Count / 2]) / 2
}
$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    sourceExecutable = $Executable
    isolatedExecutable = $sandboxExecutable
    lightPetDllSha256 = (Get-FileHash (Join-Path $sandboxDirectory 'LightPet.dll') -Algorithm SHA256).Hash
    runs = $Runs
    medianWindowMilliseconds = [math]::Round($median, 1)
    maximumWindowMilliseconds = ($windowTimes | Measure-Object -Maximum).Maximum
    freshCopyThresholdMilliseconds = 5000
    subsequentThresholdMilliseconds = 2000
    medianThresholdMilliseconds = 1500
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0 -and $median -le 1500
    results = $results
}
New-Item -ItemType Directory -Force (Split-Path $Report -Parent) | Out-Null
$payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Format-Table -AutoSize
"Median window: $($payload.medianWindowMilliseconds) ms; maximum: $($payload.maximumWindowMilliseconds) ms"
if (-not $payload.passed) { exit 1 }
