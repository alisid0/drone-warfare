"""NumPy mirror of Assets/DroneSandbox/Resources/Shaders/ForestDetail.hlsl after the glades change; render.py shades
'after' with it and check_parity.py runs the HLSL on WARP against it. Keep the two in step: P holds the shader's
literal constants. Tools/CanopyRelief2/crowns.py remains the mirror of the f7588a1 shader ('before')."""
import numpy as np

M32 = np.uint64(0xffffffff)
CELL = 15.0                        # big-crown lattice spacing in metres; small crowns use half of it
COS, SIN = 0.8944272, 0.4472136    # lattice rotation, atan(1/2), away from the world grid and the 96 m detail blocks
CLUMP = 40.0                       # photo crown map repeat when reused as foliage clumps inside crowns
JITTER = 0.45                      # centre spread in cells; 2x2 cells are exact while radius * 1.135 <= 1 - JITTER / 2
P = dict(big_radius=(0.50, 0.14), big_top=(3.5, 2.5), small_radius=(0.50, 0.12), small_top=2.0,
         big_depth=(0.50, 0.15), small_depth=(0.55, 0.30), value=(0.84, 0.32), rim=0.20, gap=(0.6, 0.08), gap_clump=0.5,
         shadow=0.42, clump_value=0.8, clump_slope=0.35, warp=1.5,
         fade_small=(1.9, 3.5), fade_big=(2.2, 3.6), scale=(1.331, -0.098), far=0.989,
         # Glades: per-crown cover thresholds, the cover-gradient cap, the open ramp and the meadow floor.
         big_threshold=(0.03, 0.55), small_threshold=(0.08, 0.84), grad_cap=0.15, open=(0.1, 0.7),
         meadow_cells=(9.0, 3.7), scrub=(0.45, 0.65), meadow_value=(1.087, 0.867), tussock=0.1, meadow_slope=(0.15, 0.45),
         fade_floor=(1.8, 3.5), side=(0.88, 0.7), side_flatten=0.6)


def smoothstep(a, b, x):
    """HLSL smoothstep."""
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def saturate(x):
    """HLSL saturate."""
    return np.clip(x, 0.0, 1.0)


def u32(x):
    """An int cell index as the uint bits HLSL's asuint gives."""
    return np.asarray(x).astype(np.int64).astype(np.uint64) & M32


def crown_hash(i, j, salt):
    """ForestCrownHash: the block hash with a salt, so lattices and stands draw independent values."""
    h = (u32(i) * np.uint64(73856093) ^ u32(j) * np.uint64(19349663) ^ np.uint64(salt) * np.uint64(83492791)) & M32
    h ^= h >> np.uint64(16); h = (h * np.uint64(0x7feb352d)) & M32; h ^= h >> np.uint64(15)
    h = (h * np.uint64(0x846ca68b)) & M32; h ^= h >> np.uint64(16)
    return h


def remix(h):
    """ForestCrownRemix: a second independent 32 bits from one hash."""
    h = ((h ^ (h >> np.uint64(15))) * np.uint64(0x2c1b3c6d)) & M32
    return h ^ (h >> np.uint64(12))


def unit(h, shift):
    """Eight bits of a hash as a value in [0, 1]."""
    return ((h >> np.uint64(shift)) & np.uint64(255)).astype(np.float64) / 255.0


def nudge(h, shift):
    """Two hash bits as a -1/0/0/+1 cell offset."""
    b = ((h >> np.uint64(shift)) & np.uint64(3)).astype(np.int64)
    return np.where(b == 0, -1, np.where(b == 3, 1, 0))


def big_share(stand):
    """Share of a stand's cells holding one emergent crown instead of four smaller ones: young, mixed or old."""
    u = unit(stand, 0)
    return np.where(u < 0.35, 0.05 + 0.3 * u, np.where(u < 0.7, 0.35 + 0.8 * (u - 0.35), 0.75 + 0.5 * (u - 0.7)))


