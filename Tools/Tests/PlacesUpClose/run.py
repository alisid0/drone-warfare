"""One bounded managed process or Blender process at a time; no Unity or imported Library."""
from pathlib import Path
import os
import subprocess
import sys
from PIL import Image

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
DOTNET=Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
BLENDER=Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
env=dict(os.environ,DOTNET_PROCESSOR_COUNT='2',DOTNET_TieredCompilation='0',MSBUILDDISABLENODEREUSE='1')
review=HERE/'Review';review.mkdir(exist_ok=True)
output=ROOT/'Temp/places-up-close/review';output.mkdir(parents=True,exist_ok=True)
scenes=[a for a in sys.argv[1:] if not a.startswith('--')] or ['LandmarkIndustrialZone','LandmarkAirfield','LandmarkRailJunction','Village6','TownOutskirts']
for scene in scenes:
    if '--render-only' not in sys.argv:
        r=subprocess.run([str(DOTNET),str(ROOT/'Temp/places-up-close/build/bin/Release/net8.0/harness.dll'),scene,str(output)],cwd=ROOT,env=env,capture_output=True,text=True)
        print(r.stdout+r.stderr,flush=True)
        (review/(scene+'.log')).write_text(r.stdout+r.stderr)
        r.check_returncode()
        if not scene.startswith('Landmark') and scene != 'Checks':
            Image.open(output/(scene+'.ppm')).save(output/(scene+'.png'))
    if '--renders' in sys.argv or '--render-only' in sys.argv:
        variants=['before','after'] if scene.startswith('Landmark') else ['after']
        for phase in variants:
            result=subprocess.run([str(BLENDER),'--background','--factory-startup','--threads','2','--python-exit-code','1','--python',str(HERE/'render.py'),'--',scene,phase],cwd=ROOT,env=env,capture_output=True,text=True)
            (review/(scene+'-'+phase+'-render.log')).write_text(result.stdout+result.stderr)
            print(scene+' '+phase+' render exit '+str(result.returncode),flush=True)
            result.check_returncode()
