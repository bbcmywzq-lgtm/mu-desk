param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LightPet.App\bin\Release\net10.0-windows\LightPet.exe'),
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\pack-restart-report.json')
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$sourceDirectory = Split-Path $Executable -Parent
$workspace = Split-Path $PSScriptRoot -Parent
$sandboxRoot = Join-Path $workspace 'work\qa-pack-restart-isolated'
$sandboxDirectory = Join-Path $sandboxRoot 'LightPet'
$sandboxExecutable = Join-Path $sandboxDirectory 'LightPet.exe'
$resolvedWork = [IO.Path]::GetFullPath((Join-Path $workspace 'work'))
$resolvedSandboxRoot = [IO.Path]::GetFullPath($sandboxRoot)
if (-not $resolvedSandboxRoot.StartsWith(
        $resolvedWork + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a restart sandbox outside the workspace work directory.'
}

if (@(Get-Process LightPet -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close every running LightPet instance before the isolated pack restart test.'
}

if (-not ('LightPetRestartNative' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class LightPetRestartNative {
  public delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint threadId, EnumCallback callback, IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr word, IntPtr parameter);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr word, IntPtr parameter);
  [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
  [DllImport("user32.dll")] public static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint item, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

  public static IntPtr FindTrayWindow(uint threadId) {
    IntPtr result = IntPtr.Zero;
    EnumThreadWindows(threadId, (hwnd, parameter) => {
      var title = new StringBuilder(256);
      GetWindowText(hwnd, title, title.Capacity);
      if (title.ToString() == "LightPet.TrayMessageWindow") { result = hwnd; return false; }
      return true;
    }, IntPtr.Zero);
    return result;
  }

  public static IntPtr[] FindMenuWindows(uint targetProcessId) {
    var result = new List<IntPtr>();
    EnumWindows((hwnd, parameter) => {
      uint processId;
      GetWindowThreadProcessId(hwnd, out processId);
      if (processId == targetProcessId && IsWindowVisible(hwnd)) {
        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() == "#32768") result.Add(hwnd);
      }
      return true;
    }, IntPtr.Zero);
    return result.ToArray();
  }

  public static IntPtr FindPetWindow(uint targetProcessId) {
    IntPtr result = IntPtr.Zero;
    EnumWindows((hwnd, parameter) => {
      uint processId;
      GetWindowThreadProcessId(hwnd, out processId);
      if (processId == targetProcessId && IsWindowVisible(hwnd)) {
        var className = new StringBuilder(64);
        GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() != "#32768") { result = hwnd; return false; }
      }
      return true;
    }, IntPtr.Zero);
    return result;
  }
}
'@
}

function Get-SandboxProcesses {
    @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $sandboxExecutable })
}

