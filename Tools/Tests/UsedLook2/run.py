"""Run one managed check scene at a time with two worker threads, then convert any PPM pictures to PNG."""
from pathlib import Path
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DOTNET = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
env = dict(os.environ, DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0', MSBUILDDISABLENODEREUSE='1')
phase = next((a[8:] for a in sys.argv if a.startswith('--phase=')), 'after')
out = ROOT / 'Temp/used-look-2/review' / phase
out.mkdir(parents=True, exist_ok=True)
scenes = [a for a in sys.argv[1:] if not a.startswith('--')] or ['Checks']
for scene in scenes:
    build = 'build-before' if phase == 'before' else 'build'
    command = [str(DOTNET), str(ROOT / 'Temp/used-look-2' / build / 'bin/Release/net8.0/harness.dll'), scene, str(out)]
    if '--render' in sys.argv:
        command.append('render')
    r = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
    print(r.stdout + r.stderr, flush=True)
    (out / (scene + '.log')).write_text(r.stdout + r.stderr)
    r.check_returncode()
try:
    from PIL import Image
    for p in out.glob('*.ppm'):
        Image.open(p).save(p.with_suffix('.png'))
        p.unlink()
except ImportError:
    print('PIL missing; PPM pictures kept')
