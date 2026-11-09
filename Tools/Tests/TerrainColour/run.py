"""Run one review scene at a time (two worker threads), then convert the PPM pictures to PNG."""
from pathlib import Path
import os
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DOTNET = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
env = dict(os.environ, DOTNET_PROCESSOR_COUNT='2', DOTNET_TieredCompilation='0', MSBUILDDISABLENODEREUSE='1', DOTNET_gcServer='0')
phase = next((a[8:] for a in sys.argv if a.startswith('--phase=')), 'after')
out = ROOT / 'Temp/terrain-colour/review' / phase
out.mkdir(parents=True, exist_ok=True)
owners = [a for a in sys.argv[1:] if not a.startswith('--')] or ['4']
build = 'build-before' if phase == 'before' else 'build'
for owner in owners:
    command = [str(DOTNET), str(ROOT / 'Temp/terrain-colour' / build / 'bin/Release/net8.0/harness.dll'), owner, str(out)]
    if '--render' in sys.argv:
        command.append('render')
    r = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
    print(r.stdout + r.stderr, flush=True)
    (out / ('sector-' + owner + '.log')).write_text(r.stdout + r.stderr)
    r.check_returncode()
try:
    from PIL import Image
    for p in out.glob('*.ppm'):
        Image.open(p).save(p.with_suffix('.png'))
        p.unlink()
except ImportError:
    print('PIL missing; PPM pictures kept')
