"""Render one modest Blender job at a time, with two CPU threads and worktree-local caches."""
from pathlib import Path
import os
import runpy
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
temp = root / 'Temp/glade-rims'
(temp / 'blender-user').mkdir(exist_ok=True)
env = dict(os.environ, BLENDER_USER_RESOURCES=str(temp / 'blender-user'), PYTHONWARNINGS='ignore::DeprecationWarning')
png = runpy.run_path(str(root / 'Tools/ForestAbove/make_detail.py'))['png']
for path in temp.glob('*/canopy-*.ppm'):
    _, dimensions, _, pixels = path.read_bytes().split(b'\n', 3)
    png(path.with_suffix('.png'), int(dimensions.split()[0]), pixels)
blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
views = ('glade-140m', 'glade-300m', 'edge-140m', 'edge-300m')
if '--edge-only' in sys.argv: views = views[2:]
phases = [arg for arg in sys.argv[1:] if arg != '--edge-only'] or ('before', 'after')
for view in views:
    for phase in phases:
        if not (temp / (phase + '-' + view) / 'scene.json').exists(): continue
        with (temp / ('render-' + phase + '-' + view + '.log')).open('w') as log:
            subprocess.run([blender, '--background', '--factory-startup', '--threads', '2', '--python-exit-code', '1',
                            '--python', 'Tools/GladeRims/render.py', '--', phase, view], cwd=root, env=env,
                           stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW, check=True)
        print(phase + ' ' + view + ' rendered', flush=True)
