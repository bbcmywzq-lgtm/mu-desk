$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$artifactsDirectory = Join-Path $PSScriptRoot 'artifacts'
$publishDirectory = Join-Path $artifactsDirectory 'DesktopOrganizer-win-x64'
$archivePath = Join-Path $artifactsDirectory 'DesktopOrganizer-win-x64.zip'
$checksumPath = "$archivePath.sha256"
$workspaceRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPublishDirectory = [IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the workspace: $resolvedPublishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& $dotnet publish (Join-Path $PSScriptRoot 'src\DesktopOrganizer.App\DesktopOrganizer.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $publishDirectory
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  DesktopOrganizer-win-x64.zip" -Encoding ascii

Write-Host "Published: $archivePath"
Write-Host "SHA256:    $hash"
