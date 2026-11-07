"""Paints the forest floor terrain texture: fallen leaves, pine needles, twigs and moss on dark soil, seamless,
2048 square over 6 m, with its OpenGL normal map. The woods stood on the meadow layer and looked like trees on a
lawn; this is the ground under them (Ground.PaintForestFloor). Made here rather than downloaded, so it needs no
network. Writes Assets/DroneSandbox/Resources/Textures/Terrain/litter_diff.jpg and litter_nor.jpg.

Run from the project root:  python -I Tools/make_forest_floor.py
"""
import os
import numpy as np
from PIL import Image

N = 2048
OUT = os.path.join("Assets", "DroneSandbox", "Resources", "Textures", "Terrain")
rng = np.random.default_rng(1207)


def noise(scale, power=2.0, lowest=12):
    """Seamless fractal noise in 0..1: white noise filtered in frequency space, so it wraps at the tile edges.
    Nothing coarser than `lowest` cycles per tile (50 cm): a patch the size of the tile would repeat every 6 m and
    draw a grid over the woods from the air, so broad variation comes from the terrain's splat instead."""
    f = np.fft.fftfreq(N)
    k = np.sqrt(f[:, None] ** 2 + f[None, :] ** 2)
    k[0, 0] = 1.0
    spectrum = np.fft.fft2(rng.standard_normal((N, N))) / (k * N / scale) ** power
    spectrum[k * N < lowest] = 0
    n = np.real(np.fft.ifft2(spectrum))
    return (n - n.min()) / (n.max() - n.min())


