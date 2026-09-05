$executable = Join-Path $PSScriptRoot 'artifacts\PersonalToolbox-win-x64\PersonalToolbox.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    & (Join-Path $PSScriptRoot 'publish-toolbox.ps1')
}

Start-Process -FilePath $executable
