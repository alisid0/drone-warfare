"""Render exactly one Blender process at a time and keep all caches inside the worktree."""
from pathlib import Path
import os
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'Temp/canopy-relief'
env=dict(os.environ,BLENDER_USER_RESOURCES=str(out/'blender-user'),PYTHONWARNINGS='ignore::DeprecationWarning')
(out/'blender-user').mkdir(exist_ok=True)
blender='C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
views=('varrow-bridge-early','varrow-farmland-early','varrow-base-early',
       'deep-100m','deep-300m','deep-2500m','border-100m','border-300m','border-2500m')
if os.environ.get('FOREST_REVIEW_VARROW_ONLY') == '1': views=views[:3]
for view in views:
    for phase in (sys.argv[1:] or ('before','after')):
        with (out/('render-'+phase+'-'+view+'.log')).open('w') as log:
            subprocess.run([blender,'--background','--factory-startup','--threads','2','--python-exit-code','1',
                            '--python','Tools/CanopyRelief/render.py','--',phase,view],cwd=ROOT,env=env,
                           stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW,check=True)
        assert (ROOT/'Tools/CanopyRelief/Review'/(view+'_'+phase+'.png')).exists()
        print(phase+' '+view+' rendered',flush=True)
