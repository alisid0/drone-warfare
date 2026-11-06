"""Generate a second deterministic crown field from the existing photo stamps, without another asset source."""
from pathlib import Path
import runpy

ROOT = Path(__file__).resolve().parents[2]
source = ROOT / 'Tools/ForestAbove/make_detail.py'
scope = runpy.run_path(str(source))
unit = scope['unit']


def alternate(x, z, salt):
    """Use an independent periodic seed so the second field has different crown centers, widths and photographs."""
    return unit(x, z, salt + 137)


scope['sample'].__globals__['unit'] = alternate
n = 512
pixels = bytearray()
for z in reversed(range(n)):
    for x in range(n):
        light, gx, gz = scope['sample']((x + .5) * 128 / n, (z + .5) * 128 / n)
        pixels.extend(round(c * 255) for c in (light, gx * .5 + .5, gz * .5 + .5))
folder = ROOT / 'Assets/DroneSandbox/Resources/Textures'
scope['png'](folder / 'ForestDetailB.png', n, pixels)
meta = (folder / 'ForestDetail.png.meta').read_text().replace('da29c1a119c84a899701210a1fb7b6e2', 'bd6e20b64b324fa1a0372f894e497c90')
(folder / 'ForestDetailB.png.meta').write_bytes(meta.replace('\n', '\r\n').encode())
