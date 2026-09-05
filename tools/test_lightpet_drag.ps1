param(
    [Parameter(Mandatory = $true)]
    [string] $Executable,

    [Parameter(Mandatory = $true)]
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class LightPetDragNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr window, out RECT rectangle);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
}
'@

function Get-Rectangle([IntPtr] $WindowHandle) {
    $rectangle = New-Object LightPetDragNative+RECT
    if (-not [LightPetDragNative]::GetWindowRect($WindowHandle, [ref] $rectangle)) {
        throw 'GetWindowRect failed.'
    }
    return $rectangle
}

$resolvedExecutable = [IO.Path]::GetFullPath($Executable)
if (-not [IO.File]::Exists($resolvedExecutable)) {
    throw "Executable not found: $resolvedExecutable"
}

$resolvedReport = [IO.Path]::GetFullPath($ReportPath)
$tracePath = [IO.Path]::ChangeExtension($resolvedReport, '.trace.json')
$reportDirectory = [IO.Path]::GetDirectoryName($resolvedReport)
[IO.Directory]::CreateDirectory($reportDirectory) | Out-Null

$startInfo = [Diagnostics.ProcessStartInfo]::new($resolvedExecutable)
$startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($resolvedExecutable)
$startInfo.UseShellExecute = $false
$startInfo.Environment['LIGHTPET_QA_WINDOW'] = '1'
$startInfo.Environment['LIGHTPET_QA_SIZE'] = '260'
$startInfo.Environment['LIGHTPET_QA_PLACEMENT'] = 'center'
$startInfo.Environment['LIGHTPET_QA_ACTION'] = 'idle'
$startInfo.Environment['LIGHTPET_QA_TRACE'] = $tracePath
$startInfo.Environment['LIGHTPET_QA_EXIT_MS'] = '9000'

$process = [Diagnostics.Process]::Start($startInfo)
$mouseIsDown = $false
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) {
        throw 'LightPet did not expose its QA window.'
    }

    # A WPF HWND exists before the Loaded handler finishes restoring/clamping
    # position and starting the first animation. Wait for that initialization so
    # the test does not mistake QA placement for a drag jump.
    Start-Sleep -Milliseconds 900
    $process.Refresh()
    $initial = Get-Rectangle $process.MainWindowHandle
    $windowWidth = $initial.Right - $initial.Left
    $windowHeight = $initial.Bottom - $initial.Top
    $startX = $initial.Left + [int] [Math]::Round($windowWidth * 0.5)
    # The layered transparent window only receives input on an opaque character
    # pixel. The torso is a stable hit target across idle frames.
    $startY = $initial.Top + [int] [Math]::Round($windowHeight * 0.55)

    [LightPetDragNative]::SetCursorPos($startX, $startY) | Out-Null
    Start-Sleep -Milliseconds 150
    [LightPetDragNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    $mouseIsDown = $true

    $samples = [Collections.Generic.List[object]]::new()
    for ($step = 1; $step -le 24; $step++) {
        $cursorX = $startX + $step * 10
        [LightPetDragNative]::SetCursorPos($cursorX, $startY) | Out-Null
        Start-Sleep -Milliseconds 35
        $rectangle = Get-Rectangle $process.MainWindowHandle
        $samples.Add([pscustomobject]@{
            step = $step
            cursorX = $cursorX
            windowLeft = $rectangle.Left
            windowTop = $rectangle.Top
        })
    }

    [LightPetDragNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    $mouseIsDown = $false
    Start-Sleep -Milliseconds 2600
    $final = Get-Rectangle $process.MainWindowHandle

    $activeSamples = @($samples | Select-Object -Skip 1)
    $reverseJumps = 0
    $maximumReverseJump = 0
    for ($index = 1; $index -lt $activeSamples.Count; $index++) {
        $change = $activeSamples[$index].windowLeft - $activeSamples[$index - 1].windowLeft
        if ($change -lt -2) {
            $reverseJumps++
            $maximumReverseJump = [Math]::Max($maximumReverseJump, -$change)
        }
    }

    $horizontalTravel = $final.Left - $initial.Left
    $verticalDrift = [Math]::Abs($final.Top - $initial.Top)
    $positionPassed = $reverseJumps -eq 0 -and $horizontalTravel -ge 190 -and $verticalDrift -le 4

    $process.WaitForExit(12000) | Out-Null
    $actions = @()
    if (Test-Path -LiteralPath $tracePath) {
        $trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
        $actions = @($trace.entries | Select-Object -ExpandProperty action -Unique)
    }
    $actionPassed = $actions -contains 'raise' -and $actions -contains 'fall-land'

    $report = [ordered]@{
        passed = $positionPassed -and $actionPassed
        executable = $resolvedExecutable
        initialWindow = [ordered]@{
            left = $initial.Left
            top = $initial.Top
            width = $windowWidth
            height = $windowHeight
        }
        horizontalTravel = $horizontalTravel
        verticalDrift = $verticalDrift
        reverseJumps = $reverseJumps
        maximumReverseJump = $maximumReverseJump
        observedActions = $actions
        positionPassed = $positionPassed
        actionPassed = $actionPassed
        samples = $samples
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedReport -Encoding utf8
    $report | ConvertTo-Json -Depth 4
    if (-not $report.passed) {
        exit 1
    }
}
finally {
    if ($mouseIsDown) {
        [LightPetDragNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    }
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
}
