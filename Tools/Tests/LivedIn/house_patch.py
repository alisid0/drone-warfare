"""Carry uniform house sizes through the two owned-by-Claude distant consumers without editing forest files."""
from pathlib import Path
import difflib
ROOT=Path(__file__).resolve().parents[3]
patch=''
for name in ('FarScatter.cs','SettlementHlod.cs'):
    path=ROOT/'Assets/DroneSandbox/Scripts'/name
    before=path.read_text()
    old='Quaternion.Euler(0f, house.yaw, 0f), Vector3.one)'
    assert before.count(old)==1
    after=before.replace(old,'Quaternion.Euler(0f, house.yaw, 0f), Vector3.one * house.Scale)')
    target=ROOT/'Temp/lived-in'/('patched-'+name);target.parent.mkdir(exist_ok=True,parents=True)
    target.write_bytes(after.replace('\n','\r\n').encode())
    patch+=''.join(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='a/Assets/DroneSandbox/Scripts/'+name,tofile='b/Assets/DroneSandbox/Scripts/'+name))
(Path(__file__).parent/'Claude-house-scale.patch').write_bytes(patch.replace('\n','\r\n').encode())
