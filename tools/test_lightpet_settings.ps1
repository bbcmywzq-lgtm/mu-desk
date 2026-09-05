param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\LightPet-win-x64\LightPet.exe'),
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\settings-report.json')
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$sourceDirectory = Split-Path $Executable -Parent
$workspace = Split-Path $PSScriptRoot -Parent
$sandboxRoot = Join-Path $workspace 'work\qa-settings-isolated'
$sandboxDirectory = Join-Path $sandboxRoot 'LightPet-win-x64'
$resolvedWork = [IO.Path]::GetFullPath((Join-Path $workspace 'work'))
$resolvedSandboxRoot = [IO.Path]::GetFullPath($sandboxRoot)
if (-not $resolvedSandboxRoot.StartsWith(
        $resolvedWork + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a settings sandbox outside the workspace work directory.'
}

$running = @(Get-Process LightPet -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw 'Close every running LightPet instance before the isolated settings test.'
}

if (-not ('LightPetSettingsNative' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LightPetSettingsNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
}
'@
}

function Write-TestSettings {
    param([string] $Text)
    $dataDirectory = Join-Path $sandboxDirectory 'data'
    New-Item -ItemType Directory -Force $dataDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $dataDirectory 'settings.json') -Value $Text -Encoding utf8
}

function Remove-TestSettings {
    $dataDirectory = Join-Path $sandboxDirectory 'data'
    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-Item -LiteralPath $dataDirectory -Recurse
    }
}

function Start-TestPet {
    param([int] $ExitMilliseconds = 1800)
    $env:LIGHTPET_QA_WINDOW = '1'
    $env:LIGHTPET_QA_EXIT_MS = $ExitMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    try {
        $process = Start-Process -FilePath (Join-Path $sandboxDirectory 'LightPet.exe') -WindowStyle Hidden -PassThru
    } finally {
        Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_EXIT_MS -ErrorAction SilentlyContinue
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 80
        $process.Refresh()
    } while (-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'LightPet did not expose its QA window during the settings test.'
    }
    return $process
}

function Read-WindowSnapshot {
    param([Diagnostics.Process] $Process)
    $rect = New-Object LightPetSettingsNative+RECT
    [LightPetSettingsNative]::GetWindowRect($Process.MainWindowHandle, [ref] $rect) | Out-Null
    $monitor = [LightPetSettingsNative]::MonitorFromWindow($Process.MainWindowHandle, 2)
    $info = New-Object LightPetSettingsNative+MONITORINFO
    $info.cbSize = [Runtime.InteropServices.Marshal]::SizeOf($info)
    [LightPetSettingsNative]::GetMonitorInfo($monitor, [ref] $info) | Out-Null
    [pscustomobject]@{
        left = $rect.Left
        top = $rect.Top
        right = $rect.Right
        bottom = $rect.Bottom
        width = $rect.Right - $rect.Left
        height = $rect.Bottom - $rect.Top
        inside = $rect.Left -ge $info.rcWork.Left -and
            $rect.Top -ge $info.rcWork.Top -and
            $rect.Right -le $info.rcWork.Right -and
            $rect.Bottom -le $info.rcWork.Bottom
    }
}

function Stop-And-ReadSettings {
    param([Diagnostics.Process] $Process)
    try {
        Wait-Process -Id $Process.Id -Timeout 8
        $Process.Refresh()
        if ($Process.ExitCode -ne 0) {
            throw "LightPet exited with code $($Process.ExitCode)."
        }
    } finally {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force
        }
    }
    return Get-Content (Join-Path $sandboxDirectory 'data\settings.json') -Raw | ConvertFrom-Json
}

