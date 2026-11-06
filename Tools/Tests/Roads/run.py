"""Run one small managed scene or one Blender job at a time, with two worker threads."""
from pathlib import Path
import os
import subprocess
import sys
from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DOTNET = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
BLENDER = Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
env = dict(os.environ,DOTNET_PROCESSOR_COUNT='2',DOTNET_TieredCompilation='0',MSBUILDDISABLENODEREUSE='1')
out = ROOT/'Temp/roads/review';out.mkdir(parents=True,exist_ok=True)
review = HERE/'Review';review.mkdir(exist_ok=True)
scenes = [a for a in sys.argv[1:] if not a.startswith('--')] or ['Checks','Textures','River','Village']
for scene in scenes:
    if '--render-only' not in sys.argv:
        command=[str(DOTNET),str(ROOT/'Temp/roads/build/bin/Release/net8.0/harness.dll'),scene,str(out)]
        if '--geometry-only' in sys.argv:command.append('geometry')
        r = subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True)
        print(r.stdout+r.stderr,flush=True);(review/(scene+'.log')).write_text(r.stdout+r.stderr);r.check_returncode()
        for p in out.glob(scene+'-*.ppm'):Image.open(p).save(p.with_suffix('.png'))
    if '--renders' in sys.argv or '--render-only' in sys.argv:
        for phase in ('before','after'):
            r = subprocess.run([str(BLENDER),'--background','--factory-startup','--threads','2','--python-exit-code','1','--python',str(HERE/'render.py'),'--',scene,phase],cwd=ROOT,env=env,capture_output=True,text=True)
            (review/(scene+'-'+phase+'-render.log')).write_text(r.stdout+r.stderr);print(scene+' '+phase+' render exit '+str(r.returncode),flush=True);r.check_returncode()
