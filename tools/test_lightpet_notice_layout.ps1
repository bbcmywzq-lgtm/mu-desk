param(
    [string] $Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\LightPet.App\bin\Release\net10.0-windows\LightPet.exe'),
    [string] $Text = '',
    [string] $Report = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work\qa-runtime\notice-layout-report.json')
)

$ErrorActionPreference = 'Stop'
$Text = if ([string]::IsNullOrEmpty($Text)) {
    -join [char[]]@(0x5BF9, 0x8BDD, 0x672A, 0x5F00, 0x653E, 0xFF0C, 0x4E0D, 0x4F1A, 0x8054, 0x7F51, 0x3002)
} else {
    $Text
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$existing = @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Executable })
if ($existing.Count -gt 0) {
    throw 'Close the running LightPet instance before the isolated notice-layout test.'
}

if (-not ('LightPetNoticeNative' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LightPetNoticeNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
'@
}

$results = @()
$expandedCasual = $Text.Length -gt 15
$kinds = @(
    [pscustomobject]@{
        name = 'casual'
        width = if ($expandedCasual) { 218 } else { 198 }
        height = if ($expandedCasual) { 78 } else { 64 }
    },
    [pscustomobject]@{ name = 'notification'; width = 232; height = 88 }
)
foreach ($kind in $kinds) {
foreach ($size in @(130, 160, 170, 200, 260, 320, 400)) {
    $env:LIGHTPET_QA_WINDOW = '1'
    $env:LIGHTPET_QA_SIZE = $size.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:LIGHTPET_QA_PLACEMENT = 'center'
    $env:LIGHTPET_QA_NOTICE = $Text
    $env:LIGHTPET_QA_NOTICE_KIND = $kind.name
    try {
        $process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
    } finally {
        Remove-Item Env:LIGHTPET_QA_WINDOW, Env:LIGHTPET_QA_SIZE, Env:LIGHTPET_QA_PLACEMENT, Env:LIGHTPET_QA_NOTICE, Env:LIGHTPET_QA_NOTICE_KIND -ErrorAction SilentlyContinue
    }

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(8)
        do {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        } while (
            -not $process.HasExited -and
            ($process.MainWindowHandle -eq [IntPtr]::Zero -or $process.MainWindowTitle -notmatch '^LightPet QA notice=') -and
            [DateTime]::UtcNow -lt $deadline)

        if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "LightPet failed to expose a QA window at $size px."
        }

        $rect = New-Object LightPetNoticeNative+RECT
        [LightPetNoticeNative]::GetWindowRect($process.MainWindowHandle, [ref] $rect) | Out-Null
        $title = $process.MainWindowTitle
        $match = [regex]::Match($title, 'notice=Visible visible=True size=(\d+)x(\d+) placement=(above|right|left|below)$')
        $bubbleWidth = if ($match.Success) { [int]$match.Groups[1].Value } else { 0 }
        $bubbleHeight = if ($match.Success) { [int]$match.Groups[2].Value } else { 0 }
        $windowWidth = $rect.Right - $rect.Left
        $windowHeight = $rect.Bottom - $rect.Top
        $scaleX = $windowWidth / [double]$size
        $scaleY = $windowHeight / [double]$size
        $windowMatchesRequestedSize = $windowWidth -gt 0 -and
            $windowHeight -gt 0 -and
            [Math]::Abs($scaleX - $scaleY) -lt 0.02 -and
            $scaleX -ge 0.75 -and $scaleX -le 4
        $passed = $match.Success -and
            $windowMatchesRequestedSize -and
            $bubbleWidth -eq $kind.width -and
            $bubbleHeight -eq $kind.height
        $results += [pscustomobject]@{
            size = $size
            kind = $kind.name
            window = "$windowWidth`x$windowHeight"
            bubble = "$bubbleWidth`x$bubbleHeight"
            placement = if ($match.Success) { $match.Groups[3].Value } else { 'unknown' }
            title = $title
            passed = $passed
        }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        Start-Sleep -Milliseconds 250
    }
}
}

$payload = [pscustomobject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('o')
    executable = $Executable
    text = $Text
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    results = $results
}
New-Item -ItemType Directory -Force (Split-Path $Report -Parent) | Out-Null
$payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding utf8
$results | Format-Table -AutoSize
if (-not $payload.passed) { exit 1 }
