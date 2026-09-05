$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

& (Join-Path $PSScriptRoot 'build-remindernotes.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
$stablePublishDirectory = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'ReminderNotes-win-x64'))
$stableExecutable = Join-Path $stablePublishDirectory 'ReminderNotes.exe'
$stableInUse = @(Get-Process ReminderNotes -ErrorAction SilentlyContinue | Where-Object {
    try {
        [string]::Equals(
            [IO.Path]::GetFullPath($_.Path),
            [IO.Path]::GetFullPath($stableExecutable),
            [StringComparison]::OrdinalIgnoreCase)
    } catch {
        $false
    }
}).Count -gt 0
$publishDirectory = if ($stableInUse) {
    [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'ReminderNotes-win-x64-next'))
} else {
    $stablePublishDirectory
}
$archive = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'ReminderNotes-win-x64.zip'))
if (-not $publishDirectory.StartsWith(
        $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a publish path outside the artifacts directory.'
}

New-Item -ItemType Directory -Force $artifactsRoot | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse
}
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive
}

& $dotnet publish (Join-Path $PSScriptRoot 'src\PersonalTools.App\PersonalTools.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot 'REMINDER_NOTES_README.md') `
    -Destination (Join-Path $publishDirectory 'README.md')
Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archive -CompressionLevel Optimal
$size = [math]::Round((Get-Item -LiteralPath $archive).Length / 1KB, 1)
Write-Output "ReminderNotes package: $archive ($size KB)"
if ($stableInUse) {
    Write-Output "Running package was left untouched; unpacked build: $publishDirectory"
}
