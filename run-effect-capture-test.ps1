$ErrorActionPreference = "Stop"

$dotnet = Join-Path $PSScriptRoot "work\dotnet-sdk\dotnet.exe"
$project = Join-Path $PSScriptRoot "src\EffectCapture.TestApp\EffectCapture.TestApp.csproj"
& $dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw "动态拾取测试器构建失败。"
}

$app = Join-Path $PSScriptRoot "src\EffectCapture.TestApp\bin\Release\net10.0-windows10.0.19041.0\DynamicCaptureTest.exe"
Start-Process -FilePath $app
