"""Render source-backed before/after plans; no Unity or hand-transcribed placement algorithm."""
import json
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(sys.argv[1])
OUT = Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)
CROPS = {'Wheat': '#c1aa69', 'YoungCrop': '#75814b', 'Ploughed': '#705445', 'Sunflower': '#aa994e', 'MownHay': '#a2ab7c'}
KINDS = {'Hedge': '#23492d', 'Ditch': '#337e9a', 'Fence': '#d9d5c9', 'Open': '#cdbda2'}


def outline(f):
    """Straight geometry before the identical gentle land warp, for edge length comparisons."""
    cx, cy = f['center']; ax, ay = f['along']
    result = []
    for u, v in [(-1, -1), (1, -1), (1, 1), (-1, 1)]:
        u *= f['halfLength']
        v = v * f['halfWidth'] * (1 + f['taper'] * u / f['halfLength']) + f['shear'] * u
        result.append((cx + ax * u - ay * v, cy + ay * u + ax * v))
    return result


def contains(f, p):
    """Independent scalar containment for the district envelope, before warp."""
    dx, dy = p[0]-f['center'][0], p[1]-f['center'][1]
    ax, ay = f['along']; u = dx*ax+dy*ay; v = -dx*ay+dy*ax-f['shear']*u
    return abs(u) < f['halfLength'] and abs(v) < f['halfWidth']*(1+f['taper']*u/f['halfLength'])


def shared_metrics(data):
    """Measure matched edge metres, allowing <=2 m baulks, T-junctions and the baseline's slightly unequal slopes."""
    edges = []
    for i, f in enumerate(data['plots']):
        pts = outline(f)
        for k in range(4):
            a, b = pts[k], pts[(k + 1) % 4]
            dx, dy = b[0] - a[0], b[1] - a[1]
            length = math.hypot(dx, dy)
            edges.append((i, a, b, (dx / length, dy / length), length))
    total = sum(e[4] for e in edges)
    matched = internal = shared_internal = 0
    mask = {(round((p[0]+982)/6), round((p[1]+982)/6)) for p in data['blocked']}
    for n, (i, a, b, axis, length) in enumerate(edges):
        spans = []
        for j, c, d, direction, otherlen in edges:
            if i == j or axis[0] * direction[0] + axis[1] * direction[1] > -.998:
                continue
            dx, dy = c[0] - a[0], c[1] - a[1]
            ex, ey = d[0] - a[0], d[1] - a[1]
            if max(abs(dx * axis[1] - dy * axis[0]), abs(ex * axis[1] - ey * axis[0])) > 2.3:
                continue
            u, v = dx * axis[0] + dy * axis[1], ex * axis[0] + ey * axis[1]
            lo, hi = max(0, min(u, v)), min(length, max(u, v))
            if hi > lo: spans.append((lo, hi))
        end = 0
        for lo, hi in sorted(spans):
            matched += max(0, hi - max(lo, end))
            end = max(end, hi)
        # Internal excludes district exteriors and obstacle-facing edges (within 12 m of the blocked mask).
        # The baseline has no districts; its agricultural envelope is the central survey square used by FieldCoverage.
        steps = max(1, math.ceil(length/3))
        for k in range(steps):
            t = (k+.5)*length/steps
            p = (a[0]+axis[0]*t, a[1]+axis[1]*t)
            # Counter-clockwise polygon: outside is the right-hand normal.
            q = (p[0]+axis[1]*2, p[1]-axis[0]*2)
            if data.get('districts'):
                if not any(contains(f,q) for f in data['districts']): continue
            elif max(abs(q[0]),abs(q[1])) > 625: continue
            if any((round((p[0]+axis[1]*r+982)/6), round((p[1]-axis[0]*r+982)/6)) in mask for r in (3,6,9,12)): continue
            internal += length/steps
            if any(lo<=t<=hi for lo,hi in spans): shared_internal += length/steps
    return (matched / total * 100 if total else 0, shared_internal/internal*100 if internal else 0)


