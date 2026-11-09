"""Scale-matched, linear-light CPU albedo renders of production tile inputs and the actual sector painters."""
from pathlib import Path
import json
import math
import re
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(sys.argv[1]).resolve()
W, H = 768, 432
FONT = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 20)
SMALL = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 16)

def linear(a):
    """Decode sRGB before filtering and splat blending."""
    return np.where(a <= .04045, a / 12.92, ((a + .055) / 1.055) ** 2.4)

def srgb(a):
    """Encode only after the material has been evaluated in linear space."""
    a = np.maximum(a, 0)
    return np.where(a <= .0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - .055)

def bilinear(a, u, v, repeat=True):
    """Sample at texel centres with Unity-style repeat or clamp addressing."""
    h, w = a.shape[:2]
    x, y = u * w - .5, v * h - .5
    ix, iy = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = (x - ix)[..., None], (y - iy)[..., None]
    index = (lambda q, n: q % n) if repeat else (lambda q, n: np.clip(q, 0, n - 1))
    p = a[index(iy, h), index(ix, w)] * (1 - fx) + a[index(iy, h), index(ix + 1, w)] * fx
    q = a[index(iy + 1, h), index(ix, w)] * (1 - fx) + a[index(iy + 1, h), index(ix + 1, w)] * fx
    return p * (1 - fy) + q * fy

def mip_chain(a):
    """Box-filter linear mipmaps; no gamma-space averaging."""
    chain = [a]
    while a.shape[0] > 1:
        a = (a[::2, ::2] + a[1::2, ::2] + a[::2, 1::2] + a[1::2, 1::2]) * .25
        chain.append(a)
    return chain

def filtered(chain, u, v, footprint):
    """Trilinear reference for the top-down camera's constant world-space pixel footprint."""
    lod = np.clip(math.log2(max(footprint * 512, 1)), 0, len(chain) - 1)
    lo, hi = int(lod), min(int(lod) + 1, len(chain) - 1)
    return bilinear(chain[lo], u, v) * (1 - (lod - lo)) + bilinear(chain[hi], u, v) * (lod - lo)

def band(x, period, width, footprint):
    """Same area integral as GroundBand in the compiled HLSL."""
    a, b = (x - footprint * .5) / period, (x + footprint * .5) / period
    def integral(t):
        return np.floor(t) * (width / period) + np.minimum(t % 1, width / period)
    return np.clip((integral(b) - integral(a)) * period / footprint, 0, 1)

metres = [7, 6, 5, 12, 8, 8, 8, 8, 8, 8]
tints = [[.40, .74, .57], [.21, .55, .59], [1.2, 1.23, 1.28], [1, 1, 1], [.68, .83, .80]]
palette = (ROOT / 'Assets/DroneSandbox/Scripts/FarDetailPalette.cs').read_text().split('public static readonly Vector3[] Means = {')[1].split('};')[0]
means = np.array([[float(x.strip().rstrip('f')) for x in m.split(',')] for m in re.findall(r'new Vector3\(([^)]+)\)', palette)])
textures = []
for k, name in enumerate(['meadow', 'lush', 'dirt', 'rock', 'meadow']):
    a = linear(np.asarray(Image.open(ROOT / f'Assets/DroneSandbox/Resources/Textures/Terrain/{name}_diff.jpg')).astype(float) / 255)
    v, u = np.mgrid[0:512, 0:512] / 512 + .5 / 512
    textures.append(mip_chain(bilinear(a, u, v) * tints[k]))
for k in range(5):
    a = np.fromfile(OUT / f'crop-{k}.rgba', np.uint8).reshape(512, 512, 4)[..., :3] / 255
    textures.append(mip_chain(linear(a)))

def ground_sample(code, x, z, pixel):
    """Evaluate surface IDs independently, including oriented rows and area-normalized crop detail."""
    layer = code[..., 0].astype(int)
    angle = (code[..., 1].astype(float) + code[..., 2].astype(float) * 256) * (math.pi / 65535)
    across = np.where(layer >= 5, -x * np.sin(angle) + z * np.cos(angle), x)
    along = np.where(layer >= 5, x * np.cos(angle) + z * np.sin(angle), z)
    result = np.ones((*x.shape, 3))
    for k in range(10):
        mask = layer == k
        if not mask.any():
            continue
        detail = filtered(textures[k], across[mask] / metres[k], along[mask] / metres[k], pixel / metres[k]) / means[k]
        footprint = pixel * (np.abs(np.cos(angle[mask])) + np.abs(np.sin(angle[mask])))
        if k == 9:
            detail *= ((.96 + .26 * band(across[mask], 7, 1.1, footprint)) / (.96 + .26 * 1.1 / 7))[..., None]
        if k in (5, 6, 8):
            tracks = band(across[mask], 24, .3, footprint) + band(across[mask] + 1.8, 24, .3, footprint)
            detail *= ((1 - .28 * tracks) / (1 - .28 * .6 / 24))[..., None]
        strength = code[..., 3][mask, None] / 255
        result[mask] = 1 + (detail - 1) * strength
    return result

