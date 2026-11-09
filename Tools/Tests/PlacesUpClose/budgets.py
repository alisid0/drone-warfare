"""Count full-compound submitted FBX geometry at the review camera, including both selected transition tiers."""
from pathlib import Path
import json
import math
import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
helper=ROOT/'Tools/WorldLandmarks/render.py'
scope={'__file__':str(helper),'__name__':'place_budget_reader'}
exec(compile(helper.read_text().split('def render(kind,')[0],str(helper),'exec'),scope)
template=scope['template']
pitch=math.radians(42+22*((300-15*5.807)/((110-15)*5.807)))
eye=np.array([-math.sin(math.radians(35))*300*math.cos(pitch),300*math.sin(pitch),-math.cos(math.radians(35))*300*math.cos(pitch)])
reports=[]
for kind in ['IndustrialZone','Airfield','RailJunction']:
    data=json.loads((ROOT/'Temp/places-up-close/review'/f'{kind}.json').read_text())
    sizes={(b['model'],round(b['at'][0],2),round(b['at'][1],2)):b['size'] for b in data['models']}
    groups={}
    for p in data['placements']:
        pos=np.array(p['position']);pos[1]-=data['elevation']
        d=np.linalg.norm(pos-eye)
        prop=p.get('clutter',False);detail=p.get('nearOnly',False)
        key=(p['model'],round(pos[0],2),round(pos[2],2))
        radius=16 if prop else np.linalg.norm(sizes[key])*.5
        limits=[150,275] if prop else [65,150,275] if detail else [65,250,1100]
        for tier,end in enumerate(limits):
            start=0 if tier==0 else limits[tier-1]
            if d+radius<start*.9 or d-radius>end:continue
            lod=tier+1 if prop else tier
            groups[(p['model'],lod)]=groups.get((p['model'],lod),0)+1
    triangles=0;submeshes=0;rows=[]
    for (name,lod),count in sorted(groups.items()):
        v,t,uv,slots,ids=template(name+'_LOD'+str(lod))
        triangles+=len(t)*count;submeshes+=len(slots)
        rows.append(dict(model=name,lod=lod,instances=count,triangles=len(t)*count,materialSlots=len(slots)))
    report=dict(kind=kind,slantRange=300,lodBias=1,verticalFov=45,fullCompoundTriangles=triangles,materialSlotSubmissions=submeshes,groups=rows)
    reports.append(report)
    print(kind,triangles,'submitted FBX triangles;',submeshes,'material slots; all compound roots counted, no frustum rejection',flush=True)
(HERE/'Review/mesh-budgets.json').write_text(json.dumps(reports,indent=2))
