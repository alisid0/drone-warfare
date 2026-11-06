"""Run one sector/revision at a time with real forest code and recording Unity storage; no GPU timing claim."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
sector = sys.argv[2] if len(sys.argv) > 2 else 'deep'
preparation = (root / 'Tools/DistantForest/prepare.py').read_text().split("refs = ''.join")[0]
preparation = preparation.replace("temp = root / 'Temp/distant-forest'", "temp = root / 'Temp/canopy-relief'")
preparation += "\nfiles += [scripts / (n + '.cs') for n in ['VillagePlan', 'VillageLayout', 'VillageScenery', 'WorldLandmarkLayout.Yards', 'FarDetailMips', 'WatercourseProfile']]\n"
script = root / 'Tools/DistantForest/run.py'
source = script.read_text().split('# Bake the exact')[0]
source = source.replace("exec(compile((Path(__file__).parent / 'prepare.py').read_text(), str(Path(__file__).parent / 'prepare.py'), 'exec'))", "exec(compile(preparation, str(script), 'exec'))")
exec(compile(source, str(script), 'exec'))
files += [scripts / 'WaterAppearance.cs']
setup = (root / 'Tools/DistantForest/check_render.py').read_text()
start = setup.index('runtime = [p for p')
setup = setup[start:setup.index("includes = ''.join", start)]
exec(compile(setup, str(script), 'exec'))
runtime = [p for p in runtime if p.name not in {'RenderStorage.cs', 'RenderChecks.cs'}]
hdrp = (root / 'Tools/WorldSectors/HdrpStubs.cs').read_text().replace('public WaterSurfaceType surfaceType;', '''public enum FadeMode { Custom }
        public FadeMode ripplesFadeMode;
        public float ambientScattering, heightScattering, ripplesFadeStart, ripplesFadeDistance, tessellationFactorFadeStart, tessellationFactorFadeRange;
        public bool caustics;
        public WaterSurfaceType surfaceType;''')
write(temp / 'HdrpStubs.cs', hdrp)
runtime = [temp / 'HdrpStubs.cs' if p.name == 'HdrpStubs.cs' else p for p in runtime]
runtime += [scripts / n for n in ('WaterAppearance.cs', 'FarDetailWater.cs', 'FarDetailRegionData.cs')]

engine = (temp / 'RuntimeEngine.cs').read_text()
engine = engine.replace('(byte)Mathf.Clamp(c.b*255,0,255),255)', '(byte)Mathf.Clamp(c.b*255,0,255),(byte)Mathf.Clamp(c.a*255,0,255))')
engine = engine.replace('public Transform parent;', 'public Transform parent; public Vector3 forward=>rotation.forward;')
engine += '\nnamespace UnityEngine { public static class Debug { public static void Log(object v)=>System.Console.WriteLine(v); public static void LogWarning(object v)=>System.Console.WriteLine(v); } }'
engine = engine.replace('public class LODGroup : Component {', 'public class LODGroup : Component { public float size=20; public void SetLODs(LOD[] a){lods=a;} public void RecalculateBounds(){}')
engine = engine.replace('public struct LOD {', 'public struct LOD { public LOD(float f,Renderer[] r){renderers=r;}')
engine = engine.replace('public static class Debug', 'public static class Debug')
engine = engine.replace('public static void Log(', 'public static void Log(')
if 'LogWarning(' not in engine:
    engine = engine.replace('public static class Debug {', 'public static class Debug { public static void LogWarning(object v)=>System.Console.WriteLine(v);')
if 'public static void Log(' not in engine:
    engine = engine.replace('public static class Debug {', 'public static class Debug { public static void Log(object v)=>System.Console.WriteLine(v);')
engine = engine.replace('public class Texture2D', 'public class Texture2D')
engine = engine.replace('public void SetPixels32(Color32[] p) { }', 'public Color32[] pixels; public void SetPixels32(Color32[] p) { pixels=p; }')
write(temp / 'RuntimeEngine.cs', engine)
services = (temp / 'RuntimeServices.cs').read_text()
services += '\nnamespace DroneSandbox { public static class LandmarkClutter { public static void Tune(UnityEngine.GameObject a,Site s){} public static void Build(UnityEngine.Transform t){} } }'
write(temp / 'RuntimeServices.cs', services)

storage = (root / 'Tools/DistantForest/RenderStorage.cs').read_text()
storage = storage.replace('public Vector3 position, scale;', '''public Vector3 position, scale;
        public float m11;
        public Matrix4x4 inverse=>new Matrix4x4 { position=new Vector3(-position.x/scale.x,-position.y/scale.y,-position.z/scale.z),scale=new Vector3(1/scale.x,1/scale.y,1/scale.z) };
        public Vector3 MultiplyVector(Vector3 v)=>new Vector3(v.x*scale.x,v.y*scale.y,v.z*scale.z);''')
storage = storage.replace('public Bounds box;', 'public Bounds box; public Vector3 normal; public float offset;')
storage = storage.replace('{ box = default; }', '{ this=default; }')
start = storage.index('public static void CalculateFrustumPlanes(')
end = storage.index('\n    public enum FilterMode', start)
storage = storage[:start] + '''public static void CalculateFrustumPlanes(Camera c, Plane[] p)
        {
            Vector3 f=c.transform.forward, r=Vector3.Cross(Vector3.up,f).normalized, u=Vector3.Cross(f,r);
            float tan=1/c.projectionMatrix.m11;
            Vector3[] n={f,-f,f*tan*c.aspect+r,f*tan*c.aspect-r,f*tan+u,f*tan-u};
            for(int i=0;i<6;i++){p[i].normal=n[i];p[i].offset=-Vector3.Dot(n[i],c.transform.position)+(i==1?c.reach:0);}
        }
        public static bool TestPlanesAABB(Plane[] p, Bounds b)
        {
            foreach(var v in p){var n=v.normal;var e=b.extents;float radius=Math.Abs(n.x)*e.x+Math.Abs(n.y)*e.y+Math.Abs(n.z)*e.z;
                if(Vector3.Dot(n,b.center)+v.offset+radius<0)return false;} return true;
        }
    }
''' + storage[end:]
storage = storage.replace('public float reach = 4000f;', '''public float reach = 10000f;
        public bool orthographic; public int pixelHeight=900; public float aspect=1600f/900f;
        public Matrix4x4 projectionMatrix=>new Matrix4x4 { m11=1f/(float)Math.Tan(45f*Math.PI/360) };''')
storage = storage.replace('public bool HasProperty(int id)', 'public void SetTexture(string name,Texture2D value){} public bool HasProperty(int id)')
storage = storage.replace('if (typeof(T) == typeof(Texture2D))', 'if(path.StartsWith("Materials/Forest/")&&path.EndsWith(".001"))return default; if (typeof(T) == typeof(Texture2D))')
storage = storage.replace('int triangles = tier == 2 ? 8 : tier == 1 || path.Contains("Reduced") ? 3000 : 23000;', '''int species=path.Contains("Birch")?1:path.Contains("Spruce")?2:path.Contains("Pine")?3:0;
                    int triangles = tier == 2 ? 8 : tier == 1 || path.Contains("Reduced") ? 3000 : new[]{10282,6780,23134,7410}[species];''')
write(temp / 'ProjectionStorage.cs', storage)
runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/CanopyRelief/Harness.cs']
if phase != 'before':
    subprocess.run([sys.executable, '-I', str(root / 'Tools/CanopyRelief/ground_patch.py')], check=True)
    runtime = [temp / 'Ground.patched.cs' if p.name == 'Ground.cs' else p for p in runtime]
if phase == 'before':
    baseline = temp / 'baseline'; baseline.mkdir(exist_ok=True)
    for name in ['ForestPattern', 'ForestPalette', 'SectorForest', 'ForestBudget', 'FarScatter', 'ForestHlod', 'ForestMaterials']:
        text = subprocess.check_output(['git', '-c', 'safe.directory=' + str(root).replace('\\', '/'), 'show', '0542d98:Assets/DroneSandbox/Scripts/' + name + '.cs'], text=True)
        write(baseline / (name + '.cs'), text)
        runtime = [baseline / p.name if p.stem == name else p for p in runtime]
includes = ''.join('<Compile Include="' + str(p) + '" />' for p in runtime)
write(temp / 'above.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><DefineConstants>' + ('CANDIDATE;POLISH' if phase != 'before' else 'CANDIDATE') + '</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>')
subprocess.run([str(dotnet), 'build', str(temp / 'above.csproj'), '-c', 'Release', '--nologo', '--configfile', str(temp / 'NuGet.Config')], env=env, check=True)
subprocess.run([str(dotnet), str(temp / 'bin/Release/net8.0/above.dll'), str(temp / (phase + '-' + sector)), sector], env=env, check=True)
import runpy
png = runpy.run_path(str(root / 'Tools/ForestAbove/make_detail.py'))['png']
for path in temp.glob(phase + '-' + sector + '*/canopy-*.ppm'):
    header, dimensions, maximum, pixels = path.read_bytes().split(b'\n', 3)
    png(path.with_suffix('.png'), int(dimensions.split()[0]), pixels)
