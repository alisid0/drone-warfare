"""Replay the ForestLit canopy shading on Harness.cs G-buffers; these are offline approximations, not HDRP captures.

  render.py              before and after views at 400 m, 1 km and 2.5 km into Review/
  render.py sheets       before|after sheets per view (half-size frames, 1:1 crops at the centre and top)
  render.py measure      tone, crown contrast, stand highlight spread and half-pixel stability into Review/measurements.json
Run `run.py after render` first. 'before' replays the shader at b8d8322 (stochastic photo crown map only);
'after' replays this branch's ForestCrowns.hlsl. Both use the same production geometry and macro maps.
"""
from pathlib import Path
import json
import sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
GBUFFER = ROOT / 'Temp/canopy-relief-2/after-render'
REVIEW = ROOT / 'Tools/CanopyRelief2/Review'
TEXTURES = ROOT / 'Assets/DroneSandbox/Resources/Textures'
# Atmosphere.ApplyLighting's default sun, Euler(36, -35, 0); the vector points from the ground towards the sun.
SUN = np.array([0.4641, 0.5878, -0.6628], dtype=np.float32)


def smoothstep(a, b, x):
    """HLSL smoothstep, including reversed edges."""
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def pyramid(path):
    """Linear box-filtered mips of a data PNG, flipped so row 0 is texture v = 0 as Unity samples it."""
    image = np.asarray(Image.open(path).convert('RGB'), dtype=np.float32)[::-1] / 255.0
    levels = [image]
    while levels[-1].shape[0] > 1:
        a = levels[-1]
        levels.append((a[0::2, 0::2] + a[1::2, 0::2] + a[0::2, 1::2] + a[1::2, 1::2]) * 0.25)
    return levels


MAPS = None


def bilinear(level, u, v):
    """Repeat-wrapped bilinear sample of one mip level."""
    h, w = level.shape[:2]
    x, y = u * w - 0.5, v * h - 0.5
    i, j = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = (x - i)[..., None], (y - j)[..., None]
    i0, i1, j0, j1 = i % w, (i + 1) % w, j % h, (j + 1) % h
    return ((level[j0, i0] * (1 - fx) + level[j0, i1] * fx) * (1 - fy)
            + (level[j1, i0] * (1 - fx) + level[j1, i1] * fx) * fy)


def sample_grad(levels, u, v, dudx, dvdx, dudy, dvdy, aniso=4):
    """SampleGrad with up to 4x anisotropic taps along the major axis, trilinear between mips."""
    n = levels[0].shape[0]
    ax, ay, bx, by = dudx * n, dvdx * n, dudy * n, dvdy * n
    la, lb = np.hypot(ax, ay), np.hypot(bx, by)
    major = np.maximum(la, lb); minor = np.maximum(np.minimum(la, lb), 1e-8)
    ratio = np.minimum(major / minor, aniso)
    taps = np.ceil(ratio - 1e-3).clip(1, aniso)
    lod = np.clip(np.log2(np.maximum(major / ratio, 1e-8)), 0, len(levels) - 1)
    mx = np.where(la >= lb, ax, bx) / n; my = np.where(la >= lb, ay, by) / n
    total = np.zeros(u.shape + (3,), dtype=np.float32)
    for k in range(aniso):
        use = (k < taps)
        offset = np.where(use, (k + 0.5) / taps - 0.5, 0.0)
        uu, vv = u + mx * offset, v + my * offset
        l0 = np.floor(lod).astype(np.int64); t = (lod - l0)[..., None]
        value = np.zeros_like(total)
        for level in range(len(levels)):
            for which, weight in ((l0 == level, 1 - t), (np.minimum(l0 + 1, len(levels) - 1) == level, t)):
                if which.any():
                    sel = which & use
                    if sel.any():
                        value[sel] += bilinear(levels[level], uu[sel], vv[sel]) * weight[sel]
        total += np.where(use[..., None], value, 0.0)
    return total / taps[..., None]


