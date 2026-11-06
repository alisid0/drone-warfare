"""Compile the used-look sources as C# 9 against the installed Unity 6 modules, then build the managed check harness.

Never launches Unity. Reuses the PlacesUpClose scaffold (and through it the Water/StreamingPerf substitutes),
adding the road files that Ground.cs and Farmland.cs now reference.
"""
from pathlib import Path
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BEFORE = '--before' in sys.argv  # the fe0df56 sources, for before/after timings and pictures
out = ROOT / ('Temp/used-look/build-before' if BEFORE else 'Temp/used-look/build')
out.mkdir(parents=True, exist_ok=True)
base = HERE.parent / 'PlacesUpClose/prepare.py'
setup = base.read_text().split('\nenv=dict(')[0]
setup = setup.replace("ROOT/'Temp/places-up-close/build'", 'Path(' + repr(str(out)) + ')')
scope = {'__file__': str(base)}
exec(compile(setup, str(base), 'exec'), scope)
files, scripts, unity, dotnet, write, body = [scope[k] for k in ('files', 'scripts', 'unity', 'dotnet', 'write', 'body')]
files += [p for p in scripts.glob('Road*.cs') if p.name != 'Roadside.cs']
files = list(dict.fromkeys(files))
if BEFORE:
    old = out / 'src'
    old.mkdir(exist_ok=True)
    listing = subprocess.check_output(['git', 'ls-tree', '--name-only', 'fe0df56', 'Assets/DroneSandbox/Scripts/'], cwd=ROOT, text=True).split()
    for name in listing:
        if name.endswith('.cs'):
            (old / Path(name).name).write_bytes(subprocess.check_output(['git', 'show', 'fe0df56:' + name], cwd=ROOT))
    files = [old / p.name if scripts in p.parents and (old / p.name).exists() else p for p in files if scripts not in p.parents or (old / p.name).exists()]
    scripts = old
engine = (out / 'Engine.cs').read_text()
if 'class MonoBehaviour' not in engine:
    engine += '\nnamespace UnityEngine { public class MonoBehaviour : Component {} public static class Time { public static float time; } public class Camera : Component {} public struct Matrix4x4 { public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s)=>new Matrix4x4(); } }\nnamespace UnityEngine.Rendering { public struct ScriptableRenderContext {} }\n'
    write(out / 'Engine.cs', engine)
if 'Quaternion operator *(Quaternion' not in engine:
    engine = engine.replace('    public struct Quaternion\n    {', '    public struct Quaternion\n    {\n        public static Quaternion operator *(Quaternion a, Quaternion b) => b;', 1)
    write(out / 'Engine.cs', engine)
# Forest code is out of scope; its newer members only need compile-time substitutes here.
services = (out / 'Services.cs').read_text()
if 'HasFloorCoverage' not in services:
    services = services.replace('public static class SectorForest { public static void Build(System.Random r,System.Collections.Generic.List<UnityEngine.Vector2> w){} }',
        'public static class SectorForest { public static void Build(System.Random r,System.Collections.Generic.List<UnityEngine.Vector2> w){} public static bool HasFloorCoverage(UnityEngine.Vector2 p)=>false; public static UnityEngine.Vector2 EdgeLean(UnityEngine.Vector2 w,float c,bool l)=>default; }')
    write(out / 'Services.cs', services)
env = dict(os.environ, DOTNET_CLI_HOME=str(out / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0',
           MSBUILDDISABLENODEREUSE='1')
modes = ('api',) if '--api' in sys.argv else ('harness',) if '--harness' in sys.argv else ('api', 'harness')
for mode in modes:
    sources = list(files)
    refs = ''
    if mode == 'api':
        services = (out / 'Services.cs').read_text()
        services = services.replace(body(services, '    public static class Farmland'), '')
        services = services.replace('public void Clear(){} public static bool InSector', 'public const float HouseReach=1500f; public void House(SettlementPattern.House h){} public static float GroundAt(Vector2 p)=>0f; public void Clear(){} public static bool InSector')
        write(out / 'ApiServices.cs', services)
        sources = [out / 'ApiServices.cs' if p.name == 'Services.cs' else p for p in sources]
        sources += [scripts / n for n in ('Farmland.cs', 'SettlementHlod.cs', 'Roadside.cs', 'LandmarkClutter.cs', 'LandmarkHlod.cs', 'LandmarkInstances.cs', 'LandmarkLod.cs',
                                          'FarDetailEdge.cs', 'FarChunkUpload.cs', 'FarDetailChunks.cs')]
        for m in ('CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule'):
            refs += f'<Reference Include="UnityEngine.{m}"><HintPath>{unity / ("Managed/UnityEngine/UnityEngine." + m + ".dll")}</HintPath><Private>false</Private></Reference>'
    else:
        sources = [out / 'Ground.review.cs' if p.name == 'Ground.cs' else p for p in sources]
        sources += [out / n for n in ('ConsumerMath.cs', 'Engine.cs', 'ClutterStub.cs')] + [HERE / 'Checks.cs']
    sources = list(dict.fromkeys(sources))
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / (mode + '.csproj'), f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Library" if mode == "api" else "Exe"}</OutputType><TargetFramework>{"netstandard2.1" if mode == "api" else "net8.0"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>{'<DefineConstants>BEFORE</DefineConstants>' if BEFORE else ''}</PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(out / (mode + '.csproj')), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)
