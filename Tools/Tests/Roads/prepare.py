"""Compile against Unity 6 without importing a Library; bounded managed review uses the established scaffold."""
from pathlib import Path
import os
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
out = ROOT / 'Temp/roads/build'
out.mkdir(parents=True, exist_ok=True)
base = HERE.parent / 'PlacesUpClose/prepare.py'
source = base.read_text().split('\nenv=dict(')[0]
source = source.replace("ROOT/'Temp/places-up-close/build'", 'Path(' + repr(str(out)) + ')')
scope = {'__file__': str(base)}
exec(compile(source, str(base), 'exec'), scope)
files, scripts, unity, dotnet, write, body = [scope[k] for k in ('files','scripts','unity','dotnet','write','body')]
files += list(scripts.glob('Road*.cs'))
files = list(dict.fromkeys(files))
subprocess.run([sys.executable, '-I', str(HERE/'patches.py')], check=True)
patched = ROOT/'Temp/roads/patched'
engine = (out/'Engine.cs').read_text()
engine += '\nnamespace UnityEngine { public class MonoBehaviour : Component {} public static class Time { public static float time; } public class Camera : Component {} public struct Matrix4x4 { public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s)=>new Matrix4x4(); } }\nnamespace UnityEngine.Rendering { public struct ScriptableRenderContext {} }\n'
write(out/'Engine.cs',engine)
# Use the original sector splat cell verbatim for the before road layer, omitting its four-million-cell storage.
ground = (out/'Ground.review.cs').read_text()
original = (scripts/'Ground.cs').read_text()
splat = body(original,'        static float[,,] BuildSplat(')
cell = splat[splat.index('                    float steep ='):splat.index('\n                }\n            });')]
cell = re.sub(r'splat\[zi, xi, (\d)\] = ',r'w[\1] = ',cell).replace('splat[zi, xi, layer]','w[layer]')
sampler = '''        public static Color32 ReviewBefore(float x,float z)
        {
            var river=MapLayout.RiverField;var roads=MapLayout.RoadField;var tracks=MapLayout.TrackField;
            var junctions=MapLayout.Junctions;float hw=MapLayout.RiverHalfWidth;var w=new float[8];
''' + cell + '''
            Vector3 colour=Vector3.zero;
            for(int i=0;i<8;i++)colour+=(i<5?FarDetailPalette.Means[i]:i==5?LitterMean:i==6?WaterAppearance.WetMud:WaterAppearance.Gravel)*w[i];
            return FarDetailPalette.Encode(colour);
        }
'''
ground = ground.replace('        static float[,,] BuildSplat(',sampler+'        static float[,,] BuildSplat(')
# A second extracted sampler measures the changed splat work on exactly the same point batch.
after_splat = body((patched/'Ground.cs').read_text(),'        static float[,,] BuildSplat(')
after_cell = after_splat[after_splat.index('                    float steep ='):after_splat.index('\n                }\n            });')]
after_cell = re.sub(r'splat\[zi, xi, (\d)\] = ',r'w[\1] = ',after_cell).replace('splat[zi, xi, layer]','w[layer]')
after_sampler = sampler.replace('ReviewBefore','ReviewAfter').replace(cell,after_cell)
ground = ground.replace('        static float[,,] BuildSplat(',after_sampler+'        static float[,,] BuildSplat(')
write(out/'Ground.review.cs',ground)
# Actual roadside selection/budget code runs with resource loading and GPU submission substituted only.
side = (scripts/'Roadside.cs').read_text()
side = side.replace('public int cost;','public int cost; public string name;')
side = side.replace('public static int Trees', 'public static readonly List<(string name,Vector3 p,Vector2 dir,float scale)> Review = new List<(string,Vector3,Vector2,float)>();\n        public static int Trees')
side = side.replace(body(side,'        Model Load('), '        Model Load(string name,bool tree) { var m=new Model{name=name,tree=tree,cost=3,tiers=new Part[3][]}; models.Add(m); return m; }')
side = side.replace(body(side,'        void Draw('), '        void Draw(ScriptableRenderContext context,Camera camera) {}')
side = side.replace('void OnEnable() => RenderPipelineManager.beginCameraRendering += Draw;', 'void OnEnable() {}').replace('void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;', 'void OnDisable() {}')
side = side.replace('            allowance -= model.cost; return true;', '            Review.Add((model.name,at,direction,scale)); allowance -= model.cost; return true;')
side = '#pragma warning disable CS0649\n' + side
write(out/'Roadside.review.cs',side)
# The review omits native splat storage but retains the full terrain and road mesh builders.
paint = (scripts/'FarDetailPaint.cs').read_text().replace('            c = RoadAppearance.Paint(c, road, pixel, land.Weight(Cover.Farmland) > .5f);', '            if (lanes != null && lanes.Length == 0) return c;\n            c = RoadAppearance.Paint(c, road, pixel, land.Weight(Cover.Farmland) > .5f);')
write(out/'FarDetailPaint.review.cs', paint)
env = dict(os.environ, DOTNET_CLI_HOME=str(out/'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0', MSBUILDDISABLENODEREUSE='1')
for mode in ('api',) if '--api' in sys.argv else ('harness',) if '--harness' in sys.argv else ('api','harness'):
    sources = list(files); refs = ''
    if mode == 'api':
        services = (out/'Services.cs').read_text()
        services = services.replace(body(services,'    public static class Farmland'), '')
        services = services.replace('public void Clear(){} public static bool InSector', 'public const float HouseReach=1500f; public void House(SettlementPattern.House h){} public static float GroundAt(Vector2 p)=>0f; public void Clear(){} public static bool InSector')
        write(out/'ApiServices.cs',services)
        sources = [out/'ApiServices.cs' if p.name == 'Services.cs' else p for p in sources]
        sources += [scripts/n for n in ('Farmland.cs','SettlementHlod.cs','LandmarkClutter.cs','LandmarkHlod.cs','LandmarkInstances.cs','LandmarkLod.cs','FarDetailEdge.cs','FarChunkUpload.cs','FarDetailChunks.cs')]
        sources = [patched/p.name if p.name in ('Ground.cs','Farmland.cs') else p for p in sources]
        for m in ('CoreModule','TerrainModule','TerrainPhysicsModule','PhysicsModule','AnimationModule'):
            refs += f'<Reference Include="UnityEngine.{m}"><HintPath>{unity/("Managed/UnityEngine/UnityEngine."+m+".dll")}</HintPath><Private>false</Private></Reference>'
    else:
        sources = [out/'Ground.review.cs' if p.name == 'Ground.cs' else out/'Roadside.review.cs' if p.name == 'Roadside.cs' else p for p in sources]
        sources = [out/'FarDetailPaint.review.cs' if p.name == 'FarDetailPaint.cs' else p for p in sources]
        sources += [out/n for n in ('ConsumerMath.cs','Engine.cs','ClutterStub.cs')]+[HERE/'Harness.cs']
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out/(mode+'.csproj'), f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Library" if mode=="api" else "Exe"}</OutputType><TargetFramework>{"netstandard2.1" if mode=="api" else "net8.0"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
    subprocess.run([str(dotnet),'build',str(out/(mode+'.csproj')),'-c','Release','--nologo','-m:1','--configfile',str(out/'NuGet.Config')],env=env,check=True)
