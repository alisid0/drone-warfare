"""Package compact review evidence, normalize only this task's files, and check the allowed scope."""
from pathlib import Path
import json
import subprocess

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
git=['git','-c','safe.directory='+ROOT.as_posix()]
paths=subprocess.check_output(git+['diff','9667d1f','--name-only'],cwd=ROOT,text=True).splitlines()
paths+=subprocess.check_output(git+['ls-files','--others','--exclude-standard','Assets/DroneSandbox','Tools/Tests/PlacesUpClose'],cwd=ROOT,text=True).splitlines()
paths=[p for p in paths if p.startswith('Assets/DroneSandbox/') or p.startswith('Tools/Tests/PlacesUpClose/')]
for name in paths:
    path=ROOT/name
    if path.suffix not in ('.cs','.py','.hlsl','.shader','.meta','.mat','.md','.txt','.json','.html') and path.name not in ('.gitignore','.gitattributes'):continue
    text=path.read_text(encoding='utf-8')
    if path.suffix in ('.mat','.shader','.hlsl','.md'):text='\n'.join(line.rstrip() for line in text.splitlines())+'\n'
    if path.suffix in ('.md','.txt','.json','.html'):text=text.rstrip()+'\n'
    if path.suffix=='.cs' and 'System.Linq' in text:raise RuntimeError('LINQ '+name)
    path.write_bytes(text.replace('\r\n','\n').replace('\n','\r\n').encode())
    if path.suffix=='.cs' and ('Forest' in path.name or path.name in ['Ground.cs','Farmland.cs','Atmosphere.cs','SandboxGame.cs','Theatre.cs','Coach.cs','AutoTest.cs','Missions.cs']):raise RuntimeError('Protected source changed '+name)
before=subprocess.check_output(git+['show','9667d1f:Assets/DroneSandbox/Scripts/World.cs'],cwd=ROOT,text=True)
after=(ROOT/'Assets/DroneSandbox/Scripts/World.cs').read_text()
assert before.split('static void BuildVillages')[0]==after.split('static void BuildVillages')[0]
assert before.split('// ---- Woods ----')[1]==after.split('// ---- Woods ----')[1]
source=ROOT/'Temp/places-up-close/review'
for name in ('checks.json','determinism.txt'):
    if (source/name).exists():(HERE/'Review'/name).write_bytes(((source/name).read_text().rstrip()+'\n').replace('\n','\r\n').encode())
report=json.loads((source/'checks.json').read_text())
print('Village properties:',sum(r['n'] for r in report),'overlaps:',sum(r['overlap'] for r in report))
images=['IndustrialZone_300m_before.png','IndustrialZone_300m_after.png','Airfield_300m_before.png','Airfield_300m_after.png','RailJunction_300m_before.png','RailJunction_300m_after.png','Village6_300m.png','VillageLegacy_300m.png','TownOutskirts_400m.png']
html='''<!doctype html><html lang="en"><meta charset="utf-8"><title>Places up close</title><style>body{font:16px system-ui;margin:32px;background:#182126;color:#eee}main{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:22px}img{width:100%}figure{margin:0}figcaption{padding:8px}p{max-width:1000px;line-height:1.5}</style><h1>Places up close</h1><p>Blender geometry reviews using production layouts and existing FBXs. Landmark pairs use the same 300 m slant-range camera and unchanged yard surfaces. Settlement labels are camera altitude. These are not HDRP frames or GPU measurements. The coarse review terrain is lowered beneath accepted community surfaces to prevent tessellation intersections. Native culling, dither and terrain joins require Claude's Unity checks.</p><main>'''
for name in images:
    if not (HERE/'Review'/name).exists():raise RuntimeError('Missing review '+name)
    html+='<figure><a href="'+name+'"><img src="'+name+'"></a><figcaption>'+name.replace('_',' ')+'</figcaption></figure>'
html+='</main></html>'
(HERE/'Review/index.html').write_text(html)
print('Packaged',len(images),'review images; task source scope and CRLF checked.')
