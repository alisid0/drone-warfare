"""Run sequential scene exports and optional one-at-a-time Blender reviews."""
from pathlib import Path
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DOTNET = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
BLENDER = Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
env = dict(os.environ, DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0', MSBUILDDISABLENODEREUSE='1')
for phase in ('before', 'after'):
    subprocess.run([sys.executable, '-I', str(HERE / 'prepare.py'), phase], cwd=ROOT, env=env, check=True)
    for scene in ('Varrow', 'Bend', 'Reservoir', 'Far'):
        output = ROOT / 'Temp/water/review' / phase / scene
        output.mkdir(parents=True, exist_ok=True)
        result = subprocess.run([str(DOTNET), str(ROOT / 'Temp/water' / phase / 'bin/Release/net8.0/harness.dll'), str(output), scene],
                                cwd=ROOT, env=env, check=True, capture_output=True, text=True)
        print(phase, result.stdout, flush=True)
        log = HERE / 'Review' / (scene + '_' + phase + '.log')
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_bytes(result.stdout.replace('\r\n', '\n').replace('\n', '\r\n').encode())
        if '--renders' in sys.argv:
            subprocess.run([str(BLENDER), '--background', '--factory-startup', '--python-exit-code', '1', '--python', str(HERE / 'render.py'), '--', phase, scene],
                           cwd=ROOT, env=env, check=True)
