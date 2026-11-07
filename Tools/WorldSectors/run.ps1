param(
    [string]$Output = "$PSScriptRoot/../../Temp/world-sectors/results",
    [string]$Dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe',
    [string]$Python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temp = Join-Path $repo 'Temp/world-sectors'
$scripts = Join-Path $repo 'Assets/DroneSandbox/Scripts'
New-Item -ItemType Directory -Force $temp, $Output | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $temp 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$encoding = New-Object Text.UTF8Encoding($false)

# Normalize generated files without changing any production source or importing Unity.
function Write-Generated($path, $text) {
    [IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n").Replace("`n", "`r`n"), $encoding)
}
Write-Generated (Join-Path $temp 'NuGet.Config') '<configuration><packageSources><clear /></packageSources></configuration>'
& $Python -I (Join-Path $PSScriptRoot 'prepare_consumers.py') $temp
if ($LASTEXITCODE -ne 0) { throw 'Consumer harness preparation failed' }
$theatre = [IO.File]::ReadAllText((Join-Path $scripts 'Theatre.cs'))
$hotspot = $theatre.Substring($theatre.IndexOf('    public class Hotspot'), $theatre.IndexOf('    // The theatre of war') - $theatre.IndexOf('    public class Hotspot'))
Write-Generated (Join-Path $temp 'Hotspot.cs') ("using UnityEngine; namespace DroneSandbox {`r`n" + $hotspot + '}')
$files = @('Scale.cs','Sector.cs','SectorGenerator.cs','SectorIdentity.cs','PathField.cs','MapLayout.cs','FieldParcels.cs','Situations.cs','Missions.cs','Ground.cs','Water.cs','World.cs') | ForEach-Object { Join-Path $scripts $_ }
$files += Get-ChildItem -LiteralPath $scripts -Filter 'WorldPlan*.cs' | ForEach-Object FullName
$files += Get-ChildItem -LiteralPath $scripts -Filter 'WorldSector*.cs' | ForEach-Object FullName
$files += (Join-Path $temp 'Hotspot.cs'), (Join-Path $PSScriptRoot 'ConsumerServices.cs'), (Join-Path $PSScriptRoot 'HdrpStubs.cs'), (Join-Path $temp 'Enums.cs'), (Join-Path $temp 'SandboxFlowProbe.cs')

# Execute complete consumer sources with managed storage, and separately check real Unity engine APIs.
foreach ($mode in 'api','harness') {
    $sources = @($files)
    $extra = ''
    if ($mode -eq 'harness') {
        $sources += (Join-Path $temp 'ConsumerMath.cs'), (Join-Path $PSScriptRoot 'EngineStubs.cs'),
            (Join-Path $PSScriptRoot 'Harness.cs'), (Join-Path $PSScriptRoot 'ConsumerChecks.cs')
        $outputType = 'Exe'
    }
    else {
        $assemblies = Join-Path (Split-Path (Split-Path $Dotnet -Parent) -Parent) 'Managed/UnityEngine'
        foreach ($name in 'CoreModule','TerrainModule','TerrainPhysicsModule','PhysicsModule','AnimationModule') {
            $assembly = Join-Path $assemblies ("UnityEngine.$name.dll")
            $extra += '<Reference Include="UnityEngine.' + $name + '"><HintPath>' + $assembly + '</HintPath></Reference>'
        }
        $outputType = 'Library'
    }
    $includes = ($sources | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`r`n"
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>$outputType</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup>
<ItemGroup>$includes$extra</ItemGroup>
</Project>
"@
    Write-Generated (Join-Path $temp "$mode.csproj") $project
    & $Dotnet build (Join-Path $temp "$mode.csproj") -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$mode compilation failed" }
}
& $Dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $Output
if ($LASTEXITCODE -ne 0) { throw 'World-sector validation failed' }
& $Dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $Output --legacy
if ($LASTEXITCODE -ne 0) { throw 'Legacy consumer validation failed' }

# Compare the unchanged operation/Varrow path against the requested starting commit, without another checkout.
$baseline = Join-Path $temp 'baseline'
New-Item -ItemType Directory -Force $baseline | Out-Null
$project = [IO.File]::ReadAllText((Join-Path $temp 'harness.csproj'))
foreach ($name in 'Ground.cs','Water.cs','World.cs','MapLayout.cs','FieldParcels.cs') {
    $source = (& git -c "safe.directory=$($repo.Replace('\','/'))" show "1016ddc:Assets/DroneSandbox/Scripts/$name") -join "`r`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline $name" }
    $target = Join-Path $baseline $name
    Write-Generated $target $source
    $project = $project.Replace((Join-Path $scripts $name), $target)
}
Write-Generated (Join-Path $temp 'baseline.csproj') $project
& $Dotnet build (Join-Path $temp 'baseline.csproj') -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Baseline compilation failed' }
& $Dotnet (Join-Path $temp 'bin/Release/net8.0/baseline.dll') $baseline --legacy
if ($LASTEXITCODE -ne 0) { throw 'Baseline consumers failed' }
$before = Get-Content (Join-Path $baseline 'legacy.json') -Raw | ConvertFrom-Json
$after = Get-Content (Join-Path $Output 'legacy.json') -Raw | ConvertFrom-Json
for ($i = 0; $i -lt $before.Count; $i++) {
    if ($before[$i].hash -ne $after[$i].hash) { throw "Legacy output changed for $($before[$i].kind)" }
}
Write-Host 'PASS: legacy terrain, water, woods and parcels match 1016ddc exactly for all four recipes.'
if (-not $CheckOnly) {
    & $Python -I (Join-Path $PSScriptRoot 'render_consumers.py') $Output
    if ($LASTEXITCODE -ne 0) { throw 'World-sector rendering failed' }
}
