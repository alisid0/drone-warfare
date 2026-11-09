"""Make labelled CPU-render comparisons and a crossing animation; never label these as Unity captures."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Review'
FONT = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 23)
SMALL = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 17)

def caption(image, title, detail):
    """Keep provenance and scale on each exported view."""
    sheet = Image.new('RGB', (image.width, image.height + 72), '#18221f')
    sheet.paste(image, (0, 72))
    draw = ImageDraw.Draw(sheet)
    draw.text((16, 8), title, font=FONT, fill='#edf4ec')
    draw.text((16, 41), detail, font=SMALL, fill='#bfcac1')
    return sheet

for phase in ('before', 'after'):
    for file in (OUT / phase).glob('*.ppm'):
        if file.stem.startswith('crossing'):
            continue
        image = Image.open(file)
        caption(image, f'{phase.capitalize()} | {file.stem}', 'CPU albedo | fixed seed 1200 | 45 degree vertical FOV | no HDRP lighting or props').save(file.with_suffix('.png'))

for spot in ('road-farm', 'farmland-forest', 'village', 'forest-edge'):
    for altitude in (2500, 6000):
        name = f'{spot}-{altitude}'
        paths = [OUT / phase / f'{name}.ppm' for phase in ('before', 'after')]
        if not all(p.exists() for p in paths):
            continue
        pair = Image.new('RGB', (1280, 2 * (720 + 72)), '#18221f')
        for row, (phase, path) in enumerate(zip(('Before: integrated f5a5877', 'After: regional masks'), paths)):
            panel = caption(Image.open(path), f'{phase} | {spot} | {altitude / 1000:g} km', 'CPU albedo | same location and scale | actual BC1 pages after | no HDRP lighting or props')
            pair.paste(panel, (0, row * 792))
        pair.save(OUT / f'{name}.png')

frames = []
for file in sorted((OUT / 'after').glob('crossing-*.ppm')):
    number = int(file.stem.split('-')[-1])
    panel = caption(Image.open(file), f'Tile to regional crossing | eye moved {number * 200} m', 'CPU macro colour | fixed 3.6 km ground strip | 900-1300 m blend | no geometry/normal simulation')
    frames.append(panel)
if frames:
    frames[0].save(OUT / 'crossing.webp', save_all=True, append_images=frames[1:], duration=450, loop=0, lossless=True)
    frames[len(frames) // 2].save(OUT / 'crossing.png')
