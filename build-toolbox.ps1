$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'

& $dotnet restore (Join-Path $PSScriptRoot 'PersonalToolbox.slnx')
if ($LASTEXITCODE -ne 0) { throw 'Toolbox restore failed.' }
& $dotnet build (Join-Path $PSScriptRoot 'PersonalToolbox.slnx') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Toolbox build failed.' }
& $dotnet run --project (Join-Path $PSScriptRoot 'src\Toolbox.Tests\Toolbox.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Toolbox tests failed.' }
& $dotnet run --project (Join-Path $PSScriptRoot 'src\MouseRing.Tests\MouseRing.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'MouseRing tests failed.' }
& $dotnet run --project (Join-Path $PSScriptRoot 'src\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'DesktopOrganizer tests failed.' }
& $dotnet run --project (Join-Path $PSScriptRoot 'src\PersonalTools.Tests\PersonalTools.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'ReminderNotes tests failed.' }
