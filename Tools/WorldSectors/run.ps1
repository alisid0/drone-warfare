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
$enums = ''
foreach ($pair in @(@('EnemyUnit','EnemyType'), @('Drone','DroneType'))) {
    $source = [IO.File]::ReadAllText((Join-Path $scripts ($pair[0] + '.cs')))
    $enums += [regex]::Match($source, ('public enum ' + $pair[1] + '\s*\{[^}]+\}')).Value
}
$math = [IO.File]::ReadAllText((Join-Path $repo 'Tools/Tests/Parcels/UnityMath.cs'))
$math = [regex]::Replace($math, 'public enum EnemyType\s*\{[^}]+\}', $enums)
$math = $math.Replace('public static Vector3 up =>', @'
public static Vector3 zero => new Vector3();
        public static Vector3 operator /(Vector3 a, float b) => new Vector3(a.x/b,a.y/b,a.z/b);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
        public static Vector3 up =>
'@)
Write-Generated (Join-Path $temp 'UnityMath.cs') $math
$theatre = [IO.File]::ReadAllText((Join-Path $scripts 'Theatre.cs'))
$hotspot = $theatre.Substring($theatre.IndexOf('    public class Hotspot'), $theatre.IndexOf('    // The theatre of war') - $theatre.IndexOf('    public class Hotspot'))
Write-Generated (Join-Path $temp 'Hotspot.cs') ("using UnityEngine; namespace DroneSandbox {`r`n" + $hotspot + '}')
$files = @('Scale.cs','Sector.cs','SectorGenerator.cs','PathField.cs','MapLayout.cs','FieldParcels.cs','Situations.cs','Missions.cs') | ForEach-Object { Join-Path $scripts $_ }
$files += Get-ChildItem -LiteralPath $scripts -Filter 'WorldPlan*.cs' | ForEach-Object FullName
$files += Get-ChildItem -LiteralPath $scripts -Filter 'WorldSector*.cs' | ForEach-Object FullName
$files += (Join-Path $temp 'Hotspot.cs'), (Join-Path $PSScriptRoot 'GameStubs.cs')

# Compile production, the real Unity API check, and a separate review-only water-query integration probe.
foreach ($mode in 'harness','api','probe') {
    $sources = @($files)
    $extra = ''
    if ($mode -ne 'api') { $sources += (Join-Path $temp 'UnityMath.cs'), (Join-Path $PSScriptRoot 'Harness.cs'); $outputType = 'Exe' }
    else {
        $ground = $math.Substring($math.IndexOf('    public static partial class Ground'))
        Write-Generated (Join-Path $temp 'ApiTypes.cs') ("using UnityEngine; namespace DroneSandbox { $enums $ground")
        $sources += Join-Path $temp 'ApiTypes.cs'
        $assembly = Join-Path (Split-Path (Split-Path $Dotnet -Parent) -Parent) 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
        $extra = '<Reference Include="UnityEngine.CoreModule"><HintPath>' + $assembly + '</HintPath></Reference>'
        $outputType = 'Library'
    }
    $defines = ''
    if ($mode -eq 'probe') {
        # Review-only consumer proof: compile a generated copy; never edit the forbidden production MapLayout.
        $map = [IO.File]::ReadAllText((Join-Path $scripts 'MapLayout.cs'))
        $old = 'Mathf.Min(ReservoirDistance(x, z), PondDistance(x, z, out _));'
        if (-not $map.Contains($old)) { throw 'LakeDistance integration probe anchor changed' }
        $map = $map.Replace($old, 'Plan.world != null ? Plan.world.LakeDistance(new Vector2(x, z), out _) : ' + $old)
        $old = 'p.river != null ? new[] { RiverLine } : new List<Vector2>[0]'
        if (-not $map.Contains($old)) { throw 'RiverField integration probe anchor changed' }
        $map = $map.Replace($old, 'p.world != null ? p.world.RiverLines() : (' + $old + ')')
        Write-Generated (Join-Path $temp 'MapLayout.probe.cs') $map
        $sources = @($sources | Where-Object { $_ -ne (Join-Path $scripts 'MapLayout.cs') })
        $sources += Join-Path $temp 'MapLayout.probe.cs'
        $defines = '<DefineConstants>WORLD_WATER_PROBE</DefineConstants>'
    }
    $includes = ($sources | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`r`n"
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>$outputType</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>$defines</PropertyGroup>
<ItemGroup>$includes$extra</ItemGroup>
</Project>
"@
    Write-Generated (Join-Path $temp "$mode.csproj") $project
    & $Dotnet build (Join-Path $temp "$mode.csproj") -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$mode compilation failed" }
}
& $Dotnet (Join-Path $temp 'bin/Release/net8.0/harness.dll') $Output
if ($LASTEXITCODE -ne 0) { throw 'World-sector validation failed' }
& $Dotnet (Join-Path $temp 'bin/Release/net8.0/probe.dll') (Join-Path $Output 'water-hook-probe')
if ($LASTEXITCODE -ne 0) { throw 'Consumer water-hook probe failed' }
if (-not $CheckOnly) {
    & $Python -I (Join-Path $PSScriptRoot 'render.py') $Output
    if ($LASTEXITCODE -ne 0) { throw 'World-sector rendering failed' }
}
