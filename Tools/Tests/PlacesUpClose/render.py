"""Fixed-camera geometry reviews with the actual FBXs; not HDRP or GPU timing evidence."""
from pathlib import Path
import sys

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
args=sys.argv[sys.argv.index('--')+1:]
scene,phase=args[:2]
if not scene.startswith('Landmark'):
    helper=HERE.parent/'LivedIn/render.py'
    source=helper.read_text().split("args=sys.argv")[0]
    source=source.replace("ROOT/'Temp/lived-in/review'", "ROOT/'Temp/places-up-close/review'")
    source=source.replace("solid('Hedge',(.16,.22,.10))", "solid('Hedge',(.16,.22,.10)),solid('Chalk',(.82,.82,.76))")
    # The review's 10 m ground is coarser than the exported surfaces: lower it locally to avoid false intersections.
    source=source.replace("faces=np.array(faces);uv=points[faces.flatten()][:,:2]/2000+.5", '''faces=np.array(faces)
    for p in data.get('plots',[]):
        if not 0<p.get('landUse',0)<5 or not p.get('accepted'):continue
        q=points[:,[0,1]]*np.array([1,-1])-np.array(p['front'])
        u=q@np.array(p['along']);v=q@np.array(p['outward'])
        inside=(np.abs(u)<p['frontage']*.5+12)&(v>-12)&(v<p['depth']+12)
        points[inside,2]-=2
    uv=points[faces.flatten()][:,:2]/2000+.5''')
    source=source.replace("if name=='TownOutskirts':centre=np.clip(centre,-550,550)", '''if name=='TownOutskirts':
        pitch=next((p for p in data['plots'] if p.get('landUse')==2),None)
        if pitch:centre=np.array(pitch['front'])+np.array(pitch['outward'])*pitch['depth']*.5
        centre=np.clip(centre,-700,700)''')
    context={'__file__':str(helper),'__name__':'place_review'}
    exec(compile(source,str(helper),'exec'),context)
    context['HERE']=HERE
    context['render'](scene,300 if scene.startswith('Village') else 400)
else:
    helper=HERE.parent/'LandmarkRealism/render.py'
    source=helper.read_text().split('args = sys.argv')[0]
    source=source.replace("ROOT / f'Temp/landmark-realism/{phase}/results/{kind}.json'", "ROOT / f'Temp/places-up-close/review/{kind}.json'")
    source=source.replace("data['far' if distance == 2500 else 'surfaces']", "data['far' if phase == 'before' else 'surfaces']")
    source=source.replace("models(data, distance == 2500, position)", "models(data, phase == 'before', position)")
    # Match the strategy camera's 300 m slant range and yaw; use a wider overview inset separately if needed.
    source=source.replace("a = np.array(data['along']); across = np.array([-a[1], a[0]])", "a = np.array(data['along']); across = np.array([-a[1], a[0]])")
    source=source.replace("direction = a * .25 - across; direction /= np.linalg.norm(direction)", "direction = np.array([-math.sin(math.radians(35)), -math.cos(math.radians(35))])")
    source=source.replace("pitch = math.radians(52 if distance != 2500 else 32)", "pitch = math.radians(42 + 22 * ((300-15*5.807)/((110-15)*5.807)))")
    source=source.replace("camera.data.lens = 20 if distance != 2500 else 44", "camera.data.sensor_fit = 'VERTICAL'; camera.data.sensor_height = 24; camera.data.lens = 12/math.tan(math.radians(22.5))")
    context={'__file__':str(helper),'__name__':'place_landmark_review'}
    exec(compile(source,str(helper),'exec'),context)
    context['output']=HERE/'Review'
    context['render'](phase,scene[8:],300)