def draw_plan(data, phase):
    """Draw crops and map constraints at a fixed scale so coverage changes are visible."""
    size, pad = 1200, 55
    im = Image.new('RGB', (size + 2 * pad, size + 165), '#ecebe4')
    d = ImageDraw.Draw(im)
    def xy(p): return (pad + (p[0] + 1000) * size / 2000, pad + (1000 - p[1]) * size / 2000)
    d.rectangle((pad, pad, pad + size, pad + size), fill='#7f8964')
    for p in data['blocked']:
        x, y = xy(p); d.rectangle((x - 2, y - 2, x + 2, y + 2), fill='#555f4d')
    for f in data['plots']:
        pts = [xy(p) for p in f['outline']]
        d.polygon(pts, fill=CROPS[f['crop']]); d.line(pts + [pts[0]], fill='#8b916c', width=1)
    for f in data['orchards']:
        d.polygon([xy(p) for p in f], fill='#3e6439')
    for p in data['wet']:
        x, y = xy(p); d.rectangle((x - 2, y - 2, x + 2, y + 2), fill='#496e7d')
    if data['river']: d.line([xy(p) for p in data['river']], fill='#496e7d', width=max(2, round(data['riverHalfWidth'] * size / 1000)))
    for line in data['roads']: d.line([xy(p) for p in line], fill='#d7cdb5', width=3)
    for line in data['tracks']: d.line([xy(p) for p in line], fill='#b4aa8e', width=2)
    for p in data['villages']:
        x, y = xy(p); r = 45 * size / 2000
        d.ellipse((x-r,y-r,x+r,y+r), fill='#a28b78', outline='#d9c9b8', width=2)
    for x,y,r in data['sites']:
        x,y = xy((x,y)); r *= size/2000
        d.ellipse((x-r,y-r,x+r,y+r), fill='#a49683')
    for b in data['boundaries']:
        if len(b['points']) > 1: d.line([xy(p) for p in b['points']], fill=KINDS[b['kind']], width=2 if b['kind'] != 'Open' else 1)
    for p in data.get('props', []):
        if p['model'] not in ('Scenery_Tree', 'Scenery_Birch', 'Scenery_Poplar'): continue
        x,y=xy((p['x'],p['z'])); r=2.3
        d.ellipse((x-r,y-r,x+r,y+r), fill='#304c2e')
    d.text((pad, 14), f"{data['sector']}  |  {phase.upper()}  |  seed {data['seed']}  |  {len(data['plots'])} parcels  |  central land coverage {data['coverage']:.1f}%", fill='#232c25', font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 22))
    y = size + pad + 18; x = pad
    for name, colour in {**CROPS, **KINDS, 'Blocked': '#555f4d', 'Village/site': '#a28b78'}.items():
        d.rectangle((x,y,x+15,y+15), fill=colour); d.text((x+20,y), name, fill='#232c25'); x += 112
        if x > size-100: x=pad; y+=25
    d.text((pad,size+145), '2 km x 2 km | Dark ground: blocked cells | Reference Perlin substitute, not an in-engine render', fill='#444b40')
    im.save(OUT / f"{data['sector']}_{phase}.png")
    return im


rows = []
for sector in ['VarrowValley', 'RiverValley', 'VillageCluster', 'Marshland', 'DeepForest']:
    ims = []
    for phase in ['before', 'after']:
        data = json.loads((ROOT / phase / f'{sector}.json').read_text())
        areas = sorted(4*f['halfWidth']*f['halfLength'] for f in data['plots'])
        q = lambda p: areas[round((len(areas)-1)*p)] if areas else 0
        boundaries = {k: sum(b['kind'] == k for b in data['boundaries']) for k in KINDS}
        if phase == 'before': boundaries['Open'] = len(areas)*4-boundaries['Hedge']
        all_shared, internal_shared = shared_metrics(data)
        row = {'sector':sector, 'phase':phase, 'count':len(areas), 'coverage':round(data['coverage'],2), 'shared_all_edges':round(all_shared,2), 'shared_internal':round(internal_shared,2), 'p10':round(q(.1)), 'median':round(q(.5)), 'p90':round(q(.9)), 'min':round(q(0)), 'boundaries':boundaries, 'renderers':data.get('renderers'), 'layout_ms':round(data['elapsedMs']), 'farmland_cpu_ms':round(data.get('farmlandCpuMs',0)), 'field_timings':data['fieldTimings']}
        rows.append(row); print(row, flush=True)
        ims.append(draw_plan(data, phase))
    sheet = Image.new('RGB', (ims[0].width*2, ims[0].height))
    sheet.paste(ims[0], (0,0)); sheet.paste(ims[1], (ims[0].width,0)); sheet.save(OUT / f'{sector}_comparison.png')