def block_hash(a, b):
    """ForestBlockHash: the uint hash of a 96 m block index."""
    m = np.uint64(0xffffffff)
    h = ((a.astype(np.int64).astype(np.uint64) * np.uint64(73856093)) ^ (b.astype(np.int64).astype(np.uint64) * np.uint64(19349663))) & m
    h ^= h >> np.uint64(16); h = (h * np.uint64(0x7feb352d)) & m; h ^= h >> np.uint64(15)
    h = (h * np.uint64(0x846ca68b)) & m; h ^= h >> np.uint64(16)
    return h


def block_detail(x, z, a, b, dx, dz, ex, ez):
    """ForestBlockDetail: rotated, offset photo crown map, slopes rotated back to world axes."""
    h = block_hash(a, b)
    angle = (h & np.uint64(65535)).astype(np.float64) * (6.2831853 / 65536.0)
    sn, cs = np.sin(angle), np.cos(angle)
    u = (cs * x - sn * z) / 128.0 + ((h >> np.uint64(16)) & np.uint64(255)).astype(np.float64) / 256.0
    v = (sn * x + cs * z) / 128.0 + ((h >> np.uint64(24)) & np.uint64(255)).astype(np.float64) / 256.0
    alt = (h & np.uint64(65536)) != 0
    result = np.zeros(x.shape + (3,), dtype=np.float32)
    for index, mask in ((0, ~alt), (1, alt)):
        if mask.any():
            s = sample_grad(MAPS[index], u[mask], v[mask], (cs * dx - sn * dz)[mask], (sn * dx + cs * dz)[mask],
                            (cs * ex - sn * ez)[mask], (sn * ex + cs * ez)[mask])
            result[mask] = s
    sx, sz = result[..., 1] * 2 - 1, result[..., 2] * 2 - 1
    return np.stack([result[..., 0], cs * sx + sn * sz, -sn * sx + cs * sz], axis=-1)


def crown_detail(x, z, ddx, ddy):
    """ForestCrownDetail: three triangular-weighted blocks of 96 m."""
    gx, gz = x / 96.0, z / 96.0
    a, b = np.floor(gx), np.floor(gz)
    fx, fz = gx - a, gz - b
    cx = (fx > fz).astype(np.float64); cz = 1 - cx
    w = np.stack([1 - np.maximum(fx, fz), np.abs(fx - fz), np.minimum(fx, fz)], axis=-1) ** 2
    w /= w.sum(axis=-1, keepdims=True)
    dx, dz, ex, ez = ddx[0] / 128.0, ddx[1] / 128.0, ddy[0] / 128.0, ddy[1] / 128.0
    return (block_detail(x, z, a, b, dx, dz, ex, ez) * w[..., 0:1]
            + block_detail(x, z, a + cx, b + cz, dx, dz, ex, ez) * w[..., 1:2]
            + block_detail(x, z, a + 1, b + 1, dx, dz, ex, ez) * w[..., 2:3])


def derivatives(x, z):
    """Screen-space world derivatives like ddx/ddy, falling back to the other neighbour at depth breaks."""
    def diff(a, axis):
        forward = np.diff(a, axis=axis, append=np.take(a, [-1], axis=axis))
        backward = np.diff(a, axis=axis, prepend=np.take(a, [0], axis=axis))
        return np.where(np.abs(forward) <= np.abs(backward) * 4 + 1e-6, forward, backward)
    return (diff(x, 1), diff(z, 1)), (diff(x, 0), diff(z, 0))


def shade_before(g, world, ddx, ddy):
    """b8d8322 GetSurfaceAndBuiltinData tier-3 block: photo crown map tints albedo and tilts the normal."""
    detail = crown_detail(world[0], world[1], ddx, ddy)
    weight = smoothstep(0.1, 0.7, g['alpha'])
    albedo = g['albedo'] * (1.0 + (detail[..., 0] - 0.5) * 1.6 * weight)[..., None]
    slope = detail[..., 1:3] * (0.65 * weight)[..., None]
    normal = g['normal'] + np.stack([slope[..., 0], np.zeros_like(weight), slope[..., 1]], axis=-1)
    return albedo, normal


