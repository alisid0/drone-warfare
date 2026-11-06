"""Check task scope, patch applicability and line endings, and create stable Unity metadata for the new helpers."""
from pathlib import Path
import subprocess
import uuid

ROOT=Path(__file__).resolve().parents[3]
S=ROOT/'Assets/DroneSandbox/Scripts'
new=['VillageUse','VillageGrowth','VillageAccess','FieldAppearance','FieldMargins']
changed=['VillagePlan','VillageLayout','VillageScenery','MapLayout','World','FarDetailPaint','FarFieldPlan','FarFieldSector','FieldParcels','SettlementPattern']+new
for name in new:
    p=S/(name+'.cs.meta')
    if not p.exists():p.write_bytes(('fileFormatVersion: 2\r\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'drone-warfare/lived-in/'+name).hex+'\r\n').encode())
for name in changed:
    p=S/(name+'.cs');t=p.read_text(encoding='utf-8');assert 'using System.Linq' not in t
    p.write_bytes(t.replace('\r\n','\n').replace('\n','\r\n').encode())
for p in Path(__file__).parent.rglob('*'):
    if p.is_file() and p.suffix in ('.py','.cs','.md','.txt','.json','.log','.html'):
        t=p.read_text(encoding='utf-8');p.write_bytes(t.replace('\r\n','\n').replace('\n','\r\n').encode())
git=['git','-c','safe.directory='+ROOT.as_posix()]
base=subprocess.check_output(git+['show','cb49269:Assets/DroneSandbox/Scripts/World.cs'],cwd=ROOT).decode().replace('\r\n','\n')
world=(S/'World.cs').read_text(encoding='utf-8')
assert base.split('        // ---- Villages ----')[0]==world.split('        // ---- Villages ----')[0]
assert base.split('        // ---- Woods ----')[1]==world.split('        // ---- Woods ----')[1]
for name in ('Ground.cs','Farmland.cs','Atmosphere.cs','SandboxGame.cs','Theatre.cs','Coach.cs','AutoTest.cs','Missions.cs'):
    old=subprocess.check_output(git+['show','cb49269:Assets/DroneSandbox/Scripts/'+name],cwd=ROOT).decode().replace('\r\n','\n')
    assert old==(S/name).read_text(encoding='utf-8'),name+' was changed'
for patch in Path(__file__).parent.glob('Claude-*.patch'):
    subprocess.run(git+['apply','--check','--ignore-space-change',str(patch)],cwd=ROOT,check=True)
print('Scope, C# source line endings, protected files and patch applicability passed.')