class Best:
    """The highest crown surface found so far at each pixel."""
    def __init__(self, shape):
        self.z = np.full(shape, -1e9); self.second = np.full(shape, -1e9); self.gx = np.zeros(shape); self.gz = np.zeros(shape)
        self.r = np.ones(shape); self.q = np.ones(shape)
        self.hash = np.zeros(shape, dtype=np.uint64); self.big = np.zeros(shape, dtype=bool)
        self.inside = np.zeros(shape, dtype=bool)

    def offer(self, present, h, cx, cz, radius, top, depth, px, pz, big):
        """ForestCrownOffer: an elliptical paraboloid cap; the higher surface wins."""
        s, e = (unit(h, 0) - 0.5) * 0.4, (unit(h, 8) - 0.5) * 0.4
        dx, dz = px - cx, pz - cz
        q = ((1 + s) * dx * dx + (1 - s) * dz * dz + e * dx * dz) / (radius * radius)
        z = top - depth * q
        take = present & (q < 1.0) & (z > self.z)
        self.second = np.where(take, self.z, np.where(present & (q < 1.0), np.maximum(self.second, z), self.second))
        self.z = np.where(take, z, self.z); self.q = np.where(take, q, self.q)
        # Outward slope is depth times the gradient of q.
        self.gx = np.where(take, depth * (2 * (1 + s) * dx + e * dz) / (radius * radius), self.gx)
        self.gz = np.where(take, depth * (2 * (1 - s) * dz + e * dx) / (radius * radius), self.gz)
        self.r = np.where(take, radius, self.r); self.hash = np.where(take, h, self.hash)
        self.big = np.where(take, big, self.big); self.inside |= take


