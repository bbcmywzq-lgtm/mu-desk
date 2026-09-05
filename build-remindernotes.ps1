$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$solution = Join-Path $PSScriptRoot 'ReminderNotes.slnx'
& $dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet run --project (Join-Path $PSScriptRoot 'src\PersonalTools.Tests\PersonalTools.Tests.csproj') -c Release --no-build
exit $LASTEXITCODE
