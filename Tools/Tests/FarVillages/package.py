"""Copy the far-village review's measurements and pictures into Review/: JPEG before/after pictures, before-above-after
pairs, and hd180's 300 m shot above the harness's before picture of the same frame."""
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
RUN = ROOT / 'Temp/far-villages/review'
OUT = HERE / 'Review'
OUT.mkdir(exist_ok=True)
SHOT = Path('C:/Users/ali10/AppData/Local/Temp/dwprev/hd180/91_flight_1_village_300m.png')

for phase in ('before', 'after'):
    source = RUN / phase / 'measure.json'
    if source.exists():
        text = source.read_text().replace('\r\n', '\n').replace('\n', '\r\n')
        (OUT / f'measure-{phase}.json').write_bytes(text.encode())
for place in ('ashcombe', 'brenmoor'):
    for view in ('300m', '2500m'):
        pictures = []
        for phase in ('before', 'after'):
            source = RUN / phase / f'{place}-{view}.png'
            if not source.exists():
                continue
            image = Image.open(source).convert('RGB')
            image.save(OUT / f'{place}-{view}-{phase}.jpg', quality=88, optimize=True)
            pictures.append(image)
        if len(pictures) == 2:
            w, h = pictures[0].size
            pair = Image.new('RGB', (w, h * 2 + 8), (255, 255, 255))
            pair.paste(pictures[0], (0, 0)); pair.paste(pictures[1], (0, h + 8))
            pair.resize((w // 2, (h * 2 + 8) // 2), Image.LANCZOS).save(OUT / f'{place}-{view}-pair.jpg', quality=85, optimize=True)
before = RUN / 'before/ashcombe-300m.png'
if SHOT.exists() and before.exists():
    shot, render = Image.open(SHOT).convert('RGB'), Image.open(before).convert('RGB')
    w, h = render.size
    pair = Image.new('RGB', (w, h * 2 + 8), (255, 255, 255))
    pair.paste(shot.resize((w, h)), (0, 0)); pair.paste(render, (0, h + 8))
    pair.resize((w // 2, (h * 2 + 8) // 2), Image.LANCZOS).save(OUT / 'hd180-300m-shot-vs-harness.jpg', quality=85, optimize=True)
for p in sorted(OUT.iterdir()):
    print(f'{p.name} {p.stat().st_size // 1024} KiB')