def field(px, pz, footprint, broadleaf, sox, soz, canopy, gx, gz):
    """Crown surface at one pixel in rotated lattice metres (px, pz); see ForestCrownField. canopy is the macro cover
    and (gx, gz) its gradient in lattice metres: each crown keeps its own threshold against the cover at its centre."""
    shape = px.shape
    # A gentle warp bends the lattice rows; its slope stays near a quarter, so crowns keep their shape.
    w = P['warp']
    px, pz = px + w * np.sin(pz * 0.13 + 1.3 * np.sin(px * 0.071)), pz + w * np.sin(px * 0.12 + 1.1 * np.sin(pz * 0.083))
    qx, qz = px / CELL, pz / CELL
    bx, bz = np.floor(qx - 0.5), np.floor(qz - 0.5)
    fx, fz = qx - 0.5 - bx, qz - 0.5 - bz
    best = Best(shape)
    strength = np.zeros(shape); share = np.zeros(shape)
    crowns = []

    def cover_at(cx, cz):
        return canopy + gx * (cx - px) + gz * (cz - pz)

    for k in range(4):
        ox, oz = k & 1, k >> 1
        ci, cj = bx + ox, bz + oz
        h = crown_hash(ci, cj, 1)
        stand = crown_hash(np.floor_divide(ci.astype(np.int64) + nudge(h, 24), 4),
                           np.floor_divide(cj.astype(np.int64) + nudge(h, 26), 4), 2)
        p = big_share(stand)
        wgt = (fx if ox else 1 - fx) * (fz if oz else 1 - fz)
        strength += wgt * (0.75 + 0.5 * unit(stand, 8)); share += wgt * p
        cx = (ci + 0.5 + (unit(h, 0) - 0.5) * JITTER) * CELL
        cz = (cj + 0.5 + (unit(h, 8) - 0.5) * JITTER) * CELL
        threshold = P['big_threshold'][0] + P['big_threshold'][1] * ((h >> np.uint64(28)) & np.uint64(15)).astype(np.float64) / 15.0
        big = (unit(h, 16) < p) & (threshold < cover_at(cx, cz))
        g = remix(h)
        radius = CELL * (P['big_radius'][0] + P['big_radius'][1] * unit(g, 16))
        top = P['big_top'][0] + P['big_top'][1] * unit(g, 24)
        depth = radius * (P['big_depth'][0] + P['big_depth'][1] * (1 - broadleaf))
        best.offer(big, g, cx, cz, radius, top, depth, px, pz, True)
        crowns.append((big, g, cx, cz, radius, top, CELL))
    sx0, sz0 = np.floor(qx * 2 - 0.5), np.floor(qz * 2 - 0.5)
    half = CELL * 0.5
    for k in range(4):
        ox, oz = k & 1, k >> 1
        si, sj = sx0 + ox, sz0 + oz
        # The parent big cell is always one of the four above; a big parent leaves no room for small crowns.
        parent = (np.floor(si * 0.5) - bx) + 2 * (np.floor(sj * 0.5) - bz)
        taken = np.select([parent == 0, parent == 1, parent == 2], [crowns[0][0], crowns[1][0], crowns[2][0]], crowns[3][0])
        h = crown_hash(si, sj, 3)
        g = remix(h)
        cx = (si + 0.5 + (unit(h, 0) - 0.5) * JITTER) * half
        cz = (sj + 0.5 + (unit(h, 8) - 0.5) * JITTER) * half
        present = ~taken & (P['small_threshold'][0] + P['small_threshold'][1] * unit(g, 16) < cover_at(cx, cz))
        radius = half * (P['small_radius'][0] + P['small_radius'][1] * unit(h, 16)) * (0.94 + 0.12 * broadleaf)
        top = P['small_top'] * (unit(h, 24) - 0.5)
        depth = radius * (P['small_depth'][0] + P['small_depth'][1] * (1 - broadleaf))
        best.offer(present, g, cx, cz, radius, top, depth, px, pz, False)
        crowns.append((present, g, cx, cz, radius, top, half))
    edge = np.maximum(0.3, 1.6 * footprint)
    # Edge cover in metres from the rim: (1 - q) * R / 2 approximates the distance inside the ellipse.
    inner = (1.0 - best.q) * best.r * 0.5
    # Where a lower crown meets the winner, the height gap over twice the slope is the distance to that crease.
    crease = (best.z - best.second) / (2.0 * np.hypot(best.gx, best.gz) + 0.05)
    cover = np.where(best.inside, smoothstep(0.0, edge, np.minimum(inner, crease)), 0.0)
    rim = np.where(best.inside, best.q, 1.0)
    tone = remix(best.hash)
    value = (P['value'][0] + P['value'][1] * unit(tone, 0)) * (1.0 - P['rim'] * rim)
    gap = P['gap'][0] + P['gap'][1] * broadleaf
    # Wide edges blend each rim with its gap; lifting the gap as they widen keeps the mean steady with distance.
    gap = gap + (0.9 - gap) * 0.55 * saturate((edge - 0.8) / 4.0)
    factor = gap + (value - gap) * cover
    # Emergent crowns shade what lies below them on the side away from the sun, onto gaps and lower neighbours. A
    # shadow moves at most a quarter cell and is 3/4 of its crown wide, so it never reaches past the four cells searched.
    shade = np.zeros(shape)
    surface = np.where(best.inside, best.z, -4.0)
    longest = 0.25 * CELL / max(np.hypot(sox, soz), 1e-3)
    for present, h, cx, cz, radius, top, cell in crowns[:4]:
        lift = np.minimum(np.maximum(0.0, top - surface), longest)
        ux, uz = px - cx - sox * lift, pz - cz - soz * lift
        r = radius * 0.75
        own = best.inside & (best.hash == h)
        inside = 1.0 - smoothstep(np.maximum(r - edge, 0.0) ** 2, r * r, ux * ux + uz * uz)
        shade = np.maximum(shade, np.where(present, inside * np.where(own, 1.0 - cover, 1.0), 0.0))
    shadow = 1.0 - P['shadow'] * shade
    return dict(factor=factor * shadow, slope_x=best.gx * cover, slope_z=best.gz * cover, big=best.big & best.inside,
                strength=strength, share=share, hash=tone, cover=cover, px=px, pz=pz, shadow=shadow)


