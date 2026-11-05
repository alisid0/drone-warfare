param(
    [string]$Output = 'C:/Users/ali10/AppData/Local/Temp/dwprev/codex/parcels',
    [string]$Dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe',
    [string]$Python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$temp = Join-Path $repo 'Temp/parcels'
$scripts = Join-Path $repo 'Assets/DroneSandbox/Scripts'
New-Item -ItemType Directory -Force $temp, $Output | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $temp 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$encoding = New-Object Text.UTF8Encoding($false)

# Writes only generated harness files; the integration snapshot is read without checking out another branch.
function Write-Generated($path, $text) {
    [IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n").Replace("`n", "`r`n"), $encoding)
}
foreach ($name in 'MapLayout', 'Farmland') {
    $source = (& git -C $repo -c "safe.directory=$repo" show "61757a3:Assets/DroneSandbox/Scripts/$name.cs") -join "`r`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not read integration snapshot' }
    if ($name -eq 'Farmland') {
        # Observe actual successful old hedge runs without adding any random draws or changing placement decisions.
        $start = $source.IndexOf('        static void Hedgerows(')
        $end = $source.IndexOf('        // Pays for the actual', $start)
        $part = $source.Substring($start, $end - $start)
        $part = $part.Replace('                for (float s = start;', "                RenderAudit.BeginOldEdge();`r`n                for (float s = start;")
        $part = $part.Replace("                }`r`n            }`r`n        }", "                }`r`n                RenderAudit.EndOldEdge();`r`n            }`r`n        }")
        $source = $source.Substring(0, $start) + $part + $source.Substring($end)
    }
    Write-Generated (Join-Path $temp "$name.before.cs") $source
}
Write-Generated (Join-Path $temp 'NuGet.Config') '<configuration><packageSources><clear /></packageSources></configuration>'

# The installed SDK supplies all framework references; no NuGet package or Unity editor process is used.
foreach ($phase in 'before', 'after') {
    $files = @('UnityMath.cs', 'RenderStubs.cs', 'PlanHarness.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $files += @('Scale.cs', 'Sector.cs', 'SectorGenerator.cs', 'PathField.cs') | ForEach-Object { Join-Path $scripts $_ }
    if ($phase -eq 'before') { $files += (Join-Path $temp 'MapLayout.before.cs'), (Join-Path $temp 'Farmland.before.cs') }
    else { $files += @('MapLayout.cs', 'Farmland.cs', 'FieldParcels.cs') | ForEach-Object { Join-Path $scripts $_ } }
    $includes = ($files | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`r`n"
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
<ItemGroup>$includes</ItemGroup>
</Project>
"@
    $projectPath = Join-Path $temp "$phase.csproj"
    Write-Generated $projectPath $project
    & $Dotnet build $projectPath -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$phase compilation failed" }
    & $Dotnet (Join-Path $temp "bin/Release/net8.0/$phase.dll") (Join-Path $temp $phase)
    if ($LASTEXITCODE -ne 0) { throw "$phase layout or renderer checks failed" }
}

# Separately type-check every changed game file against the installed Unity 6 API, not the harness substitutes.
$unityAssembly = Join-Path (Split-Path (Split-Path $Dotnet -Parent) -Parent) 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
$files = @('Scale.cs', 'Sector.cs', 'SectorGenerator.cs', 'PathField.cs', 'MapLayout.cs', 'FieldParcels.cs', 'Farmland.cs') | ForEach-Object { Join-Path $scripts $_ }
$files += Join-Path $PSScriptRoot 'ApiStubs.cs'
$includes = ($files | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`r`n"
$apiProject = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
<ItemGroup>$includes<Reference Include="UnityEngine.CoreModule"><HintPath>$unityAssembly</HintPath></Reference></ItemGroup>
</Project>
"@
Write-Generated (Join-Path $temp 'api.csproj') $apiProject
& $Dotnet build (Join-Path $temp 'api.csproj') -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Unity API compilation failed' }
& $Python -I (Join-Path $PSScriptRoot 'render.py') $temp $Output
if ($LASTEXITCODE -ne 0) { throw 'Plan rendering failed' }
Copy-Item -LiteralPath (Join-Path $temp 'before'), (Join-Path $temp 'after') -Destination $Output -Recurse -Force
