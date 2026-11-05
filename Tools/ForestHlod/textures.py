from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[2]
for p in (root/'Temp/forest-hlod').rglob('*.ppm'):
    Image.open(p).save(p.with_suffix('.png'))
