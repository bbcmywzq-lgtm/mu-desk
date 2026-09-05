param(
    [string]$ToolboxExecutable = ""
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Process PersonalToolbox -ErrorAction SilentlyContinue)) {
    if ([string]::IsNullOrWhiteSpace($ToolboxExecutable) -or
        -not (Test-Path -LiteralPath $ToolboxExecutable)) {
        throw 'MU Desk is not running and no valid -ToolboxExecutable was supplied.'
    }

    Start-Process `
        -FilePath $ToolboxExecutable `
        -ArgumentList '--minimized' `
        -WorkingDirectory (Split-Path -Parent $ToolboxExecutable) | Out-Null
}

$reply = $null
for ($attempt = 0; $attempt -lt 16 -and $null -eq $reply; $attempt++) {
    $pipe = $null
    try {
        $pipe = [IO.Pipes.NamedPipeClientStream]::new(
            '.',
            'MuDesk.FileShelf.v1',
            [IO.Pipes.PipeDirection]::InOut,
            [IO.Pipes.PipeOptions]::Asynchronous)
        $pipe.Connect(500)
        $writer = [IO.StreamWriter]::new($pipe)
        $writer.AutoFlush = $true
        $reader = [IO.StreamReader]::new($pipe)
        $writer.WriteLine('{"command":"status"}')
        $reply = $reader.ReadLine()
        $reader.Dispose()
    } catch [TimeoutException] {
        Start-Sleep -Milliseconds 250
    } catch [IO.IOException] {
        Start-Sleep -Milliseconds 250
    } finally {
        if ($null -ne $pipe) {
            $pipe.Dispose()
        }
    }
}

if ([string]::IsNullOrWhiteSpace($reply)) {
    throw 'The MU Desk file shelf bridge did not respond.'
}

$response = $reply | ConvertFrom-Json
if ($response.ok -ne $true -or $response.itemCount -lt 0) {
    throw "Unexpected file shelf bridge response: $reply"
}

Write-Output "PASS LightPet <-> MU Desk file shelf bridge (items=$($response.itemCount))"
