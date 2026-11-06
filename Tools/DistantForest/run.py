"""Matched baseline/candidate production builders. Managed engine storage is not an HDRP performance measurement."""
from pathlib import Path
import subprocess
import sys

exec(compile((Path(__file__).parent / 'prepare.py').read_text(), str(Path(__file__).parent / 'prepare.py'), 'exec'))
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
runtime = list(files)
excluded = {'FarScatter', 'ForestHlod', 'ForestMaterials', 'SettlementPattern', 'SettlementHlod', 'LandmarkHlod', 'Scans'}
runtime = [p for p in runtime if p.stem not in excluded]
stub = services + '''
namespace DroneSandbox { public class FarScatter {
public static bool InSector(UnityEngine.Vector2 p)=>System.Math.Abs(p.x)<1000&&System.Math.Abs(p.y)<1000;
public static float GroundAt(UnityEngine.Vector2 p)=>InSector(p)?Ground.HeightAt(new UnityEngine.Vector3(p.x,0,p.y)):WorldPlan.Height(p.x+FarTerrain.Origin.x,p.y+FarTerrain.Origin.y);
}
public static class Scans {
public static readonly string[] Ferns={},Nettles={},Moss={},Bushes={},Shrubs={},Wildflowers={},Pebbles={},Stumps={},Boulders={},Rocks={};
public const string FlatStone="",Stone="",FallenTrunk="";
public static bool Has(string s)=>false;
public static string Pick(string[] s,double r)=>null;
public static UnityEngine.GameObject Place(string m,UnityEngine.Vector3 p,float y,float s,UnityEngine.Transform t,float sink,float tilt,float foot,bool shadows)=>null;
public static UnityEngine.GameObject Rock(string[] a,double r,UnityEngine.Vector3 p,float y,float s,UnityEngine.Transform t,string f,float fs,float sink)=>null;
} }
'''
stub = stub.replace('Ferns={}', 'Ferns={"missing"}')
write(temp / 'RuntimeServices.cs', stub)
runtime = [temp / 'RuntimeServices.cs' if p.name == 'Services.cs' else p for p in runtime]
engine = (temp / 'Engine.cs').read_text()
engine += '\nnamespace UnityEngine { public struct Vector2Int { public int x,y; public Vector2Int(int x,int y){this.x=x;this.y=y;} public override int GetHashCode()=>unchecked(x*73856093^y*19349663); public override bool Equals(object o)=>o is Vector2Int b&&x==b.x&&y==b.y; } }'
engine = engine.replace('public static Color white', 'public static Color gray=>new Color(.5f,.5f,.5f); public static Color white')
engine = engine.replace('public Color linear=>new Color((float)Math.Pow(r,2.2),(float)Math.Pow(g,2.2),(float)Math.Pow(b,2.2),a);', '''static float Decode(float c)=>c<=.04045f?c/12.92f:(float)Math.Pow((c+.055f)/1.055f,2.4);
static float Encode(float c)=>c<=.0031308f?c*12.92f:1.055f*(float)Math.Pow(c,1/2.4)-.055f;
public Color linear=>new Color(Decode(r),Decode(g),Decode(b),a);public Color gamma=>new Color(Encode(r),Encode(g),Encode(b),a);''')
engine = engine.replace('public static implicit operator Color32(Color c)=>new Color32();', 'public static implicit operator Color32(Color c)=>new Color32((byte)Mathf.Clamp(c.r*255,0,255),(byte)Mathf.Clamp(c.g*255,0,255),(byte)Mathf.Clamp(c.b*255,0,255),255);')
engine = engine.replace('public struct Vector4 {', 'public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}')
engine = engine.replace('GetComponentsInChildren<T>()', 'GetComponentsInChildren<T>(bool inactive=false)')
engine = engine.replace('public class Renderer : Component {', 'public class Renderer : Component { public Bounds bounds;')
engine += '''
namespace UnityEngine {
public static class Time { public static int frameCount; public static float unscaledTime; }
public class LODGroup : Component { public LOD[] GetLODs()=>new LOD[0]; }
public struct LOD { public Renderer[] renderers; }
public struct Bounds { public Vector3 center,size; public Bounds(Vector3 c,Vector3 s){center=c;size=s;}
public Vector3 extents=>size*.5f; public Vector3 min=>center-extents;public Vector3 max=>center+extents;
public void Encapsulate(Bounds b){} }
}
'''
write(temp / 'RuntimeEngine.cs', engine)
math = (temp / 'ConsumerMath.cs').read_text()
math = math.replace('public static Vector2 zero', '''public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y; public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);
public override bool Equals(object o)=>o is Vector2 b&&this==b; public override int GetHashCode()=>x.GetHashCode()^y.GetHashCode(); public static Vector2 zero''')
write(temp / 'RuntimeMath.cs', math)
runtime += [temp / 'RuntimeMath.cs', temp / 'RuntimeEngine.cs', root / 'Tools/DistantForest/Harness.cs']
# Bake the exact production surface functions, omitting only the native upload/camera submission methods.
hlod = (scripts / 'ForestHlod.cs').read_text()
if phase == 'before':
    hlod = subprocess.check_output(['git', '-c', 'safe.directory=' + str(root).replace(chr(92), '/'), 'show', '9f8a34b:Assets/DroneSandbox/Scripts/ForestHlod.cs'], text=True)
data_start = hlod.index('public sealed class Data')
data_end = hlod.index('\n        }', data_start) + len('\n        }')
surface = hlod[hlod.rfind('        //', 0, hlod.index('        sealed class Surface')):hlod.index('        // Find the largest')]
write(temp / 'CanopyBake.cs', 'using System.Collections; using System.Collections.Generic; using UnityEngine; namespace DroneSandbox { public static class ForestHlod { public const int BlockSize=2048; public const int TileSize=256; ' + hlod[data_start:data_end] + surface + ' } }')
runtime += [temp / 'CanopyBake.cs']
if phase != 'before':
    runtime += [root / 'Tools/DistantForest/Checks.cs']
    reference = subprocess.check_output(['git', '-c', 'safe.directory=' + str(root).replace(chr(92), '/'), 'show', '9f8a34b:Assets/DroneSandbox/Scripts/Canopy.cs'], text=True)
    write(temp / 'ReferenceCanopy.cs', reference.replace('public static class Canopy', 'public static class ReferenceCanopy'))
    runtime += [temp / 'ReferenceCanopy.cs', root / 'Tools/DistantForest/CrownChecks.cs']
if phase == 'before':
    baseline = temp / 'baseline'; baseline.mkdir(exist_ok=True)
    for name in ['ForestPattern', 'ForestPalette', 'SectorForest', 'ForestBudget']:
        source = subprocess.check_output(['git', '-c', 'safe.directory=' + str(root).replace('\\', '/'), 'show', '9f8a34b:Assets/DroneSandbox/Scripts/' + name + '.cs'], text=True)
        write(baseline / (name + '.cs'), source)
        runtime = [baseline / p.name if p.stem == name else p for p in runtime]
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in runtime)
write(temp / 'harness.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><DefineConstants>' + ('CANDIDATE' if phase != 'before' else 'BASELINE') + '</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'harness.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
subprocess.run([str(dotnet), str(temp / 'bin/Release/net8.0/harness.dll'), str(temp / phase)], env=env, check=True)
