param([string]$AssetDirectory = $PSScriptRoot, [switch]$SkipPreviewCopy)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath (Join-Path $AssetDirectory 'README.md')) {
    if ((Get-Content -LiteralPath (Join-Path $AssetDirectory 'README.md') -Raw) -match 'DO NOT INSTALL') {
        throw 'This asset directory contains rejected artwork; installation is blocked.'
    }
}
$libraryPath = Join-Path $env:LOCALAPPDATA 'MU Desk\cursor-gallery\library.json'
$library = Get-Content -LiteralPath $libraryPath -Raw | ConvertFrom-Json
$packages = @($library | Where-Object { $_.id -eq 'curated-maple' })
if ($packages.Count -ne 1) { throw 'Expected exactly one Maple package.' }
$roles = @($packages[0].roles | Where-Object { $_.windowsKey -eq 'Hand' })
if ($roles.Count -ne 1) { throw 'Expected exactly one link-selection role.' }
$role = $roles[0]
$skinRoot = [IO.Path]::GetFullPath($packages[0].storagePath)
$cursorPath = [IO.Path]::GetFullPath($role.filePath)
$previewPath = [IO.Path]::GetFullPath($role.previewPath)
if ($cursorPath -ne (Join-Path $skinRoot 'Link.cur') -or !$previewPath.StartsWith($skinRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected Maple asset paths; refusing replacement.'
}
$allPaths = @($packages[0].roles | Where-Object { $_.windowsKey -ne 'Hand' -and $_.exists } | ForEach-Object { $_.filePath })
$before = @{}
foreach ($path in $allPaths) { $before[$path] = (Get-FileHash -LiteralPath $path).Hash }
$libraryHash = (Get-FileHash -LiteralPath $libraryPath).Hash
$backup = Join-Path (Split-Path $libraryPath) ('backups\maple-link-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $cursorPath -Destination (Join-Path $backup 'Link.cur')
Copy-Item -LiteralPath $previewPath -Destination (Join-Path $backup 'preview.png')
Copy-Item -LiteralPath $libraryPath -Destination (Join-Path $backup 'library.json')
if (!$SkipPreviewCopy) {
    Copy-Item -LiteralPath (Join-Path $AssetDirectory 'preview.png') -Destination $previewPath -Force
}
Copy-Item -LiteralPath (Join-Path $AssetDirectory 'Maple-Link-Minimal.cur') -Destination $cursorPath -Force
foreach ($path in $allPaths) {
    if ($before[$path] -ne (Get-FileHash -LiteralPath $path).Hash) { throw "Unrelated role changed: $path" }
}
if ($libraryHash -ne (Get-FileHash -LiteralPath $libraryPath).Hash) { throw 'Library changed concurrently.' }

# Update only the live hand if it is already assigned to this exact Maple asset.
# Do not apply a scheme, rewrite registry values, or touch the other cursor roles.
$currentHand = (Get-ItemProperty -LiteralPath 'HKCU:\Control Panel\Cursors').Hand
$liveUpdated = $false
if ([string]::Equals($currentHand, $cursorPath, [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MapleHandLive {
 [DllImport("user32.dll", EntryPoint="LoadImageW", CharSet=CharSet.Unicode, SetLastError=true)]
 public static extern IntPtr LoadImage(IntPtr instance,string path,uint type,int width,int height,uint flags);
 [DllImport("user32.dll", SetLastError=true)] public static extern bool SetSystemCursor(IntPtr cursor,uint id);
 [DllImport("user32.dll")] public static extern bool DestroyCursor(IntPtr cursor);
 [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
'@
    $cursor = [MapleHandLive]::LoadImage([IntPtr]::Zero, $cursorPath, 2,
        [MapleHandLive]::GetSystemMetrics(13), [MapleHandLive]::GetSystemMetrics(14), 0x10)
    if ($cursor -eq [IntPtr]::Zero) { throw 'Windows could not load the replacement hand.' }
    $liveUpdated = [MapleHandLive]::SetSystemCursor($cursor, 32649)
    if (!$liveUpdated) {
        [MapleHandLive]::DestroyCursor($cursor) | Out-Null
        Write-Warning 'Asset installed; the live system cursor could not refresh in this session.'
    }
}
[pscustomobject]@{ Cursor=$cursorPath; Preview=$previewPath; Backup=$backup; OtherRolesUnchanged=$true; LibraryUnchanged=$true; LiveHandUpdated=$liveUpdated } | ConvertTo-Json