def meadow_noise(px, pz, salt):
    """ForestMeadowNoise: smooth value noise on a unit lattice, three hash bytes as three channels."""
    ix, iz = np.floor(px), np.floor(pz)
    fx, fz = px - ix, pz - iz
    fx, fz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    corners = [crown_hash(ix + a, iz + b, salt) for a, b in ((0, 0), (1, 0), (0, 1), (1, 1))]
    out = []
    for shift in (0, 8, 16):
        a, b, d, e = (unit(h, shift) for h in corners)
        out.append((a + (b - a) * fx) + ((d + (e - d) * fx) - (a + (b - a) * fx)) * fz)
    return out


def meadow(px, pz):
    """ForestMeadow: grass, bracken and scrub patches on an open floor in lattice metres; factor about 1 and a slope."""
    coarse = meadow_noise(px / P['meadow_cells'][0], pz / P['meadow_cells'][0], 5)[0]
    fine = meadow_noise(px / P['meadow_cells'][1], pz / P['meadow_cells'][1], 6)
    scrub = smoothstep(*P['scrub'], coarse * 0.7 + fine[0] * 0.3)
    amount = P['meadow_slope'][0] + P['meadow_slope'][1] * scrub
    value = P['meadow_value'][0] + (P['meadow_value'][1] - P['meadow_value'][0]) * scrub + (fine[0] - 0.5) * P['tussock']
    return value, (fine[1] - 0.5) * amount, (fine[2] - 0.5) * amount


def shade(g, world, ddx, ddy, sample, sun):
    """'after' tier-3 block: crowns thinned by the macro cover, photo clumps, meadow gaps, footprint fades."""
    footprint = np.maximum(np.hypot(ddx[0], ddx[1]), np.hypot(ddy[0], ddy[1]))
    macro = g['albedo']
    broadleaf = smoothstep(0.50, 0.58, macro[..., 0] / np.maximum(macro[..., 1], 1e-6))
    upright = g['normal'][..., 1] / np.maximum(np.linalg.norm(g['normal'], axis=-1), 1e-6)
    factor, slope_x, slope_z = pattern(world, ddx, ddy, footprint, broadleaf, g['alpha'], screen_derivatives(g['alpha'], g['depth']),
                                       upright, sample, sun)
    albedo = macro * factor[..., None]
    normal = g['normal'] + np.stack([slope_x, np.zeros_like(slope_x), slope_z], axis=-1)
    return albedo, normal


def screen_derivatives(a, depth):
    """ddx/ddy of the macro cover; at a depth break use the neighbour on the same surface, as a 2x2 quad mostly does."""
    def diff(axis):
        forward = np.diff(a, axis=axis, append=np.take(a, [-1], axis=axis))
        backward = np.diff(a, axis=axis, prepend=np.take(a, [0], axis=axis))
        df = np.diff(depth, axis=axis, append=np.take(depth, [-1], axis=axis))
        db = np.diff(depth, axis=axis, prepend=np.take(depth, [0], axis=axis))
        return np.where(np.abs(df) <= np.abs(db) * 4 + 1e-6, forward, backward)
    return diff(1), diff(0)


