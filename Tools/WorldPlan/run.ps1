param(
    [string]$OutputDirectory = "$env:LOCALAPPDATA\Temp\dwprev\codex\world",
    [int[]]$Seeds = @(1, 2, 3),
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$build = Join-Path $repo 'Temp\WorldPlan'
New-Item -ItemType Directory -Force -Path $build, $OutputDirectory | Out-Null
# Extract the real enum on every run, so the harness cannot silently invent sector names.
$sector = [IO.File]::ReadAllText((Join-Path $repo 'Assets\DroneSandbox\Scripts\Sector.cs'))
$enum = [regex]::Match($sector, 'public enum SectorKind\s*\{[^}]+\}').Value
if (-not $enum) { throw 'SectorKind was not found in Sector.cs' }
[IO.File]::WriteAllText((Join-Path $build 'SectorKind.cs'), "namespace DroneSandbox { $enum }")
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'Assets\DroneSandbox\Scripts') -Filter 'WorldPlan*.cs' | ForEach-Object FullName)
$sources += (Join-Path $build 'SectorKind.cs'), (Join-Path $PSScriptRoot 'WorldPlanHarness.cs')
$exe = Join-Path $build 'WorldPlanHarness.exe'
& $compiler /nologo /optimize+ /platform:x64 /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'WorldPlan compilation failed' }
foreach ($seed in $Seeds) {
    if ($CheckOnly) { & $exe $OutputDirectory $seed --check } else { & $exe $OutputDirectory $seed }
    if ($LASTEXITCODE -ne 0) { throw "WorldPlan seed $seed failed" }
}
