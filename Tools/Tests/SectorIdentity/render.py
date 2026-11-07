"""Draw source-backed plan comparisons and measure nominal canopy from actual successful tree placements."""
import json
import math
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageChops

ROOT = Path(sys.argv[1])
KINDS = ['DeepForest', 'IndustrialZone', 'Airfield', 'RailJunction', 'TownOutskirts', 'Marshland', 'MarketTown']
CROPS = {'Wheat': '#c1aa69', 'YoungCrop': '#75814b', 'Ploughed': '#705445', 'Sunflower': '#aa994e', 'MownHay': '#a2ab7c'}


def font(size):
    """Use the installed system font, with a portable fallback for other reviewers."""
    try:
        return ImageFont.truetype('C:/Windows/Fonts/arial.ttf', size)
    except OSError:
        return ImageFont.load_default(size=size)


def polygon(center, along, length, width):
    """Convert an oriented metre footprint to its four plan corners."""
    x, y = center
    a, b = along
    return [(x+a*u-b*v, y+b*u+a*v) for u, v in [(-length/2, -width/2), (length/2, -width/2), (length/2, width/2), (-length/2, width/2)]]


def draw_plan(data, size=1000, center=(0, 0), span=2000, reserves=False):
    """Render the executed placements at one fixed scale; labels and reserve outlines are optional overlays."""
    im = Image.new('RGB', (size, size), '#929f80')
    d = ImageDraw.Draw(im)
    scale = size / span

    def xy(p):
        return ((p[0]-center[0])*scale+size/2, size/2-(p[1]-center[1])*scale)

    def poly(points, fill, outline=None):
        d.polygon([xy(p) for p in points], fill=fill, outline=outline)

    def line(points, fill, width):
        if len(points) > 1:
            d.line([xy(p) for p in points], fill=fill, width=max(1, round(width*scale)), joint='curve')

    def disc(p, radius, fill):
        x, y = xy(p)
        r = max(.7, radius*scale)
        if x+r < 0 or y+r < 0 or x-r > size or y-r > size:
            return
        d.ellipse((x-r, y-r, x+r, y+r), fill=fill)

    if data['kind'] == 'Marshland':
        for area in data['reserves']:
            poly(area, '#687d64')
    for f in data['plots']:
        poly(f['outline'], CROPS.get(f['crop'], '#b4a570'), '#8a8d6a')
    for s in data['surfaces']:
        poly(s['outline'], '#b2b2a7' if s['concrete'] else '#8c887e')
    line(data['river'], '#547d91', 9)
    for p in data['ponds']:
        disc(p['center'], p['radius']+3, '#657e64')
        disc(p['center'], p['radius'], '#547d91')
    for t in data['trees']:
        disc(t['at'], t['radius'], '#3d603f' if t['group'] == 'Features' else '#4f6d47')
    for road in data['roads']:
        line(road, '#b5a68b', 7)
        line(road, '#bfb49b', 3.5)
    for rail in data['rails']:
        line(rail, '#85817b', 5)
        line(rail, '#484747', 1.5)
    if data['airstripLength'] > 0:
        c, along, length = data['airstripCenter'], data['airstripAlong'], data['airstripLength']
        poly(polygon(c, along, length, 24 if data['reserves'] else 30), '#575b59')
        for u in range(-int(length/2)+25, int(length/2)-25, 24):
            a = [c[0]+along[0]*u, c[1]+along[1]*u]
            b = [a[0]+along[0]*12, a[1]+along[1]*12]
            line([a, b], '#e1ddce', 1)
    for b in data['otherBuildings']:
        poly(polygon(b['at'], [1, 0], 10, 8), '#8a5546', '#5e483d')
    for s in data['sites']:
        yaw = math.radians(s['yaw'])
        poly(polygon(s['at'], [math.sin(yaw), math.cos(yaw)], s['size'][1], s['size'][0]), '#8e6f61', '#493f39')
    for fence in data['fences']:
        line(fence, '#c2c4b8', 2)
    for at in [data['basePosition'], data['hq']]:
        poly(polygon(at, [1, 0], 30, 30), '#707970', '#4a5149')
    if reserves:
        for area in data['reserves']:
            line(area+[area[0]], '#f1d69c', 2)
    return im


def canopy(data, footprint):
    """Union crown discs at 0.5 m pixels, clipped to the same reserved footprint before and after."""
    xs, ys = [p[0] for p in footprint], [p[1] for p in footprint]
    x0, y0 = min(xs), min(ys)
    w, h = math.ceil((max(xs)-x0)*2)+1, math.ceil((max(ys)-y0)*2)+1
    mask, crowns = Image.new('1', (w, h)), Image.new('1', (w, h))
    md, cd = ImageDraw.Draw(mask), ImageDraw.Draw(crowns)
    md.polygon([((x-x0)*2, (y-y0)*2) for x, y in footprint], fill=1)
    for tree in data['trees']:
        x, y = [(tree['at'][i]-[x0, y0][i])*2 for i in range(2)]
        r = tree['radius']*2
        cd.ellipse((x-r, y-r, x+r, y+r), fill=1)
    return 100*sum(ImageChops.logical_and(mask, crowns).histogram()[1:])/sum(mask.histogram()[1:])


