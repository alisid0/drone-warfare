"""Paints four billowing smoke puffs into a 2x2 atlas for the explosion and smoke particles (Effects.cs).

Each puff is a cluster of overlapping balls, so its edge is lumpy like a cauliflower, roughened with noise and shaded
as if lit from above. The colour is grey shading only: the game tints it per particle (black smoke, brown dust,
fire). Run from the repo root:  python Tools/make_smoke_puffs.py
"""
import os

import numpy as np
from PIL import Image

CELL = 512
OUT = os.path.join("Assets", "DroneSandbox", "Resources", "Textures", "FX", "SmokePuffs.png")


def noise(rng, size, scale):
    """Smooth value noise in 0-1: a coarse random grid blown up with bicubic filtering."""
    grid = rng.random((scale, scale)).astype(np.float32)
    img = Image.fromarray((grid * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32) / 255.0


def fbm(rng, size, octaves=(4, 8, 16, 32, 64)):
    total, weight, amplitude = np.zeros((size, size), np.float32), 0.0, 1.0
    for scale in octaves:
        total += noise(rng, size, scale) * amplitude
        weight += amplitude
        amplitude *= 0.55
    return total / weight


def blur(image, sigma):
    """Gaussian blur through the FFT (the image wraps round, which is fine: its edges are empty)."""
    n = image.shape[0]
    f = np.fft.fftfreq(n)
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (f[:, None] ** 2 + f[None, :] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(image) * kernel)).astype(np.float32)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def puff(seed):
    rng = np.random.default_rng(seed)
    n = CELL
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    u, v = (xs / n * 2.0 - 1.0) * 0.82, (ys / n * 2.0 - 1.0) * 0.82  # v grows downwards, as in the image

    # Warp the space with noise first, so the balls come out lumpy and torn rather than round.
    detail, fine = fbm(rng, n), fbm(rng, n, (24, 48, 96))
    warp_x, warp_y = fbm(rng, n, (3, 6, 12, 24)), fbm(rng, n, (3, 6, 12, 24))
    wu, wv = u + (warp_x - 0.5) * 0.3, v + (warp_y - 0.5) * 0.3

    # The cluster: a big soft ball in the middle and smaller ones round it, as a sum of soft blobs.
    density = np.zeros((n, n), np.float32)
    blobs = [(0.0, 0.04, 0.26, 1.0)]
    for _ in range(rng.integers(14, 22)):
        angle, reach = rng.random() * 2 * np.pi, 0.1 + rng.random() * 0.34
        blobs.append((np.cos(angle) * reach, np.sin(angle) * reach * 0.8 + 0.05, 0.08 + rng.random() * 0.09, 0.5 + rng.random() * 0.5))
    for bx, by, sigma, weight in blobs:
        d2 = (wu - bx) ** 2 + (wv - by) ** 2
        density += weight * np.exp(-d2 / (2 * sigma * sigma))

    body = density * (0.7 + 0.6 * detail) + (fine - 0.5) * 0.25
    alpha = smoothstep(0.15, 0.85, body)
    alpha *= smoothstep(0.82, 0.68, np.sqrt(u * u + v * v))  # nothing reaches the cell's edge

    # Shading: normals from the bumpy body, light from above and a little to the left, darker underneath and
    # in the folds between the lumps.
    h = blur(body / body.max(), 7.0)
    gy, gx = np.gradient(h)
    normal = np.stack([-gx * n * 0.5, gy * n * 0.5, np.ones_like(h)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    light = np.array([-0.4, 0.7, 0.6], np.float32)
    light /= np.linalg.norm(light)
    diffuse = np.clip(normal @ light, 0.0, 1.0)
    shade = 0.3 + 0.62 * diffuse + 0.16 * (detail - 0.5) + 0.08 * (fine - 0.5)
    shade *= 1.0 - 0.25 * smoothstep(-0.25, 0.6, v)  # the underside in its own shadow
    shade = np.clip(shade, 0.0, 1.0)

    rgba = np.stack([shade, shade, shade, alpha], axis=-1)
    return (rgba * 255 + 0.5).astype(np.uint8)


def main():
    atlas = np.zeros((CELL * 2, CELL * 2, 4), np.uint8)
    for i, seed in enumerate((11, 23, 37, 53)):
        row, col = divmod(i, 2)
        atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = puff(seed)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(OUT)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
