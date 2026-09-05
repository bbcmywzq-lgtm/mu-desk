$ErrorActionPreference = 'Stop'

$localDotnet = Join-Path $PSScriptRoot 'work\dotnet-sdk\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$publishDirectory = Join-Path $PSScriptRoot 'artifacts\MouseRing-win-x64'
$workspaceRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPublishDirectory = [IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the workspace: $resolvedPublishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
& $dotnet publish (Join-Path $PSScriptRoot 'src\MouseRing.App\MouseRing.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDirectory `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
exit $LASTEXITCODE