$results = @()
try {
    New-Item -ItemType Directory -Force $sandboxRoot | Out-Null
    if (Test-Path -LiteralPath $sandboxDirectory) {
        Remove-Item -LiteralPath $sandboxDirectory -Recurse
    }
    Copy-Item -LiteralPath $sourceDirectory -Destination $sandboxDirectory -Recurse

    Remove-TestSettings
    $process = Start-TestPet
    $snapshot = Read-WindowSnapshot $process
    $settings = Stop-And-ReadSettings $process
    $passed = $snapshot.width -eq 260 -and $snapshot.height -eq 260 -and $snapshot.inside -and $settings.size -eq 260
    $results += [pscustomobject]@{ case = 'clean-defaults'; window = $snapshot; settings = $settings; passed = $passed }

    Write-TestSettings '{"packId":"violet-alex-benchmark","left":100,"top":100,"size":160,"clickThrough":false,"autonomousActivity":false}'
    $process = Start-TestPet
    $snapshot = Read-WindowSnapshot $process
    $settings = Stop-And-ReadSettings $process
    $passed = $snapshot.width -eq 160 -and $snapshot.height -eq 160 -and $snapshot.inside -and
        $settings.size -eq 160 -and $settings.autonomousActivity -eq $false
    $results += [pscustomobject]@{ case = 'persisted-size-and-toggle'; window = $snapshot; settings = $settings; passed = $passed }

    Write-TestSettings '{"packId":"violet-alex-benchmark","left":-999999,"top":999999,"size":999,"clickThrough":false,"autonomousActivity":true}'
    $process = Start-TestPet
    $snapshot = Read-WindowSnapshot $process
    $settings = Stop-And-ReadSettings $process
    $process = Start-TestPet
    $restartSnapshot = Read-WindowSnapshot $process
    $restartSettings = Stop-And-ReadSettings $process
    $passed = $snapshot.width -eq 400 -and $snapshot.height -eq 400 -and $snapshot.inside -and
        $restartSnapshot.width -eq 400 -and $restartSnapshot.height -eq 400 -and $restartSnapshot.inside -and
        $settings.size -eq 400 -and [double]::IsFinite($settings.left) -and [double]::IsFinite($settings.top) -and
        [math]::Abs($restartSnapshot.left - $snapshot.left) -le 1 -and
        [math]::Abs($restartSnapshot.top - $snapshot.top) -le 1 -and
        $restartSettings.size -eq 400
    $results += [pscustomobject]@{
        case = 'normalize-size-and-recover-position'
        window = $snapshot
        restartWindow = $restartSnapshot
        settings = $restartSettings
        passed = $passed
    }

    Write-TestSettings '{broken json'
    $process = Start-TestPet
    $snapshot = Read-WindowSnapshot $process
    $settings = Stop-And-ReadSettings $process
    $passed = $snapshot.width -eq 260 -and $snapshot.height -eq 260 -and $snapshot.inside -and $settings.size -eq 260
    $results += [pscustomobject]@{ case = 'recover-corrupt-settings'; window = $snapshot; settings = $settings; passed = $passed }

    Remove-TestSettings
    $primary = Start-TestPet 5000
    try {
        $secondary = Start-Process -FilePath (Join-Path $sandboxDirectory 'LightPet.exe') -WindowStyle Hidden -PassThru
        try {
            $secondaryExited = $secondary.WaitForExit(3000)
            $primary.Refresh()
            $passed = $secondaryExited -and $secondary.ExitCode -eq 0 -and -not $primary.HasExited
            $results += [pscustomobject]@{
                case = 'single-instance'
                primaryStayedRunning = -not $primary.HasExited
                secondaryExited = $secondaryExited
                secondaryExitCode = if ($secondaryExited) { $secondary.ExitCode } else { $null }
                passed = $passed
            }
        } finally {
            if (-not $secondary.HasExited) { Stop-Process -Id $secondary.Id -Force }
        }
    } finally {
        if (-not $primary.HasExited) { Stop-Process -Id $primary.Id -Force }
    }
} finally {
    Get-Process LightPet -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -like "$sandboxDirectory*" } |
        Stop-Process -Force
}

$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    sourceExecutable = $Executable
    isolatedExecutable = Join-Path $sandboxDirectory 'LightPet.exe'
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    results = $results
}
New-Item -ItemType Directory -Force (Split-Path $Report -Parent) | Out-Null
$payload | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Select-Object case, passed, primaryStayedRunning, secondaryExited, secondaryExitCode,
    @{n='window';e={ if ($_.window) { "$($_.window.left),$($_.window.top),$($_.window.right),$($_.window.bottom)" } }} |
    Format-Table -AutoSize
if (-not $payload.passed) { exit 1 }
