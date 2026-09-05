$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

& $dotnet run --project (Join-Path $PSScriptRoot 'src\LightPet.App\LightPet.App.csproj') -c Release --no-build
exit $LASTEXITCODE
