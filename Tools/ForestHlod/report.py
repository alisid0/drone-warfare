"""Package exact-source before/after renders, submissions and reproducible budget estimates."""
from pathlib import Path
import json, shutil, math
from PIL import Image, ImageDraw
root=Path(__file__).resolve().parents[2];out=root/'Tools/ForestHlod/Review';temp=root/'Temp/forest-hlod'
rows=[]
for phase in ['before','after']:
    values=json.loads((temp/phase/'metrics.json').read_text())
    for row in values:
        data=json.loads((temp/phase/(row['name']+'.json')).read_text())
        forests=[m for m in data['meshes'] if m['name'].startswith('Forest canopy')]
        row['forestVertices']=sum(len(m['vertices']) for m in forests)
        row['maximumBlockVertices']=max(len(m['vertices']) for m in forests)
        row['maximumBlockTriangles']=max(len(m['indices'])//3 for m in forests)
        texture_bytes=0;estimated=0
        for m in forests:
            pic=Image.open(temp/phase/m['texture']);pixels=pic.width*pic.height
            texture_bytes+=math.ceil(pixels*4*4/3)
            estimated+=len(m['vertices'])*116+len(m['indices'])*12+math.ceil(pixels*4*4/3)
        row['forestMipmappedTextureBytes']=texture_bytes
        row['estimatedForestResidentBytes']=estimated
    rows+=values
for file in (temp/'after').glob('*seed-*.json'):shutil.copyfile(file,out/file.name)
shutil.copyfile(temp/'after'/'near-tier.json',out/'near-tier.json')
(out/'metrics.json').write_text(json.dumps(rows,indent=2))
lines=['# Forest submissions against 788df6f','','Offline production-source harness; 1600 x 900 review cameras. Triangle/draw submissions are not measured GPU time.','','| View | Cards before / after | Forest triangles before / after | Total draws before / after | Forest texture MiB before / after |','|---|---:|---:|---:|---:|']
for index,view in enumerate(['300m','2500m','9000m','edge']):
    a,b=rows[index],rows[index+4]
    lines.append(f"| {view} | {a['cards']:,} / {b['cards']:,} | {a['forestTriangles']:,} / {b['forestTriangles']:,} | {a['draws']} / {b['draws']} | {a['forestMipmappedTextureBytes']/1048576:.2f} / {b['forestMipmappedTextureBytes']/1048576:.2f} |")
lines+=['','The same seed-1 interior forest is at (16500, -4500). Edge target: '+str(rows[7]['world'])+'.', '',
        'Fine blocks: 32 m interiors, 16 m glade rims, 8 m external margins; 516 x 516 RGBA32 mipmapped crown texture (512 interior pixels plus a two-texel gutter). Distant blocks retain 64/128 m geometry; texture interiors are 128/64 pixels plus gutters. Each occupied block is one opaque draw, with no alpha test or shadow casting.', '',
        'The builder retains the 0.7 ms main-thread slice and yields within sampling, stamping and skirt loops. Native upload and final array copies remain indivisible; profile these in HDRP.', '',
        'Resident estimates in metrics.json assume 116 bytes/vertex (source, optional bubble copy, readable mesh and GPU mesh), 12 bytes/index (source/mesh/GPU) and RGBA mipmaps. They exclude managed object overhead, construction dictionaries/lists, driver overhead and temporarily overlapping old/new blocks.', '',
        'Required Unity gates remain 114 fps sandbox, 121 fps wide, low-flight p95 <= 12.2 ms at 1600 x 900 on the RTX 3050 laptop. No new Unity FPS or GPU timing is claimed.']
(out/'counts.md').write_text('\n'.join(lines)+'\n')
# Preserve each full-resolution view; the contact sheet is a navigation aid, not the only evidence.
canvas=Image.new('RGB',(3200,4*944),(19,24,23));draw=ImageDraw.Draw(canvas)
for j,view in enumerate(['300m','2500m','9000m','edge']):
    for i,phase in enumerate(['before','after']):
        canvas.paste(Image.open(out/f'forest_{view}_{phase}.png').convert('RGB'),(i*1600,j*944+44))
        draw.text((i*1600+18,j*944+14),f'{view} | '+('788df6f' if phase=='before' else 'forest-natural')+' | OFFLINE BLENDER, NOT HDRP',fill='white')
canvas.save(out/'forest-comparison.png')
canvas=Image.new('RGB',(3200,944),(19,24,23));draw=ImageDraw.Draw(canvas)
for i,name in enumerate(['calibration_cards','calibration_canopy']):
    canvas.paste(Image.open(out/(name+'.png')).convert('RGB'),(i*1600,44));draw.text((i*1600+18,14),name+' | MATCHED 600 m CAMERA / FIXED EXPOSURE',fill='white')
canvas.save(out/'calibration-comparison.png')
print('\n'.join(lines[:10]))
