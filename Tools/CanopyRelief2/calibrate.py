"""Top-down crown patches and the far-field normalisation for ForestCrowns.

  calibrate.py patch     240 m patches at 0.25 m and 2 m per pixel, before and after, into Temp/canopy-relief-2
  calibrate.py means     mean lit radiance of each regime against a flat surface, for P['scale']
  calibrate.py ramp      the same ratio against footprint (0.25-4 m), before and after: distant tone must not dip

The mean is taken under the default sun with the canopy's own sun-plus-sky light; the far-field target is the
'before' photo map's mip-averaged factor, so distant woods keep the hd162 tone while near crowns gain structure.
"""
from pathlib import Path
import sys
import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
import render as R
import crowns as C

OUT = R.ROOT / 'Temp/canopy-relief-2'


def lit(factor, slope_x, slope_z, albedo=(0.06, 0.11, 0.045)):
    """The render.py light model on a flat, upward macro surface."""
    normal = np.stack([slope_x, np.ones_like(slope_x), slope_z], axis=-1)
    return R.light(np.array(albedo) * factor[..., None], normal, np.full(factor.shape, 2000.0))


def patch(origin, metres, pixel, phase, broadleaf=0.5):
    """Shade a flat square of canopy seen straight down; footprint equals the pixel size."""
    n = int(metres / pixel)
    z, x = np.meshgrid(origin[1] + (np.arange(n)[::-1] + 0.5) * pixel, origin[0] + (np.arange(n) + 0.5) * pixel, indexing='ij')
    ddx = (np.full(x.shape, pixel), np.zeros(x.shape)); ddy = (np.zeros(x.shape), np.full(x.shape, -pixel))
    footprint = np.full(x.shape, pixel)
    alpha = np.ones(x.shape)
    if phase == 'before':
        detail = R.crown_detail(x, z, ddx, ddy)
        return 1.0 + (detail[..., 0] - 0.5) * 1.6, detail[..., 1] * 0.65, detail[..., 2] * 0.65
    sample = lambda *a: R.sample_grad(R.MAPS[0], *a)
    return C.pattern((x, z), ddx, ddy, footprint, np.full(x.shape, broadleaf), alpha, sample, R.SUN)


def main():
    R.MAPS = [R.pyramid(R.TEXTURES / 'ForestDetail.png'), R.pyramid(R.TEXTURES / 'ForestDetailB.png')]
    mode = sys.argv[1] if len(sys.argv) > 1 else 'patch'
    origin = (21500.0, 18500.0)
    if mode == 'patch':
        for pixel in (0.25, 2.0):
            row = []
            for phase in ('before', 'after'):
                f, sx, sz = patch(origin, 240.0, pixel, phase)
                row.append(lit(f, sx, sz))
                print(phase, pixel, 'factor mean %.4f' % f.mean(), 'lit mean', (row[-1].mean(axis=(0, 1)) * 255).round(1))
            image = np.concatenate([row[0], np.ones((row[0].shape[0], 8, 3)), row[1]], axis=1)
            Image.fromarray((np.clip(image, 0, 1) * 255 + 0.5).astype(np.uint8)).save(OUT / ('patch-%g.png' % pixel))
    else:
        # Linear sun-plus-sky radiance (no haze or display transform) of the full-detail pattern against the far field.
        def radiance(f, sx, sz):
            n = np.stack([sx, np.ones_like(sx), sz], axis=-1)
            n /= np.linalg.norm(n, axis=-1, keepdims=True)
            return (f * (R.CAL['sun'] * np.clip(n @ R.SUN, 0, None) + 0.72 + 0.28 * n[..., 1])).mean()
        flat = C.P['far'] * (R.CAL['sun'] * R.SUN[1] + 1.0)
        if mode == 'ramp':
            for phase in ('before', 'after'):
                ratios = []
                for pixel in (0.25, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0):
                    f, sx, sz = patch(origin, max(240.0, pixel * 400), pixel, phase)
                    ratios.append('%.2f m %.3f' % (pixel, radiance(f, sx, sz) / flat))
                print(phase, 'near/far radiance by footprint:', ', '.join(ratios))
            return
        for phase in ('before', 'after'):
            for broadleaf in (0.0, 0.5, 1.0):
                f, sx, sz = patch(origin, 480.0, 0.25, phase, broadleaf)
                print(phase, 'broadleaf', broadleaf, 'near/far radiance %.4f' % (radiance(f, sx, sz) / flat))

if __name__ == '__main__':
    main()