def light(albedo, normal, distance):
    """Sun plus sky, then per-channel exposure and haze fitted to hd162's 88 shot from the 'before' 2.5 km view."""
    normal = normal / np.linalg.norm(normal, axis=-1, keepdims=True)
    sun = np.clip(normal @ SUN, 0, None)
    sky = 0.72 + 0.28 * normal[..., 1]
    radiance = albedo * (CAL['sun'] * sun + sky)[..., None]
    t = np.exp(-distance / CAL['haze_distance'])[..., None]
    colour = np.clip(radiance * np.array(CAL['gain']) * t + np.array(CAL['haze']) * (1 - t), 0, 1)
    return np.where(colour <= 0.0031308, colour * 12.92, 1.055 * np.power(np.clip(colour, 0.0031308, None), 1 / 2.4) - 0.055)


# Fitted so view2500 'before' matches the hd162 88_world_forest_hlod mean and spread per channel within 1 sRGB level.
CAL = {'sun': 2.2, 'haze_distance': 6500.0, 'gain': [0.9021, 0.6598, 0.5619], 'haze': [0.0633, 0.2068, 0.2230]}


def load(view):
    """One Harness.cs G-buffer as named arrays."""
    w, h = view['width'], view['height']
    raw = np.fromfile(GBUFFER / (view['name'] + '.f32'), dtype='<f4').reshape(12, h, w)
    valid = ~np.isnan(raw[0])
    normal = np.stack([raw[3], raw[4], raw[5]], axis=-1)
    normal[~valid] = (0, 1, 0)
    albedo = np.stack([raw[6], raw[7], raw[8]], axis=-1)
    return {'valid': valid, 'x': np.nan_to_num(raw[0]), 'z': np.nan_to_num(raw[1]), 'y': raw[2], 'normal': normal,
            'albedo': albedo, 'alpha': raw[9], 'step': raw[10], 'depth': raw[11]}


def render(view, phase, target, shift=(0.0, 0.0)):
    """Shade one view and return sRGB in [0, 1]; shift moves every sample by a fraction of its pixel, as a jitter."""
    g = load(view)
    ddx, ddy = derivatives(g['x'], g['z'])
    world = (g['x'] + target[0] + ddx[0] * shift[0] + ddy[0] * shift[1], g['z'] + target[1] + ddx[1] * shift[0] + ddy[1] * shift[1])
    if phase == 'before':
        albedo, normal = shade_before(g, world, ddx, ddy)
    else:
        import crowns
        albedo, normal = crowns.shade(g, world, ddx, ddy, lambda *a: sample_grad(MAPS[0], *a), SUN)
    eye = np.array(view['eye'])
    distance = np.sqrt((g['x'] - eye[0]) ** 2 + (g['y'] - eye[1]) ** 2 + (g['z'] - eye[2]) ** 2)
    colour = light(albedo, normal, distance)
    colour[~g['valid']] = (0.36, 0.42, 0.33)
    return np.clip(colour, 0, 1), g


