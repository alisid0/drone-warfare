"""Compile changed production C# against Unity 6000.6.7f1 assemblies without starting the Editor."""
from pathlib import Path
import os
import subprocess

root = Path(__file__).resolve().parents[2]
template = (root / 'Tools/WorldLandmarks/prepare.py').read_text()
prefix = template[:template.index("for mode in (")]
prefix = prefix.replace("temp = root / 'Temp/world-landmarks'", "temp = root / 'Temp/dense-forest'")
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
added = ['SectorForest', 'ForestBudget', 'Undergrowth', 'FarScatter', 'ForestHlod', 'SettlementPattern', 'SettlementHlod', 'LandmarkHlod']
files += [scripts / (name + '.cs') for name in added]
# Scans' placement is an unrelated service; retain all of its public identifiers for Undergrowth's API check.
scan = (scripts / 'Scans.cs').read_text()
services = remove_class(services, 'public static class Scans')
write(temp / 'Services.cs', services)
files += [scripts / 'Scans.cs']
files += [scripts / (name + '.cs') for name in ['FarFieldPlan', 'FarFieldSector', 'FarFieldSurface']]
detail = (scripts / 'FarDetailData.cs').read_text() + (scripts / 'FarDetailPaint.cs').read_text()
def declaration(text, name):
    start = text.index('public struct ' + name)
    end = text.index('}', start) + 1
    return text[start:end]
write(temp / 'FieldTypes.cs', 'using UnityEngine; namespace DroneSandbox { public partial class FarDetailData {' + declaration(detail, 'Segment') + declaration(detail, 'FieldSample') + '} }')
files += [temp / 'FieldTypes.cs']
subprocess.run([sys.executable, '-I', str(root / 'Tools/DenseForest/canopy_patch.py')], check=True)
files += [temp / 'Canopy.cs']
refs = ''.join('<Reference Include="UnityEngine.' + name + '"><HintPath>' + str(unity / ('Managed/UnityEngine/UnityEngine.' + name + '.dll')) + '</HintPath></Reference>'
               for name in ['CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule'])
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in files)
write(temp / 'api.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + refs + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'api.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
