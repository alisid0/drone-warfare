"""Measure the linear texture averages that HDRP actually filters, with Ground's tints."""
from pathlib import Path
from PIL import Image
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
def linear(a):
    """Decode texture sRGB before averaging as the GPU does."""
    return np.where(a <= .04045, a / 12.92, ((a + .055) / 1.055) ** 2.4)

def srgb(a):
    """Encode the filtered linear value for a painted colour texture."""
    return np.where(a <= .0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - .055)

if __name__ == '__main__':
    for name, tint in [('meadow', [.40, .74, .57]), ('lush', [.21, .55, .59]), ('dirt', [1.2, 1.23, 1.28]), ('rock', [1, 1, 1]), ('meadow', [.68, .83, .80])]:
        a = linear(np.asarray(Image.open(ROOT / ('Assets/DroneSandbox/Resources/Textures/Terrain/' + name + '_diff.jpg'))).astype(float) / 255).mean(axis=(0, 1))
        print(name, 'raw linear', a, 'tinted sRGB', srgb(a * tint) * 255)
