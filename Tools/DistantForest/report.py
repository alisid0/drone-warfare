"""Convert harness textures, then measure masked render luminance and publish matched greyscale comparisons."""
from pathlib import Path
import json
import shutil
import sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Tools/DistantForest/Review'


def linear(values):
    """Decode sRGB before measuring luminance; gamma-encoded greyscale values are reported separately."""
    return np.where(values <= .04045, values / 12.92, ((values + .055) / 1.055) ** 2.4)


def textures():
    """Only change the container format; the renderer sees the exact RGB bytes exported by production C#."""
    for phase in ['before', 'after']:
        for path in (ROOT / 'Temp/distant-forest' / phase).glob('*.ppm'):
            Image.open(path).save(path.with_suffix('.png'))


def report():
    """Use visible forest/pasture masks from the same geometry, excluding antialiased boundary pixels."""
    rows = []
    for altitude in [300, 1000, 2500, 9000]:
        for phase in ['before', 'after']:
            path = OUT / f'forest_{altitude}m_{phase}.png'
            if not path.exists(): continue
            rgb = np.asarray(Image.open(path).convert('RGB'), dtype=float) / 255
            labels = np.asarray(Image.open(ROOT / 'Temp/distant-forest' / phase / f'mask_{altitude}.png').convert('RGB'))
            forest = (labels[:,:,0] > 250) & (labels[:,:,1] < 3) & (labels[:,:,2] < 3)
            pasture = (labels[:,:,1] > 250) & (labels[:,:,0] < 3) & (labels[:,:,2] < 3)
            fields = (labels[:,:,0] > 250) & (labels[:,:,1] > 250) & (labels[:,:,2] < 3)
            lum = linear(rgb) @ [.2126,.7152,.0722]
            grey = np.where(lum <= .0031308, lum*12.92, 1.055*lum**(1/2.4)-.055)
            Image.fromarray(np.uint8(np.clip(grey*255,0,255))).save(path.with_name(path.stem+'_grey.png'))
            row = {'altitude':altitude,'phase':phase,'forestPixels':int(forest.sum()),'pasturePixels':int(pasture.sum()),
                   'forestY':float(lum[forest].mean()),'pastureY':float(lum[pasture].mean()),
                   'forestGrey':float(grey[forest].mean()*255),'pastureGrey':float(grey[pasture].mean()*255)}
            row['linearRatio'] = row['forestY']/row['pastureY']
            row['greyscaleRatio'] = row['forestGrey']/row['pastureGrey']
            if fields.any(): row['forestFieldRatio'] = row['forestY']/float(lum[fields].mean())
            rows.append(row)
    (OUT/'luminance.json').write_text(json.dumps(rows,indent=2)+'\n')
    for phase in ['before','after']:
        for name in ['metrics','canopy-budget','close-budget','distant-budget','checks','crown-queries']:
            src=ROOT/'Temp/distant-forest'/phase/(name+'.json')
            if src.exists(): shutil.copyfile(src,OUT/(name+'-'+phase+'.json'))
    if (ROOT/'Temp/distant-forest/render-checks.json').exists():
        shutil.copyfile(ROOT/'Temp/distant-forest/render-checks.json', OUT/'render-checks.json')
    body = ['<!doctype html><meta charset="utf-8"><title>Distant forest review</title><style>body{background:#18201c;color:#eee;font:16px system-ui;max-width:1500px;margin:24px auto}img{width:49%}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #536057}</style>',
            '<h1>Distant forest — fixed Blender structure review</h1><p>Before: 9f8a34b. Production meshes, texture bytes and normals. Fixed 35° sun, exposure 0, Standard transform. No fog, HDRP, native uploads or GPU timing. Ground pasture reference: sRGB (105,126,86). Camera labels are altitude above terrain; oblique slant ranges are longer.</p>',
            '<table><tr><th>Altitude</th><th>Revision</th><th>Forest / pasture linear Y</th><th>Forest / pasture greyscale</th></tr>']
    for row in rows:
        body.append(f"<tr><td>{row['altitude']} m</td><td>{row['phase']}</td><td>{row['linearRatio']:.3f}</td><td>{row['forestGrey']:.1f} / {row['pastureGrey']:.1f} = {row['greyscaleRatio']:.3f}</td></tr>")
    body.append('</table>')
    for altitude in [300,1000,2500,9000]:
        body.append(f'<h2>{altitude} m — before / after</h2>')
        for suffix in ['', '_grey']:
            for phase in ['before','after']:
                body.append(f'<a href="forest_{altitude}m_{phase}{suffix}.png"><img src="forest_{altitude}m_{phase}{suffix}.png"></a>')
    (OUT/'index.html').write_text('\n'.join(body)+'\n',encoding='utf-8')
    print(json.dumps(rows,indent=2))


textures()
if '--textures-only' not in sys.argv: report()
