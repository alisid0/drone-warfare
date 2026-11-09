"""Small sequential forest timings and API checks; no Unity, images or renders."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
sector = sys.argv[2] if len(sys.argv) > 2 else 'varrow'
# Reuse the existing managed engine scaffolding, stopping before any build or run.
setup = (root / 'Tools/CanopyRelief/run.py').read_text()
setup = setup[:setup.index("runtime += [temp / 'ProjectionStorage.cs'")]
setup = setup.replace("Temp/canopy-relief", "Temp/woods-build")
exec(compile(setup, str(root / 'Tools/CanopyRelief/run.py'), 'exec'))
env['DOTNET_TieredCompilation'] = '0'
added = [p for p in scripts.glob('Village*.cs') if p not in files] + [scripts / 'FieldAppearance.cs']
files += added
runtime += added
engine = (temp / 'RuntimeEngine.cs').read_text()
engine = engine.replace('public Transform parent;', 'public Vector3 lossyScale => localScale; public Transform parent;')
engine = engine.replace('public struct LOD {', 'public struct LOD { public float screenRelativeTransitionHeight;')
engine = engine.replace('renderers=r;}', 'renderers=r;screenRelativeTransitionHeight=f;}')
write(temp / 'RuntimeEngine.cs', engine)
runtime += [temp / 'ProjectionStorage.cs', root / 'Tools/WoodsBuild/Harness.cs']
world = (scripts / 'World.cs').read_text().replace('SectorForest.Build(rng, woodland);', 'WoodsBuildHarness.BuildForest(rng, woodland);')
write(temp / 'World.cs', world)
runtime = [temp / 'World.cs' if p.name == 'World.cs' else p for p in runtime]
if phase == 'before':
    for name in ['SectorForest', 'ForestHlod']:
        source = subprocess.check_output(['git', '-c', 'safe.directory=' + root.as_posix(), 'show', 'cd8b1e6:Assets/DroneSandbox/Scripts/' + name + '.cs'], text=True)
        if name == 'SectorForest':
            source = source.replace('public static double BuildMilliseconds { get; private set; }', '''public static double BuildMilliseconds { get; private set; }
        public static double StemsMilliseconds { get; private set; }
        public static double IndicesMilliseconds { get; private set; }
        public static double CanopyMilliseconds { get; private set; }''')
            source = source.replace('            // Stable row order', '            StemsMilliseconds = timer.Elapsed.TotalMilliseconds;\n            // Stable row order')
            source = source.replace('            PreparedCanopy = PrepareCanopy();', '            IndicesMilliseconds = timer.Elapsed.TotalMilliseconds - StemsMilliseconds;\n            PreparedCanopy = PrepareCanopy();')
            source = source.replace('            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;', '            BuildMilliseconds = timer.Elapsed.TotalMilliseconds;\n            CanopyMilliseconds = BuildMilliseconds - StemsMilliseconds - IndicesMilliseconds;')
        write(temp / ('Before' + name + '.cs'), source)
        runtime = [temp / ('Before' + name + '.cs') if p.stem == name else p for p in runtime]

refs = ''.join('<Reference Include="UnityEngine.' + name + '"><HintPath>' + str(unity / ('Managed/UnityEngine/UnityEngine.' + name + '.dll')) + '</HintPath></Reference>'
               for name in ['CoreModule', 'TerrainModule', 'TerrainPhysicsModule', 'PhysicsModule', 'AnimationModule', 'IMGUIModule'])


def build(name, sources, references='', executable=False):
    """Use Unity's installed compiler and local SDK references with one build node."""
    includes = ''.join('<Compile Include="' + str(p) + '" />' for p in sources)
    write(temp / (name + '.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>' + ('Exe' if executable else 'Library') + '</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>9</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation></PropertyGroup><ItemGroup>' + includes + references + '</ItemGroup></Project>')
    result = subprocess.run([str(dotnet), 'build', str(temp / (name + '.csproj')), '-c', 'Release', '--nologo', '-m:1', '--configfile', str(temp / 'NuGet.Config')], env=env, capture_output=True, text=True)
    write(temp / (name + '-build.log'), result.stdout + result.stderr)
    if result.returncode:
        print('\n'.join(dict.fromkeys(line for line in result.stdout.splitlines() if ': error ' in line)))
        raise SystemExit(result.returncode)
    print(name + ': compile passed (zero warnings/errors)', flush=True)


if phase == 'api':
    api = list(files) + [scripts / (name + '.cs') for name in ['LandmarkClutter', 'WaterAppearance', 'FarDetailWater', 'FarDetailRegionData'] if scripts / (name + '.cs') not in files]
    hdrp = (temp / 'HdrpStubs.cs').read_text()
    write(temp / 'HdrpContract.cs', hdrp)
    api = [temp / 'HdrpContract.cs' if p.name == 'HdrpStubs.cs' else p for p in api]
    build('api', api, refs)
else:
    build('timing', runtime, executable=True)
    result = subprocess.run([str(dotnet), str(temp / 'bin/Release/net8.0/timing.dll'), phase, sector], env=env, capture_output=True, text=True)
    write(temp / (phase + '-' + sector + '.log'), result.stdout + result.stderr)
    print(result.stdout + result.stderr)
    result.check_returncode()
    before = temp / ('before-' + sector + '.log')
    if phase == 'after' and before.exists():
        expected = [line for line in before.read_text().splitlines() if line.startswith('signature ')]
        actual = [line for line in result.stdout.splitlines() if line.startswith('signature ')]
        assert actual == expected and actual, 'Baseline and candidate forest geometry differ'
        print('Baseline geometry match passed')
