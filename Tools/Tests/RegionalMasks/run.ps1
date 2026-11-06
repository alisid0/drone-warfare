param([string]$Phase = 'after', [switch]$CompileOnly, [string]$Mode = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$temp = Join-Path $repo "Temp/regional-masks/$Phase"
$output = Join-Path $PSScriptRoot "Review/$Phase"
$editor = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data'
$dotnet = Join-Path $editor 'DotNetSdk/dotnet.exe'
$python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe'
New-Item -ItemType Directory -Force $temp, $output | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $temp 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $python -I (Join-Path $PSScriptRoot 'prepare.py') $temp (Join-Path $editor 'Managed/UnityEngine') $Phase
if ($LASTEXITCODE -ne 0) { throw 'Preparation failed' }
foreach ($buildMode in @('api','harness')) {
    & $dotnet build (Join-Path $temp "$buildMode.csproj") -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$buildMode compile failed" }
}
if ($CompileOnly) { return }
$env:DOTNET_TieredCompilation = '0'
& $dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $output $Mode
if ($LASTEXITCODE -ne 0) { throw 'Regional harness failed' }
