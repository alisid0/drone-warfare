"""Compose fixed-location albedo comparisons without changing pixels or independently redrawing topology."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent / 'Review'
assert json.loads((ROOT / 'before/legacy.json').read_text()) == json.loads((ROOT / 'after/legacy.json').read_text()), 'Legacy field geometry changed'
audit = json.loads((ROOT / 'after/agreement.json').read_text())
assert audit['cutMismatches'] == 0 and audit['cutCoverMismatches'] == 0, 'Sector cut ownership changed'
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 18)
small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 14)
panels = []
for name in ('wheat-300', 'wheat-2500', 'ploughed-300', 'ploughed-2500', 'road-farm-300', 'road-farm-2500', 'village-300', 'village-2500', 'sector-edge', 'sector-edge-close'):
    pair = Image.new('RGB', (1556, 516), '#142027')
    draw = ImageDraw.Draw(pair)
    title = name.replace('-2500', ' | 2.5 km AGL equivalent').replace('-300', ' | 300 m AGL equivalent').replace('-', ' ')
    draw.text((10, 5), title + ' | same world position, seed 1200', font=font, fill='#e4edf0')
    for i, phase in enumerate(('before', 'after')):
        image = Image.open(ROOT / phase / (name + '.ppm'))
        pair.paste(image, (10 + i * 778, 58))
        if name == 'sector-edge-close':
            for y in range(58, 490, 16):
                draw.line((394 + i * 778, y, 394 + i * 778, y + 6), fill='#a8d7d7', width=1)
        draw.text((10 + i * 778, 33), 'Before: e45f400' if i == 0 else 'After: shared bounded parcels', font=small, fill='#b9d1db')
    caption = 'Parcel geometry: sector crops on meadow at left of dashed cut, 2 m world painter at right; no Ground/Farmland shading' if name == 'sector-edge-close' else 'CPU albedo harness | no HDRP lighting or props | 45 degree vertical FOV; sector overview spans 3.2 km'
    draw.text((10, 493), caption, font=small, fill='#b9d1db')
    pair.save(ROOT / (name + '.png'))
    panels.append(pair)
sheet = Image.new('RGB', (1556, len(panels) * 516), '#142027')
for i, panel in enumerate(panels):
    sheet.paste(panel, (0, i * 516))
sheet.resize((1167, len(panels) * 387), Image.Resampling.LANCZOS).save(ROOT / 'comparison-sheet.png')
