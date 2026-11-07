"""Build real planners against Unity APIs and managed engine storage without opening Unity."""
from pathlib import Path
import os
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
scripts = root / 'Assets/DroneSandbox/Scripts'
temp = root / 'Temp/world-landmarks'
temp.mkdir(parents=True, exist_ok=True)
unity = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data')
dotnet = unity / 'DotNetSdk/dotnet.exe'


def write(path, text):
    """Generated scaffolding stays in Temp and keeps repository line endings."""
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())


subprocess.run([sys.executable, '-I', str(root / 'Tools/WorldSectors/prepare_consumers.py'), str(temp)], check=True)
theatre = (scripts / 'Theatre.cs').read_text()
hotspot = theatre[theatre.index('    public class Hotspot'):theatre.index('    // The theatre of war')]
write(temp / 'Hotspot.cs', 'using UnityEngine; namespace DroneSandbox {\n' + hotspot + '}')
files = [scripts / (x + '.cs') for x in ['Scale', 'Sector', 'SectorGenerator', 'SectorIdentity', 'PathField', 'MapLayout', 'FieldParcels', 'Ground', 'Water', 'World', 'WorldLandmarkLayout', 'LandmarkMesh', 'LandmarkCheck', 'ForestPattern', 'ForestPalette', 'Features', 'FeatureScenery', 'Situations', 'Missions']]
files += list(scripts.glob('WorldPlan*.cs')) + list(scripts.glob('WorldSector*.cs'))
files += [scripts / 'WorldFeatureMap.cs']
services = (root / 'Tools/WorldSectors/ConsumerServices.cs').read_text()
services = services.replace('public static class Features { public static void Build(Transform t) { } }', '')
services = services.replace('public static Material NewMaterial(Color c, bool glow = false) => null;', 'public static Material NewMaterial(Color c, bool glow = false) => new Material(Shader.Find("HDRP/Lit")); public static Material Shared(Color c, bool glow = false) => NewMaterial(c);')
services = services.replace('new Material(Shader.Find("HDRP/Lit"));', 'new Material(Shader.Find("HDRP/Lit")) { color=c };')
services = services.replace('Boulders = { }, Rocks = { }', 'Boulders = { }, Rocks = { }, Stumps = { }')
services = services.replace('Vector3? euler = null) => null;', 'Vector3? euler = null) => new GameObject("Primitive");')
services = services.replace('var mesh = new GameObject(m);', 'var mesh = new GameObject(m); mesh.transform.localScale = Vector3.one * scale;')
write(temp / 'Services.cs', services)
files += [temp / 'Hotspot.cs', temp / 'Enums.cs', temp / 'Services.cs', root / 'Tools/WorldSectors/HdrpStubs.cs']
write(temp / 'DetailStub.cs', 'using UnityEngine; namespace DroneSandbox { public static class FarDetail { public static float HeightAt(Vector2 p)=>WorldPlan.Height(p.x,p.y); public static bool TryHeight(Vector2 p,out float h){h=0;return false;} public static bool Raycast(Ray r,out Vector3 p){p=default;return false;} } }')
files += [temp / 'DetailStub.cs']
write(temp / 'NuGet.Config', '<configuration><packageSources><clear /></packageSources></configuration>')
env = dict(os.environ, DOTNET_CLI_HOME=str(temp / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1')
engine = (root / 'Tools/WorldSectors/EngineStubs.cs').read_text()
engine = engine.replace('public Vector3 TransformPoint', 'public T[] GetComponentsInChildren<T>() where T:new()=>new T[0]; public Vector3 TransformPoint')
engine = engine.replace('public Material sharedMaterial;', 'public Material sharedMaterial; public Material[] sharedMaterials;')
engine = engine.replace('public Rendering.IndexFormat indexFormat;', 'public Rendering.IndexFormat indexFormat; public int subMeshCount; public void SetNormals(List<Vector3> n){normals=n.ToArray();}')
engine = engine.replace('public class Mesh : Object\n    {', 'public class Mesh : Object\n    { public static readonly List<Mesh> All=new List<Mesh>(); public Mesh(){All.Add(this);} public readonly Dictionary<int,int[]> subs=new Dictionary<int,int[]>();')
engine = engine.replace('triangles = t.ToArray();', 'triangles = t.ToArray(); subs[sub]=triangles;')
engine = engine.replace('public class Material : Object {', 'public class Shader { public static Shader Find(string name)=>new Shader(); } public class Material : Object { public Material(){} public Material(Shader s){}')
engine = engine.replace('public Material(){}', 'public Color color; public Material(){}')
engine = engine.replace('public struct Color32 { }', 'public struct Color32 { public byte r,g,b,a; public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;} public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f); }')
engine = engine.replace('public static Color white', 'public Color linear=>new Color((float)Math.Pow(r,2.2),(float)Math.Pow(g,2.2),(float)Math.Pow(b,2.2),a); public static Color operator*(Color c,float f)=>new Color(c.r*f,c.g*f,c.b*f,c.a*f); public static Color white')
math = (temp / 'ConsumerMath.cs').read_text().replace('public const float PI', 'public static float Exp(float f)=>(float)System.Math.Exp(f); public const float PI')
write(temp / 'ConsumerMath.cs', math)
write(temp / 'Engine.cs', engine)
for mode in (['harness'] if '--harness-only' in sys.argv else ['api', 'harness']):
    sources = list(files)
    refs = ''
    if mode == 'harness':
        sources += [temp / 'ConsumerMath.cs', temp / 'Engine.cs', Path(__file__).parent / 'Harness.cs']
    else:
        for name in ['CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule']:
            refs += '<Reference Include="UnityEngine.' + name + '"><HintPath>' + str(unity / ('Managed/UnityEngine/UnityEngine.' + name + '.dll')) + '</HintPath></Reference>'
    include = ''.join('<Compile Include="' + str(p) + '" />' for p in sources)
    write(temp / (mode + '.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>' + ('Exe' if mode == 'harness' else 'Library') + '</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + include + refs + '</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(temp / (mode + '.csproj')), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)

if '--harness-only' in sys.argv:
    sys.exit(0)

# The far-scene API group includes complete FarScatter, ForestHlod and FarTerrain against real Unity assemblies.
template = (root / 'Tools/ForestHlod/prepare.py').read_text()
support = re.search(r"write\(temp/'ApiSupport.cs','''(.*?)'''\)", template, re.S).group(1)
write(temp / 'RenderSupport.cs', support)
sector = (scripts / 'Sector.cs').read_text()
area = sector[sector.index('    public struct FeatureArea'):sector.index('    // A terrain-following yard')]
kind = re.search(r'public enum SectorKind\s*\{[^}]+\}', sector).group()
write(temp / 'RenderTypes.cs', 'using UnityEngine; namespace DroneSandbox { ' + kind + area + '}')
render = list(scripts.glob('WorldPlan*.cs')) + [scripts / (x + '.cs') for x in ['FarTerrain', 'FarScatter', 'ForestPattern', 'ForestPalette', 'ForestHlod', 'SettlementPattern', 'SettlementHlod', 'LandmarkMesh', 'LandmarkHlod', 'WorldLandmarkLayout']]
render += [temp / 'RenderSupport.cs', temp / 'RenderTypes.cs']
render += [scripts / 'WorldFeatureMap.cs']
include = ''.join('<Compile Include="' + str(p) + '" />' for p in render)
refs = ''.join('<Reference Include="UnityEngine.' + name + '"><HintPath>' + str(unity / ('Managed/UnityEngine/UnityEngine.' + name + '.dll')) + '</HintPath></Reference>' for name in ['CoreModule', 'PhysicsModule'])
write(temp / 'render-api.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + include + refs + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'render-api.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
