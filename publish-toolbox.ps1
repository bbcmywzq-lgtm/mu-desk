[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$project = Join-Path $PSScriptRoot 'src\Toolbox.App\Toolbox.App.csproj'
$stableOutput = Join-Path $PSScriptRoot 'artifacts\PersonalToolbox-win-x64'
$stableExecutable = Join-Path $stableOutput 'PersonalToolbox.exe'
$stableInUse = @(Get-Process PersonalToolbox -ErrorAction SilentlyContinue | Where-Object {
    try {
        [string]::Equals(
            [IO.Path]::GetFullPath($_.Path),
            [IO.Path]::GetFullPath($stableExecutable),
            [StringComparison]::OrdinalIgnoreCase)
    } catch {
        $false
    }
}).Count -gt 0
$output = if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory }
    else { Join-Path $PSScriptRoot $OutputDirectory }
} elseif (Test-Path -LiteralPath $stableOutput) {
    Join-Path $PSScriptRoot ('artifacts\PersonalToolbox-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
} else {
    $stableOutput
}
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
$resolvedOutput = [IO.Path]::GetFullPath($output)
if (-not $resolvedOutput.StartsWith(
        $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a toolbox publish path outside the artifacts directory.'
}
if (Test-Path -LiteralPath $resolvedOutput) {
    throw "Publish destination already exists; choose a fresh directory: $resolvedOutput"
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) { throw 'Toolbox publish failed.' }

$reminderNotesOutput = Join-Path $output 'tools\ReminderNotes'
& $dotnet publish (Join-Path $PSScriptRoot 'src\PersonalTools.App\PersonalTools.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $reminderNotesOutput
if ($LASTEXITCODE -ne 0) { throw 'ReminderNotes publish failed.' }

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot 'REMINDER_NOTES_README.md') `
    -Destination (Join-Path $reminderNotesOutput 'README.md')

$lightPetOutput = Join-Path $output 'tools\LightPet'
& $dotnet publish (Join-Path $PSScriptRoot 'src\LightPet.App\LightPet.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $lightPetOutput
if ($LASTEXITCODE -ne 0) { throw 'LightPet publish failed.' }

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot 'LIGHTPET_README.md') `
    -Destination (Join-Path $lightPetOutput 'README.md')

# MouseRing is referenced as a module, but its standalone launcher is not part of the toolbox package.
$standaloneArtifacts = @(
    (Join-Path $output 'MouseRing.exe'),
    (Join-Path $output 'MouseRing.runtimeconfig.json'),
    (Join-Path $output 'DesktopOrganizer.exe'),
    (Join-Path $output 'DesktopOrganizer.runtimeconfig.json')
)
foreach ($artifact in $standaloneArtifacts) {
    $parent = [System.IO.Path]::GetFullPath((Split-Path -Parent $artifact))
    if ($parent -ne [System.IO.Path]::GetFullPath($output)) {
        throw "Unexpected publish cleanup target: $artifact"
    }

    Remove-Item -LiteralPath $artifact -Force -ErrorAction SilentlyContinue
}

Write-Host "Published: $(Join-Path $output 'PersonalToolbox.exe')"
if ($stableInUse) {
    Write-Host "Running package was left untouched; use the -next directory after exiting it."
}
