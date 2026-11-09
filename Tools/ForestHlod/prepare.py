from pathlib import Path
import re,subprocess,os
root=Path(__file__).resolve().parents[2];scripts=root/'Assets/DroneSandbox/Scripts';temp=root/'Temp/forest-hlod';temp.mkdir(parents=True,exist_ok=True)
unity=Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data');dotnet=unity/'DotNetSdk/dotnet.exe'
def write(p,text):p.write_bytes(text.replace('\r\n','\n').replace('\n','\r\n').encode())
def block(src,sig):
    a=src.index(sig);b=src.index('{',a);e=b+1;depth=1
    while depth:
        if src[e]=='{':depth+=1
        elif src[e]=='}':depth-=1
        e+=1
    return src[a:e]
sector=re.search(r'public enum SectorKind\s*\{[^}]+\}',(scripts/'Sector.cs').read_text()).group()
write(temp/'SectorKind.cs','namespace DroneSandbox { '+sector+' }')
write(temp/'ApiSupport.cs','''using UnityEngine;
namespace DroneSandbox {
public static class Ground { public const float Size=2000f; public static float HeightAt(Vector3 p)=>WorldPlan.Height(p.x+FarTerrain.Origin.x,p.z+FarTerrain.Origin.y); }
public static class World { public static int WorldSeed=1; public static string Timings=""; }
public class SectorPlan { public Vector2 damPoint; public static SectorPlan Varrow()=>new SectorPlan(); }
public static class Art { public static Material NewMaterial(Color c)=>null; public static void Discard(Object o){} }
public static class FarDetail { public static void Clear(){} public static void Initialise(Transform t){} public static bool TryHeight(Vector2 p,out float h){h=0;return false;} public static float ScatterHeight(Vector2 p,bool tree)=>FarTerrain.GroundAt(p); }
public static class FarDetailChunks { public static void Register(Vector2 p,MeshFilter m,LODGroup g){} }
public enum DroneType { Scout,Recon }
public class Drone : MonoBehaviour { public bool IsAlive=true;public float Battery=1;public void MoveTo(Vector3 p){} }
public class SandboxGame { public Vector3 basePosition,enemyHqPosition;public void StartSandbox(){} public Drone SpawnDrone(DroneType t)=>null; }
public class RtsController { public void LookAt(Vector3 p,float d,float y){} }
}
''')
files=list(scripts.glob('WorldPlan*.cs'))+[scripts/(x+'.cs') for x in ['FarTerrain','FarScatter','ForestPattern','ForestPalette','ForestHlod','SettlementPattern','SettlementHlod']]+[temp/(x+'.cs') for x in ['SectorKind','ApiSupport']]
refs=['CoreModule','PhysicsModule']
includes=''.join('<Compile Include="'+str(p)+'" />' for p in files)
references=''.join('<Reference Include="UnityEngine.'+n+'"><HintPath>'+str(unity/('Managed/UnityEngine/UnityEngine.'+n+'.dll'))+'</HintPath></Reference>' for n in refs)
write(temp/'api.csproj','<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>'+includes+references+'</ItemGroup></Project>')
write(temp/'NuGet.Config','<configuration><packageSources><clear /></packageSources></configuration>')
env=dict(os.environ,DOTNET_CLI_HOME=str(temp/'dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1')
subprocess.run([str(dotnet),'build',str(temp/'api.csproj'),'-c','Release','--nologo','--configfile',str(temp/'NuGet.Config')],env=env,check=True)