def tile(spot, altitude):
    """Sample the actual worker maps; the shader's gutters, discrete loads and four surface blends are reproduced."""
    name = spot['name']
    macro = linear(np.fromfile(OUT / f'{name}-macro.rgba', np.uint8).reshape(258, 258, 4)[..., :3] / 255)
    control = np.fromfile(OUT / f'{name}-control.rgba', np.uint8).reshape(258, 258, 4)
    span = 2 * altitude * math.tan(math.pi / 8) * W / H
    j, i = np.mgrid[0:H, 0:W]
    cx = np.clip(spot['center']['x'], spot['tileX'] * 512 + span / 2, (spot['tileX'] + 1) * 512 - span / 2)
    cz = np.clip(spot['center']['z'], spot['tileZ'] * 512 + span * H / W / 2, (spot['tileZ'] + 1) * 512 - span * H / W / 2)
    x, z = cx + ((i + .5) / W - .5) * span, cz + ((j + .5) / H - .5) * span * H / W
    u, v = (x - spot['tileX'] * 512) / 512, (z - spot['tileZ'] * 512) / 512
    if u.min() < 0 or v.min() < 0 or u.max() > 1 or v.max() > 1:
        raise RuntimeError(f'{name} view exceeds exported tile')
    p, q = u * 256 + .5, v * 256 + .5
    ix, iy = np.floor(p).astype(int), np.floor(q).astype(int)
    fx, fy = (p - ix)[..., None], (q - iy)[..., None]
    detail = np.zeros((H, W, 3))
    for dx, dy, weight in [(0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)]:
        detail += ground_sample(control[iy + dy, ix + dx], x, z, span / W) * weight
    return bilinear(macro, (u * 256 + 1) / 258, (v * 256 + 1) / 258, False) * detail

def sector(name, altitude):
    """Terrain uses actual Ground weights; fields use actual layouts, Farmland colours, tint RNG and detail transfer."""
    reference = json.loads((OUT / f'{name}-reference.json').read_text())
    weights = np.fromfile(OUT / f'{name}-reference-{altitude}-weights.bin', '<f4').reshape(H, W, 5)
    fields = np.fromfile(OUT / f'{name}-reference-{altitude}-fields.bin', '<f4').reshape(H, W, 4)
    span = 2 * altitude * math.tan(math.pi / 8) * W / H
    j, i = np.mgrid[0:H, 0:W]
    x, z = reference['x'] + ((i + .5) / W - .5) * span, reference['z'] + ((j + .5) / H - .5) * span * H / W
    result = np.zeros((H, W, 3))
    for k in range(5):
        result += filtered(textures[k], (x + 1000) / metres[k], (z + 1000) / metres[k], span / W / metres[k]) * weights[..., k, None]
    for index, field in enumerate(reference['fields']):
        mask = fields[..., 0] == index
        if not mask.any():
            continue
        colour = filtered(textures[field['layer']], fields[..., 1][mask], fields[..., 2][mask], span / W / 8) * field['tint']
        detail = fields[..., 3][mask] * 2 - 1
        amount = (np.abs(detail) ** 2)[..., None]
        result[mask] = (np.sqrt(colour) * (1 - amount) + (detail > 0)[..., None] * amount) ** 2
    result[fields[..., 0] == -2] = linear(np.array([54, 82, 78]) / 255)
    return result

def png(a):
    """One display conversion for both sides; no auto contrast or per-image exposure."""
    return Image.fromarray(np.uint8(np.clip(srgb(a) * 255 + .5, 0, 255)))

metrics = json.loads((OUT / 'metrics.json').read_text())
pairs = []
for spot in metrics['spots']:
    for altitude in (50, 300):
        left, right = sector(spot['name'], altitude), tile(spot, altitude)
        canvas = Image.new('RGB', (W * 2 + 36, H + 112), '#131c23')
        draw = ImageDraw.Draw(canvas)
        title = f"{spot['name'].replace('-', ' ').title()} | {altitude} m AGL equivalent | 45° vertical FOV"
        draw.text((12, 8), title, font=FONT, fill='#e8eff1')
        draw.text((12, 39), 'Detailed sector: Ground + Farmland', font=SMALL, fill='#b8d5dc')
        draw.text((W + 24, 39), 'Streamed tile: shared textures + directed rows', font=SMALL, fill='#b8d5dc')
        canvas.paste(png(left), (12, 64)); canvas.paste(png(right), (W + 24, 64))
        draw.text((12, H + 77), 'CPU linear-albedo harness · actual sector layouts, different nearby fields · no props, lighting or Unity render', font=SMALL, fill='#c1ccd3')
        path = OUT / f'{spot["name"]}-{altitude}m.png'
        canvas.save(path)
        pairs.append(canvas)

edge = Image.fromarray(np.fromfile(OUT / 'sector-edge.rgba', np.uint8).reshape(800, 800, 4)[..., :3])
canvas = Image.new('RGB', (1050, 880), '#131c23')
canvas.paste(edge, (12, 54))
draw = ImageDraw.Draw(canvas)
draw.text((12, 12), 'Sector edge | actual splat colour → sampled world picture', font=FONT, fill='white')
edge_metrics = json.loads((OUT / 'edge-metrics.json').read_text())
draw.text((830, 64), 'Max RGB difference\nin linear albedo', font=SMALL, fill='#b8d5dc')
for k, side in enumerate(edge_metrics['sides']):
    draw.text((830, 135 + k * 40), f"{'WENS'[k]}: {side['maxChannelPercent']:.2f}%", font=FONT, fill='#d5e8bb')
draw.text((830, 325), '8 m strips\nAll four sides\nTarget <5%\n\n256 m collar\n3.2 km view\n\nNo exposure\nor lighting', font=SMALL, fill='#b8d5dc')
draw.text((12, 856), 'Ground and crop albedo; the sector is the central 2 km square. Native Perlin is substituted.', font=SMALL, fill='#c1ccd3')
canvas.save(OUT / 'sector-edge.png')

sheet = Image.new('RGB', (792 * 2, 288 * 4), '#131c23')
for k, pair in enumerate(pairs):
    thumb = pair.resize((792, 272), Image.Resampling.LANCZOS)
    sheet.paste(thumb, ((k % 2) * 792, (k // 2) * 288))
sheet.save(OUT / 'comparison-sheet.png')
print('Rendered eight scale-matched pairs, overview and measured sector edge.')
