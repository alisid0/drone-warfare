"""Compile the terrain-colour sources as C# 9 against the installed Unity 6 modules, then build the review harness.

Never launches Unity. Reuses the UsedLook scaffold (and through it PlacesUpClose, Water and StreamingPerf). The harness
compiles the real Ground splat and Farmland (field meshes, tints, detail maps, margins) over a few extra engine
substitutes; --before builds the same harness from the d3778ed sources for before/after pictures and timings.
"""
from pathlib import Path
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BEFORE = '--before' in sys.argv
out = ROOT / ('Temp/terrain-colour/build-before' if BEFORE else 'Temp/terrain-colour/build')
out.mkdir(parents=True, exist_ok=True)
base = HERE.parent / 'UsedLook/prepare.py'
setup = base.read_text().split('\nenv = dict(')[0]
setup = setup.replace("ROOT / ('Temp/used-look/build-before' if BEFORE else 'Temp/used-look/build')", 'Path(' + repr(str(out)) + ')')
setup = setup.replace("'fe0df56:'", "'d3778ed:'").replace("'fe0df56'", "'d3778ed'")
assert 'd3778ed' in setup and 'terrain-colour' in setup
scope = {'__file__': str(base)}
exec(compile(setup, str(base), 'exec'), scope)
files, scripts, unity, dotnet, write, body = [scope[k] for k in ('files', 'scripts', 'unity', 'dotnet', 'write', 'body')]
if (scripts / 'FieldPalette.cs').exists():
    files.append(scripts / 'FieldPalette.cs')
files = list(dict.fromkeys(files))


def patch(name, pairs):
    """Apply idempotent text substitutions to a generated scaffold file."""
    path = out / name
    text = path.read_text()
    for old, new in pairs:
        if new in text:
            continue
        assert old in text, (name, old)
        text = text.replace(old, new, 1)
    write(path, text)


# Engine substitutes for Farmland's materials and detail maps, the captured splat and a renderer registry for pictures.
patch('Engine.cs', [
    ('public void SetAlphamaps(int x,int z,float[,,] h) { }', 'public float[,,] alphamaps; public void SetAlphamaps(int x,int z,float[,,] h) { if (x == 0 && z == 0) alphamaps = h; }'),
    ('public class MeshRenderer : Renderer { }', 'public class MeshRenderer : Renderer { public static readonly List<MeshRenderer> All = new List<MeshRenderer>(); public MeshRenderer() { All.Add(this); } }'),
    ('public Material(Shader s){} public void SetFloat(string n, float v) { } }',
     'public Material(Shader s){} public Material(Material m){ color = m.color; } public Texture2D mainTexture; '
     'public readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(); public readonly Dictionary<string, Vector2> scales = new Dictionary<string, Vector2>(), offsets = new Dictionary<string, Vector2>(); '
     'public void SetTexture(string n, Texture2D t) { textures[n] = t; } public void SetTextureScale(string n, Vector2 s) { scales[n] = s; } public void SetTextureOffset(string n, Vector2 o) { offsets[n] = o; } '
     'public void SetFloat(string n, float v) { } }'),
    ('public TextureWrapMode wrapMode; public int anisoLevel;', 'public TextureWrapMode wrapMode; public int anisoLevel; public FilterMode filterMode; public int width, height; public byte[] data;'),
    ('public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false) { }', 'public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false) { width = w; height = h; }'),
    ('public void SetPixelData(byte[] p,int mip) { }', 'public void SetPixelData(byte[] p,int mip) { data = p; }'),
    ('public enum TextureWrapMode { Clamp, Repeat }', 'public enum TextureWrapMode { Clamp, Repeat } public enum FilterMode { Point, Bilinear, Trilinear }'),
    ('public enum ShadowCastingMode { Off }', 'public enum ShadowCastingMode { Off, On }'),
    ('public void RecalculateBounds() { }\n    }', 'public void RecalculateBounds() { }\n        public Bounds bounds => new Bounds();\n    }\n    public struct Bounds { public Vector3 center; }'),
    ('public class LODGroup : Component { public float size=100f;', 'public class LODGroup : Component { public Vector3 localReferencePoint; public float size=100f;'),
])
patch('ConsumerMath.cs', [
    ('        public static float PerlinNoise(float x, float y)',
     '        public static float LinearToGammaSpace(float x) => x <= .0031308f ? 12.92f * x : 1.055f * (float)Math.Pow(x, 1 / 2.4) - .055f;\n        public static float PerlinNoise(float x, float y)'),
])
services = (out / 'Services.cs').read_text()
if 'public static class Farmland' in services:
    services = services.replace(body(services, '    public static class Farmland'), '')
if 'class Roadside' not in services:
    services += '\nnamespace DroneSandbox { public sealed class Roadside { public static void Build(UnityEngine.Transform t, ref int allowance) { } } }\n'
write(out / 'ReviewServices.cs', services)

env = dict(os.environ, DOTNET_CLI_HOME=str(out / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0',
           MSBUILDDISABLENODEREUSE='1')
modes = ('api',) if '--api' in sys.argv else ('harness',) if '--harness' in sys.argv else ('api', 'harness')
for mode in modes:
    sources = list(files)
    refs = ''
    if mode == 'api':
        api = (out / 'Services.cs').read_text()
        if 'public static class Farmland' in api:
            api = api.replace(body(api, '    public static class Farmland'), '')
        api = api.replace('public void Clear(){} public static bool InSector', 'public const float HouseReach=1500f; public void House(SettlementPattern.House h){} public static float GroundAt(Vector2 p)=>0f; public void Clear(){} public static bool InSector')
        write(out / 'ApiServices.cs', api)
        sources = [out / 'ApiServices.cs' if p.name == 'Services.cs' else p for p in sources]
        sources += [scripts / n for n in ('Farmland.cs', 'SettlementHlod.cs', 'Roadside.cs', 'LandmarkClutter.cs', 'LandmarkHlod.cs', 'LandmarkInstances.cs', 'LandmarkLod.cs',
                                          'FarDetailEdge.cs', 'FarChunkUpload.cs', 'FarDetailChunks.cs')]
        for m in ('CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule'):
            refs += f'<Reference Include="UnityEngine.{m}"><HintPath>{unity / ("Managed/UnityEngine/UnityEngine." + m + ".dll")}</HintPath><Private>false</Private></Reference>'
    else:
        sources = [out / 'ReviewServices.cs' if p.name == 'Services.cs' else p for p in sources]
        sources += [scripts / 'Farmland.cs'] + [out / n for n in ('ConsumerMath.cs', 'Engine.cs', 'ClutterStub.cs')] + [HERE / 'Review.cs']
    sources = list(dict.fromkeys(sources))
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / (mode + '.csproj'), f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Library" if mode == "api" else "Exe"}</OutputType><TargetFramework>{"netstandard2.1" if mode == "api" else "net8.0"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>{"<DefineConstants>BEFORE</DefineConstants>" if BEFORE else ""}</PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(out / (mode + '.csproj')), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)
