"""Package full-resolution before/after sheets and the measured submission budget."""
from pathlib import Path
import json,shutil
from PIL import Image,ImageDraw,ImageFont
root=Path(__file__).resolve().parents[2];out=root/'Tools/ForestHlod/Review';exports=root/'Temp/forest-hlod/exports'
font_path='C:/Windows/Fonts/segoeui.ttf'
font=ImageFont.truetype(font_path,24);small=ImageFont.truetype(font_path,18)
metrics=json.loads((exports/'metrics.json').read_text());rows={r['name']:r for r in metrics}
for region in ['forest','town']:
    canvas=Image.new('RGB',(3200,4*956+80),(20,27,31));draw=ImageDraw.Draw(canvas)
    draw.text((24,18),f'DRONE WARFARE / {region.upper()} — offline Blender harness, 1600 × 900 per panel',font=font,fill='white')
    for row,(view,label) in enumerate([('above','Above / 1.2 km'),('300m','300 m camera range'),('2500m','2.5 km camera range'),('9000m','9 km camera range')]):
        for col,phase in enumerate(['before','after']):
            name=f'{region}_{view}_{phase}';y=80+row*956
            canvas.paste(Image.open(out/(name+'.png')).convert('RGB'),(col*1600,y+52))
            m=rows[name]
            draw.text((col*1600+18,y+6),f'{label} · {phase.upper()} · {m["trees"]:,} trees / {m["houses"]:,} houses / {m["draws"]:,} draws / {m["triangles"]:,} tris',font=small,fill=(209,225,227))
    canvas.save(out/f'{region}-comparison.png',optimize=True)
canvas=Image.new('RGB',(3200,970),(20,27,31));draw=ImageDraw.Draw(canvas)
for i,name in enumerate(['calibration_cards','calibration_canopy']):
    canvas.paste(Image.open(out/(name+'.png')).convert('RGB'),(i*1600,70))
    draw.text((i*1600+18,14),f'{name.replace("_"," ").title()} / same camera at 600 m, 60° pitch / linear-luminance ROI outlined',font=small,fill='white')
    draw.rectangle((i*1600+600,370,i*1600+1000,670),outline=(250,200,70),width=2)
canvas.save(out/'calibration-comparison.png',optimize=True)
for name in ['metrics.json','near-tier.json','checks-seed-1.json','checks-seed-2.json','checks-seed-3.json']:
    shutil.copyfile(exports/name,out/name)
text='| View | Mesh trees | Cards | Houses | Forest blocks / tris | Town blocks / tris | Draw submissions | Total tris |\n|---|---:|---:|---:|---:|---:|---:|---:|\n'
for m in metrics:
    text+=f'| {m["name"].replace("_"," ")} | {m["meshTrees"]:,} | {m["cards"]:,} | {m["houses"]:,} | {m["forestBlocks"]:,} / {m["forestTriangles"]:,} | {m["townBlocks"]:,} / {m["townTriangles"]:,} | {m["draws"]:,} | {m["triangles"]:,} |\n'
(out/'counts.md').write_bytes(text.replace('\n','\r\n').encode())
print('Packaged forest, town and calibration sheets plus count tables.')
