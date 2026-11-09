"""Replay the ForestLit canopy shading on Tools/Glades/Harness.cs G-buffers; offline approximations, not HDRP captures.

  render.py            before and after views at 300 m, 1 km, 2.5 km and the 512 m top-down into Review/
  render.py sheets     before|after sheets per view (compare_*.jpg), and sheet.jpg with all four
  render.py measure    glade floor, rim and wood tones per view and phase into Review/measurements.json
Run `run.py before render` and `run.py after render` first. 'before' shades the f7588a1 G-buffers with
Tools/CanopyRelief2/crowns.py (the f7588a1 shader); 'after' shades this branch's G-buffers with Tools/Glades/crowns.py.
Lighting, haze and exposure are CanopyRelief2's sun-plus-sky fit to hd162; there are no individual trees, terrain or TAA.
"""
from pathlib import Path
import importlib.util
import json
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE.parent / 'CanopyRelief2'))
import render as R
import crowns as BEFORE
spec = importlib.util.spec_from_file_location('glade_crowns', HERE / 'crowns.py')
AFTER = importlib.util.module_from_spec(spec); spec.loader.exec_module(AFTER)
REVIEW = HERE / 'Review'
LEVELS = None


def folder(phase):
    """Each phase rasterises its own production geometry and macro maps."""
    return ROOT / 'Temp/glades' / (phase + '-render')


def load(view, phase):
    """One Harness.cs G-buffer as named arrays."""
    w, h = view['width'], view['height']
    raw = np.fromfile(folder(phase) / (view['name'] + '.f32'), dtype='<f4').reshape(12, h, w)
    valid = ~np.isnan(raw[0])
    normal = np.stack([raw[3], raw[4], raw[5]], axis=-1)
    normal[~valid] = (0, 1, 0)
    albedo = np.stack([raw[6], raw[7], raw[8]], axis=-1)
    return {'valid': valid, 'x': np.nan_to_num(raw[0]), 'z': np.nan_to_num(raw[1]), 'y': np.nan_to_num(raw[2]), 'normal': normal,
            'albedo': albedo, 'alpha': np.nan_to_num(raw[9]), 'step': raw[10], 'depth': raw[11]}


def render(view, phase, target):
    """Shade one view; returns sRGB in [0, 1] and the G-buffer."""
    g = load(view, phase)
    ddx, ddy = R.derivatives(g['x'], g['z'])
    world = (g['x'] + target[0], g['z'] + target[1])
    sample = lambda *a: R.sample_grad(LEVELS, *a)
    mirror = BEFORE if phase == 'before' else AFTER
    albedo, normal = mirror.shade(g, world, ddx, ddy, sample, R.SUN)
    eye = np.array(view['eye'])
    distance = np.sqrt((g['x'] - eye[0]) ** 2 + (g['y'] - eye[1]) ** 2 + (g['z'] - eye[2]) ** 2)
    colour = R.light(albedo, normal, distance)
    colour[~g['valid']] = (0.36, 0.42, 0.33)
    return np.clip(colour, 0, 1), g


def measure(meta):
    """Per view and phase: tones of open glade floor, rim and dense wood, and the floor's own small-scale texture."""
    results = {}
    for view in meta['views']:
        row = {}
        for phase in ('before', 'after'):
            colour, g = render(view, phase, meta['target'])
            srgb = colour * 255
            luma = srgb @ np.array([0.2126, 0.7152, 0.0722])
            a, valid = g['alpha'], g['valid']
            wood, floor, rim = valid & (a >= 0.95), valid & (a < 0.05), valid & (a > 0.1) & (a < 0.9)
            blur = np.asarray(Image.fromarray(np.clip(luma, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(4)), dtype=np.float64)
            fine = luma - blur
            m = lambda mask: round(float(luma[mask].mean()), 1) if mask.any() else None
            row[phase] = {'wood_luma': m(wood), 'floor_luma': m(floor), 'rim_luma': m(rim),
                          'floor_minus_wood': round(float(luma[floor].mean() - luma[wood].mean()), 1) if floor.any() else None,
                          'rim_minus_darker_side': round(float(luma[rim].mean() - min(luma[wood].mean(), luma[floor].mean())), 1)
                          if rim.any() and floor.any() else None,
                          'floor_texture_rms': round(float(fine[floor].std()), 2) if floor.any() else None,
                          'pixels_wood_rim_floor': [int(wood.sum()), int(rim.sum()), int(floor.sum())]}
            print(view['name'], phase, json.dumps(row[phase]), flush=True)
        results[view['name']] = row
    return results


def label(image, text):
    """Name each panel so a sheet stands on its own."""
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 8 + 7 * len(text), 18), fill=(0, 0, 0))
    draw.text((4, 3), text, fill=(255, 255, 255))
    return image


