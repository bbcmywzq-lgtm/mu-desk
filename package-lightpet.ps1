$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

& (Join-Path $PSScriptRoot 'build-lightpet.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& py -3.12 (Join-Path $PSScriptRoot 'tools\lightpet_animation_qa.py') `
    (Join-Path $PSScriptRoot 'pets\violet-alex-benchmark') `
    --output (Join-Path $PSScriptRoot 'work\qa-package')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$artifactsRoot = Join-Path $PSScriptRoot 'artifacts'
$stablePublishDirectory = Join-Path $artifactsRoot 'LightPet-win-x64'
$stableExecutable = Join-Path $stablePublishDirectory 'LightPet.exe'
$stableInUse = @(Get-Process LightPet -ErrorAction SilentlyContinue | Where-Object {
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
    Join-Path $artifactsRoot 'LightPet-win-x64-next'
} else {
    $stablePublishDirectory
}
$archive = Join-Path $artifactsRoot 'LightPet-win-x64.zip'
New-Item -ItemType Directory -Force $artifactsRoot | Out-Null

$resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot)
$resolvedPublish = [IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublish.StartsWith($resolvedArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a publish path outside the artifacts directory.'
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse
}
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive
}

& $dotnet publish (Join-Path $PSScriptRoot 'src\LightPet.App\LightPet.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$personalToolsDirectory = Join-Path $publishDirectory 'tools\ReminderNotes'
& $dotnet publish (Join-Path $PSScriptRoot 'src\PersonalTools.App\PersonalTools.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $personalToolsDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishedPack = Join-Path $publishDirectory 'pets\violet-alex-benchmark'
& py -3.12 (Join-Path $PSScriptRoot 'tools\optimize_lightpet_runtime_pack.py') `
    $publishedPack `
    --canvas-size 600
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& py -3.12 (Join-Path $PSScriptRoot 'tools\optimize_lightpet_palette_pack.py') `
    $publishedPack `
    --colors 256 `
    --report (Join-Path $PSScriptRoot 'work\qa-package-palette.json')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& py -3.12 (Join-Path $PSScriptRoot 'tools\lightpet_animation_qa.py') `
    $publishedPack `
    --output (Join-Path $PSScriptRoot 'work\qa-package-runtime')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot 'LIGHTPET_README.md') `
    -Destination (Join-Path $publishDirectory 'README.md')
Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot 'REMINDER_NOTES_README.md') `
    -Destination (Join-Path $personalToolsDirectory 'README.md')

Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archive -CompressionLevel Optimal
$size = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 1)
Write-Output "LightPet package: $archive ($size MB)"
if ($stableInUse) {
    Write-Output "Running package was left untouched; unpacked build: $publishDirectory"
}