def main():
    """Write paired overviews, feature details, an unlabelled contact sheet and machine-readable measurements."""
    overview = Image.new('RGB', (1440, len(KINDS)*760), '#f0ede4')
    od = ImageDraw.Draw(overview)
    blind = Image.new('RGB', (1800, 1200), '#f0ede4')
    numbers = []
    for index, kind in enumerate(KINDS):
        before = json.loads((ROOT/'before'/f'{kind}.json').read_text())
        after = json.loads((ROOT/'after'/f'{kind}.json').read_text())
        pair = Image.new('RGB', (2040, 1120), '#f0ede4')
        pd = ImageDraw.Draw(pair)
        for col, (phase, data) in enumerate([('Before / 596e874', before), ('After / sector identity', after)]):
            im = draw_plan(data)
            pair.paste(im, (20+col*1000, 70))
            pd.text((24+col*1000, 18), phase, fill='#263830', font=font(24))
            overview.paste(im.resize((700, 700), Image.Resampling.LANCZOS), (10+col*720, index*760+40))
        pd.text((24, 1080), f'{kind} | seed {after["seed"]} | 2 km square | source-backed plan, native terrain/graphics substituted', fill='#34443a', font=font(21))
        pair.save(ROOT/f'{kind}_before_after.png')
        od.text((12, index*760+7), f'{kind} - seed {after["seed"]}                 BEFORE                                         AFTER', fill='#263830', font=font(23))
        if index < 6:
            blind.paste(draw_plan(after, 600), ((index%3)*600, (index//3)*600))
        points = [p for area in after['reserves'] for p in area]
        center = [(min(p[i] for p in points)+max(p[i] for p in points))/2 for i in range(2)]
        span = max(max(p[i] for p in points)-min(p[i] for p in points) for i in range(2))+120
        detail = Image.new('RGB', (2040, 1100), '#f0ede4')
        dd = ImageDraw.Draw(detail)
        for col, data in enumerate([before, after]):
            detail.paste(draw_plan(data, 1000, center, span), (20+col*1000, 65))
        dd.text((24, 18), f'{kind} | same location, {span:.0f} m square | BEFORE                                      AFTER', fill='#263830', font=font(25))
        detail.save(ROOT/f'{kind}_detail.png')
        row = {'sector': kind, 'seed': after['seed']}
        for key in ['totalBuildings', 'featureBuildings', 'renderers', 'treeCount', 'coreTrees', 'fields', 'coverage', 'wholeMapFields', 'elapsedMs']:
            row[key] = [before[key], after[key]]
        if kind == 'DeepForest':
            row['nominalCanopyPercent'] = [canopy(data, after['reserves'][0]) for data in [before, after]]
        numbers.append(row)
    overview.quantize(colors=128).save(ROOT/'comparison.png')
    blind.save(ROOT/'unlabelled_after.png')
    (ROOT/'metrics.json').write_text(json.dumps(numbers, indent=2))
    lines = ['# Measured plan comparison', '',
             'All pairs are **before -> after**. Renderer and build-time limits are explained in [the handoff](../README.md).', '',
             '| Sector / seed | Buildings, whole sector | Buildings at planned sites | Scenery renderers, all LODs | Central field survey % | Whole-map parcel area % | Harness build ms |',
             '| --- | ---: | ---: | ---: | ---: | ---: | ---: |']
    for row in numbers:
        def pair(key, fmt):
            """Format both sides from the same JSON used by the renders."""
            return ' -> '.join(format(value, fmt) for value in row[key])
        lines.append(f'| {row["sector"]} / {row["seed"]} | {pair("totalBuildings", ",d")} | {pair("featureBuildings", ",d")} | '
                     f'{pair("renderers", ",d")} | {pair("coverage", ".1f")} | {pair("wholeMapFields", ".1f")} | {pair("elapsedMs", ".0f")} |')
    lines += ['', 'DeepForest nominal canopy at the same reserved location: '
              + ' -> '.join(f'{n:.1f}%' for n in numbers[0]['nominalCanopyPercent'])
              + '. The candidate planted 6,049 core trees; the complete sector stayed within 11,500 trees.', '',
              'These CPU timings exclude native terrain generation, Unity object creation, imports/uploads and GPU work. They do not certify the 15-second game build gate.', '']
    (ROOT/'measurements.md').write_bytes('\r\n'.join(lines).encode('utf-8'))
    for row in numbers:
        print(row)


if __name__ == '__main__':
    main()