def sheets(meta):
    """Before|after per view (half-size frames, then 1:1 crops at the centre), and one overview sheet of all views."""
    tiles = []
    for view in meta['views']:
        before = Image.open(REVIEW / (view['name'] + '_before.png')); after = Image.open(REVIEW / (view['name'] + '_after.png'))
        w, h = before.size
        scale = 2 if w > 1100 else 1
        sheet = Image.new('RGB', (2 * (w // scale) + 8, h // scale + 8 + 400), (255, 255, 255))
        sheet.paste(label(before.resize((w // scale, h // scale), Image.LANCZOS), view['name'] + ' before (f7588a1)'), (0, 0))
        sheet.paste(label(after.resize((w // scale, h // scale), Image.LANCZOS), view['name'] + ' after'), (w // scale + 8, 0))
        box = (w // 2 - w // (2 * scale), h // 2 - 200, w // 2 + w // (2 * scale), h // 2 + 200)
        sheet.paste(before.crop(box), (0, h // scale + 8)); sheet.paste(after.crop(box), (w // scale + 8, h // scale + 8))
        sheet.save(REVIEW / ('compare_' + view['name'] + '.jpg'), quality=92)
        tw = 760
        th = int(h * tw / w)
        tiles.append((label(before.resize((tw, th), Image.LANCZOS), view['name'] + ' before'), label(after.resize((tw, th), Image.LANCZOS), view['name'] + ' after')))
    total = sum(t[0].size[1] + 6 for t in tiles)
    overview = Image.new('RGB', (2 * 760 + 6, total), (255, 255, 255))
    y = 0
    for b, a in tiles:
        overview.paste(b, (0, y)); overview.paste(a, (766, y)); y += b.size[1] + 6
    overview.save(REVIEW / 'sheet.jpg', quality=90)


def main():
    global LEVELS
    LEVELS = R.pyramid(R.TEXTURES / 'ForestDetail.png')
    meta = json.loads((folder('after') / 'views.json').read_text())
    before = json.loads((folder('before') / 'views.json').read_text())
    assert meta['target'] == before['target'], 'both phases must look at the same place'
    REVIEW.mkdir(parents=True, exist_ok=True)
    if sys.argv[1:] == ['measure']:
        out = {'views': measure(meta), 'survey': {'before': before['survey'], 'after': meta['survey']},
               'build': {v['name']: {'before_ms': b['buildMs'], 'after_ms': v['buildMs'], 'before_triangles': b['emitted'],
                                     'after_triangles': v['emitted'], 'steps': v['steps']}
                         for v, b in zip(meta['views'], before['views'])}, 'target': meta['target']}
        (REVIEW / 'measurements.json').write_text(json.dumps(out, indent=2))
        return
    if sys.argv[1:] == ['sheets']:
        sheets(meta)
        return
    for view in meta['views']:
        for phase in ('before', 'after'):
            colour, g = render(view, phase, meta['target'])
            Image.fromarray((colour * 255 + 0.5).astype(np.uint8)).save(REVIEW / (view['name'] + '_' + phase + '.png'))
            print(view['name'], phase, 'mean sRGB', (colour[g['valid']].mean(axis=0) * 255).round(1), flush=True)


if __name__ == '__main__':
    main()
