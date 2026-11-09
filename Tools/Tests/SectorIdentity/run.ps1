param(
    [string]$Only = 'all',
    [switch]$SkipRender,
    [string]$Output = '',
    [string]$Dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe',
    [string]$Python = 'C:/Users/ali10/AppData/Local/Programs/Python/Python313/python.exe'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')).Replace('\', '/')
$temp = Join-Path $repo 'Temp/sector-identity'
if (!$Output) { $Output = Join-Path $temp 'review' }
New-Item -ItemType Directory -Force $temp, $Output | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $temp 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$encoding = New-Object Text.UTF8Encoding($false)

# Writes generated projects beneath this worktree, without starting an editor or restoring packages.
function Write-Generated($path, $text) {
    [IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n").Replace("`n", "`r`n"), $encoding)
}
Write-Generated (Join-Path $temp 'NuGet.Config') '<configuration><packageSources><clear /></packageSources></configuration>'
$names = @('Scale', 'Sector', 'SectorGenerator', 'PathField', 'MapLayout', 'FieldParcels', 'Farmland', 'Features', 'World')
foreach ($phase in 'before', 'after') {
    if ($Only -ne 'all' -and $Only -ne $phase) { continue }
    $files = @('UnityMath.cs', 'RenderStubs.cs', 'HarnessSupport.cs', 'Harness.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    foreach ($name in $names) {
        $file = Join-Path $repo "Assets/DroneSandbox/Scripts/$name.cs"
        if ($phase -eq 'before') {
            $source = (& git -C $repo -c "safe.directory=$repo" show "596e874:Assets/DroneSandbox/Scripts/$name.cs") -join "`r`n"
            if ($LASTEXITCODE -ne 0) { throw 'Could not read baseline' }
            $file = Join-Path $temp "$name.before.cs"
            Write-Generated $file $source
        }
        $files += $file
    }
    if ($phase -eq 'after') { $files += @('SectorIdentity', 'FeatureScenery') | ForEach-Object { Join-Path $repo "Assets/DroneSandbox/Scripts/$_.cs" } }
    $includes = ($files | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`r`n"
    $define = if ($phase -eq 'after') { 'AFTER' } else { 'BEFORE' }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><DefineConstants>$define</DefineConstants></PropertyGroup>
<ItemGroup>$includes</ItemGroup>
</Project>
"@
    $projectPath = Join-Path $temp "$phase.csproj"
    Write-Generated $projectPath $project
    & $Dotnet build $projectPath -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw "$phase compilation failed" }
    & $Dotnet (Join-Path $temp "bin/Release/net8.0/$phase.dll") (Join-Path $Output $phase) $repo
    if ($LASTEXITCODE -ne 0) { throw "$phase checks failed" }
}

# Compile game sources against the installed Unity API as well as the executable substitutes above.
$assembly = Join-Path (Split-Path (Split-Path $Dotnet -Parent) -Parent) 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
$files = @($names + 'SectorIdentity' + 'FeatureScenery') | ForEach-Object { Join-Path $repo "Assets/DroneSandbox/Scripts/$_.cs" }
$files += Join-Path $PSScriptRoot 'ApiStubs.cs'
$includes = ($files | ForEach-Object { '<Compile Include="' + $_ + '" />' }) -join "`r`n"
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
<ItemGroup>$includes<Reference Include="UnityEngine.CoreModule"><HintPath>$assembly</HintPath></Reference></ItemGroup>
</Project>
"@
Write-Generated (Join-Path $temp 'api.csproj') $project
& $Dotnet build (Join-Path $temp 'api.csproj') -c Release --nologo --configfile (Join-Path $temp 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Unity API compilation failed' }
if (!$SkipRender) {
    & $Python -I (Join-Path $PSScriptRoot 'render.py') $Output
    if ($LASTEXITCODE -ne 0) { throw 'Plan rendering failed' }
}
