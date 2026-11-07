"""Count actual FBX submeshes and triangles; these are geometry bounds, not GPU or SetPass measurements."""
from pathlib import Path
import json
import sys
import bpy

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
helper = ROOT / 'Tools/ForestHlod/render.py'
scope = {'__file__': str(helper), '__name__': 'asset_counts'}
exec(compile(helper.read_text().split('def render(name):')[0], str(helper), 'exec'), scope)
template = scope['template']


def count(name, lod):
    """Only nonempty material slots count as submitted geometry."""
    _, triangles, _, slots, ids = template(name + '_LOD' + str(lod))
    return len(set(int(i) for i in ids)), len(triangles), {slots[int(i)] for i in ids}


report = {}
for kind in ('IndustrialZone', 'Airfield', 'RailJunction'):
    row = {}
    for phase in ('before', 'after'):
        data = json.loads((ROOT / f'Temp/landmark-realism/{phase}/results/{kind}.json').read_text())
        levels = []
        for lod in range(3):
            draws = triangles = 0; assets = {}
            for model in data['models']:
                a, b, _ = count(model['model'], lod); draws += a; triangles += b
                assets[model['model']] = a
            groups = {}
            for prop in data.get('props', []):
                _, b, slots = count(prop['model'], max(1, lod)); triangles += b
                groups.setdefault(prop['group'], set()).update(slots)
            draws += sum(len(g) for g in groups.values())
            draws += sum(bool(t) for t in data['surfaces']['triangles']) + int(bool(data.get('fences')))
            # The new works siding is three material submissions in Features.Railway.
            if phase == 'after' and kind == 'IndustrialZone': draws += 3
            triangles += sum(len(t) // 3 for t in data['surfaces']['triangles'])
            triangles += sum(len(t) // 3 for mesh in data.get('fenceMeshes', []) for t in mesh['triangles'])
            levels.append({'lod': lod, 'materialSubmissions': draws, 'triangles': triangles, 'perAssetDraws': assets})
        row[phase] = {'levels': levels, 'hlodDraws': data['draws'], 'hlodTriangles': data['triangles'],
                      'managedBuildMs': data['buildMs'], 'surfaceAndHlodMeshMs': data['meshMs']}
    row['delta'] = [row['after']['levels'][i]['materialSubmissions'] - row['before']['levels'][i]['materialSubmissions'] for i in range(3)]
    report[kind] = row
print(json.dumps(report, indent=2))
(HERE / 'Review/budgets.json').write_text(json.dumps(report, indent=2))
