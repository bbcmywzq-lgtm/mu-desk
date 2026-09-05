$ErrorActionPreference = 'Stop'

$workspace = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $workspace 'pet-production\violet-alex-vpet-18\frames'
$packRoot = Join-Path $workspace 'pets\violet-alex-benchmark\actions'

function Copy-Phase {
    param(
        [Parameter(Mandatory)] [string] $SourceAction,
        [Parameter(Mandatory)] [string] $TargetAction,
        [Parameter(Mandatory)] [string] $Phase,
        [Parameter(Mandatory)] [int[]] $Indices,
        [Parameter(Mandatory)] [int[]] $Durations
    )

    if ($Indices.Count -ne $Durations.Count) {
        throw "Index/duration mismatch for $TargetAction/$Phase"
    }

    $sourceDirectory = Join-Path $sourceRoot $SourceAction
    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.png' | Sort-Object Name)
    $destination = Join-Path $packRoot (Join-Path $TargetAction $Phase)
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem -LiteralPath $destination -Filter '*.png' -ErrorAction SilentlyContinue | Remove-Item

    for ($sequence = 0; $sequence -lt $Indices.Count; $sequence++) {
        $sourceIndex = $Indices[$sequence]
        if ($sourceIndex -lt 0 -or $sourceIndex -ge $sourceFiles.Count) {
            throw "Source index $sourceIndex is out of range for $SourceAction"
        }

        $fileName = 'frame_{0:D3}_{1}.png' -f $sequence, $Durations[$sequence]
        Copy-Item -LiteralPath $sourceFiles[$sourceIndex].FullName -Destination (Join-Path $destination $fileName)
    }
}

Copy-Phase idle-random idle-random single @(0, 1, 2, 3, 4, 5) @(420, 180, 220, 260, 180, 440)
Copy-Phase raise raise start @(0, 1, 2) @(200, 150, 150)
Copy-Phase raise raise loop @(3, 4, 5) @(220, 220, 220)
Copy-Phase fall-land fall-land single @(0, 1, 2, 3, 4, 5) @(140, 120, 160, 180, 220, 420)
Copy-Phase touch-body touch-body single @(0, 1, 2, 3, 4, 5) @(260, 160, 180, 220, 180, 420)
Copy-Phase pinch pinch single @(0, 1, 2, 3, 4, 5) @(260, 160, 160, 220, 200, 420)
Copy-Phase startup startup single @(0, 1, 2, 3, 4, 5) @(240, 180, 200, 220, 300, 520)
Copy-Phase shutdown shutdown single @(0, 1, 2, 3, 4, 5) @(400, 220, 220, 240, 280, 520)
Copy-Phase shutdown sleep start @(0, 1, 2, 3, 4) @(300, 220, 260, 300, 380)
Copy-Phase sleep sleep loop @(4, 5, 6, 7) @(700, 350, 700, 450)
Copy-Phase shutdown sleep end @(4, 3, 2, 1, 0) @(380, 300, 260, 220, 400)
Copy-Phase think think single @(0, 1, 2, 3, 4, 5) @(300, 180, 220, 320, 260, 480)
Copy-Phase say say single @(0, 1, 2, 3, 4, 5) @(260, 180, 220, 200, 220, 360)
Copy-Phase happy happy single @(0, 1, 2, 3, 4, 5) @(260, 180, 220, 340, 220, 440)
Copy-Phase poor-ill sad single @(0, 1, 2, 3, 4, 5) @(300, 220, 320, 460, 360, 520)
Copy-Phase surprised surprised single @(0, 1, 2, 3, 4, 5) @(260, 150, 160, 220, 200, 420)
Copy-Phase work focus loop @(0, 1, 2, 3, 4, 5) @(420, 240, 260, 420, 240, 520)
Copy-Phase wave wave single @(0, 1, 2, 3, 4, 5) @(260, 160, 180, 220, 180, 420)

$importedActions = @(
    'idle-random', 'raise', 'fall-land', 'touch-body', 'pinch', 'startup',
    'shutdown', 'sleep', 'think', 'say', 'happy', 'sad', 'surprised', 'focus', 'wave'
)
foreach ($action in $importedActions) {
    & py -3.12 (Join-Path $PSScriptRoot 'normalize_lightpet_imports.py') (Join-Path $packRoot $action)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# The imported fall/land strip settles at roughly 85% of the benchmark idle
# scale. Ease the six frames back to full size so drag release does not end in
# a visibly small character and then pop on the first idle frame.
& py -3.12 (Join-Path $PSScriptRoot 'normalize_lightpet_imports.py') `
    (Join-Path $packRoot 'fall-land\single') `
    --scales '1.02,1.04,1.08,1.12,1.15,1.175'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$frameCount = (Get-ChildItem -LiteralPath $packRoot -Recurse -Filter '*.png').Count
Write-Output "Assembled LightPet action frames: $frameCount"
