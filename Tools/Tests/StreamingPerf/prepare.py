"""Prepare small, sequential data benchmarks and compile consumers against the installed Unity assemblies."""
from pathlib import Path
import os
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
out = ROOT / 'Temp/streaming-perf' / phase
out.mkdir(parents=True, exist_ok=True)
base = HERE.parent / 'LandmarkRealism/prepare.py'
source = base.read_text().split("for mode in (('harness',)")[0]
source = source.replace("phase = sys.argv[1] if len(sys.argv) > 1 else 'after'", "phase = 'after'", 1)
source = source.replace("out = ROOT / 'Temp/landmark-realism' / phase", "out = Path(" + repr(str(out)) + ")", 1)
scope = {'__file__': str(base)}
exec(compile(source, str(base), 'exec'), scope)
files, scripts, unity, dotnet, write = [scope[k] for k in ('files', 'scripts', 'unity', 'dotnet', 'write')]
files += list(scripts.glob('WorldLandmarkLayout*.cs')) + list(scripts.glob('Village*.cs'))
if phase != 'before':
    files += [scripts / 'FarDetailMips.cs']
services = (out / 'Services.cs').read_text() + '''
namespace DroneSandbox {
public static class SectorForest { public static void Build(System.Random r,System.Collections.Generic.List<UnityEngine.Vector2> w){} }
public class FarScatter : UnityEngine.Component { public void Clear(){} public static bool InSector(UnityEngine.Vector2 p)=>false; }
}
'''
write(out / 'Services.cs', services)
engine = re.sub(r'public struct Color\s*\{', 'public struct Color { public Color gamma=>this;', (out / 'Engine.cs').read_text())
engine += '\nnamespace UnityEngine { public struct LOD { public LOD(float h,Renderer[] r){} } public class LODGroup : Component { public float size=100f; public void SetLODs(LOD[] l){} public void RecalculateBounds(){} } }\n'
write(out / 'Engine.cs', engine)
write(out / 'ClutterStub.cs', 'namespace DroneSandbox { public static class LandmarkClutter { public static void Build(UnityEngine.Transform t) {} public static void Tune(UnityEngine.GameObject p,Site s) {} } }')
env = dict(os.environ, DOTNET_CLI_HOME=str(out / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0', MSBUILDDISABLENODEREUSE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
for mode in ('harness', 'api'):
    sources = list(files)
    if mode == 'harness':
        sources += [out / n for n in ('ConsumerMath.cs', 'Engine.cs', 'ClutterStub.cs')]
        sources += [HERE / 'Harness.cs', scripts / 'FarDetailRegionData.cs']
    else:
        sources = [p for p in sources if p.name not in ('DetailStub.cs', 'Services.cs')]
        sources += list(scripts.glob('FarDetail*.cs'))
        sources += [scripts / n for n in ('FarTerrain.cs', 'LandmarkClutter.cs', 'LandmarkHlod.cs', 'SettlementPattern.cs')]
        sources += list(scripts.glob('FarWorld*.cs'))
        services = (out / 'Services.cs').read_text()
        a = services.index('    public static class FarTerrain')
        b = services.index('    public static class Backdrop', a)
        services = services[:a] + services[b:]
        services += '''
namespace DroneSandbox {
public class Drone : UnityEngine.MonoBehaviour { public bool IsAlive=>true; }
public class DroneFeed { public static DroneFeed Instance; public Drone Source; }
}
'''
        write(out / 'ApiServices.cs', services)
        sources += [out / 'ApiServices.cs']
    sources = list(dict.fromkeys(sources))
    if phase == 'before':
        for i, path in enumerate(sources):
            if scripts not in path.parents:
                continue
            dest = out / (path.stem + '.before.cs')
            dest.write_bytes(subprocess.check_output(['git', '-c', 'safe.directory=' + ROOT.as_posix(), 'show',
                             '6611380:' + path.relative_to(ROOT).as_posix()], cwd=ROOT))
            sources[i] = dest
    refs = ''
    if mode == 'api':
        for module in ('CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule', 'IMGUIModule'):
            refs += f'<Reference Include="UnityEngine.{module}"><HintPath>{unity / ("Managed/UnityEngine/UnityEngine." + module + ".dll")}</HintPath></Reference>'
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / (mode + '.csproj'), f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Exe" if mode == "harness" else "Library"}</OutputType><TargetFramework>{"net8.0" if mode == "harness" else "netstandard2.1"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation><DefineConstants>{"BEFORE" if phase == "before" else "AFTER"}</DefineConstants></PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(out / (mode + '.csproj')), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)

if '--run' in sys.argv:
    subprocess.run([str(dotnet), str(out / 'bin/Release/net8.0/harness.dll')], env=env, check=True)

if phase != 'before':
    sources = [HERE / 'Lifecycle.cs'] + [scripts / n for n in ('FarDetailRegional.cs', 'FarWorldPicture.cs', 'FarDetailTiming.cs')]
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / 'lifecycle.csproj', f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(out / 'lifecycle.csproj'), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)
    if '--run' in sys.argv:
        subprocess.run([str(dotnet), str(out / 'bin/Release/net8.0/lifecycle.dll')], env=env, check=True)
