"""Compile the regional-realism sources as C# 9 against the installed Unity 6 modules, then build the review harness.

Never launches Unity. Reuses the UsedLook scaffold (and through it PlacesUpClose, Water and StreamingPerf), as UsedLook2
does, adding FieldPalette.cs; --before builds the same harness from the 5fb5060 sources for before/after pictures.
"""
from pathlib import Path
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BEFORE = '--before' in sys.argv
out = ROOT / ('Temp/regional-realism/build-before' if BEFORE else 'Temp/regional-realism/build')
out.mkdir(parents=True, exist_ok=True)
base = HERE.parent / 'UsedLook/prepare.py'
setup = base.read_text().split('\nenv = dict(')[0]
setup = setup.replace("ROOT / ('Temp/used-look/build-before' if BEFORE else 'Temp/used-look/build')", 'Path(' + repr(str(out)) + ')')
setup = setup.replace("'fe0df56:'", "'5fb5060:'").replace("'fe0df56'", "'5fb5060'")
assert '5fb5060' in setup and 'regional-realism' in setup
scope = {'__file__': str(base)}
exec(compile(setup, str(base), 'exec'), scope)
files, scripts, unity, dotnet, write, body = [scope[k] for k in ('files', 'scripts', 'unity', 'dotnet', 'write', 'body')]
files.append(scripts / 'FieldPalette.cs')
files = list(dict.fromkeys(files))
if BEFORE:
    # The scaffold made Ground.review.cs from the working Ground.cs; the baseline needs it from the 5fb5060 copy.
    text = (out / 'src' / 'Ground.cs').read_text()
    text = text.replace(body(text, '        static float[,,] BuildSplat('), '        static float[,,] BuildSplat(TerrainData data) => new float[1,1,8];')
    text = text.replace(body(text, '        public static void FlushWear('), '        public static void FlushWear() { pending.Clear(); }')
    write(out / 'Ground.review.cs', text)
if '--list' in sys.argv:
    print(' '.join(sorted(p.name for p in files)))


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


# The sector builder makes the landmark surface palette texture and material.
patch('Engine.cs', [
    ('public Material(Shader s){} public void SetFloat(string n, float v) { } }', 'public Material(Shader s){} public Texture2D mainTexture; public void SetFloat(string n, float v) { } }'),
    ('public enum TextureWrapMode { Clamp, Repeat }', 'public enum TextureWrapMode { Clamp, Repeat } public enum FilterMode { Point, Bilinear, Trilinear }'),
    ('public TextureWrapMode wrapMode; public int anisoLevel;', 'public TextureWrapMode wrapMode; public int anisoLevel; public FilterMode filterMode;'),
])

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
        sources = [out / 'Ground.review.cs' if p.name == 'Ground.cs' else p for p in sources]
        sources += [out / n for n in ('ConsumerMath.cs', 'Engine.cs', 'ClutterStub.cs')] + [HERE / 'Review.cs']
    sources = list(dict.fromkeys(sources))
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / (mode + '.csproj'), f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Library" if mode == "api" else "Exe"}</OutputType><TargetFramework>{"netstandard2.1" if mode == "api" else "net8.0"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>{"<DefineConstants>BEFORE</DefineConstants>" if BEFORE else ""}</PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(out / (mode + '.csproj')), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)
