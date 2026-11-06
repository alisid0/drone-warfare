param(
    [string]$Python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe',
    [string]$Blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe',
    [string]$Dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe',
    [switch]$SkipRender
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
    & $Python -I Tools/ForestHlod/prepare.py
    if ($LASTEXITCODE -ne 0) { throw 'Unity assembly API check failed' }
    foreach ($phase in 'before','after') {
        & $Python -I Tools/ForestHlod/run.py $phase
        if ($LASTEXITCODE -ne 0) { throw 'Production-source harness failed' }
    }
    & $Dotnet Temp/forest-hlod/bin/Release/net8.0/harness.dll $repo Temp/forest-hlod/after --checks
    if ($LASTEXITCODE -ne 0) { throw 'Seed checks failed' }
    if (-not $SkipRender) {
        & $Python -I Tools/ForestHlod/textures.py
        if ($LASTEXITCODE -ne 0) { throw 'Texture export failed' }
        foreach ($phase in 'before','after') {
            $views = @("--$phase")
            foreach ($view in '300m','2500m','9000m','edge') { $views += "forest_${view}_${phase}" }
            if ($phase -eq 'after') { $views += @('calibration_cards','calibration_canopy','forest_near_after') }
            & $Blender --background --factory-startup --python Tools/ForestHlod/render.py -- @views
            if ($LASTEXITCODE -ne 0) { throw 'Offline renders failed' }
        }
        & $Python -I Tools/ForestHlod/luminance.py
        if ($LASTEXITCODE -ne 0) { throw 'Luminance check failed' }
        & $Python -I Tools/ForestHlod/report.py
        if ($LASTEXITCODE -ne 0) { throw 'Review packaging failed' }
    }
}
finally { Pop-Location }
