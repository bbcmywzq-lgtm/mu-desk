param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LightPet.App\bin\Release\net10.0-windows\LightPet.exe'),
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\edge-report.json')
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$existing = @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable })
if ($existing.Count -gt 0) {
    throw 'Close the running LightPet instance before the isolated edge test.'
}

if (-not ('LightPetEdgeNative' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LightPetEdgeNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
}
'@
}

function Start-QaPet {
    param([string] $Placement, [string] $Action, [int] $WaitMilliseconds, [int] $Size = 260)
    $env:LIGHTPET_QA_WINDOW = '1'
    $env:LIGHTPET_QA_PLACEMENT = $Placement
    $env:LIGHTPET_QA_ACTION = $Action
    $env:LIGHTPET_QA_SIZE = $Size.ToString([Globalization.CultureInfo]::InvariantCulture)
    try {
        $process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
    } finally {
        Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_PLACEMENT, Env:LIGHTPET_QA_ACTION, Env:LIGHTPET_QA_SIZE -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds $WaitMilliseconds
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "LightPet failed to expose a QA window for $Placement/$Action."
    }
    return $process
}

function Read-Snapshot {
    param([Diagnostics.Process] $Process)
    $rect = New-Object LightPetEdgeNative+RECT
    [LightPetEdgeNative]::GetWindowRect($Process.MainWindowHandle, [ref] $rect) | Out-Null
    $monitor = [LightPetEdgeNative]::MonitorFromWindow($Process.MainWindowHandle, 2)
    $info = New-Object LightPetEdgeNative+MONITORINFO
    $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($info)
    [LightPetEdgeNative]::GetMonitorInfo($monitor, [ref] $info) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $visibleLeft = $rect.Left + $width * 0.25
    $visibleTop = $rect.Top + $height * 0.055
    $visibleRight = $rect.Left + $width * 0.75
    $visibleBottom = $rect.Top + $height * 0.95
    return [pscustomobject]@{
        left = $rect.Left
        top = $rect.Top
        right = $rect.Right
        bottom = $rect.Bottom
        workLeft = $info.rcWork.Left
        workTop = $info.rcWork.Top
        workRight = $info.rcWork.Right
        workBottom = $info.rcWork.Bottom
        visibleLeftGap = [Math]::Round($visibleLeft - $info.rcWork.Left, 2)
        visibleTopGap = [Math]::Round($visibleTop - $info.rcWork.Top, 2)
        visibleRightGap = [Math]::Round($info.rcWork.Right - $visibleRight, 2)
        visibleBottomGap = [Math]::Round($info.rcWork.Bottom - $visibleBottom, 2)
        inside = $visibleLeft -ge $info.rcWork.Left - 1 -and
            $visibleTop -ge $info.rcWork.Top - 1 -and
            $visibleRight -le $info.rcWork.Right + 1 -and
            $visibleBottom -le $info.rcWork.Bottom + 1
    }
}

function Test-CornerPlacement {
    param([object] $Snapshot, [string] $Placement)
    $horizontalGap = if ($Placement.EndsWith('left')) { $Snapshot.visibleLeftGap } else { $Snapshot.visibleRightGap }
    $verticalGap = if ($Placement.StartsWith('top')) { $Snapshot.visibleTopGap } else { $Snapshot.visibleBottomGap }
    return $Snapshot.inside -and [Math]::Abs($horizontalGap - 2) -le 1.1 -and [Math]::Abs($verticalGap - 2) -le 1.1
}

$results = @()
foreach ($placement in @('top-left', 'top-right', 'bottom-left', 'bottom-right')) {
    $process = Start-QaPet $placement idle 1800
    try {
        $snapshot = Read-Snapshot $process
        $results += [pscustomobject]@{ case = $placement; kind = 'corner'; snapshot = $snapshot; passed = (Test-CornerPlacement $snapshot $placement) }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        Start-Sleep -Milliseconds 300
    }
}

$process = Start-QaPet top-left idle 1800 160
try {
    $snapshot = Read-Snapshot $process
    $results += [pscustomobject]@{ case = 'top-left-minimum'; kind = 'corner'; snapshot = $snapshot; passed = (Test-CornerPlacement $snapshot 'top-left') -and ($snapshot.right - $snapshot.left) -eq 160 }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    Start-Sleep -Milliseconds 300
}

$process = Start-QaPet bottom-right idle 1800 320
try {
    $snapshot = Read-Snapshot $process
    $results += [pscustomobject]@{ case = 'bottom-right-large'; kind = 'corner'; snapshot = $snapshot; passed = (Test-CornerPlacement $snapshot 'bottom-right') -and ($snapshot.right - $snapshot.left) -eq 320 }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    Start-Sleep -Milliseconds 300
}

$process = Start-QaPet top-right idle 1800 400
try {
    $snapshot = Read-Snapshot $process
    $results += [pscustomobject]@{ case = 'top-right-maximum'; kind = 'corner'; snapshot = $snapshot; passed = (Test-CornerPlacement $snapshot 'top-right') -and ($snapshot.right - $snapshot.left) -eq 400 }
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    Start-Sleep -Milliseconds 300
}

foreach ($test in @(@('bottom-right', 'walk-right'), @('bottom-left', 'walk-left'))) {
    $process = Start-QaPet $test[0] $test[1] 4200
    try {
        $snapshot = Read-Snapshot $process
        $movedInward = if ($test[0] -eq 'bottom-right') {
            $snapshot.right -lt $snapshot.workRight
        } else {
            $snapshot.left -gt $snapshot.workLeft
        }
        $results += [pscustomobject]@{ case = "$($test[0])+$($test[1])"; kind = 'edge-turn'; snapshot = $snapshot; movedInward = $movedInward; passed = $snapshot.inside -and $movedInward }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        Start-Sleep -Milliseconds 300
    }
}

foreach ($test in @(@('near-right', 'walk-right'), @('near-left', 'walk-left'))) {
    $process = Start-QaPet $test[0] $test[1] 4200
    try {
        $snapshot = Read-Snapshot $process
        $turnedBeforeEdge = if ($test[0] -eq 'near-right') {
            $snapshot.right -lt $snapshot.workRight - 160
        } else {
            $snapshot.left -gt $snapshot.workLeft + 160
        }
        $results += [pscustomobject]@{ case = "$($test[0])+$($test[1])"; kind = 'early-turn'; snapshot = $snapshot; movedInward = $turnedBeforeEdge; passed = $snapshot.inside -and $turnedBeforeEdge }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        Start-Sleep -Milliseconds 300
    }
}

$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    executable = $Executable
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    results = $results
}
New-Item -ItemType Directory -Force (Split-Path $Report -Parent) | Out-Null
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Select-Object case, kind, passed, movedInward, @{n='visibleGaps L/T/R/B';e={"$($_.snapshot.visibleLeftGap)/$($_.snapshot.visibleTopGap)/$($_.snapshot.visibleRightGap)/$($_.snapshot.visibleBottomGap)"}}, @{n='window';e={"$($_.snapshot.left),$($_.snapshot.top),$($_.snapshot.right),$($_.snapshot.bottom)"}} | Format-Table -AutoSize
if (-not $payload.passed) { exit 1 }
