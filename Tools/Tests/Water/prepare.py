"""Compile C# 9 against installed Unity modules, then make a bounded, source-data review harness."""
from pathlib import Path
import os
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
out = ROOT / 'Temp/water' / phase
out.mkdir(parents=True, exist_ok=True)


def write(path, text):
    """Normalize review scaffolding without changing the production files."""
    path.write_bytes(text.replace('\r\n', '\n').replace('\n', '\r\n').encode())


def body(text, anchor):
    """Copy a full method with balanced braces, so the review uses production equations."""
    start = text.index(anchor)
    brace = text.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        if text[end] == '{': depth += 1
        if text[end] == '}': depth -= 1
        end += 1
    return text[start:end]


# Reuse the repository's consumer scaffold only; no world generation or Unity import occurs in preparation.
base = HERE.parent / 'StreamingPerf/prepare.py'
setup = base.read_text().split('\nenv = dict(')[0]
setup = setup.replace("phase = sys.argv[1] if len(sys.argv) > 1 else 'after'", "phase = 'after'", 1)
setup = setup.replace("out = ROOT / 'Temp/streaming-perf' / phase", 'out = Path(' + repr(str(out)) + ')', 1)
scope = {'__file__': str(base)}
exec(compile(setup, str(base), 'exec'), scope)
files, scripts, unity, dotnet = [scope[k] for k in ('files', 'scripts', 'unity', 'dotnet')]
files += [scripts / n for n in ('WaterAppearance.cs', 'WatercourseProfile.cs', 'FarDetailWater.cs', 'FarDetailRegionData.cs')]

hdrp = unity / 'Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition'
surface = (hdrp / 'Runtime/Water/WaterSurface/WaterSurface.cs').read_text()
simulation = (hdrp / 'Runtime/Water/WaterSurface/WaterSurface.Simulation.cs').read_text()
added_floats = ['ambientScattering', 'heightScattering', 'ripplesFadeStart', 'ripplesFadeDistance',
                'tessellationFactorFadeStart', 'tessellationFactorFadeRange']
for name in added_floats:
    assert re.search(r'public float ' + name + r'\s*=', surface + simulation), name
assert 'public bool caustics = true;' in surface
assert 'public FadeMode ripplesFadeMode' in simulation
assert 'Custom' in body(simulation, 'public enum FadeMode')
stub = (ROOT / 'Tools/WorldSectors/HdrpStubs.cs').read_text()
stub = stub.replace('public class WaterSurface : Component\n    {', '''public class WaterSurface : Component
    {
        public enum FadeMode { None, Automatic, Custom }
        public FadeMode ripplesFadeMode;
        public bool caustics;
        public float ''' + ', '.join(added_floats) + ';')
stub = stub.replace('public enum FadeMode', '''public static WaterSurface Last;
        public WaterSurface() { Last = this; ambientScattering = .1f; startSmoothness = .95f; }
        public enum FadeMode''')
write(out / 'HdrpContract.cs', stub)
files = [out / 'HdrpContract.cs' if p.name == 'HdrpStubs.cs' else p for p in files]

# The terrain patch is reviewed separately, and compiled here without changing the read-only original.
subprocess.run([sys.executable, '-I', str(HERE / 'ground_patch.py')], check=True)
for i, p in enumerate(files):
    if p.name == 'Ground.cs' and phase == 'after': files[i] = ROOT / 'Temp/water/Ground.patched.cs'
    elif phase == 'before' and scripts in p.parents and p.name not in ('WaterAppearance.cs', 'WatercourseProfile.cs', 'FarDetailWater.cs'):
        dest = out / p.name
        dest.write_bytes(subprocess.check_output(['git', '-c', 'safe.directory=' + ROOT.as_posix(), 'show',
                         'da49170:' + p.relative_to(ROOT).as_posix()], cwd=ROOT))
        files[i] = dest
if phase == 'before': files = [p for p in files if p.name != 'FarDetailWater.cs']
files = list(dict.fromkeys(files))

# The managed review samples the real splat cell body on demand, never allocates a 2048-square splat array.
ground = next(p for p in files if p.name in ('Ground.cs', 'Ground.patched.cs'))
text = ground.read_text()
splat = body(text, '        static float[,,] BuildSplat(TerrainData data)')
sample = splat[splat.index('                    float steep ='):splat.index('\n                }\n            });')]
sample = re.sub(r'splat\[zi, xi, (\d)\] = ', r'w[\1] = ', sample)
sample = sample.replace('splat[zi, xi, layer]', 'w[layer]')
sample = sample.replace('                    ', '            ')
preamble = '''
            var river = MapLayout.RiverField;
            var roads = MapLayout.RoadField;
            var tracks = MapLayout.TrackField;
            var junctions = MapLayout.Junctions;
            float hw = MapLayout.RiverHalfWidth;
            float[] w = new float[8];
'''
review = '''        // Review-only point sampler, extracted automatically from the production splat body.
        public static Color32 ReviewColour(float x, float z)
        {''' + preamble + sample + '''
            Vector3 colour = Vector3.zero;
            for (int layer = 0; layer < 8; layer++) colour += (layer < 5 ? FarDetailPalette.Means[layer]
                : layer == 5 ? LitterMean : layer == 6 ? WaterAppearance.WetMud : WaterAppearance.Gravel) * w[layer];
            return FarDetailPalette.Encode(colour);
        }

        // Full geometry, with splat storage omitted for the bounded render export.
        static float[,,] BuildSplat(TerrainData data) => new float[1, 1, 8];'''
write(out / 'Ground.review.cs', text.replace(splat, review))

env = dict(os.environ, DOTNET_CLI_HOME=str(out / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0', MSBUILDDISABLENODEREUSE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
for mode in ('api', 'harness'):
    sources = list(files)
    refs = ''
    if mode == 'api':
        sources += [scripts / 'LandmarkClutter.cs', scripts / 'LandmarkHlod.cs']
        if phase == 'after':
            sources += [scripts / 'FarDetailEdge.cs', ROOT / 'Temp/water/Grass.patched.cs']
            services = (out / 'Services.cs').read_text().replace('public static class Grass { public static void Plant(Terrain t) { } }', '')
            services += '\nnamespace DroneSandbox { public static class Atmosphere { public static float GrassDistance = 180f, GrassDensity = 1f; } }\n'
            write(out / 'ApiServices.cs', services)
            sources = [out / 'ApiServices.cs' if p.name == 'Services.cs' else p for p in sources]
        for module in ('CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule'):
            refs += f'<Reference Include="UnityEngine.{module}"><HintPath>{unity / ("Managed/UnityEngine/UnityEngine." + module + ".dll")}</HintPath><Private>false</Private></Reference>'
    else:
        sources = [out / 'Ground.review.cs' if p == ground else p for p in sources]
        sources += [out / n for n in ('ConsumerMath.cs', 'Engine.cs', 'ClutterStub.cs')]
        sources += [HERE / 'Harness.cs']
    includes = ''.join(f'<Compile Include="{p}" />' for p in sources)
    write(out / (mode + '.csproj'), f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>{"Exe" if mode == "harness" else "Library"}</OutputType><TargetFramework>{"net8.0" if mode == "harness" else "netstandard2.1"}</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation><DefineConstants>{phase.upper()}</DefineConstants></PropertyGroup><ItemGroup>{includes}{refs}</ItemGroup></Project>')
    subprocess.run([str(dotnet), 'build', str(out / (mode + '.csproj')), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(out / 'NuGet.Config')], env=env, check=True)
print('HDRP additions verified against installed 17.6 source; API build uses real Unity modules and a source-checked HDRP contract.')