def measure(meta):
    """Per view and phase: tone, crown-scale contrast, stand-to-stand highlight spread and sub-pixel stability."""
    from PIL import ImageFilter
    results = {}
    for view in meta['views']:
        g = load(view)
        ddx, ddy = derivatives(g['x'], g['z'])
        footprint = np.maximum(np.hypot(*ddx), np.hypot(*ddy))
        dense = g['valid'] & (g['alpha'] > 0.95)
        bins = [(0, 1), (1, 2), (2, 3), (3, 4), (4, 99)]
        row = {'canopy_pixels': int(dense.sum()), 'crown_branch_share': round(float((dense & (footprint < 3.6)).mean()), 3),
               'footprint_p5_p50_p95': np.percentile(footprint[dense], [5, 50, 95]).round(2).tolist()}
        for phase in ('before', 'after'):
            c0 = render(view, phase, meta['target'])[0] * 255
            green = c0[..., 1]
            blur = np.asarray(Image.fromarray(np.clip(green, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6)), dtype=np.float64)
            high = green - blur
            # Highlight strength per 60 m stand: how far the brightest crowns (p90) sit above the median, in sRGB levels.
            cell = np.floor(g['x'] / 60.0) * 100000 + np.floor(g['z'] / 60.0)
            keys = cell[dense]; values = green[dense]
            order = np.argsort(keys, kind='stable'); keys, values = keys[order], values[order]
            edges = np.flatnonzero(np.diff(keys)) + 1
            strength = [np.percentile(v, 90) - np.percentile(v, 50) for v in np.split(values, edges) if v.size >= 400]
            # Stability: RMS change when every sample moves half a pixel, as TAA jitter or a slow pan would.
            jitter = {}
            c1 = render(view, phase, meta['target'], (0.5, 0.0))[0] * 255
            c2 = render(view, phase, meta['target'], (0.0, 0.5))[0] * 255
            change = np.sqrt(0.5 * ((c1[..., 1] - green) ** 2 + (c2[..., 1] - green) ** 2))
            # Aliasing: RMS gap between the single sample and a 4-sample rotated-grid average (what TAA converges to).
            reference = (c1[..., 1] + c2[..., 1]) * 0.0
            for offset in ((0.125, 0.375), (-0.375, 0.125), (-0.125, -0.375), (0.375, -0.125)):
                reference += render(view, phase, meta['target'], offset)[0][..., 1] * 255 * 0.25
            alias = {}
            for lo, hi in bins:
                sel = dense & (footprint >= lo) & (footprint < hi)
                if sel.sum() > 1000:
                    jitter['%g-%g m' % (lo, hi)] = round(float(np.sqrt((change[sel] ** 2).mean())), 2)
                    alias['%g-%g m' % (lo, hi)] = round(float(np.sqrt(((green - reference)[sel] ** 2).mean())), 2)
            row[phase] = {'mean_srgb': c0[dense].mean(axis=0).round(1).tolist(),
                          'crown_contrast_levels': round(float(high[dense].std()), 2),
                          'stand_highlight_p90_minus_median': round(float(np.median(strength)), 2) if strength else None,
                          'stand_highlight_half_range': round(float((np.percentile(strength, 95) - np.percentile(strength, 5)) / 2), 2) if strength else None,
                          'stands': len(strength), 'half_pixel_jitter_rms_by_footprint': jitter,
                          'single_vs_4_sample_rms_by_footprint': alias}
            print(view['name'], phase, json.dumps(row[phase]), flush=True)
        results[view['name']] = row
    return results


def sheets(meta):
    """Side-by-side review sheets: whole frames at half size, then a 1:1 crop at the centre and near the top."""
    for view in meta['views']:
        before = Image.open(REVIEW / (view['name'] + '_before.png')); after = Image.open(REVIEW / (view['name'] + '_after.png'))
        w, h = before.size
        sheet = Image.new('RGB', (w + 8, h // 2 + 8 + 400 * 2 + 8), (255, 255, 255))
        sheet.paste(before.resize((w // 2, h // 2), Image.LANCZOS), (0, 0)); sheet.paste(after.resize((w // 2, h // 2), Image.LANCZOS), (w // 2 + 8, 0))
        for row, top in enumerate((h // 2 - 200, 120)):
            box = (w // 2 - 480, top, w // 2 + 480, top + 400)
            sheet.paste(before.crop(box), (0, h // 2 + 8 + row * 408)); sheet.paste(after.crop(box), (w // 2 + 8, h // 2 + 8 + row * 408))
        sheet.save(REVIEW / ('compare_' + view['name'] + '.png'))


def main():
    global MAPS
    MAPS = [pyramid(TEXTURES / 'ForestDetail.png'), pyramid(TEXTURES / 'ForestDetailB.png')]
    meta = json.loads((GBUFFER / 'views.json').read_text())
    if sys.argv[1:] == ['measure']:
        (REVIEW / 'measurements.json').write_text(json.dumps(measure(meta), indent=2))
        return
    if sys.argv[1:] == ['sheets']:
        sheets(meta)
        return
    phases = sys.argv[1:] or ['before', 'after']
    REVIEW.mkdir(parents=True, exist_ok=True)
    for view in meta['views']:
        for phase in phases:
            colour, g = render(view, phase, meta['target'])
            Image.fromarray((colour * 255 + 0.5).astype(np.uint8)).save(REVIEW / (view['name'] + '_' + phase + '.png'))
            print(view['name'], phase, 'mean sRGB', (colour[g['valid']].mean(axis=0) * 255).round(1), flush=True)


if __name__ == '__main__':
    sys.path.insert(0, str(Path(__file__).parent))
    main()
