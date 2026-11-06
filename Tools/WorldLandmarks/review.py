"""Package source-backed world footprints, image comparisons and validation metrics for Claude."""
from pathlib import Path
import json
import math
import statistics
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
source = root / 'Temp/world-landmarks/results'
out = root / 'Tools/WorldLandmarks/Review'
out.mkdir(parents=True, exist_ok=True)


def font(size):
    """Use a readable installed font with a portable fallback."""
    try:
        return ImageFont.truetype('C:/Windows/Fonts/arial.ttf', size)
    except OSError:
        return ImageFont.load_default(size=size)


data = json.loads((source / 'world.json').read_text())
palette = ['#687d50','#aaa06b','#344b30','#526a48','#3e6378','#a18c80','#838665','#858379']
im = Image.new('RGB', (data['n'],data['n']))
im.putdata([tuple(bytes.fromhex(palette[i][1:])) for i in data['land']])
im = im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).resize((1400,1400))
canvas = Image.new('RGB',(1840,1530),'#14232b'); canvas.paste(im,(45,90))
d = ImageDraw.Draw(canvas)
d.text((45,22),f"Persistent world landmarks / seed {data['seed']} / 56 x 56 km",fill='white',font=font(32))


def xy(p):
    """Keep reserve geometry to scale instead of enlarging small footprints into map icons."""
    return 45+(p[0]+28000)/40,90+(28000-p[1])/40


for line in data['roads']:
    d.line([xy(p) for p in line],fill='#b8aa8b',width=1)
for line in data['rails']:
    d.line([xy(p) for p in line],fill='#303138',width=2)
colors = {'IndustrialZone':'#ffbd6b','Airfield':'#6ed9ff','RailJunction':'#ef91ea'}
row = 120
for f in data['features']:
    c,a=f['center'],f['along']; color=colors[f['kind']]
    for reserve in [True,False]:
        u=f['halfLength']+(f['clearance'] if reserve else 0);v=f['halfWidth']+(f['clearance'] if reserve else 0)
        points=[xy([c[0]+a[0]*x-a[1]*z,c[1]+a[1]*x+a[0]*z]) for x,z in [(-u,-v),(u,-v),(u,v),(-u,v)]]
        d.polygon(points,fill=None if reserve else color,outline=color,width=1)
    x,y=xy(c); d.text((x+12,y-12),str(f['id']),font=font(20),fill='white',stroke_width=2,stroke_fill='#24353d')
    d.text((1480,row),f"{f['id']}   {f['kind']}",font=font(21),fill=color)
    d.text((1480,row+28),f"{c[0]/1000:.2f}, {c[1]/1000:.2f} km",font=font(17),fill='#c0cbd1');row+=76
d.text((1480,1020),'Fill: footprint\nOutline: 64 m reserve\nTan: roads / access\nDark: world rail / sidings',font=font(19),fill='#d3dde2',spacing=12)
d.line([(65,1450),(315,1450)],fill='white',width=4);d.text((65,1460),'10 km',font=font(19),fill='white')
canvas.save(out/'world-map.png')

for kind in ['IndustrialZone','Airfield','RailJunction']:
    files=[out/f'{kind}_{tier}_{distance}m.png' for tier,distance in [('hlod',2500),('sector',2500),('sector',1000)]]
    if not all(p.exists() for p in files):
        continue
    sheet=Image.new('RGB',(2400,590),'#14232b');draw=ImageDraw.Draw(sheet)
    for i,(path,label) in enumerate(zip(files,['HLOD / 2.5 km','Built sector / 2.5 km','Built sector / 1 km'])):
        sheet.paste(Image.open(path).resize((800,500)),(i*800,55));draw.text((i*800+18,15),label,font=font(25),fill='white')
    draw.text((18,558),f'{kind} / same world feature and terrain / offline Blender reconstruction of harness output',font=font(18),fill='#b7c4cb')
    sheet.save(out/f'{kind}-comparison.png')

validation=json.loads((source/'validation.json').read_text())
(out/'validation.json').write_text(json.dumps(validation,indent=2))
timings=[r['featureMs'] for r in validation['seeds']]
counts=[]
for kind in ['IndustrialZone','Airfield','RailJunction']:
    feature=json.loads((source/f'{kind}.json').read_text())
    counts.append({'kind':kind,'id':feature['id'],'center':feature['center'],'draws':feature['draws'],'triangles':feature['triangles']})
(out/'geometry.json').write_text(json.dumps(counts,indent=2))
for path in source.glob('benchmark-*.json'):
    (out/path.name).write_text(json.dumps(json.loads(path.read_text()),indent=2))
print(json.dumps({'seeds':len(timings),'featureMedianMs':statistics.median(timings),'featureMaxMs':max(timings),'geometry':counts},indent=2))
