"""Copy the town review's measurements and pictures into Review/: JPEG before/after pictures and side-by-side pairs."""
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
RUN = ROOT / 'Temp/towns/review'
OUT = HERE / 'Review'
OUT.mkdir(exist_ok=True)
from PIL import Image

for phase in ('before', 'after'):
    source = RUN / phase / 'measure.json'
    if source.exists():
        text = source.read_text().replace('\r\n', '\n').replace('\n', '\r\n')
        (OUT / f'measure-{phase}.json').write_bytes(text.encode())
for place in ('brenmoor', 'halvard', 'ashcombe'):
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
for name in ('Scenery_House_Semi', 'Scenery_Townhouse_Old', 'Scenery_FillingStation'):
    source = ROOT / 'Temp/towns/previews' / (name + '.png')
    if source.exists():
        Image.open(source).convert('RGB').save(OUT / (name + '.jpg'), quality=85, optimize=True)
for p in sorted(OUT.iterdir()):
    print(f'{p.name} {p.stat().st_size // 1024} KiB')
