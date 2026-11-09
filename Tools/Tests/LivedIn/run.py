"""Run one lean scene process and one Blender process at a time, then release both before the next scene."""
from pathlib import Path
import os
import random
import subprocess
import sys
from PIL import Image

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
DOTNET=Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')
BLENDER=Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
env=dict(os.environ,DOTNET_PROCESSOR_COUNT='2',DOTNET_TieredCompilation='0',MSBUILDDISABLENODEREUSE='1')
review=HERE/'Review';review.mkdir(exist_ok=True)
scenes=['Village'+str(i) for i in sorted(random.Random(1200).sample(range(4,24),5))]+['TownOutskirts','Field']
requested=[a for a in sys.argv[1:] if not a.startswith('--')]
if requested:scenes=requested
print('Review scenes: '+', '.join(scenes),flush=True)
for scene in scenes:
    result=subprocess.run([str(DOTNET),str(ROOT/'Temp/lived-in/build/bin/Release/net8.0/harness.dll'),scene,str(ROOT/'Temp/lived-in/review')],cwd=ROOT,env=env,capture_output=True,text=True,check=True)
    print(result.stdout,flush=True)
    (review/(scene+'.log')).write_bytes(result.stdout.replace('\r\n','\n').replace('\n','\r\n').encode())
    Image.open(ROOT/'Temp/lived-in/review'/f'{scene}.ppm').save(ROOT/'Temp/lived-in/review'/f'{scene}.png')
    if '--renders' in sys.argv:
        for altitude in ([150,400] if scene.startswith('Village') else [200] if scene=='Field' else [400]):
            subprocess.run([str(BLENDER),'--background','--factory-startup','--threads','2','--python-exit-code','1','--python',str(HERE/'render.py'),'--',scene,str(altitude)],cwd=ROOT,env=env,check=True)
