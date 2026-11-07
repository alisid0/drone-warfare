"""Make the near-bubble review: build the harness, render, and write JPEGs and numbers into Tools/NearBubble/Review.

One process on one thread for the pictures (page colour painting uses two, as in the game). Never starts Unity or
uses the GPU. About 40 s and under 1 GB on the 15 GB machine.
  py -3.13 -I Tools/NearBubble/run.py
"""
from pathlib import Path
import os
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
TEMP = ROOT / 'Temp/near-bubble'
REVIEW = HERE / 'Review'
DOTNET = Path('C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe')

subprocess.run([sys.executable, '-I', str(HERE / 'prepare.py')], check=True)
out = TEMP / 'review'
if out.exists():
    shutil.rmtree(out)
out.mkdir(parents=True)
env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_gcServer='0', DOTNET_PROCESSOR_COUNT='2')
result = subprocess.run([str(DOTNET), str(TEMP / 'bin/Release/net8.0/harness.dll'), str(out), str(TEMP / 'textures')],
                        env=env, capture_output=True, text=True)
(out / 'run.log').write_text(result.stdout + result.stderr)
print(result.stdout[-1500:] + result.stderr[-3000:])
result.check_returncode()

from PIL import Image
REVIEW.mkdir(exist_ok=True)
for p in sorted(out.glob('*.ppm')):
    Image.open(p).save(REVIEW / (p.stem + '.jpg'), quality=88)
shutil.copy2(out / 'review.json', REVIEW / 'review.json')
# One sheet: before on the left, after on the right, for each scene at 50, 100 and 300 m.
rows = [(s, a) for s in ('field', 'road', 'village') for a in ('50m', '100m', '300m')]
w, h, gap = 640, 360, 6
sheet = Image.new('RGB', (2 * w + gap, len(rows) * (h + gap) - gap), (255, 255, 255))
for k, (scene, altitude) in enumerate(rows):
    for c, phase in enumerate(('before', 'after')):
        image = Image.open(REVIEW / f'{scene}-{altitude}-{phase}.jpg').resize((w, h), Image.LANCZOS)
        sheet.paste(image, (c * (w + gap), k * (h + gap)))
sheet.save(REVIEW / 'sheet.jpg', quality=85)
subprocess.run([sys.executable, '-I', str(HERE / 'shader_check.py')], check=True,
               stdout=open(REVIEW / 'shader-check.txt', 'w'))
print('review written to', REVIEW)
