param(
    [switch]$SkipRender,
    [switch]$SkipBenchmark,
    [string]$Python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe',
    [string]$Dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe',
    [string]$Blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
    & $Python -I Tools/WorldLandmarks/prepare.py
    if ($LASTEXITCODE -ne 0) { throw 'Unity API or harness compilation failed' }
    & $Python -I Tools/WorldLandmarks/hooks.py --check-api
    if ($LASTEXITCODE -ne 0) { throw 'Terrain hook API compilation failed' }
    & $Dotnet Temp/world-landmarks/bin/Release/net8.0/harness.dll 1200 20
    if ($LASTEXITCODE -ne 0) { throw 'Landmark validation failed' }
    if (!$SkipBenchmark) {
        & $Python -I Tools/WorldLandmarks/benchmark.py
        if ($LASTEXITCODE -ne 0) { throw 'Before/after benchmark failed' }
    }
    if (!$SkipRender) {
        & $Blender --background --factory-startup --python Tools/WorldLandmarks/render.py
        if ($LASTEXITCODE -ne 0) { throw 'Offline renders failed' }
    }
    & $Python -I Tools/WorldLandmarks/review.py
    if ($LASTEXITCODE -ne 0) { throw 'Review packaging failed' }
}
finally { Pop-Location }
