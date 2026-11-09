"""Collect compact evidence, a paired-view gallery, and CRLF text without touching another task's files."""
from pathlib import Path
import json
import shutil

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
review=HERE/'Review'
out=ROOT/'Temp/roads/review'
stats=[]
for name in ('Main','River','Village'):
    shutil.copyfile(out/(name+'-audit.json'),review/(name+'-audit.json'))
    last=None
    for line in (review/(name+'.log')).read_text().splitlines():
        if line.startswith('{'):
            item=json.loads(line)
            if 'scene' in item:last=item
    if last is not None:stats.append(last)
shutil.copyfile(out/'checks.txt',review/'checks.txt')
(review/'scene-budgets.json').write_text(json.dumps(stats,indent=2))
sections=[]
for title,name in [('Main road','MainRoad'),('Junction','Junction'),('Farm track','FarmTrack'),('Village street','VillageStreet')]:
    for height in (150,300):
        cards=''.join(f'<figure><figcaption>{phase.title()}</figcaption><a href="{name}_{height}m_{phase}.png"><img src="{name}_{height}m_{phase}.png" alt="{title}, {height} metres, {phase}" loading="lazy"></a></figure>' for phase in ('before','after'))
        sections.append(f'<section><h2>{title} · {height} m altitude</h2><div class="pair">{cards}</div></section>')
html='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Drone Warfare · Roads</title><style>body{margin:32px auto;max-width:1500px;padding:0 20px;background:#17201c;color:#e8ece6;font:16px/1.5 system-ui}h1{font-size:30px}h2{font-size:21px;margin-top:44px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:14px}figure{margin:0}figcaption{padding:6px 0;color:#aebdaf}img{width:100%;display:block}p{max-width:1000px}a{color:#b6d9bc}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style>
<h1>Roads and tracks · 150 m / 300 m</h1>
<p>Fixed-camera Blender reviews of production road meshes, baked textures, terrain samples and FBX placements. These are not Unity/HDRP screenshots. Click a view for its full-size PNG.</p>
<p>The before view uses the original Ground road-splat equations on the same prepared layout. It isolates the old road surface; the old random avenue placement is not reconstructed. After includes the replacement roadside instance plan. Woods and native crop shaders are omitted; terrain, lighting and LOD appearance are approximate. Cameras are the stated height above the ground target.</p>
<p><a href="../HANDOFF.md">Report, integration patch and build checklist</a></p>
'''+''.join(sections)+'</html>\n'
(review/'index.html').write_text(html,encoding='utf-8')
paths=list(HERE.rglob('*'))+[ROOT/'Tools/Blender/make_road_pole.py']
scripts=ROOT/'Assets/DroneSandbox/Scripts'
paths+=list(scripts.glob('Road*'))+[scripts/(n+'.cs') for n in ('FarDetailData','FarDetailEdge','FarDetailPaint','MapLayout','VillageLayout','VillagePlan','World')]
for folder in ('Materials','Textures','Models'):
    paths+=list((ROOT/'Assets/DroneSandbox/Resources'/folder).glob('Road*' if folder!='Models' else 'Scenery_TelegraphPole*'))
for p in paths:
    if not p.is_file() or (p.suffix not in ('.cs','.py','.meta','.mat','.md','.txt','.patch','.html','.json') and p.name not in ('.gitignore','.gitattributes')):continue
    raw=p.read_bytes()
    text=raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8').replace('\r\n','\n')
    if p.suffix in ('.mat','.meta'):text='\n'.join(line.rstrip() for line in text.split('\n'))
    p.write_bytes(text.replace('\n','\r\n').encode('utf-8'))
print('Packaged 16 fixed-camera views, audits, scene budgets and scoped CRLF text.')
