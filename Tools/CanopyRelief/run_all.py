"""Keep sector memory bounded by running exactly one managed harness process at a time."""
from pathlib import Path
import subprocess
import sys
import json

ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'Temp/canopy-relief'
for sector in (sys.argv[1:] or ('deep','border','varrow-base','varrow-bridge','varrow-farmland')):
    for phase in ('before','after'):
        cached=out/(phase+'-'+sector)/'flight.json'
        if phase=='before' and cached.exists() and 'peakCombinedTriangles' in json.loads(cached.read_text()):
            print(phase+' '+sector+' already exported from 0542d98',flush=True)
            continue
        with (out/(phase+'-'+sector+'.log')).open('w') as log:
            subprocess.run([sys.executable,'-I',str(ROOT/'Tools/CanopyRelief/run.py'),phase,sector],stdout=log,stderr=subprocess.STDOUT,check=True)
        print(phase+' '+sector+' passed',flush=True)
