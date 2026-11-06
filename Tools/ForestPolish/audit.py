"""Collect reproducible forest evidence; managed scheduling and Blender views are not native GPU measurements."""
from pathlib import Path
import json
import math

ROOT = Path(__file__).resolve().parents[2]
TEMP = ROOT / 'Temp/forest-polish'
OUT = ROOT / 'Tools/ForestPolish/Review'
OUT.mkdir(exist_ok=True)


def screen(scene):
    """Compare occupied 100-pixel bins across the entire strategy view, including woods beyond the focus."""
    eye, target = scene['eye'], scene['target']
    forward = [b-a for a,b in zip(eye,target)]
    length = math.sqrt(sum(v*v for v in forward)); forward = [v/length for v in forward]
    right = [forward[2],0,-forward[0]]
    length = math.sqrt(sum(v*v for v in right)); right = [v/length for v in right]
    up = [forward[1]*right[2],forward[2]*right[0]-forward[0]*right[2],-forward[1]*right[0]]
    logical, drawn = [0]*144, [0]*144
    for tree in scene['logical']:
        offset = [tree['p'][k]-eye[k]+(tree['height']*.75 if k==1 else 0) for k in range(3)]
        depth = sum(a*b for a,b in zip(offset,forward))
        if depth <= 0: continue
        x = sum(a*b for a,b in zip(offset,right))/(depth*math.tan(math.pi/8)*1600/900)
        y = sum(a*b for a,b in zip(offset,up))/(depth*math.tan(math.pi/8))
        if not(-1<=x<1 and -1<=y<1): continue
        index = int((y+1)*4.5)*16 + int((x+1)*8)
        logical[index] += 1
        if tree['tier']>=0: drawn[index]+=1
    wooded = [k for k in range(144) if logical[k]>=4]
    return dict(woodedBins=len(wooded),binsWithCrowns=sum(drawn[k]>0 for k in wooded),logicalStems=sum(logical),submittedRoots=sum(drawn))


def geometry(folder):
    """Count retained canopy geometry separately from the per-camera individual tree budget."""
    vertices=triangles=0
    for path in folder.glob('canopy-*.json'):
        mesh=json.loads(path.read_text());vertices+=len(mesh['vertices']);triangles+=len(mesh['indices'])//3
    return dict(vertices=vertices,triangles=triangles)


results = {}
for sector in ('varrow-base','varrow-bridge','deep'):
    for phase in ('before','after'):
        folder = TEMP/(phase+'-'+sector)
        result = {}
        for name in ('metrics','startup','flight'):
            path = folder/(name+'.json')
            if path.exists(): result[name] = json.loads(path.read_text())
        for suffix, name in (('', 'settledScreen'),('-early','halfSecondScreen')):
            scene = json.loads((TEMP/(phase+'-'+sector+suffix)/'scene.json').read_text())
            result[name] = screen(scene)
        result['earlyCanopyGeometry']=geometry(TEMP/(phase+'-'+sector+'-early'))
        result['settledCanopyGeometry']=geometry(folder)
        results[phase+'-'+sector] = result
        if phase == 'after' and sector.startswith('varrow'):
            assert result['startup']['densePatches']==result['startup']['patchesWithCanopy']
            assert result['halfSecondScreen']['woodedBins']==result['halfSecondScreen']['binsWithCrowns']
        if phase == 'after':
            flight=result['flight']
            assert flight['peakTriangles']<=650000 and flight['peakCards']<=1800
            assert flight['refinementStarts']<=1 and flight['fastFrames']>=350

preview=TEMP/'after-preview'
if (preview/'metrics.json').exists():
    results['after-preview']={name:json.loads((preview/(name+'.json')).read_text()) for name in ('metrics','startup','flight')}

(OUT/'measurements.json').write_bytes((json.dumps(results,indent=2)+'\n').replace('\n','\r\n').encode())
print(json.dumps({key:{'screen':value.get('halfSecondScreen'),'flightP95':value['metrics']['lowFlight']['managedP95Ms']} for key,value in results.items()},indent=2))
