$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$project = Join-Path $PSScriptRoot 'src\CispTrainer.App\CispTrainer.App.csproj'
$output = Join-Path $PSScriptRoot 'artifacts\CISP-Trainer-win-x64'
$package = Join-Path $PSScriptRoot 'artifacts\CISP-Trainer-win-x64.zip'

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) { throw 'CISP trainer publish failed.' }

$resolvedOutput = [System.IO.Path]::GetFullPath($output)
$resolvedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
if (-not $resolvedOutput.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unexpected CISP trainer output: $resolvedOutput"
}

Remove-Item -LiteralPath $package -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $package -CompressionLevel Optimal
Write-Host "Published: $(Join-Path $output 'CISP-Trainer.exe')"
Write-Host "Package: $package"
