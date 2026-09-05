param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LightPet.App\bin\Release\net10.0-windows\LightPet.exe'),
    [int] $Minutes = 5,
    [int] $SampleSeconds = 30,
    [int] $Size = 400,
    [switch] $NormalActivity,
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\soak-report.json')
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$existing = @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable })
if ($existing.Count -gt 0) {
    throw 'Close the running LightPet instance before the isolated soak test.'
}
if ($Minutes -lt 1 -or $SampleSeconds -lt 5 -or $Size -lt 160 -or $Size -gt 400) {
    throw 'Minutes must be at least 1, SampleSeconds at least 5, and Size between 160 and 400.'
}

if (-not ('LightPetSoakNative' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LightPetSoakNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
'@
}

$env:LIGHTPET_QA_WINDOW = '1'
if (-not $NormalActivity) {
    $env:LIGHTPET_QA_STRESS = '1'
}
$env:LIGHTPET_QA_PLACEMENT = 'bottom-right'
$env:LIGHTPET_QA_SIZE = $Size.ToString([Globalization.CultureInfo]::InvariantCulture)
try {
    $process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
} finally {
    Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_STRESS, Env:LIGHTPET_QA_PLACEMENT, Env:LIGHTPET_QA_SIZE -ErrorAction SilentlyContinue
}

$samples = @()
$sampleCount = [math]::Ceiling($Minutes * 60 / $SampleSeconds)
$windowSnapshot = $null
try {
    for ($index = 0; $index -le $sampleCount; $index++) {
        if ($index -gt 0) {
            Start-Sleep -Seconds $SampleSeconds
        } else {
            Start-Sleep -Seconds 5
        }
        $process.Refresh()
        if ($process.HasExited) {
            throw "LightPet exited during soak test with code $($process.ExitCode)."
        }
        if ($null -eq $windowSnapshot -and $process.MainWindowHandle -ne [IntPtr]::Zero) {
            $rect = New-Object LightPetSoakNative+RECT
            [LightPetSoakNative]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
            $windowSnapshot = [pscustomobject]@{
                left = $rect.Left
                top = $rect.Top
                width = $rect.Right - $rect.Left
                height = $rect.Bottom - $rect.Top
            }
        }
        $sample = [pscustomobject]@{
            sample = $index
            elapsedSeconds = if ($index -eq 0) { 5 } else { 5 + $index * $SampleSeconds }
            workingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
            privateMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
            cpuSeconds = [math]::Round($process.TotalProcessorTime.TotalSeconds, 2)
            handles = $process.HandleCount
            threads = $process.Threads.Count
            responding = $process.Responding
        }
        $samples += $sample
        $sample | Format-Table -HideTableHeaders sample, elapsedSeconds, workingSetMB, privateMB, cpuSeconds, handles, threads, responding
    }
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    executable = $Executable
    mode = if ($NormalActivity) { 'normal-autonomous-activity' } else { 'accelerated-autonomous-activity' }
    requestedMinutes = $Minutes
    sampleSeconds = $SampleSeconds
    requestedSize = $Size
    window = $windowSnapshot
    passed = @($samples | Where-Object { -not $_.responding }).Count -eq 0 -and
        $null -ne $windowSnapshot -and $windowSnapshot.width -eq $Size -and $windowSnapshot.height -eq $Size
    summary = [pscustomobject]@{
        startWorkingSetMB = $samples[0].workingSetMB
        peakWorkingSetMB = ($samples | Measure-Object workingSetMB -Maximum).Maximum
        endWorkingSetMB = $samples[-1].workingSetMB
        endPrivateMB = $samples[-1].privateMB
        cpuSeconds = $samples[-1].cpuSeconds
    }
    samples = $samples
}
New-Item -ItemType Directory -Force (Split-Path $Report -Parent) | Out-Null
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Report -Encoding utf8
$payload.summary | Format-List
if (-not $payload.passed) { exit 1 }
