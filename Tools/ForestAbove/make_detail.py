"""Generate one small, periodic linear crown-light/normal texture; no image service or new package required."""
from pathlib import Path
import math
import struct
import zlib
import base64
import re

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT / 'Assets/DroneSandbox/Scripts/ForestPalette.cs').read_text()
encoded = source.split('System.Convert.FromBase64String(', 1)[1].split(');', 1)[0]
pictures = base64.b64decode(''.join(re.findall(r'"([^"]+)"', encoded)))


def picture(a, b, u, v):
    """Reuse the existing twelve photo-derived crown stamps instead of lighting smooth circular blobs."""
    variant = int(unit(a, b, 27) * 12)
    x, z = max(0, min(15, u*15)), max(0, min(15, v*15))
    i, j = min(14, int(x)), min(14, int(z)); fx, fz = x-i, z-j
    k = (variant*256 + j*16+i)*2
    values = []
    for channel, scale in ((0, 128), (1, 255)):
        values.append(((pictures[k+channel]*(1-fx)+pictures[k+2+channel]*fx)*(1-fz)
                       +(pictures[k+32+channel]*(1-fx)+pictures[k+34+channel]*fx)*fz)/scale)
    return values


def unit(x, z, salt):
    """Periodic integer hashing gives each eight-metre crown a stable center and radius."""
    h = (((x % 16) * 73856093) ^ ((z % 16) * 19349663) ^ (salt * 1640531513)) & 0xffffffff
    h ^= h >> 16
    h = (h * 0x7feb352d) & 0xffffffff
    h ^= h >> 15
    return (h & 65535) / 65536


def sample(x, z):
    """A continuous four-metre crown dome supplies matching light and analytic surface slopes."""
    best, gx, gz, light, alpha = 0, 0, 0, .7, 0
    i, j = math.floor(x / 8), math.floor(z / 8)
    for dz in (-1, 0, 1):
        for dx in (-1, 0, 1):
            a, b = i + dx, j + dz
            u = x - (a + .1 + .8 * unit(a, b, 7)) * 8
            v = z - (b + .1 + .8 * unit(a, b, 9)) * 8
            r = 4.4 + unit(a, b, 13) * 2.1
            q = max(0, 1 - (u*u + v*v) / (r*r))
            h = q*q * (3 + unit(a, b, 19))
            if h > best:
                best = h
                gx, gz = 4*q*u/(r*r)*3.5, 4*q*v/(r*r)*3.5
                light, alpha = picture(a, b, u/(2*r)+.5, v/(2*r)+.5)
    value = .53 + (light-1)*.36 + (best/4-.35)*.15 - (1-alpha)*.12
    return max(0, min(1, value)), max(-1, min(1, gx)), max(-1, min(1, gz))


def png(path, n, pixels):
    """Write an untagged RGB data PNG so the Unity importer can explicitly keep it linear."""
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    raw = b''.join(b'\x00' + pixels[y*n*3:(y+1)*n*3] for y in range(n))
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', n, n, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))


if __name__ == '__main__':
    n = 512
    pixels = bytearray()
    for z in reversed(range(n)):
        for x in range(n):
            light, gx, gz = sample((x + .5) * 128/n, (z + .5) * 128/n)
            pixels.extend(round(c * 255) for c in (light, gx*.5+.5, gz*.5+.5))
    png(ROOT / 'Assets/DroneSandbox/Resources/Textures/ForestDetail.png', n, pixels)