def pattern(world, ddx, ddy, footprint, broadleaf, canopy, canopy_d, upright, sample, sun):
    """Albedo factor and world slopes; split out so check_parity.py can drive it on a flat patch."""
    ox, oz = -sun[0] / max(sun[1], 0.2), -sun[2] / max(sun[1], 0.2)
    # A steep canopy face is the side of the edge trees: it stays foliage, never floor.
    side = smoothstep(*P['side'], upright)
    canopy = np.maximum(canopy, side)
    open_ = 1.0 - smoothstep(*P['open'], canopy)
    px = COS * world[0] - SIN * world[1]; pz = SIN * world[0] + COS * world[1]
    # The cover's world gradient from its screen derivatives, turned into lattice metres and capped.
    det = ddx[0] * ddy[1] - ddx[1] * ddy[0]
    ok = np.abs(det) > 1e-6
    safe = np.where(ok, det, 1.0)
    wgx = np.where(ok, (canopy_d[0] * ddy[1] - canopy_d[1] * ddx[1]) / safe, 0.0)
    wgz = np.where(ok, (ddx[0] * canopy_d[1] - ddy[0] * canopy_d[0]) / safe, 0.0)
    gx, gz = (COS * wgx - SIN * wgz) * (1 - side), (SIN * wgx + COS * wgz) * (1 - side)
    length = np.hypot(gx, gz)
    cap = np.minimum(1.0, P['grad_cap'] / np.maximum(length, 1e-6))
    gx, gz = gx * cap, gz * cap
    f = field(px, pz, footprint, broadleaf, COS * ox - SIN * oz, SIN * ox + COS * oz, canopy, gx, gz)
    # Foliage clumps: the photo crown map at 2.5 m, offset per crown so no repeat lines up between crowns.
    cu = f['px'] / CLUMP + unit(f['hash'], 24); cv = f['pz'] / CLUMP + unit(f['hash'], 12)
    rdx = (COS * ddx[0] - SIN * ddx[1]) / CLUMP, (SIN * ddx[0] + COS * ddx[1]) / CLUMP
    rdy = (COS * ddy[0] - SIN * ddy[1]) / CLUMP, (SIN * ddy[0] + COS * ddy[1]) / CLUMP
    clump = sample(cu, cv, rdx[0], rdx[1], rdy[0], rdy[1])
    # Deep in an opening the shader skips the lattice: no crown, no shadow, unit strength.
    run = canopy + np.hypot(gx, gz) * (2.0 * CELL) > 0.03
    cover = np.where(run, f['cover'], 0.0)
    shadow = np.where(run, f['shadow'], 1.0)
    strength = np.where(run, f['strength'], 1.0)
    big = run & f['big']
    clumping = P['gap_clump'] + (1.0 - P['gap_clump']) * cover
    raw = f['factor'] * (1.0 + (clump[..., 0] - 0.5) * P['clump_value'] * clumping) * (P['scale'][0] + P['scale'][1] * broadleaf)
    raw = np.where(run, raw, 1.0)
    csx = np.where(run, f['slope_x'] + (clump[..., 1] * 2 - 1) * P['clump_slope'] * cover, 0.0)
    csz = np.where(run, f['slope_z'] + (clump[..., 2] * 2 - 1) * P['clump_slope'] * cover, 0.0)
    # World-plan caps stretch down a steep face; their shading is flattened there.
    csx, csz = csx * (1 - P['side_flatten'] * side), csz * (1 - P['side_flatten'] * side)
    fade = np.where(big, 1 - smoothstep(*P['fade_big'], footprint), 1 - smoothstep(*P['fade_small'], footprint))
    k = fade * strength
    # Gaps in an opening show the floor, shaded by emergent crowns; it fades out with the crowns.
    floor_share = open_ * (1.0 - cover * fade)
    value, msx, msz = meadow(px, pz)
    fade_floor = 1 - smoothstep(*P['fade_floor'], footprint)
    floor_factor = np.where(floor_share > 0, 1.0 + (value * shadow - 1.0) * fade_floor, 1.0)
    msx = np.where(floor_share > 0, msx * fade_floor, 0.0); msz = np.where(floor_share > 0, msz * fade_floor, 0.0)
    factor = (P['far'] + (raw - P['far']) * k) * (1 - floor_share) + floor_factor * floor_share
    sx = csx * k * (1 - floor_share) + msx * floor_share
    sz = csz * k * (1 - floor_share) + msz * floor_share
    # Beyond a 3.6 m footprint the shader keeps the far mean and no slope.
    near = footprint < 3.6
    factor = np.where(near, factor, P['far'] + (1.0 - P['far']) * open_)
    sx = np.where(near, sx, 0.0); sz = np.where(near, sz, 0.0)
    # Lattice slopes are in rotated metres; turn them back to world x/z.
    return factor, COS * sx + SIN * sz, -SIN * sx + COS * sz