function Wait-ForSingleSandboxProcess {
    param([int] $DifferentFrom = 0)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $processes = @(Get-SandboxProcesses)
        $candidate = @($processes | Where-Object Id -ne $DifferentFrom)
        if ($processes.Count -eq 1 -and $candidate.Count -eq 1) {
            $candidate[0].Refresh()
            if (-not $candidate[0].HasExited) {
                return $candidate[0]
            }
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'A single replacement LightPet process did not become ready in time.'
}

function Wait-ForPetWindow {
    param([Diagnostics.Process] $Process)
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 80
        $window = [LightPetRestartNative]::FindPetWindow([uint32]$Process.Id)
        if ($window -ne [IntPtr]::Zero) {
            return $window
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "LightPet process $($Process.Id) did not expose a visible pet window."
}

function Get-TrayWindow {
    param([Diagnostics.Process] $Process)
    $Process.Refresh()
    foreach ($thread in $Process.Threads) {
        $window = [LightPetRestartNative]::FindTrayWindow([uint32]$thread.Id)
        if ($window -ne [IntPtr]::Zero) {
            return $window
        }
    }
    throw "LightPet process $($Process.Id) did not expose its tray callback window."
}

function Click-Point {
    param([int] $X, [int] $Y)
    [LightPetRestartNative]::SetCursorPos($X, $Y) | Out-Null
    [LightPetRestartNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [LightPetRestartNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}

function Find-MenuByItemCount {
    param([int] $ProcessId, [int] $ItemCount)
    foreach ($window in [LightPetRestartNative]::FindMenuWindows([uint32]$ProcessId)) {
        $menu = [LightPetRestartNative]::SendMessage($window, 0x01E1, [IntPtr]::Zero, [IntPtr]::Zero)
        if ([LightPetRestartNative]::GetMenuItemCount($menu) -eq $ItemCount) {
            return [pscustomobject]@{ Window = $window; Menu = $menu }
        }
    }
    return $null
}

function Invoke-PackMenuItem {
    param([Diagnostics.Process] $Process, [string] $Label, [int] $PackCount)
    $tray = Get-TrayWindow $Process
    $main = $null
    for ($attempt = 0; $attempt -lt 3 -and $null -eq $main; $attempt++) {
        Click-Point 100 500
        Start-Sleep -Milliseconds 200
        [LightPetRestartNative]::SetCursorPos(100, 100) | Out-Null
        if (-not [LightPetRestartNative]::PostMessage(
                $tray,
                0x8001,
                [IntPtr]::Zero,
                [IntPtr]0x0205)) {
            throw 'Posting the tray callback failed.'
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(2)
        do {
            Start-Sleep -Milliseconds 100
            $main = Find-MenuByItemCount $Process.Id 14
        } while ($null -eq $main -and [DateTime]::UtcNow -lt $deadline)
    }
    if ($null -eq $main) { throw 'The main tray menu did not open.' }
    $packRect = New-Object LightPetRestartNative+RECT
    [LightPetRestartNative]::GetMenuItemRect($main.Window, $main.Menu, 8, [ref]$packRect) | Out-Null
    Click-Point `
        ([int](($packRect.Left + $packRect.Right) / 2)) `
        ([int](($packRect.Top + $packRect.Bottom) / 2))
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        Start-Sleep -Milliseconds 100
        $submenu = Find-MenuByItemCount $Process.Id $PackCount
    } while ($null -eq $submenu -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $submenu) { throw 'The character-pack submenu did not open.' }
    for ($index = 0; $index -lt $PackCount; $index++) {
        $text = New-Object Text.StringBuilder 256
        [LightPetRestartNative]::GetMenuString($submenu.Menu, [uint32]$index, $text, 256, 0x400) | Out-Null
        if ($text.ToString() -eq $Label) {
            $target = New-Object LightPetRestartNative+RECT
            [LightPetRestartNative]::GetMenuItemRect($submenu.Window, $submenu.Menu, [uint32]$index, [ref]$target) | Out-Null
            Click-Point ([int](($target.Left + $target.Right) / 2)) ([int](($target.Top + $target.Bottom) / 2))
            return
        }
    }
    throw "The character-pack submenu did not contain '$Label'."
}

$results = @()
try {
    New-Item -ItemType Directory -Force $sandboxRoot | Out-Null
    if (Test-Path -LiteralPath $sandboxDirectory) {
        Remove-Item -LiteralPath $sandboxDirectory -Recurse
    }
    Copy-Item -LiteralPath $sourceDirectory -Destination $sandboxDirectory -Recurse

    $originalPack = Join-Path $sandboxDirectory 'pets\violet-alex-benchmark'
    $testPack = Join-Path $sandboxDirectory 'pets\violet-alex-restart-test'
    Copy-Item -LiteralPath $originalPack -Destination $testPack -Recurse
    $originalManifest = Get-Content (Join-Path $originalPack 'pet.json') -Raw | ConvertFrom-Json
    $testManifest = Get-Content (Join-Path $testPack 'pet.json') -Raw | ConvertFrom-Json
    $testManifest.id = 'violet-alex-restart-test'
    $testManifest.displayName = '重启测试角色'
    $testManifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $testPack 'pet.json') -Encoding utf8

    $first = Start-Process -FilePath $sandboxExecutable -WindowStyle Hidden -PassThru
    Wait-ForPetWindow $first | Out-Null
    Start-Sleep -Milliseconds 2200

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-PackMenuItem $first '重启测试角色' 2
    $second = Wait-ForSingleSandboxProcess $first.Id
    Wait-ForPetWindow $second | Out-Null
    Start-Sleep -Milliseconds 2200
    $firstSwitchMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
    $settings = Get-Content (Join-Path $sandboxDirectory 'data\settings.json') -Raw | ConvertFrom-Json
    $passed = $second.Id -ne $first.Id -and $settings.packId -eq 'violet-alex-restart-test'
    $results += [pscustomobject]@{
        case = 'switch-to-second-pack'
        oldProcessId = $first.Id
        newProcessId = $second.Id
        elapsedMilliseconds = $firstSwitchMilliseconds
        savedPackId = $settings.packId
        processCount = @(Get-SandboxProcesses).Count
        passed = $passed
    }

    $stopwatch.Restart()
    Invoke-PackMenuItem $second $originalManifest.displayName 2
    $third = Wait-ForSingleSandboxProcess $second.Id
    Wait-ForPetWindow $third | Out-Null
    $secondSwitchMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 1)
    $settings = Get-Content (Join-Path $sandboxDirectory 'data\settings.json') -Raw | ConvertFrom-Json
    $passed = $third.Id -ne $second.Id -and $settings.packId -eq $originalManifest.id
    $results += [pscustomobject]@{
        case = 'switch-back-to-original-pack'
        oldProcessId = $second.Id
        newProcessId = $third.Id
        elapsedMilliseconds = $secondSwitchMilliseconds
        savedPackId = $settings.packId
        processCount = @(Get-SandboxProcesses).Count
        passed = $passed
    }
} finally {
    Get-SandboxProcesses | Stop-Process -Force
}

$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    sourceExecutable = $Executable
    isolatedExecutable = $sandboxExecutable
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0 -and $results.Count -eq 2
    results = $results
}
New-Item -ItemType Directory -Force (Split-Path $Report -Parent) | Out-Null
$payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Format-Table -AutoSize
if (-not $payload.passed) { exit 1 }
