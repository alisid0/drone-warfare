"""Copy the review run's JSON and pictures into Review/ with before/after names and side-by-side pairs."""
from pathlib import Path
import shutil

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
RUN = ROOT / 'Temp/used-look-2/review'
OUT = HERE / 'Review'
OUT.mkdir(exist_ok=True)
try:
    from PIL import Image
except ImportError:
    Image = None

for phase in ('before', 'after'):
    for name in ('landmarks', 'villages', 'hedges', 'sector-4', 'sector-9', 'floor'):
        source = RUN / phase / (name + '.json')
        if source.exists():
            text = source.read_text().replace('\r\n', '\n').replace('\n', '\r\n')
            (OUT / f'{phase}-{name}.json').write_bytes(text.encode())
    for name in ('view-IndustrialZone', 'view-RailJunction', 'landmark-IndustrialZone', 'landmark-RailJunction',
                 'village-view-9', 'village-view-14', 'village-9', 'floor'):
        source = RUN / phase / (name + '.png')
        if source.exists():
            shutil.copyfile(source, OUT / f'{name}-{phase}.png')

if Image:
    for name in ('view-IndustrialZone', 'view-RailJunction', 'village-view-9', 'floor'):
        a, b = OUT / f'{name}-before.png', OUT / f'{name}-after.png'
        if not (a.exists() and b.exists()):
            continue
        first, second = Image.open(a), Image.open(b)
        w, h = first.size
        wide = name == 'floor'
        pair = Image.new('RGB', (w * 2 + 8, h) if wide else (w, h * 2 + 8), (255, 255, 255))
        pair.paste(first, (0, 0)); pair.paste(second, (w + 8, 0) if wide else (0, h + 8))
        pair.save(OUT / f'{name}-pair.png', optimize=True)
for p in sorted(OUT.iterdir()):
    print(f'{p.name} {p.stat().st_size // 1024} KiB')