def blur(a, radius):
    """Seamless Gaussian blur by FFT."""
    f = np.fft.fftfreq(N)
    g = np.exp(-2 * (np.pi * radius) ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def srgb(c):
    return (np.asarray(c, np.float32) / 255.0) ** 2.2


colour = np.zeros((N, N, 3), np.float32)
height = np.zeros((N, N), np.float32)

# Dark humus soil, patchy at 20-60 cm, with crumbs.
soil = noise(6) * 0.7 + noise(40) * 0.3
colour[:] = srgb((52, 40, 28)) * (0.75 + 0.5 * soil[..., None])
height += soil * 2.0

# Small cushions of moss, a hand or two across; leaves drift less thickly over them.
moss_mask = np.clip((noise(30, 2.2, 40) - 0.66) / 0.1, 0, 1)
moss_tex = noise(160) * 0.6 + noise(40) * 0.4
moss = srgb((60, 70, 34)) * (0.7 + 0.6 * moss_tex[..., None])
colour = colour * (1 - moss_mask[..., None]) + moss * moss_mask[..., None]
height += moss_mask * (1.5 + moss_tex)

needle_zone = 0.35 + 0.4 * noise(30, 2.2, 40)  # needles mixed through, in small drifts
grain = noise(400, 1.2, 60)  # spots and veins across each leaf


def stamp(cx, cy, mask, tint, lift, shade):
    """Lays one object (mask: local coverage 0..1, shade: local brightness) over the stack, wrapping at the edges."""
    h, w = mask.shape
    ys = (np.arange(h) + cy - h // 2) % N
    xs = (np.arange(w) + cx - w // 2) % N
    region = np.ix_(ys, xs)
    under = height[region]
    base = (under * mask).sum() / max(mask.sum(), 1e-3)
    # A soft shadow round the object's edge on what lies beneath it.
    edge = np.clip(blur_small(mask) - mask, 0, 1)
    colour[region] *= (1 - 0.35 * edge)[..., None]
    top = base + lift
    over = mask > 0.02
    m = mask[..., None]
    shade = shade * (0.8 + 0.4 * grain[region])
    colour[region] = colour[region] * (1 - m) + (tint * shade[..., None]) * m
    height[region] = np.where(over, np.maximum(under, top * mask + under * (1 - mask)), under)


def blur_small(m):
    p = np.pad(m, 2)
    return (p[1:-3, 2:-2] + p[3:-1, 2:-2] + p[2:-2, 1:-3] + p[2:-2, 3:-1] + p[2:-2, 2:-2] * 2) / 6.0


def leaf(length, width, angle, curl):
    """A lens-shaped leaf with a midrib, lit from one side by its curl."""
    r = int(length) + 3
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(np.float32)
    ca, sa = np.cos(angle), np.sin(angle)
    u = x * ca + y * sa
    v = -x * sa + y * ca
    t = np.clip(1 - (u / length) ** 2, 0, 1)
    half = width * t ** 0.8
    inside = np.clip((half - np.abs(v)) * 1.5, 0, 1) * (np.abs(u) < length)
    across = np.where(half > 0.1, v / np.maximum(half, 0.1), 0)
    shade = 0.85 + curl * across * 0.25 - 0.25 * np.exp(-(v / 0.9) ** 2)  # one half lit, darker midrib
    return inside.astype(np.float32), shade.astype(np.float32)


def stick(length, width, angle):
    """A straight needle or twig."""
    r = int(length / 2) + 3
    y, x = np.mgrid[-r:r + 1, -r:r + 1].astype(np.float32)
    ca, sa = np.cos(angle), np.sin(angle)
    u = x * ca + y * sa
    v = -x * sa + y * ca
    inside = np.clip((width / 2 - np.abs(v)) * 1.5 + 0.5, 0, 1) * np.clip((length / 2 - np.abs(u)) + 0.5, 0, 1)
    shade = 0.9 + 0.2 * np.clip(-v / max(width / 2, 0.5), -1, 1)
    return inside.astype(np.float32), shade.astype(np.float32)


leaf_colours = [srgb(c) for c in [(124, 82, 42), (148, 104, 52), (102, 62, 34), (84, 58, 36), (118, 96, 64),
                                  (160, 124, 62), (96, 76, 52), (70, 50, 34), (112, 104, 52)]]

# Twigs first, under the leaves.
for _ in range(260):
    m, s = stick(rng.uniform(60, 240), rng.uniform(3, 6), rng.uniform(0, np.pi))
    stamp(rng.integers(N), rng.integers(N), m, srgb((70, 56, 42)) * rng.uniform(0.8, 1.2), rng.uniform(2.5, 4), s)

# Needles in the conifer patches.
for _ in range(20000):
    cx, cy = rng.integers(N), rng.integers(N)
    if rng.random() > needle_zone[cy, cx] * 0.9 + 0.05:
        continue
    m, s = stick(rng.uniform(22, 44), 2.2, rng.uniform(0, np.pi))
    stamp(cx, cy, m, srgb((126, 84, 48)) * rng.uniform(0.7, 1.15), 0.8, s)

# Leaves: thick drifts, thinner on the moss.
for _ in range(15000):
    cx, cy = rng.integers(N), rng.integers(N)
    if rng.random() < moss_mask[cy, cx] * 0.75 or rng.random() < needle_zone[cy, cx] * 0.45:
        continue
    length = rng.uniform(9, 17)
    m, s = leaf(length, length * rng.uniform(0.35, 0.6), rng.uniform(0, 2 * np.pi), rng.uniform(-1, 1))
    tint = leaf_colours[rng.integers(len(leaf_colours))] * rng.uniform(0.75, 1.15)
    stamp(cx, cy, m, tint, rng.uniform(1.0, 2.0), s)

# Depth: hollows between the leaves darker, tops a little lighter.
cavity = height - blur(height, 6)
colour *= np.clip(1 + cavity[..., None] * 0.06, 0.55, 1.15)

# Normal map from the height stack (OpenGL convention: green points up the texture).
h = blur(height, 0.8) * 0.55
dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
nz = np.ones_like(h)
length = np.sqrt(dx ** 2 + dy ** 2 + nz ** 2)
normal = np.stack([-dx / length, dy / length, nz / length], -1)

os.makedirs(OUT, exist_ok=True)
diffuse = np.clip(colour, 0, 1) ** (1 / 2.2)
Image.fromarray((diffuse * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "litter_diff.jpg"), quality=92)
Image.fromarray(((normal * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(OUT, "litter_nor.jpg"), quality=95)
mean = np.clip(colour, 0, 1).reshape(-1, 3).mean(0)
print("linear mean", ", ".join("%.5f" % c for c in mean))
