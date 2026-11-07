"""Assemble same-camera review sheets, model turntables and the measured JSON evidence."""
from pathlib import Path
import json
import shutil
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
REVIEW = Path(__file__).resolve().parent / 'Review'
FONT = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 24)
SMALL = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 17)


def comparison(kind):
    """Full frames retain identical camera position and lens on each side of the comparison."""
    sheet = Image.new('RGB', (1600, 1120), '#182127')
    draw = ImageDraw.Draw(sheet)
    for row, distance in enumerate((300, 2500)):
        for column, phase in enumerate(('before', 'after')):
            x, y = column * 800, row * 550
            draw.text((x + 16, y + 10), f'{kind} | {distance} m | {phase}', font=FONT, fill='white')
            frame = Image.open(REVIEW / f'{kind}_{distance}m_{phase}.png').convert('RGB')
            sheet.paste(frame.resize((800, 500), Image.Resampling.LANCZOS), (x, y + 44))
    draw.text((16, 1100), 'Offline production geometry / Blender; not HDRP captures or GPU timing evidence.', font=SMALL, fill='#b9c9cd')
    sheet.save(REVIEW / (kind + '_comparison.jpg'), quality=94)


def models():
    """Eight actual rendered angles form each loop; a contact sheet shows the three delivered assets."""
    records = json.loads((REVIEW / 'models/models.json').read_text())
    sheet = Image.new('RGB', (1536, 580), '#182127'); draw = ImageDraw.Draw(sheet)
    for column, item in enumerate(records):
        name = item['name']
        frames = [Image.open(REVIEW / 'models' / f'{name}-{i}.png').convert('RGB') for i in range(8)]
        frames[0].save(REVIEW / 'models' / (name + '.webp'), save_all=True, append_images=frames[1:], duration=220, loop=0, quality=92)
        sheet.paste(frames[1], (column * 512, 36))
        draw.text((column * 512 + 12, 8), name.removeprefix('Scenery_'), font=FONT, fill='white')
        draw.text((column * 512 + 12, 554), 'LOD triangles: ' + ' / '.join(map(str, item['triangles'])), font=SMALL, fill='#b9c9cd')
    sheet.save(REVIEW / 'models/contact.jpg', quality=94)


for kind in ('IndustrialZone', 'Airfield', 'RailJunction'):
    comparison(kind)
models()
for phase in ('before', 'after'):
    for name in ('validation', 'picture-mono'):
        source = ROOT / f'Temp/landmark-realism/{phase}/results/{name}.json'
        if source.exists(): shutil.copyfile(source, REVIEW / (name + '-' + phase + '.json'))
