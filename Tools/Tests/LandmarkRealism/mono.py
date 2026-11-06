"""Compile the same production painter against Unity's Mono profile and run its JIT without the editor."""
from pathlib import Path
import os
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[3]
here = Path(__file__).resolve().parent
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
out = root / 'Temp/landmark-realism' / phase
editor = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data')
sources = [p for p in re.findall(r'<Compile Include="([^"]+)"', (out / 'harness.csproj').read_text())
           if Path(p).name not in ('Harness.cs', 'PictureBench.cs', 'YardChecks.cs')]
sources.append(str(here / 'PictureMono.cs'))
refs = [editor / 'MonoBleedingEdge/lib/mono/4.8-api' / n for n in ('mscorlib.dll', 'System.dll', 'System.Core.dll')]
rsp = out / 'mono.rsp'
rsp.write_text('\n'.join(['/nologo', '/nostdlib+', '/optimize+', '/langversion:9', '/target:exe', '/main:PictureMono', f'/out:"{out / "picture-mono.exe"}"'] + [f'/r:"{p}"' for p in refs] + [f'"{p}"' for p in sources]))
compiler = editor / 'DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll'
subprocess.run([str(editor / 'DotNetSdk/dotnet.exe'), str(compiler), '/noconfig', '@' + str(rsp)], check=True)
env = dict(os.environ, MONO_THREADS_PER_CPU='1')
subprocess.run([str(editor / 'MonoBleedingEdge/bin/mono.exe'), str(out / 'picture-mono.exe'), str(out / 'results/picture-mono.json'), *(sys.argv[2:] or ['3'])], env=env, check=True)
