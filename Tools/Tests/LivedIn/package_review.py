"""Package the twelve sequential review renders with their provenance and a compact village comparison."""
from pathlib import Path
import html
import json
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
REVIEW = HERE / 'Review'
villages = [(6, 'Tolbeck'), (7, 'Grisham'), (10, 'Dunmere'), (13, 'Redwick'), (14, 'Sorrel')]
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 19)
sheet = Image.new('RGB', (1280, 2140), '#182126')
draw = ImageDraw.Draw(sheet)
cards = []
for row, (owner, name) in enumerate(villages):
    for col, distance in enumerate((150, 400)):
        filename = f'Village{owner}_{distance}m.png'
        image = Image.open(REVIEW / filename).convert('RGB')
        image.thumbnail((640, 400), Image.Resampling.LANCZOS)
        sheet.paste(image, (col * 640, row * 428 + 28))
        label = f'{name} / {distance} m altitude'
        draw.text((col * 640 + 12, row * 428 + 3), label, font=font, fill='white')
        cards.append((filename, label))
sheet.save(REVIEW / 'villages-contact.jpg', quality=90)
cards += [('TownOutskirts_400m.png', 'Town Outskirts / 400 m altitude'), ('Field_200m.png', 'Farm field / 200 m altitude')]
body = '\n'.join(f'<figure><a href="{filename}"><img src="{filename}" loading="lazy" alt="{html.escape(label)}"></a><figcaption>{html.escape(label)}</figcaption></figure>' for filename, label in cards)
page = '''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Drone Warfare: lived-in villages and fields</title>
<style>body{font:16px/1.5 system-ui;background:#182126;color:#e4ecee;margin:24px auto;max-width:1360px;padding:0 16px}a{color:#a8d7bd}.views{display:grid;grid-template-columns:repeat(auto-fit,minmax(420px,1fr));gap:20px}figure{margin:0}img{width:100%;height:auto}figcaption{padding:8px 0}p{max-width:1000px}@media(max-width:480px){.views{display:block}}</style>
<h1>Lived-in villages and fields</h1>
<p>World seed 1200. Five villages selected by a fixed random sample, with both requested altitudes for each. Click an image for the full 1280 × 800 render. These are sequential Blender 5.2 geometry reviews, not Unity/HDRP captures or GPU measurements.</p>
<p>Buildings, accepted placements, scale, garden meshes, field margins and soil/growth colours come from production code. Lighting, model material approximation, 10 m terrain sampling, 2 m ground colour sampling and field hedge silhouettes are review-only. The crop shader, full splat maps, shadows and LOD transitions still need Unity review.</p>
<p><a href="../HANDOFF.md">Implementation, patches and build checklist</a> · <a href="villages-contact.jpg">Five-village contact sheet</a></p>
<div class="views">''' + body + '</div></html>\n'
(REVIEW / 'index.html').write_bytes(page.replace('\n', '\r\n').encode('utf-8'))
checks = json.loads((REVIEW / 'checks.json').read_text())
totals = {key: sum(row[key] for row in checks) for key in ('n', 'old', 'shaped', 'farm', 'cultivated', 'sheds', 'logs', 'untended', 'roofs', 'overlap')}
print(json.dumps(totals))
print('Mean sampled working/soil area:', sum(row['worn'] for row in checks) / len(checks))