(OUT/'metrics.json').write_text(json.dumps(rows,indent=2))
report = ['# Shared-parcel farmland comparison', '',
          'Branch: `codex/field-parcels`, based on `61757a3`. Plans use production layout code with the same reference Perlin substitute before and after. These are standalone measurements, not Unity screenshots or engine timings.', '',
          'Scattered blocks and the gap-filling passes are replaced with connected, tapered/sheared farm districts. Small disconnected remnants stay meadow. One shared-edge record owns each hedge, ditch, bank/fence or open boundary; the field index and existing FieldPlot consumers remain compatible.', '',
          '| Sector | Version | Fields | Coverage % | Shared internal % | Shared all % | Area P10 / median / P90 (m²) |',
          '|---|---|---:|---:|---:|---:|---:|']
for r in rows:
    report.append(f"| {r['sector']} | {r['phase']} | {r['count']} | {r['coverage']:.2f} | {r['shared_internal']:.2f} | {r['shared_all_edges']:.2f} | {r['p10']} / {r['median']} / {r['p90']} |")
report += ['', '| Sector | Version | Hedge / ditch / fence-bank / open | Renderer estimate | Layout median (ms) | Farmland CPU only (ms) |', '|---|---|---:|---:|---:|---:|']
for r in rows:
    b=r['boundaries']
    report.append(f"| {r['sector']} | {r['phase']} | {b['Hedge']} / {b['Ditch']} / {b['Fence']} / {b['Open']} | {r['renderers']} | {r['layout_ms']} | {r['farmland_cpu_ms']} |")
report += ['', 'Coverage is the existing central-land survey. Internal sharing is length-weighted and excludes obstacle-facing edges and district exteriors; the baseline has no districts and uses the central survey envelope. Whole-perimeter sharing is also shown so the exclusions remain visible. Areas are before the gentle warp. Baseline boundary counts are per-field edges with successful hedge placement; candidate counts are unique shared edges. Fence edges retain a continuous low bank even where the model allowance ends.', '',
           'The four requested sectors have approximately 72–74% district occupancy. Their small-area tail consists of attached obstacle-edge plots; isolated remnants are culled and no nominal crop is below 500 m². DeepForest is an additional regression, with roughly 5% central coverage; its tiny farming sample does not meet the four-sector occupancy/sharing targets.', '',
           'Seeds: Varrow default; RiverValley 1000; Marshland 1074; VillageCluster 1555 (explicit sample because that kind is absent from the screenshot tour). DeepForest 1037. The checked-in Marshland recipe has fields = 0.15, rather than 0.3.', '',
           'Verified: C# 9 compilation against installed Unity 6000.6.7f1 API references, deterministic repeated layouts, empty-sector reset, field index versus exhaustive queries, sampled non-overlap and road/river/water/settlement clearance, unique shared-edge owners, minimum nominal area, renderer allowance and far-hedge shadow configuration.', '',
           'Not verified: native Unity noise equivalence, imported prefab renderer counts, terrain contact/material appearance, actual shadow transition in play/photo cameras, full Farmland build time or the 15-second sector budget. The CPU harness omits terrain sampling, object creation, uploads and rendering. No Unity editor/player was opened.', '',
           'No terrain edit is needed: ditches use thin damp-coloured meshes. The broad slope predictor mirrors Ground.Hills from the base commit and should be kept aligned if Claude changes that recipe. Woods are built later, so unfarmed districts/pasture reserve wild ground rather than tracing future wood footprints.', '',
           'Farmland now uses its own seeded random stream. Villages/woods will change once relative to the old shared-stream sequence; subsequent farmland edits cannot shift their random draws.', '',
           'Detailed method, limitations and rerun instructions: [harness README](harness-notes.md).', '',
           'Comparisons: '+ ' · '.join(f'[{s}]({s}_comparison.png)' for s in ['VarrowValley','RiverValley','VillageCluster','Marshland']), '']
(OUT/'report.md').write_bytes('\r\n'.join(report).encode('utf-8'))
(OUT/'harness-notes.md').write_bytes((Path(__file__).parent/'README.md').read_bytes())
