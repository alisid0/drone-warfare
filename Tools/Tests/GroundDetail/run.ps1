param([string]$Output = "$PSScriptRoot/Review", [switch]$CompileOnly, [switch]$ReferencesOnly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$temp = Join-Path $repo 'Temp/ground-detail'
$editor = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data'
$dotnet = Join-Path $editor 'DotNetSdk/dotnet.exe'
$python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe'
New-Item -ItemType Directory -Force $temp, $Output | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $temp 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $python -I (Join-Path $PSScriptRoot 'prepare.py') $temp (Join-Path $editor 'Managed/UnityEngine')
if ($LASTEXITCODE -ne 0) { throw 'Preparation failed' }
foreach ($mode in 'api','harness') {
    & $dotnet build (Join-Path $temp "$mode.csproj") -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$mode compile failed" }
}
if ($CompileOnly) { return }
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    if ($ReferencesOnly) { & $dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $Output references }
    else { & $dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $Output }
    if ($LASTEXITCODE -ne 0) { throw 'Ground harness failed' }
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
& $python -I (Join-Path $PSScriptRoot 'render.py') $Output
if ($LASTEXITCODE -ne 0) { throw 'Render harness failed' }
& $python -I (Join-Path $PSScriptRoot 'shader_check.py')
if ($LASTEXITCODE -ne 0) { throw 'HLSL sampler compile failed' }
