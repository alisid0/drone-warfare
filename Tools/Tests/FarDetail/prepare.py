"""Prepare repeatable API and pure-data checks without starting or importing Unity."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(sys.argv[1]).resolve()
UNITY = Path(sys.argv[2])
SCRIPTS = ROOT / 'Assets/DroneSandbox/Scripts'
HERE = Path(__file__).parent
OUT.mkdir(parents=True, exist_ok=True)


def write(name, text):
    """Keep generated scaffolding in Temp with repository line endings."""
    (OUT / name).write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())


def method(text, anchor):
    """Extract a complete production method verbatim, including its local functions."""
    start = text.index(anchor)
    end = text.index('{', start) + 1
    depth = 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def project(name, files, engine=False):
    """Use installed Unity assemblies only for the API build, never substitutes for engine signatures."""
    refs = ''
    if engine:
        for module in ['CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule']:
            refs += f'<Reference Include="UnityEngine.{module}"><HintPath>{UNITY / ("UnityEngine." + module + ".dll")}</HintPath></Reference>'
    includes = ''.join(f'<Compile Include="{file}" />' for file in files)
    output = 'Library' if engine else 'Exe'
    write(name + '.csproj', f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>{output}</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup>
<ItemGroup>{includes}{refs}</ItemGroup></Project>''')


write('NuGet.Config', '<configuration><packageSources><clear /></packageSources></configuration>')
sector = (SCRIPTS / 'Sector.cs').read_text()
write('SectorKind.cs', 'namespace DroneSandbox { ' + re.search(r'public enum SectorKind\s*\{[^}]+\}', sector).group() + ' }')
math = (ROOT / 'Tools/Tests/SectorIdentity/UnityMath.cs').read_text().split('namespace DroneSandbox')[0]
math = math.replace('public const float PI', '''public static float Floor(float x)=>(float)Math.Floor(x);
        public static float Ceil(float x)=>(float)Math.Ceiling(x);
        public const float PI''')
write('Math.cs', math)
far = (SCRIPTS / 'FarTerrain.cs').read_text()
canopy = far[far.index('        const float CanopyStep'):far.index('        // The ground\'s height')]
picture = far[far.index('        static void PaintWorld()'):far.rfind('\n    }')]
write('FarPicture.cs', '''using System; using UnityEngine;
namespace DroneSandbox { public static class FarPicture {
const int TextureSize=4096;
static Texture2D worldTexture; static int textureSeed;
public static void Build() { BuildCanopy(); PaintWorld(); }
public static int Seed => textureSeed;
public static float Elevation(float x,float z)=>WorldPlan.Height(x,z)+Canopy(x,z);
public static Color32 ExactField(float x,float z)=>Field(x,z,new [] {new Color32(170,146,90,255),new Color32(156,138,84,255),new Color32(98,114,62,255),new Color32(86,104,58,255),new Color32(104,82,62,255),new Color32(138,132,86,255)});
''' + canopy + picture + '\n} }')
world = sorted(SCRIPTS.glob('WorldPlan*.cs'))
helpers = [SCRIPTS / name for name in ['FarDetailData.cs', 'FarDetailChunks.cs', 'FarDetailRay.cs']]
project('harness', world + helpers + [SCRIPTS/'FarDetail.cs', SCRIPTS/'Scale.cs', OUT/'SectorKind.cs', OUT/'Math.cs', OUT/'FarPicture.cs', HERE/'EngineStubs.cs', HERE/'Harness.cs', HERE/'StreamChecks.cs'])

# Complete modified consumers and helpers compile with the real engine APIs. Only unrelated game services are stubbed.
services = (ROOT/'Tools/WorldSectors/ConsumerServices.cs').read_text()
start = services.index('    public static class FarTerrain')
end = services.index('    public static class Backdrop', start)
services = services[:start] + services[end:]
services += '''
namespace DroneSandbox {
public class Drone : UnityEngine.MonoBehaviour { public bool IsAlive=>true; public float Battery=>1; public void MoveTo(UnityEngine.Vector3 p){} }
public class DroneFeed { public static DroneFeed Instance; public bool Visible; public Drone Source; }
public class RtsController { public UnityEngine.Camera Cam; public System.Collections.Generic.List<Drone> selected=new System.Collections.Generic.List<Drone>(); public void LookAt(UnityEngine.Vector3 p,float d,float a){} }
public class SandboxGame { public UnityEngine.Vector3 basePosition,enemyHqPosition; public void StartSandbox(){} public Drone SpawnDrone(DroneType t)=>null; }
}
'''
write('Services.cs', services)
enums = '\n'.join(re.search(r'public enum '+enum+r'\s*\{[^}]+\}', (SCRIPTS/(file+'.cs')).read_text()).group()
                  for file, enum in [('EnemyUnit','EnemyType'),('Drone','DroneType')])
write('Enums.cs', 'namespace DroneSandbox { '+enums+' }')
theatre = (SCRIPTS/'Theatre.cs').read_text()
write('Hotspot.cs', 'using UnityEngine; namespace DroneSandbox { '+theatre[theatre.index('    public class Hotspot'):theatre.index('    // The theatre of war')]+' }')
autotest = (SCRIPTS/'AutoTest.cs').read_text()
write('WorldCheck.cs', '''using System; using System.Collections; using System.Collections.Generic; using System.IO; using UnityEngine;
namespace DroneSandbox { public class WorldCheckCompile {
StreamWriter log=new StreamWriter(Stream.Null); int errors;
public int Errors=>errors;
IEnumerator Shot(string name)=>null;
IEnumerator WorldMapCheck(SandboxGame g,RtsController r,Drone d)=>null;
static Vector3 Flat(Vector3 v)=>new Vector3(v.x,0,v.z);
''' + method(autotest, 'IEnumerator WorldCheck(') + '\n} }')
names = ['Scale','Sector','SectorGenerator','SectorIdentity','PathField','MapLayout','FieldParcels','Situations','Missions','Ground','Water','World','FarDetail','FarTerrain','FarScatter']
files = [SCRIPTS/(name+'.cs') for name in names] + world + sorted(SCRIPTS.glob('WorldSector*.cs')) + helpers
files += [OUT/'Services.cs', OUT/'Enums.cs', OUT/'Hotspot.cs', OUT/'WorldCheck.cs', ROOT/'Tools/WorldSectors/HdrpStubs.cs']
project('api', files, True)
