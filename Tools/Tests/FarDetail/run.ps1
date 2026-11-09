param(
    [string]$Output = "$PSScriptRoot/Review",
    [string]$Dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe',
    [string]$Python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$temp = Join-Path $repo 'Temp/far-detail'
$assemblies = Join-Path (Split-Path (Split-Path $Dotnet -Parent) -Parent) 'Managed/UnityEngine'
New-Item -ItemType Directory -Force $temp, $Output | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $temp 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $Python -I (Join-Path $PSScriptRoot 'prepare.py') $temp $assemblies
if ($LASTEXITCODE -ne 0) { throw 'Harness preparation failed' }
foreach ($mode in 'api','harness') {
    & $Dotnet build (Join-Path $temp "$mode.csproj") -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$mode compilation failed" }
}
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0' # compare optimized workers rather than different background JIT tiers
    & $Dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $Output
    if ($LASTEXITCODE -ne 0) { throw 'Detail checks failed' }
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
& $Python -I (Join-Path $PSScriptRoot 'render.py') $Output
if ($LASTEXITCODE -ne 0) { throw 'Texture rendering failed' }
