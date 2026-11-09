"""Collect the small reproducible review without retaining generated meshes or compiler caches in git."""
from pathlib import Path
import json
import struct

root = Path(__file__).resolve().parents[2]
temp = root / 'Temp/glade-rims'
out = root / 'Tools/GladeRims/Review'
results = {}
for sector in ('glade', 'edge'):
    for phase in ('before', 'after'):
        folder = temp / (phase + '-' + sector)
        item = {name: json.loads((folder / (name + '.json')).read_text()) for name in ('metrics', 'geometry')}
        if (folder / 'recipe.json').exists(): item['recipe'] = json.loads((folder / 'recipe.json').read_text())
        if phase == 'after': assert all(row['verticalFaces'] == 0 for row in item['geometry'])
        item['poses'] = {}
        for height in (140, 300):
            scene = json.loads((temp / (phase + '-' + sector + '-' + str(height) + 'm') / 'scene.json').read_text())
            item['poses'][str(height)] = {key: scene[key] for key in ('eye', 'target', 'origin')}
            image = out / (sector + '-' + str(height) + 'm_' + phase + '.png')
            header = image.read_bytes()[:24]
            assert header[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', header[16:24]) == (1600, 900)
        results[phase + '-' + sector] = item
    assert results['before-' + sector]['poses'] == results['after-' + sector]['poses']
(out / 'measurements.json').write_bytes((json.dumps(results, indent=2) + '\n').replace('\n', '\r\n').encode())
html = '''<!doctype html><html lang="en"><meta charset="utf-8"><title>Glade rims - paired harness views</title>
<style>body{background:#18201c;color:#e9eee9;font:16px system-ui;margin:28px}h1{font-size:26px}p{max-width:1000px;line-height:1.5}a{color:#bddcbf}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:16px 0 40px}figure{margin:0}img{width:100%;height:auto}figcaption{padding:6px 0}h2{margin-bottom:0}code{color:#d0dbd0}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style>
<h1>Glade rims and farmland edges</h1>
<p>Before: <code>2d99a10</code>. After: <code>ffb997b + bf1484b</code>. Same production sectors, coordinates, camera pitch, sun and exposure.
Heights are metres above the target terrain; cameras look down at 45 degrees with a 45-degree vertical field of view.
Click any image for the full 1600 x 900 view.</p>
<p>Offline Blender replay of production canopy geometry and submitted tree tiers. These are <strong>not Unity/HDRP captures</strong>.
The harness uses repository tree models, simplified meadow/forest-floor shading, and filtered production crop colours at the farmland edge.
It omits buildings, undergrowth dressings and native atmospheric/terrain shading; its midpoint LOD fade approximates HDRP dithering.</p>
<p><a href="../HANDOFF.txt">Changes and build checklist</a> &middot; <a href="measurements.json">Geometry, budgets and camera poses</a>.
Unity flight p95 &le; 11.7 ms remains an integration acceptance check.</p>'''
for sector, label in (('glade', 'Deep Forest glade'), ('edge', 'Varrow wood beside farmland')):
    for height in (140, 300):
        html += '<h2>' + label + ' - ' + str(height) + ' m</h2><div class="pair">'
        for phase in ('before', 'after'):
            name = sector + '-' + str(height) + 'm_' + phase + '.png'
            html += '<figure><a href="' + name + '"><img loading="lazy" src="' + name + '" alt="' + label + ' ' + phase + '"></a><figcaption>' + phase.title() + '</figcaption></figure>'
        html += '</div>'
html += '</html>\n'
(out / 'index.html').write_bytes(html.replace('\n', '\r\n').encode())
print('Eight matched views verified; measurements and review index written.')
