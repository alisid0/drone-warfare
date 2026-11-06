"""Compile the game's runtime scripts against Unity, or replay the streaming scheduler offline; never start Unity.

  py -3.13 -I Tools/PredictiveStreaming/run.py api [rev]   compile every runtime script (working tree, or a git revision)
  py -3.13 -I Tools/PredictiveStreaming/run.py sim         fly the hd167 route through the before and after schedulers

The api build references the installed Unity 6000.6.7f1 engine modules and copies of the package assemblies
(HDRP, Core RP, Input System, UI) from the main checkout's Library, C# 9, warnings as errors.
The sim build compiles FarDetailRoute.cs (the production route planner) with Simulation.cs and small math stubs.
Generated projects go under Temp/predictive-streaming. Run one at a time; each build uses a single node.
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
here = Path(__file__).resolve().parent
scripts = root / 'Assets/DroneSandbox/Scripts'
editor = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data')
dotnet = editor / 'DotNetSdk/dotnet.exe'
library = Path('C:/Users/ali10/Documents/DroneWarfare/Library/ScriptAssemblies')  # read only: package assemblies
temp = root / 'Temp/predictive-streaming'
temp.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, DOTNET_CLI_HOME=str(temp / 'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1',
           DOTNET_GENERATE_ASPNET_CERTIFICATE='false', MSBUILDDISABLENODEREUSE='1', DOTNET_TieredCompilation='0')
(temp / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
packages = ['Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Core.Runtime.Shared', 'Unity.RenderPipelines.HighDefinition.Runtime',
            'Unity.RenderPipelines.HighDefinition.Config.Runtime', 'Unity.RenderPipelines.GPUDriven.Runtime', 'Unity.InputSystem',
            'UnityEngine.UI', 'Unity.TextMeshPro', 'Unity.Collections', 'Unity.Burst', 'Unity.InternalAPIEngineBridge.RenderPipelines.Core.Runtime.Shared']


def build(name, sources, references, framework, executable=False, defines=''):
    """One serial build; prints the distinct compiler errors and stops on failure."""
    includes = ''.join('<Compile Include="' + str(p) + '" />' for p in sources)
    refs = ''.join('<Reference Include="' + p.stem + '"><HintPath>' + str(p) + '</HintPath><Private>false</Private></Reference>' for p in references)
    project = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>' + ('Exe' if executable else 'Library') +
               '</OutputType><TargetFramework>' + framework + '</TargetFramework><LangVersion>9</LangVersion><Nullable>disable</Nullable>'
               '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><Optimize>true</Optimize><TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
               '<NoWarn>CS0649;CS0414;CS0169;CS0067;CS0618;CS0162;CS0219;CS0168</NoWarn><DefineConstants>' + defines + '</DefineConstants>'
               '<UseSharedCompilation>false</UseSharedCompilation><AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>'
               '<OutputPath>' + str(temp / ('bin-' + name)) + '</OutputPath>'
               '</PropertyGroup><ItemGroup>' + includes + refs + '</ItemGroup></Project>')
    folder = temp / name  # one folder per project keeps their restore outputs apart
    folder.mkdir(exist_ok=True)
    (folder / (name + '.csproj')).write_text(project)
    result = subprocess.run([str(dotnet), 'build', str(folder / (name + '.csproj')), '-c', 'Release', '--nologo', '-m:1',
                             '--configfile', str(temp / 'NuGet.Config')], env=env, capture_output=True, text=True, cwd=folder)
    (temp / (name + '-build.log')).write_text(result.stdout + result.stderr)
    warnings = list(dict.fromkeys(line.strip() for line in result.stdout.splitlines() if ': error ' in line or ': warning ' in line))
    if result.returncode or warnings:
        print('\n'.join(warnings))
        raise SystemExit(name + ': compile failed')
    print(name + ': compile passed (zero warnings/errors)', flush=True)


mode = sys.argv[1] if len(sys.argv) > 1 else 'api'
if mode == 'api':
    revision = sys.argv[2] if len(sys.argv) > 2 else None
    refs = temp / 'refs'
    refs.mkdir(exist_ok=True)
    for name in packages:
        source = library / (name + '.dll')
        if source.exists() and not (refs / source.name).exists(): shutil.copy2(source, refs / source.name)
    references = sorted((editor / 'Managed/UnityEngine').glob('UnityEngine*.dll')) + sorted(refs.glob('*.dll'))
    sources = sorted(scripts.glob('*.cs'))
    if revision:
        # Reproduce a committed revision's scripts exactly, for a baseline against the same references.
        copy = temp / ('scripts-' + revision)
        if copy.exists(): shutil.rmtree(copy)
        copy.mkdir()
        listing = subprocess.check_output(['git', 'ls-tree', '--name-only', revision, 'Assets/DroneSandbox/Scripts/'], cwd=root, text=True)
        sources = []
        for path in listing.split():
            if not path.endswith('.cs'): continue
            target = copy / Path(path).name
            target.write_bytes(subprocess.check_output(['git', 'show', revision + ':' + path], cwd=root))
            sources.append(target)
    build('api' + ('-' + revision if revision else ''), sources, references, 'netstandard2.1', defines='ENABLE_INPUT_SYSTEM;UNITY_6000_0_OR_NEWER')
elif mode == 'sim':
    sources = [here / 'MathStubs.cs', scripts / 'FarDetailRoute.cs', here / 'Simulation.cs']
    build('sim', sources, [], 'net8.0', executable=True)
    result = subprocess.run([str(dotnet), str(temp / 'bin-sim/sim.dll')] + sys.argv[2:], env=env, capture_output=True, text=True)
    (temp / 'sim.log').write_text(result.stdout + result.stderr)
    print(result.stdout + result.stderr)
    result.check_returncode()
else:
    raise SystemExit(__doc__)
