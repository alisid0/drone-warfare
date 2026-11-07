"""Compile changed production C# against Unity 6000.6.7f1 assemblies without starting the Editor."""
from pathlib import Path
import os
import subprocess

root = Path(__file__).resolve().parents[2]
template = (root / 'Tools/WorldLandmarks/prepare.py').read_text()
prefix = template[:template.index("for mode in (")]
prefix = prefix.replace("temp = root / 'Temp/world-landmarks'", "temp = root / 'Temp/distant-forest'")
exec(compile(prefix, __file__, 'exec'))


def remove_class(text, signature):
    """Remove a test service when the real production consumer is being compiled instead."""
    start = text.index(signature)
    brace = text.index('{', start)
    depth, end = 1, brace + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[:start] + text[end:]


services = remove_class(services, 'public static class Undergrowth')
services = services.replace('public static bool Active => false;', '''public static bool Active => false;
        public static Vector2 Origin;
        public static bool ForestCanopyHlod = true;
        public static Color CanopyGroundColour(float x,float z)=>new Color(0.115f,0.178f,0.066f);''')
write(temp / 'Services.cs', services)
write(temp / 'DetailStub.cs', '''using UnityEngine; namespace DroneSandbox {
public static class FarDetail {
public static float HeightAt(Vector2 p)=>WorldPlan.Height(p.x,p.y);
public static float ScatterHeight(Vector2 p,bool tree)=>WorldPlan.Height(p.x+FarTerrain.Origin.x,p.y+FarTerrain.Origin.y);
public static bool TryHeight(Vector2 p,out float h){h=0;return false;}
public static bool Raycast(Ray r,out Vector3 p){p=default;return false;} } }''')
added = ['SectorForest', 'ForestBudget', 'ForestMaterials', 'Undergrowth', 'FarScatter', 'ForestHlod', 'SettlementPattern', 'SettlementHlod', 'LandmarkHlod']
files += [scripts / (name + '.cs') for name in added]
# Scans' placement is an unrelated service; retain all of its public identifiers for Undergrowth's API check.
services = remove_class(services, 'public static class Scans')
write(temp / 'Services.cs', services)
files += [scripts / 'Scans.cs']
files += sorted(scripts.glob('FarField*.cs')) + [scripts / 'FarDetailCover.cs']
files += [scripts / (name + '.cs') for name in ['FarDetailData', 'FarDetailPaint', 'FarDetailPalette']]

files += [scripts / 'Canopy.cs']
refs = ''.join('<Reference Include="UnityEngine.' + name + '"><HintPath>' + str(unity / ('Managed/UnityEngine/UnityEngine.' + name + '.dll')) + '</HintPath></Reference>'
               for name in ['CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule'])
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in files)
write(temp / 'api.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + refs + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'api.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)

# Compile the complete terrain/scatter integration as well, so future FarDetail*/FarField* additions cannot hide behind a stub.
integration = remove_class(services, 'public static class FarTerrain')
integration += '''
namespace DroneSandbox {
public sealed class Drone : UnityEngine.MonoBehaviour { public bool IsAlive => false; }
public sealed class DroneFeed { public static DroneFeed Instance => null; public Drone Source => null; }
}'''
write(temp / 'IntegrationServices.cs', integration)
complete = [temp / 'IntegrationServices.cs' if p.name == 'Services.cs' else p for p in files if p.name != 'DetailStub.cs']
complete += [scripts / 'FarTerrain.cs'] + sorted(scripts.glob('FarDetail*.cs')) + sorted(scripts.glob('FarField*.cs'))
complete = list(dict.fromkeys(complete))
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in complete)
write(temp / 'integration-api.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + refs + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'integration-api.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
