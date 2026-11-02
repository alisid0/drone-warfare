"""Builds the enemy vehicles and exports each as an FBX for Unity. (Scenery: make_scenery.py, bases: make_base.py.)

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_world.py -- Assets/DroneSandbox/Resources/Models [preview_dir] [names]

preview_dir is optional ("" skips the previews); names is an optional comma-separated filter such as
Enemy_Tank,Enemy_Truck. Keep preview paths short: Blender fails on very long ones.

These are stand-ins sized for the game. Any model dropped into Resources/Models with the same
name (e.g. an Asset Store tank saved as a prefab called Enemy_Tank) replaces them.
Rotating parts are empties named "Turret" (tank turret, AA mount, radar head). The howitzer's tube hangs on an
empty named "Barrel" (origin at the breech) under an "Elevation" empty at the trunnions, so the game's recoil
kick slides it straight back along the bore.

Big shapes are bevelled meshes. The hundreds of small parts (track links, bolts, louvres, tyre lugs, lattice
bars) are written as raw geometry by a Kit, and the meshes under each empty are merged into one, so a model
is only a few renderers.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (box, box_uv_all, export, finish, material, net, pivot,  # noqa: E402
                    render_preview, reset_scene, root, strut)

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 and args[1] else None

R90 = math.radians(90)
M = {}


def make_materials():
    # Olive, OliveDark, SteelDark, Gunmetal, RustySteel and Canvas get photo textures in Unity (ProjectSetup).
    M["olive"] = material("Olive", (0.16, 0.18, 0.10), roughness=0.8)
    M["olive_dark"] = material("OliveDark", (0.10, 0.11, 0.06), roughness=0.8)
    M["rubber"] = material("Rubber", (0.025, 0.025, 0.025), roughness=0.9)
    M["gunmetal"] = material("Gunmetal", (0.09, 0.09, 0.09), metallic=0.7, roughness=0.45)
    M["steel"] = material("SteelDark", (0.10, 0.095, 0.09), metallic=0.6, roughness=0.55)
    M["rust"] = material("RustySteel", (0.20, 0.11, 0.06), metallic=0.3, roughness=0.8)
    M["glass"] = material("Glass", (0.02, 0.03, 0.04), metallic=0.3, roughness=0.08)
    M["canvas"] = material("Canvas", (0.30, 0.29, 0.18), roughness=0.95)
    M["strap"] = material("Strap", (0.05, 0.05, 0.035), roughness=0.9)
    M["seat"] = material("SeatPad", (0.035, 0.035, 0.03), roughness=0.7)
    M["orange"] = material("MarkingOrange", (0.80, 0.26, 0.03), roughness=0.6)
    M["amber"] = material("AmberLens", (0.85, 0.42, 0.04), roughness=0.2)
    M["red_lens"] = material("RedLens", (0.45, 0.02, 0.02), roughness=0.2)
    M["brass"] = material("Brass", (0.55, 0.40, 0.15), metallic=0.9, roughness=0.35)
    M["lamp"] = material("Lamp", (1.0, 0.9, 0.6), emission=(1.0, 0.9, 0.6))
    M["beacon"] = material("Beacon", (1.0, 0.1, 0.05), emission=(1.0, 0.1, 0.05))
    M["camo_a"] = material("CamoNetA", (0.07, 0.09, 0.035), roughness=1.0)
    M["camo_b"] = material("CamoNetB", (0.20, 0.17, 0.08), roughness=1.0)


# ---------- geometry kit ----------
# Rotations are Euler tuples or 3x3 matrices. Cylinders and lathes run along local +Z; a lathe profile is a list
# of (z, radius) points, and a radius of 0 closes the end on the axis.

def rotm(rot):
    if rot is None:
        return Matrix.Identity(3)
    if isinstance(rot, Matrix):
        return rot.to_3x3()
    return Euler(rot).to_matrix()


def aim(direction):
    """Rotation turning local +Z toward direction."""
    return Vector((0, 0, 1)).rotation_difference(Vector(direction).normalized()).to_matrix()


def basis(u, v, n):
    """Rotation whose local X, Y and Z axes are u, v and n."""
    return Matrix((tuple(u), tuple(v), tuple(n))).transposed()


TO_X = rotm((0, R90, 0))      # local +Z -> +X
TO_NX = rotm((0, -R90, 0))    # local +Z -> -X
TO_FWD = rotm((R90, 0, 0))    # local +Z -> -Y, the nose
TO_BACK = rotm((-R90, 0, 0))  # local +Z -> +Y


def side_rot(s):
    """Local +Z pointing out of the vehicle's flank on side s (+1 / -1 along X)."""
    return TO_X if s > 0 else TO_NX


HEXA_FACES = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]


def _chamfer_faces():
    def corner(bits):
        return 4 * bits[0] + 2 * bits[1] + bits[2]
    faces = []
    for a in range(3):
        b, c = [d for d in range(3) if d != a]
        for s in (0, 1):
            loop = []
            for pb, pc in ((0, 0), (1, 0), (1, 1), (0, 1)):
                bits = [0, 0, 0]
                bits[a], bits[b], bits[c] = s, pb, pc
                loop.append(corner(bits) * 3 + a)
            faces.append(tuple(loop))
        for pb in (0, 1):
            for pc in (0, 1):
                b0 = [0, 0, 0]
                b0[b], b0[c] = pb, pc
                b1 = list(b0)
                b1[a] = 1
                i0, i1 = corner(b0), corner(b1)
                faces.append((i0 * 3 + b, i1 * 3 + b, i1 * 3 + c, i0 * 3 + c))
    faces += [(k * 3, k * 3 + 1, k * 3 + 2) for k in range(8)]
    return faces


CHAMFER_FACES = _chamfer_faces()


def box_geo(size, ch=0.0):
    """A box (optionally with chamfered edges) centred on the origin."""
    h = (size[0] / 2, size[1] / 2, size[2] / 2)
    if ch <= 0:
        v = [(x, y, z) for x in (-h[0], h[0]) for y in (-h[1], h[1]) for z in (-h[2], h[2])]
        return v, [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    c = min(ch, h[0] * 0.45, h[1] * 0.45, h[2] * 0.45)
    v = []
    for i in (0, 1):
        for j in (0, 1):
            for k in (0, 1):
                s = (2 * i - 1, 2 * j - 1, 2 * k - 1)
                for a in range(3):
                    v.append(tuple(s[d] * (h[d] if d == a else h[d] - c) for d in range(3)))
    return v, CHAMFER_FACES


def lathe_geo(profile, n, closed=False):
    """A surface of revolution about local Z. Open profiles are capped at both ends."""
    verts, faces, rings = [], [], []
    for z, r in profile:
        if r <= 1e-6:
            rings.append([len(verts)])
            verts.append((0.0, 0.0, z))
        else:
            rings.append(list(range(len(verts), len(verts) + n)))
            verts += [(r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n), z) for k in range(n)]
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for A, B in pairs:
        if len(A) == 1 and len(B) == 1:
            continue
        if len(A) == 1:
            faces += [(A[0], B[(k + 1) % n], B[k]) for k in range(n)]
        elif len(B) == 1:
            faces += [(A[k], A[(k + 1) % n], B[0]) for k in range(n)]
        else:
            faces += [(A[k], A[(k + 1) % n], B[(k + 1) % n], B[k]) for k in range(n)]
    if not closed:
        faces += [tuple(R) for R in (rings[0], rings[-1]) if len(R) > 1]
    return verts, faces


def loft_geo(loops, caps=True):
    """Skins equal-length loops of points in order, capping the first and last loop."""
    n = len(loops[0])
    verts = [tuple(p) for loop in loops for p in loop]
    faces = []
    for i in range(len(loops) - 1):
        a, b = i * n, (i + 1) * n
        faces += [(a + k, a + (k + 1) % n, b + (k + 1) % n, b + k) for k in range(n)]
    if caps:
        faces += [tuple(range(n - 1, -1, -1)), tuple(range((len(loops) - 1) * n, len(loops) * n))]
    return verts, faces


def xprism(profile, x0, x1):
    """Loops for a solid whose (y, z) side profile is pushed across X from x0 to x1."""
    return [[Vector((x0, y, z)) for y, z in profile], [Vector((x1, y, z)) for y, z in profile]]


def zprism(outline, z0, z1):
    return [[Vector((x, y, z0)) for x, y in outline], [Vector((x, y, z1)) for x, y in outline]]


def beam_loops(a, b, w0, h0, w1=None, h1=None):
    """Loops for a (tapered) rectangular beam from a to b, kept upright."""
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    side = d.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    up = side.cross(d).normalized()
    w1 = w0 if w1 is None else w1
    h1 = h0 if h1 is None else h1

    def rect(p, w, h):
        return [p + side * sx * w / 2 + up * sz * h / 2 for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    return [rect(a, w0, h0), rect(b, w1, h1)]


def plate_loops(pts, n, thick):
    n = Vector(n).normalized() * thick / 2
    return [[Vector(p) - n for p in pts], [Vector(p) + n for p in pts]]


def make_mesh(name, verts, faces, mat, parent, bevel=0.0):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, mat, parent, bevel)


def solid(name, loops, mat, parent, bevel=0.02):
    """A bevelled solid skinned over loops (see xprism, zprism, beam_loops, plate_loops)."""
    v, f = loft_geo(loops)
    return make_mesh(name, v, f, mat, parent, bevel)


class Kit:
    """Collects small parts as raw geometry, one mesh per material, built in one go."""

    def __init__(self):
        self.parts = {}

    def add(self, mat, verts, faces, loc=(0, 0, 0), rot=None):
        m, o = rotm(rot), Vector(loc)
        entry = self.parts.setdefault(mat.name, [mat, [], []])
        base = len(entry[1])
        entry[1].extend(tuple(o + m @ Vector(p)) for p in verts)
        entry[2].extend(tuple(base + i for i in f) for f in faces)

    def box(self, mat, size, loc, rot=None, ch=0.0):
        v, f = box_geo(size, ch)
        self.add(mat, v, f, loc, rot)

    def cyl(self, mat, r, depth, loc, rot=None, n=12, r2=None):
        v, f = lathe_geo([(-depth / 2, r), (depth / 2, r if r2 is None else r2)], n)
        self.add(mat, v, f, loc, rot)

    def lathe(self, mat, profile, loc, rot=None, n=16, closed=False):
        v, f = lathe_geo(profile, n, closed)
        self.add(mat, v, f, loc, rot)

    def bar(self, mat, p1, p2, r, n=6, square=False):
        a, b = Vector(p1), Vector(p2)
        d = b - a
        if d.length < 1e-5:
            return
        if square:
            self.box(mat, (2 * r, 2 * r, d.length), (a + b) / 2, aim(d))
        else:
            self.cyl(mat, r, d.length, (a + b) / 2, aim(d), n)

    def path(self, mat, pts, r, n=6):
        for a, b in zip(pts, pts[1:]):
            self.bar(mat, a, b, r, n)

    def loft(self, mat, loops):
        v, f = loft_geo(loops)
        self.add(mat, v, f)

    def plate(self, mat, pts, n, thick):
        self.loft(mat, plate_loops(pts, n, thick))

    def build(self, name, parent):
        for mname, (mat, verts, faces) in self.parts.items():
            make_mesh(f"{name}_{mname}", verts, faces, mat, parent)
        self.parts = {}


# ---------- detail parts ----------

def torus_profile(R, r, m=8):
    return [(r * math.sin(2 * math.pi * i / m), R + r * math.cos(2 * math.pi * i / m)) for i in range(m)]


def quad_normal(quad, hint):
    p = [Vector(q) for q in quad]
    n = (p[1] - p[0]).cross(p[3] - p[0]).normalized()
    return n if n.dot(Vector(hint)) > 0 else -n


def brick(kit, mat, quad, n, thick, inset):
    """One armour brick standing on quad (4 points on a surface), with bevelled-looking sloped sides."""
    c = sum(quad, Vector()) / 4
    top = [p + n * thick + (c - p).normalized() * inset for p in quad]
    kit.add(mat, list(quad) + top, HEXA_FACES)


def tiles(kit, mat, quad, n, nu, nv, gap=0.02, thick=0.05, inset=0.014):
    """Reactive-armour bricks tiling a 4-cornered face (p00, p10, p11, p01), standing out along n."""
    p00, p10, p11, p01 = (Vector(q) for q in quad)
    n = Vector(n).normalized()

    def at(a, b):
        return p00 * (1 - a) * (1 - b) + p10 * a * (1 - b) + p11 * a * b + p01 * (1 - a) * b
    for i in range(nu):
        for j in range(nv):
            cell = [at(i / nu, j / nv), at((i + 1) / nu, j / nv), at((i + 1) / nu, (j + 1) / nv), at(i / nu, (j + 1) / nv)]
            c = sum(cell, Vector()) / 4
            brick(kit, mat, [p + (c - p).normalized() * gap * 0.7 for p in cell], n, thick, inset)


def bolts(kit, pts, axis_rot, r=0.013, h=0.014, mat=None):
    for p in pts:
        kit.cyl(mat or M["olive_dark"], r, h, p, axis_rot, n=6)


def tyre(kit, c, rot, r_out, width, r_rim, lug_h=None, n=32):
    """An off-road tyre about local Z with staggered chevron lugs that wrap over the shoulders."""
    rot, c = rotm(rot), Vector(c)
    lug_h = lug_h or r_out * 0.055
    hw = width / 2
    body = r_out - lug_h
    sh = min(0.3 * width, 0.45 * (body - r_rim))
    wall = body - sh - r_rim
    k = 1 - 0.7071
    prof = [(-hw * 0.8, r_rim), (-hw, r_rim + 0.3 * wall), (-hw * 1.03, r_rim + 0.7 * wall), (-hw, body - sh),
            (-hw + sh * k, body - sh * k), (-hw + sh, body), (hw - sh, body), (hw - sh * k, body - sh * k),
            (hw, body - sh), (hw * 1.03, r_rim + 0.7 * wall), (hw, r_rim + 0.3 * wall), (hw * 0.8, r_rim)]
    kit.lathe(M["rubber"], prof, c, rot, n=n, closed=True)
    count = max(12, round(2 * math.pi * r_out / 0.14))
    lug_len = 2 * math.pi * r_out / count * 0.6
    depth = lug_h + sh
    ez = Vector((0, 0, 1))
    for row in (-1, 1):
        ang = row * 0.32
        z0, z1 = row * hw * 0.06, row * (hw - 0.004)
        for i in range(count):
            th = 2 * math.pi * (i + (0.5 if row > 0 else 0)) / count
            er = Vector((math.cos(th), math.sin(th), 0))
            et = Vector((-math.sin(th), math.cos(th), 0))
            X = ez * math.cos(ang) + et * math.sin(ang)
            Y = -ez * math.sin(ang) + et * math.cos(ang)
            loc = er * (r_out - depth / 2) + ez * (z0 + z1) / 2
            kit.box(M["rubber"], (abs(z1 - z0) / math.cos(ang), lug_len, depth), c + rot @ loc, rot @ basis(X, Y, er))


def rim(kit, c, rot, r_rim, width, bolt_count=8, mat=None):
    """A steel wheel inside a tyre: dished face toward local +Z, a raised hub and two rings of bolts."""
    rot, c = rotm(rot), Vector(c)
    hw = width / 2
    prof = [(-hw * 0.7, 0), (-hw * 0.7, r_rim), (hw * 0.62, r_rim), (hw * 0.62, r_rim * 0.93), (hw * 0.3, r_rim * 0.88),
            (hw * 0.18, r_rim * 0.64), (hw * 0.38, r_rim * 0.56), (hw * 0.42, r_rim * 0.38), (hw * 0.78, r_rim * 0.33),
            (hw * 0.84, r_rim * 0.2), (hw * 1.0, r_rim * 0.17), (hw * 1.0, 0)]
    kit.lathe(mat or M["olive_dark"], prof, c, rot, n=24)
    for i in range(bolt_count):
        a = 2 * math.pi * (i + 0.5) / bolt_count
        kit.cyl(M["gunmetal"], r_rim * 0.055, 0.03, c + rot @ Vector((math.cos(a) * r_rim * 0.46, math.sin(a) * r_rim * 0.46, hw * 0.4)), rot, n=6)
    for i in range(12):
        a = 2 * math.pi * i / 12
        kit.cyl(M["olive_dark"], r_rim * 0.04, 0.02, c + rot @ Vector((math.cos(a) * r_rim * 0.76, math.sin(a) * r_rim * 0.76, hw * 0.24)), rot, n=6)


def wheel(kit, c, rot, r, width, rim_ratio=0.6):
    tyre(kit, c, rot, r, width, r * rim_ratio)
    rim(kit, c, rot, r * rim_ratio, width)


def headlight(kit, c, r=0.07, depth=0.1, cage=True, lens=None):
    """A round lamp whose front rim is at c, shining toward -Y, in a wire guard."""
    c = Vector(c)
    kit.lathe(M["olive_dark"], [(-depth, 0), (-depth, r * 0.75), (-depth * 0.4, r), (0, r), (0, r * 0.84), (-0.012, r * 0.84), (-0.012, 0)],
              c, TO_FWD, n=16)
    kit.lathe(lens or M["lamp"], [(-0.016, 0), (-0.016, r * 0.84), (-0.008, r * 0.8), (0.0, r * 0.45), (0.004, 0)], c, TO_FWD, n=16)
    if not cage:
        return
    R, front = r + 0.018, 0.065
    ring = [c + TO_FWD @ Vector((R * math.cos(a), R * math.sin(a), front)) for a in (2 * math.pi * i / 8 for i in range(9))]
    kit.path(M["olive_dark"], ring, 0.006, n=5)
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        kit.bar(M["olive_dark"], c + TO_FWD @ Vector((R * math.cos(a), R * math.sin(a), -0.035)),
                c + TO_FWD @ Vector((R * math.cos(a), R * math.sin(a), front)), 0.006, n=5)
    for a in (0, math.pi / 2):
        kit.bar(M["olive_dark"], c + TO_FWD @ Vector((R * math.cos(a), R * math.sin(a), front)),
                c + TO_FWD @ Vector((-R * math.cos(a), -R * math.sin(a), front)), 0.005, n=5)


def shackle(kit, c, rot, R=0.05, r=0.014, mat=None):
    kit.lathe(mat or M["gunmetal"], torus_profile(R, r), c, rot, n=12, closed=True)


def handwheel(kit, c, rot, R=0.1, mat=None):
    mat, rot, c = mat or M["gunmetal"], rotm(rot), Vector(c)
    kit.lathe(mat, torus_profile(R, 0.011, 6), c, rot, n=18, closed=True)
    for i in range(3):
        a = 2 * math.pi * i / 3 + 0.3
        kit.bar(mat, c, c + rot @ Vector((R * math.cos(a), R * math.sin(a), 0)), 0.008, n=5)
    kit.cyl(mat, 0.024, 0.05, c, rot, n=10)
    knob = c + rot @ Vector((R * math.cos(0.3), R * math.sin(0.3), 0.035))
    kit.cyl(M["rubber"], 0.012, 0.06, knob, rot, n=8)


def jerrycan(kit, c, rot=None, mat=None):
    """A jerry can standing at c, its broad faces looking along local X."""
    mat, R, c = mat or M["olive_dark"], rotm(rot), Vector(c)
    W, D, H = 0.13, 0.24, 0.33
    kit.box(mat, (W, D, H), c, R, ch=0.018)
    for sx in (-1, 1):
        for sa in (-1, 1):
            kit.box(mat, (0.01, 0.024, 0.29), c + R @ Vector((sx * W / 2, 0, -0.01)), R @ rotm((sa * 0.62, 0, 0)))
    top = H / 2
    for y in (-0.07, 0.0, 0.07):
        kit.bar(mat, c + R @ Vector((0, y, top)), c + R @ Vector((0, y, top + 0.035)), 0.008, n=5)
    kit.bar(mat, c + R @ Vector((0, -0.08, top + 0.035)), c + R @ Vector((0, 0.08, top + 0.035)), 0.011, n=6)
    kit.cyl(mat, 0.022, 0.05, c + R @ Vector((0, 0.085, top + 0.015)), R @ rotm((-0.5, 0, 0)), n=10)


def jack(kit, x, y, top, pad_r=0.15):
    """A screw levelling jack: a tube hanging from height top, a screw and a round foot on the ground."""
    kit.cyl(M["olive_dark"], 0.05, 0.40, (x, y, top - 0.20), n=12)
    kit.cyl(M["olive_dark"], 0.064, 0.05, (x, y, top - 0.025), n=12)
    kit.cyl(M["gunmetal"], 0.027, top - 0.40 - 0.03, (x, y, 0.03 + (top - 0.40 - 0.03) / 2), n=8)
    kit.lathe(M["rust"], [(0, 0), (0, pad_r), (0.025, pad_r), (0.05, pad_r * 0.55), (0.065, 0.045), (0.065, 0)], (x, y, 0), n=16)
    kit.bar(M["gunmetal"], (x, y, top), (x, y, top + 0.07), 0.015)
    kit.bar(M["gunmetal"], (x, y, top + 0.07), (x + 0.12, y, top + 0.07), 0.011)
    kit.cyl(M["rubber"], 0.013, 0.07, (x + 0.12, y, top + 0.1), n=8)


def sweep_rect(kit, mat, pts, normals, wdir, hw, thick):
    """A flat band (straps, rails) following pts, standing out along normals, hw half-wide along wdir."""
    wdir = Vector(wdir)
    verts, faces = [], []
    for p, n in zip(pts, normals):
        p, n = Vector(p), Vector(n)
        verts += [p - wdir * hw, p + wdir * hw, p + wdir * hw + n * thick, p - wdir * hw + n * thick]
    m = len(pts)
    for i in range(m - 1):
        a, b = 4 * i, 4 * (i + 1)
        faces += [(a + j, a + (j + 1) % 4, b + (j + 1) % 4, b + j) for j in range(4)]
    last = 4 * (m - 1)
    faces += [(3, 2, 1, 0), (last, last + 1, last + 2, last + 3)]
    kit.add(mat, verts, faces)


def louvres(kit, c, s, length, height, count, mat=None, back=None):
    """A louvred grille on a flank facing side s (+1 / -1 along X): a dark recess, a frame and slanted slats."""
    c = Vector(c)
    kit.box(back or M["rubber"], (0.006, length, height), c, None)
    for z in (-height / 2, height / 2):
        kit.box(mat or M["olive_dark"], (0.02, length + 0.04, 0.02), c + Vector((0, 0, z)), None)
    for y in (-length / 2, length / 2):
        kit.box(mat or M["olive_dark"], (0.02, 0.02, height + 0.04), c + Vector((0, y, 0)), None)
    for i in range(count):
        z = -height / 2 + (i + 0.5) * height / count
        kit.box(mat or M["olive_dark"], (0.035, length, 0.008), c + Vector((s * 0.008, 0, z)), (0, s * math.radians(40), 0))


# ---------- tracks ----------

def hull_2d(points):
    pts = sorted(set(points))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]  # counter-clockwise


def track_loop(circles, pitch):
    """Evenly spaced link centres (y, z) round a belt wrapped over circles (y, z, r), their tangents, and the spacing."""
    pts = [(cy + r * math.cos(2 * math.pi * k / 120), cz + r * math.sin(2 * math.pi * k / 120))
           for cy, cz, r in circles for k in range(120)]
    ring = hull_2d(pts)
    seg = [(ring[i], ring[(i + 1) % len(ring)]) for i in range(len(ring))]
    lengths = [math.dist(p, q) for p, q in seg]
    total = sum(lengths)
    count = round(total / pitch)
    step = total / count
    out, i, acc = [], 0, 0.0
    for k in range(count):
        s = k * step
        while acc + lengths[i] < s:
            acc += lengths[i]
            i += 1
        (p, q), t = seg[i], (s - acc) / lengths[i]
        out.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
    tangents = []
    for k in range(count):
        a, b = out[k - 1], out[(k + 1) % count]
        d = math.dist(a, b)
        tangents.append(((b[0] - a[0]) / d, (b[1] - a[1]) / d))
    return out, tangents, step


def road_wheel(kit, x, y, z, s, r=0.20):
    """A double road wheel (two rubber-tyred discs either side of the track's guide horns)."""
    rot = side_rot(s)
    for dx, outer in ((0.095, True), (-0.095, False)):
        c = Vector((x + s * dx, y, z))
        if outer:
            kit.cyl(M["rubber"], r, 0.07, c, rot, n=18)
            kit.lathe(M["olive_dark"], [(0.036, r - 0.03), (0.022, r - 0.055), (0.016, 0.09), (0.04, 0.075), (0.05, 0.056),
                                        (0.07, 0.048), (0.075, 0)], c, rot, n=18)
            bolts(kit, [c + rot @ Vector((0.064 * math.cos(a), 0.064 * math.sin(a), 0.052)) for a in (2 * math.pi * i / 5 for i in range(5))],
                  rot, r=0.009, h=0.016, mat=M["gunmetal"])
        else:
            kit.cyl(M["rubber"], r, 0.08, c, rot, n=14)


def sprocket(kit, x, y, z, s):
    rot = side_rot(s)
    for dx in (0.10, -0.10):
        c = Vector((x + s * dx, y, z))
        kit.cyl(M["olive_dark"], 0.155, 0.045, c, rot, n=22)
        for i in range(11):
            a = 2 * math.pi * (i + (0.5 if dx < 0 else 0)) / 11
            er = rot @ Vector((math.cos(a), math.sin(a), 0))
            et = rot @ Vector((-math.sin(a), math.cos(a), 0))
            kit.box(M["steel"], (0.06, 0.05, 0.045), c + er * 0.17, basis(er, et, rot @ Vector((0, 0, 1))), ch=0.01)
    c = Vector((x + s * 0.12, y, z))
    kit.lathe(M["olive_dark"], [(0, 0), (0, 0.11), (0.03, 0.1), (0.06, 0.06), (0.075, 0)], c, rot, n=16)
    bolts(kit, [c + rot @ Vector((0.085 * math.cos(a), 0.085 * math.sin(a), 0.025)) for a in (2 * math.pi * i / 8 for i in range(8))],
          rot, r=0.01, h=0.02, mat=M["gunmetal"])


def idler(kit, x, y, z, s, r=0.15):
    rot = side_rot(s)
    for dx in (0.095, -0.095):
        c = Vector((x + s * dx, y, z))
        kit.lathe(M["olive_dark"], [(-0.035, 0), (-0.035, r), (0.035, r), (0.035, r - 0.02), (0.015, r - 0.04), (0.015, 0.06),
                                    (0.04, 0.05), (0.06, 0)], c, rot, n=20)
        for i in range(6):  # lightening holes read as dark dimples
            a = 2 * math.pi * i / 6
            kit.cyl(M["rubber"], 0.022, 0.006, c + rot @ Vector((0.095 * math.cos(a), 0.095 * math.sin(a), 0.017)), rot, n=8)
    kit.box(M["olive_dark"], (0.08, 0.22, 0.08), (s * 0.64, y + 0.1, z + 0.02), (math.radians(-20), 0, 0), ch=0.015)


# ---------- enemy vehicles ----------

TANK_TRACK_X = 0.84
TURRET_WALL = (0.742, 0.26)  # turret flank: x at the base (z 1.0) and its inward lean per metre up


def turret_wall_x(z):
    return TURRET_WALL[0] - TURRET_WALL[1] * (z - 1.0)


def build_tank():
    r = root("Enemy_Tank")
    k = Kit()
    o, od, st = M["olive"], M["olive_dark"], M["steel"]

    # Hull: a wide upper hull over the tracks with a long sloped glacis, and a narrower lower hull between them.
    solid("UpperHull", xprism([(-1.62, 0.70), (-1.62, 0.76), (-1.02, 0.985), (1.46, 0.985), (1.58, 0.93), (1.58, 0.70)], -1.06, 1.06), o, r)
    solid("LowerHull", xprism([(-1.30, 0.20), (1.40, 0.20), (1.56, 0.45), (1.56, 0.71), (-1.60, 0.71)], -0.60, 0.60), o, r)

    gl0, gl1 = Vector((0, -1.62, 0.76)), Vector((0, -1.02, 0.985))
    up = gl1 - gl0
    upn = up.normalized()
    gn = Vector((0, -up.z, up.y)).normalized()  # out of the glacis: forward and up
    for s in (-1, 1):
        xa, xb = Vector((s * 0.26, 0, 0)), Vector((s * 1.04, 0, 0))
        tiles(k, o, [gl0 + xa + up * 0.04, gl0 + xb + up * 0.04, gl0 + xb + up * 0.93, gl0 + xa + up * 0.93], gn, 3, 2, thick=0.05)
    # Driver's hatch and periscopes at the top of the glacis.
    k.lathe(od, [(0, 0), (0, 0.17), (0.022, 0.17), (0.036, 0.14), (0.036, 0)], (0, -0.90, 0.985), n=20)
    k.box(od, (0.10, 0.05, 0.03), (0, -0.74, 1.0), ch=0.008)
    for i in (-1, 0, 1):
        p = gl0 + up * 0.955 + Vector((i * 0.13, 0, 0))
        k.box(od, (0.09, 0.07, 0.06), p + gn * 0.025, basis((1, 0, 0), upn, gn), ch=0.01)
        k.box(M["glass"], (0.07, 0.006, 0.028), p + gn * 0.03 - upn * 0.036, basis((1, 0, 0), upn, gn))

    lf0, lf1 = Vector((0, -1.60, 0.71)), Vector((0, -1.30, 0.20))  # lower front plate
    lfd = (lf1 - lf0).normalized()
    ln = Vector((0, lfd.z, -lfd.y))  # forward and down
    for s in (-1, 1):
        x = s * 0.64
        # Headlight in a wire guard on a bracket on the glacis armour.
        k.box(od, (0.07, 0.10, 0.07), (x, -1.47, 0.85), ch=0.01)
        headlight(k, (x, -1.535, 0.93), r=0.062, depth=0.09)
        # Tow hooks on the lower front plate.
        p = lf0 + (lf1 - lf0) * 0.38 + Vector((s * 0.40, 0, 0))
        k.box(od, (0.12, 0.10, 0.13), p + ln * 0.03, basis((1, 0, 0), lfd, ln), ch=0.015)
        shackle(k, p + ln * 0.095 + Vector((0, 0, -0.03)), TO_X, R=0.055, r=0.016)
        # Mud flap in front of each track, with orange chevrons.
        xt = s * TANK_TRACK_X
        k.box(od, (0.44, 0.025, 0.17), (xt, -1.655, 0.615), ch=0.006)
        for i in (-1, 1):
            k.box(M["orange"], (0.045, 0.006, 0.15), (xt + i * 0.075, -1.669, 0.615), (0, i * math.radians(35), 0))

    # Running gear and track: links wrapped round the idler, end road wheels and sprocket.
    circles = [(-1.47, 0.40, 0.175), (-1.05, 0.25, 0.225), (1.05, 0.25, 0.225), (1.47, 0.43, 0.185)]
    pts, tans, step = track_loop(circles, 0.115)
    for s in (-1, 1):
        x = s * TANK_TRACK_X
        for (py, pz), (ty, tz) in zip(pts, tans):
            t, n = Vector((0, ty, tz)), Vector((0, tz, -ty))
            R = basis((-1, 0, 0), t, n)
            c = Vector((x, py, pz))
            k.box(st, (0.40, step * 0.84, 0.04), c, R, ch=0.007)
            k.box(st, (0.30, 0.026, 0.022), c + n * 0.028, R)
            for e in (-1, 1):
                k.box(st, (0.036, 0.05, 0.05), c + Vector((e * 0.215, 0, 0)) + t * step / 2, R)
            k.box(st, (0.045, 0.05, 0.06), c - n * 0.045, R)
        for i in range(6):
            road_wheel(k, x, -1.05 + i * 0.42, 0.25, s)
        for y in (-0.45, 0.55):  # return rollers, mostly behind the skirts
            k.cyl(od, 0.055, 0.25, (x, y, 0.505), TO_X, n=12)
        sprocket(k, x, 1.47, 0.43, s)
        k.cyl(od, 0.11, 0.2, (s * 0.66, 1.47, 0.43), TO_X, n=14)  # final drive
        idler(k, x, -1.47, 0.40, s)

        # Armoured side skirts: six bolted panels, the front one cut up over the idler.
        xs = s * 1.085
        edges = [-1.40 + i * 0.475 for i in range(7)]
        for i in range(6):
            y0, y1 = edges[i] + 0.006, edges[i + 1] - 0.006
            z0 = 0.52 if i == 0 else 0.36
            solid(f"Skirt_{s}_{i}", xprism([(y0, z0), (y1, 0.36), (y1, 0.88), (y0, 0.88)], xs - 0.0175, xs + 0.0175), o, r, bevel=0.008)
            face = xs + s * 0.0175
            bolts(k, [Vector((face, yy, zz)) for yy in (y0 + 0.05, (y0 + y1) / 2, y1 - 0.05)
                      for zz in (0.84, z0 + (0.36 - z0) * (yy - y0) / (y1 - y0) + 0.05)], side_rot(s))
        k.box(od, (0.08, 3.16, 0.035), (s * 1.08, 0.02, 0.897), ch=0.008)  # rail along the top of the skirts
        for i in range(2):  # orange chevrons on the front skirt panel
            k.box(M["orange"], (0.006, 0.05, 0.30), (s * 1.1035, -1.25 + i * 0.12, 0.68), (math.radians(-35), 0, 0))
        # Tow cable lying along the deck edge.
        k.bar(st, (s * 0.94, -0.82, 1.005), (s * 0.94, 0.80, 1.005), 0.018, n=6)
        for y in (-0.86, 0.84):
            shackle(k, (s * 0.94, y, 1.005), None, R=0.04, r=0.012, mat=st)
        k.bar(od, (s * 1.0, -1.40, 0.70), (s * 1.0, -1.40, 0.88), 0.01)  # skirt hanger

    # Engine deck louvres and access hatch.
    for s in (-1, 1):
        cx = s * 0.52
        k.box(od, (0.78, 0.46, 0.014), (cx, 1.17, 0.99), ch=0.004)
        for i in range(9):
            k.box(M["gunmetal"], (0.72, 0.035, 0.012), (cx, 0.97 + i * 0.05, 1.0), (math.radians(30), 0, 0))
    k.box(od, (0.18, 0.44, 0.014), (0, 1.17, 0.99), ch=0.004)
    for y in (1.02, 1.32):
        k.bar(M["gunmetal"], (-0.05, y, 1.0), (0.05, y, 1.0), 0.008)

    # Rear: exhaust grille, lights, tow hooks and spare track links.
    k.box(M["rust"], (0.9, 0.05, 0.16), (0, 1.60, 0.80), ch=0.01)
    for i in range(5):
        k.box(M["gunmetal"], (0.86, 0.012, 0.018), (0, 1.628, 0.74 + i * 0.03))
    for s in (-1, 1):
        k.box(od, (0.12, 0.04, 0.08), (s * 0.90, 1.595, 0.88), ch=0.01)
        k.box(M["red_lens"], (0.09, 0.01, 0.05), (s * 0.90, 1.617, 0.88))
        k.box(od, (0.12, 0.10, 0.12), (s * 0.42, 1.60, 0.45), ch=0.015)
        shackle(k, (s * 0.42, 1.68, 0.42), TO_X, R=0.055, r=0.016)
        lc = Vector((s * 0.22, 1.585, 0.60))
        k.box(st, (0.40, 0.035, 0.10), lc, ch=0.006)
        k.box(st, (0.30, 0.02, 0.025), lc + Vector((0, 0.025, 0)))

    # ---- turret ----
    t = pivot("Turret", (0, 0.15, 0.98), r)
    kt = Kit()
    bottom = [(0.30, -0.80), (0.742, -0.42), (0.742, 1.02), (0.64, 1.22), (-0.64, 1.22), (-0.742, 1.02), (-0.742, -0.42), (-0.30, -0.80)]
    top = [(0.27, -0.66), (0.625, -0.36), (0.625, 0.98), (0.56, 1.14), (-0.56, 1.14), (-0.625, 0.98), (-0.625, -0.36), (-0.27, -0.66)]
    solid("TurretBody", [[Vector((x, y, 1.0)) for x, y in bottom], [Vector((x, y, 1.45)) for x, y in top]], o, t, bevel=0.025)
    solid("Bustle", [[Vector(p) for p in ((-0.60, 1.0, 1.06), (0.60, 1.0, 1.06), (0.60, 1.0, 1.44), (-0.60, 1.0, 1.44))],
                     [Vector(p) for p in ((-0.55, 1.50, 1.10), (0.55, 1.50, 1.10), (0.55, 1.50, 1.42), (-0.55, 1.50, 1.42))]], o, t, bevel=0.03)
    kt.lathe(od, [(0.955, 0.68), (1.0, 0.68)], (0, 0.15, 0), n=32)  # turret ring
    box("Mantlet", (0.58, 0.24, 0.36), (0, -0.88, 1.22), od, t, bevel=0.04)

    # Main gun: dust cover, thermal sleeve with clamps, fume extractor and a two-port muzzle brake.
    kt.lathe(od, [(0, 0), (0, 0.13), (0.12, 0.125), (0.24, 0.088), (0.24, 0)], (0, -1.0, 1.22), TO_FWD, n=18)
    kt.lathe(od, [(0.20, 0), (0.20, 0.068), (0.55, 0.068), (0.55, 0.075), (0.61, 0.075), (0.61, 0.068), (0.70, 0.068),
                  (0.76, 0.10), (1.02, 0.10), (1.08, 0.068), (1.45, 0.066), (1.45, 0.072), (1.51, 0.072), (1.51, 0.065),
                  (2.0, 0.061), (2.0, 0)], (0, -1.0, 1.22), TO_FWD, n=18)
    mb = Vector((0, -3.0, 1.22))
    kt.lathe(M["gunmetal"], [(0, 0.064), (0.24, 0.064)], mb, TO_FWD, n=16)
    for d0, d1 in ((0.0, 0.06), (0.10, 0.14), (0.19, 0.24)):
        kt.lathe(od, [(d0, 0.066), (d0, 0.098), (d1, 0.098), (d1, 0.066)], mb, TO_FWD, n=18, closed=True)
    for zz in (-1, 1):
        kt.box(od, (0.075, 0.24, 0.034), mb + Vector((0, -0.12, zz * 0.08)), ch=0.008)
    kt.cyl(M["rubber"], 0.042, 0.004, mb + Vector((0, -0.2425, 0)), TO_FWD, n=12)
    kt.cyl(M["gunmetal"], 0.022, 0.08, (0.21, -1.03, 1.14), TO_FWD, n=10)  # coaxial MG port

    for s in (-1, 1):
        # Reactive armour on the cheeks and the front of the flanks.
        cheek = [Vector((s * 0.31, -0.79, 1.02)), Vector((s * 0.73, -0.43, 1.02)), Vector((s * 0.62, -0.37, 1.43)), Vector((s * 0.28, -0.66, 1.43))]
        tiles(kt, o, cheek, quad_normal(cheek, (s, -1, 0.3)), 3, 3, thick=0.055)
        flank = [Vector((s * turret_wall_x(z), y, z)) for y, z in ((-0.37, 1.03), (0.13, 1.03), (0.13, 1.25), (-0.37, 1.25))]
        wn = Vector((s, 0, TURRET_WALL[1])).normalized()
        tiles(kt, o, flank, wn, 2, 2, thick=0.05)
        # Orange recognition stripe just ahead of the emblem (the game paints the emblem at y 0.25..0.65).
        stripe = [Vector((s * turret_wall_x(z), y, z)) + wn * 0.003 for y, z in ((0.16, 1.03), (0.22, 1.03), (0.22, 1.43), (0.16, 1.43))]
        kt.plate(M["orange"], stripe, wn, 0.006)
        # Smoke-grenade launchers: a 2x2 cluster on a bracket, pointing forward, out and up.
        base = Vector((s * 0.70, -0.16, 1.355))
        kt.box(od, (0.10, 0.30, 0.15), base, ch=0.015)
        R = aim(Vector((s * 0.35, -1, 0.45)))
        for i in (-1, 1):
            for j in (-1, 1):
                p = base + Vector((s * 0.07, 0.08, 0)) + R @ Vector((i * 0.05, j * 0.05, 0))
                kt.lathe(od, [(0, 0), (0, 0.045), (0.21, 0.045), (0.24, 0.048), (0.24, 0.036), (0.225, 0.036), (0.225, 0)], p, R, n=12)
        # Stowage box on the rear flank, with latches and a lid lip.
        kt.box(od, (0.17, 0.40, 0.26), (s * 0.78, 0.92, 1.23), ch=0.018)
        kt.box(od, (0.18, 0.41, 0.018), (s * 0.78, 0.92, 1.33), ch=0.005)
        for y in (0.82, 1.02):
            kt.box(M["gunmetal"], (0.012, 0.04, 0.05), (s * 0.868, y, 1.29))
        # Lifting eyes on the roof corners.
        for y in (-0.28, 1.0):
            shackle(kt, (s * 0.50, y, 1.475), TO_BACK if y < 0 else TO_FWD, R=0.032, r=0.01, mat=od)

    # Bustle rack round the back of the turret, with a rolled tarp, a box and a jerry can.
    rack = M["olive_dark"]
    for x in (-0.62, -0.31, 0.0, 0.31, 0.62):
        kt.bar(rack, (x, 1.74, 1.11), (x, 1.74, 1.45), 0.012)
    for z in (1.24, 1.45):
        kt.bar(rack, (-0.62, 1.74, z), (0.62, 1.74, z), 0.012)
    for s in (-1, 1):
        kt.bar(rack, (s * 0.64, 1.05, 1.45), (s * 0.64, 1.74, 1.45), 0.012)
        for y in (1.05, 1.40):
            kt.bar(rack, (s * 0.64, y, 1.30), (s * 0.64, y, 1.45), 0.012)
            kt.bar(rack, (s * 0.60, y, 1.30), (s * 0.64, y, 1.30), 0.01)
    kt.box(rack, (1.26, 0.25, 0.02), (0, 1.615, 1.11))
    kt.cyl(M["canvas"], 0.09, 0.95, (-0.12, 1.62, 1.21), TO_X, n=14)
    for x in (-0.48, 0.28):
        kt.cyl(M["strap"], 0.093, 0.035, (x, 1.62, 1.21), TO_X, n=14)
    kt.box(od, (0.24, 0.18, 0.16), (0.47, 1.62, 1.20), ch=0.015)
    jerrycan(kt, (0.47, 1.62, 1.44), rotm((0, 0, R90)))

    # Roof: commander's cupola with periscopes, independent sight, gunner's sight box, loader's hatch and MG.
    cup = Vector((0.36, 0.42, 1.45))
    kt.lathe(o, [(0, 0), (0, 0.22), (0.09, 0.215), (0.12, 0.19), (0.12, 0)], cup, n=24)
    kt.lathe(od, [(0.12, 0), (0.12, 0.17), (0.145, 0.165), (0.165, 0.11), (0.172, 0)], cup, n=20)
    kt.box(od, (0.10, 0.05, 0.04), cup + Vector((0, 0.17, 0.14)), ch=0.008)
    for i in range(6):
        a = math.radians(-150 + i * 40)
        er = Vector((math.cos(a), math.sin(a), 0))
        R = basis(Vector((-er.y, er.x, 0)), er, (0, 0, 1))
        kt.box(od, (0.07, 0.05, 0.05), cup + er * 0.205 + Vector((0, 0, 0.065)), R, ch=0.008)
        kt.box(M["glass"], (0.055, 0.006, 0.026), cup + er * 0.232 + Vector((0, 0, 0.068)), R)
    cs = Vector((0.10, 0.05, 1.45))
    kt.lathe(od, [(0, 0), (0, 0.075), (0.04, 0.075), (0.06, 0.05), (0.22, 0.05), (0.22, 0)], cs, n=14)
    kt.box(od, (0.20, 0.20, 0.16), cs + Vector((0, 0, 0.30)), ch=0.02)
    kt.box(od, (0.22, 0.10, 0.02), cs + Vector((0, -0.05, 0.39)), ch=0.005)
    for x in (-0.045, 0.045):
        kt.box(M["glass"], (0.07, 0.006, 0.08), cs + Vector((x, -0.102, 0.30)))
    gs = Vector((-0.40, -0.20, 1.55))
    kt.box(od, (0.24, 0.30, 0.20), gs, ch=0.025)
    kt.box(M["glass"], (0.16, 0.006, 0.08), gs + Vector((0, -0.152, 0.02)))
    kt.box(od, (0.22, 0.06, 0.016), gs + Vector((0, -0.17, 0.075)), (math.radians(-15), 0, 0))
    lh = Vector((-0.32, 0.46, 1.45))
    kt.lathe(od, [(0, 0), (0, 0.19), (0.03, 0.19), (0.05, 0.15), (0.05, 0)], lh, n=20)
    kt.box(od, (0.12, 0.05, 0.04), lh + Vector((0, 0.19, 0.03)), ch=0.008)
    kt.bar(M["gunmetal"], lh + Vector((-0.06, -0.08, 0.06)), lh + Vector((0.06, -0.08, 0.06)), 0.009)
    mg = Vector((-0.32, 0.12, 1.45))
    gm = M["gunmetal"]
    kt.cyl(od, 0.08, 0.03, mg + Vector((0, 0, 0.015)), n=14)
    kt.cyl(gm, 0.025, 0.24, mg + Vector((0, 0, 0.14)), n=10)
    kt.box(gm, (0.07, 0.36, 0.09), mg + Vector((0, -0.04, 0.29)), ch=0.01)
    kt.box(gm, (0.03, 0.08, 0.04), mg + Vector((0, -0.08, 0.355)), ch=0.006)
    kt.cyl(gm, 0.022, 0.20, mg + Vector((0, -0.32, 0.30)), TO_FWD, n=10)
    kt.cyl(gm, 0.014, 0.40, mg + Vector((0, -0.58, 0.30)), TO_FWD, n=8)
    kt.cyl(gm, 0.021, 0.06, mg + Vector((0, -0.79, 0.30)), TO_FWD, n=8)
    kt.box(od, (0.09, 0.14, 0.11), mg + Vector((-0.085, -0.02, 0.265)), ch=0.01)
    kt.box(od, (0.34, 0.012, 0.20), mg + Vector((0, -0.25, 0.31)), ch=0.004)
    for x in (-0.035, 0.035):
        kt.bar(gm, mg + Vector((x, 0.15, 0.27)), mg + Vector((x, 0.18, 0.35)), 0.011)
    # Antennas and a wind sensor at the back of the roof.
    for x, h in ((-0.52, 1.35), (0.52, 0.95)):
        a = Vector((x, 1.02, 1.45))
        kt.lathe(od, [(0, 0), (0, 0.035), (0.06, 0.03), (0.08, 0.016), (0.16, 0.016), (0.16, 0)], a, n=10)
        kt.cyl(gm, 0.007, h, a + Vector((0, 0, 0.16 + h / 2)), n=5)
    kt.cyl(gm, 0.012, 0.22, (0.12, 0.98, 1.56), n=6)
    kt.lathe(gm, [(0, 0), (0, 0.03), (0.04, 0.03), (0.04, 0)], (0.12, 0.98, 1.67), n=8)

    k.build("HullParts", r)
    kt.build("TurretParts", t)
    return r


def canvas_section(y, sag, w=0.85, z0=1.08, z1=1.70, rise=0.28):
    """Cross-section of the truck's canvas cover at y: a closed outline in X/Z, sagging between the hoops."""
    side_in, roof_drop = 0.011 * sag, 0.04 * sag
    right = [(w - side_in * math.sin(math.pi * f), z0 + f * (z1 - z0)) for f in (0.0, 0.25, 0.5, 0.75)]
    arch = []
    for i in range(13):
        a = math.pi * i / 12
        drop = roof_drop * math.sin(a) ** 2
        arch.append((w * math.cos(a) * (1 - 0.01 * sag * math.sin(a)), z1 + (rise * math.sin(a) - drop)))
    left = [(-x, z) for x, z in reversed(right)]
    return [Vector((x, y, z)) for x, z in right + arch + left]


def outline_normals(pts):
    """Outward normals (in X/Z) of a counter-clockwise outline seen from the front."""
    out = []
    for i in range(len(pts)):
        a, b = pts[i - 1], pts[(i + 1) % len(pts)]
        d = b - a
        out.append(Vector((d.z, 0, -d.x)).normalized())
    return out


def build_truck():
    r = root("Enemy_Truck")
    k = Kit()
    o, od, gm = M["olive"], M["olive_dark"], M["gunmetal"]
    WR = 0.40
    axles = (-1.20, 0.50, 1.36)

    # Six chunky tyres, axles with differentials, leaf springs and the ladder frame.
    for y in axles:
        for s in (-1, 1):
            wheel(k, (s * 0.80, y, WR), side_rot(s), WR, 0.30)
        k.cyl(od, 0.055, 1.30, (0, y, WR), TO_X, n=12)
        k.lathe(od, [(-0.15, 0), (-0.15, 0.08), (-0.06, 0.16), (0.07, 0.16), (0.14, 0.08), (0.14, 0)], (0, y, WR), TO_BACK, n=14)
    for s in (-1, 1):
        k.box(od, (0.10, 3.62, 0.20), (s * 0.42, -0.07, 0.68), ch=0.01)
        for i, ln in enumerate((0.95, 0.75, 0.55)):
            k.box(M["steel"], (0.08, ln, 0.022), (s * 0.42, -1.20, 0.555 - i * 0.024))
            k.box(M["steel"], (0.08, ln + 0.25, 0.022), (s * 0.42, 0.93, 0.555 - i * 0.024))
    for y in (-1.80, -0.90, -0.10, 0.93, 1.70):
        k.box(od, (0.74, 0.08, 0.12), (0, y, 0.68))
    k.bar(gm, (0, -1.05, WR + 0.02), (0, 0.38, WR + 0.02), 0.035, n=8)
    k.bar(gm, (0, 0.62, WR + 0.02), (0, 1.24, WR + 0.02), 0.035, n=8)

    # Front: bumper with tow shackles, grille, bonnet with side louvres, wings and caged headlights.
    box("Bumper", (1.92, 0.14, 0.20), (0, -1.95, 0.66), od, r, bevel=0.02)
    for s in (-1, 1):
        k.box(od, (0.10, 0.08, 0.10), (s * 0.52, -2.04, 0.62), ch=0.012)
        shackle(k, (s * 0.52, -2.10, 0.60), TO_X, R=0.055, r=0.017)
        bolts(k, [Vector((s * x, -2.022, z)) for x in (0.25, 0.80) for z in (0.60, 0.72)], TO_FWD)
    solid("Bonnet", [[Vector(p) for p in ((-0.50, -1.90, 0.76), (0.50, -1.90, 0.76), (0.50, -1.90, 1.26), (-0.50, -1.90, 1.26))],
                     [Vector(p) for p in ((-0.50, -1.12, 0.76), (0.50, -1.12, 0.76), (0.50, -1.12, 1.32), (-0.50, -1.12, 1.32))]], o, r, bevel=0.06)
    k.box(M["rubber"], (0.80, 0.02, 0.38), (0, -1.905, 1.0))
    for x in (-0.44, 0.44):
        k.box(od, (0.06, 0.05, 0.50), (x, -1.915, 1.0), ch=0.01)
    for z in (0.75, 1.25):
        k.box(od, (0.94, 0.05, 0.06), (0, -1.915, z), ch=0.01)
    for i in range(7):
        k.box(od, (0.026, 0.03, 0.40), (-0.33 + i * 0.11, -1.925, 1.0))
    for z in (0.90, 1.0, 1.10):
        k.box(od, (0.84, 0.03, 0.022), (0, -1.928, z))
    for s in (-1, 1):
        for i in range(6):
            k.box(od, (0.03, 0.30, 0.008), (s * 0.506, -1.62, 0.90 + i * 0.03), (0, s * math.radians(40), 0))
        k.box(M["orange"], (0.006, 0.66, 0.045), (s * 0.502, -1.52, 1.13))
        k.box(gm, (0.02, 0.06, 0.03), (s * 0.505, -1.25, 1.2))
        # Front wing: flat top, sloped front apron, rear apron over the step.
        box(f"Wing_{s}", (0.48, 1.06, 0.04), (s * 0.74, -1.30, 0.98), o, r, bevel=0.012)
        box(f"WingFront_{s}", (0.48, 0.04, 0.24), (s * 0.74, -1.865, 0.875), o, r, rot=(math.radians(-22), 0, 0), bevel=0.012)
        box(f"WingBack_{s}", (0.48, 0.035, 0.30), (s * 0.74, -0.78, 0.84), o, r, bevel=0.01)
        k.box(M["rubber"], (0.32, 0.015, 0.40), (s * 0.80, -0.78, 0.49))
        headlight(k, (s * 0.66, -1.94, 0.95), r=0.085, depth=0.13)
        k.box(od, (0.05, 0.05, 0.05), (s * 0.94, -1.80, 1.02), ch=0.008)
        k.box(M["amber"], (0.035, 0.02, 0.03), (s * 0.94, -1.83, 1.02))

    # Cab with a raked two-pane windscreen, doors, steps, mirrors and a roof light bar.
    solid("Cab", xprism([(-1.12, 0.86), (-0.42, 0.86), (-0.42, 1.86), (-0.97, 1.86), (-1.12, 1.36)], -0.92, 0.92), o, r, bevel=0.05)
    w0, w1 = Vector((0, -1.12, 1.36)), Vector((0, -0.97, 1.86))
    wd = (w1 - w0)
    wn = Vector((0, -wd.z, wd.y)).normalized()
    for s in (-1, 1):
        xa, xb = s * 0.05, s * 0.84
        pane = [w0 + wd * a + Vector((x, 0, 0)) + wn * 0.006 for x, a in ((xa, 0.12), (xb, 0.12), (xb, 0.90), (xa, 0.90))]
        k.plate(M["glass"], pane, wn, 0.01)
        k.bar(M["rubber"], w0 + wd * 0.16 + Vector((s * 0.14, 0, 0)) + wn * 0.016, w0 + wd * 0.30 + Vector((s * 0.58, 0, 0)) + wn * 0.016, 0.007, n=5)
    k.bar(od, w0 + wd * 0.05 + wn * 0.008, w0 + wd * 0.97 + wn * 0.008, 0.02, n=6, square=True)
    for s in (-1, 1):
        x = s * 0.92
        k.box(o, (0.012, 0.52, 0.92), (x, -0.72, 1.36), ch=0.004)  # door skin
        k.box(M["glass"], (0.012, 0.36, 0.34), (x + s * 0.004, -0.75, 1.58))
        k.box(gm, (0.03, 0.09, 0.025), (x + s * 0.015, -0.53, 1.30))
        for z in (1.08, 1.62):
            k.cyl(gm, 0.016, 0.07, (x + s * 0.008, -0.985, z), n=8)
        # Steps under the door, a grab handle, and a mirror on an arm.
        for z in (0.48, 0.70):
            k.box(M["steel"], (0.17, 0.24, 0.03), (s * 0.86, -0.60, z), ch=0.006)
        for y in (-0.72, -0.48):
            k.box(od, (0.03, 0.02, 0.42), (s * 0.86, y, 0.66))
        k.bar(gm, (x + s * 0.03, -0.44, 1.05), (x + s * 0.03, -0.44, 1.62), 0.013)
        for z in (1.05, 1.62):
            k.bar(gm, (x, -0.44, z), (x + s * 0.03, -0.44, z), 0.01)
        k.bar(gm, (x, -1.0, 1.52), (s * 1.10, -1.06, 1.60), 0.012)
        k.bar(gm, (x, -1.0, 1.75), (s * 1.10, -1.06, 1.62), 0.01)
        k.box(od, (0.04, 0.13, 0.25), (s * 1.11, -1.06, 1.60), ch=0.01)
        k.box(M["glass"], (0.03, 0.006, 0.21), (s * 1.11, -0.993, 1.60))
    k.box(M["glass"], (0.70, 0.012, 0.28), (0, -0.414, 1.60))
    k.box(od, (1.30, 0.08, 0.06), (0, -0.90, 1.95), ch=0.01)
    for x in (-0.5, 0.5):
        k.box(od, (0.05, 0.06, 0.06), (x, -0.90, 1.89))
    for x in (-0.45, -0.15, 0.15, 0.45):
        k.lathe(od, [(-0.03, 0), (-0.03, 0.045), (0.03, 0.045), (0.03, 0)], (x, -0.92, 2.03), TO_FWD, n=12)
        k.lathe(M["amber"], [(0.02, 0), (0.02, 0.038), (0.035, 0.03), (0.04, 0)], (x, -0.92, 2.03), TO_FWD, n=12)
    k.box(od, (0.50, 0.50, 0.035), (-0.35, -0.62, 1.875), ch=0.008)
    k.cyl(od, 0.10, 0.62, (0.28, -0.62, 1.98), TO_X, n=16)
    for x in (0.08, 0.48):
        k.box(M["strap"], (0.03, 0.16, 0.13), (x, -0.62, 1.94))
    k.lathe(od, [(0, 0), (0, 0.025), (0.05, 0.02), (0.05, 0)], (-0.86, -0.50, 1.86), n=8)
    k.cyl(gm, 0.006, 1.0, (-0.86, -0.50, 2.41), n=5)

    # Behind the cab: upright spare wheel, exhaust stack with a heat shield, air filter.
    wheel(k, (0.32, -0.27, 1.30), TO_BACK, WR, 0.28)
    k.box(od, (0.10, 0.06, 0.50), (0.32, -0.40, 1.15), ch=0.01)
    k.bar(M["rust"], (-0.70, -0.30, 0.78), (-0.70, -0.30, 1.92), 0.055, n=12)
    k.bar(M["rust"], (-0.70, -0.30, 1.92), (-0.70, -0.20, 2.02), 0.05, n=12)
    k.box(od, (0.10, 0.04, 0.55), (-0.70, -0.37, 1.45), ch=0.008)
    for i in range(5):
        k.box(M["rubber"], (0.07, 0.006, 0.02), (-0.70, -0.392, 1.25 + i * 0.09))
    k.lathe(od, [(0, 0), (0, 0.13), (0.42, 0.13), (0.46, 0.08), (0.46, 0)], (-0.42, -0.27, 1.05), n=16)

    # Fuel tank on the left, jerry-can rack on the right, between the front wheels and the rear bogie.
    k.cyl(od, 0.17, 0.50, (-0.72, -0.20, 0.62), TO_BACK, n=18)
    for y in (-0.36, -0.04):
        k.cyl(M["strap"], 0.175, 0.03, (-0.72, y, 0.62), TO_BACK, n=18)
    k.cyl(gm, 0.04, 0.05, (-0.72, -0.10, 0.80), n=10)
    k.box(od, (0.20, 0.52, 0.025), (0.80, -0.19, 0.435))
    for y in (-0.30, -0.06):
        jerrycan(k, (0.80, y, 0.615))
    k.bar(M["strap"], (0.87, -0.44, 0.70), (0.87, 0.07, 0.70), 0.012)

    # Load bed: floor, ribbed drop sides (kept flush so the game's flank emblem sits proud), tailgate.
    box("BedFloor", (1.72, 1.92, 0.08), (0, 0.82, 0.88), od, r, bevel=0.01)
    for y in (-0.08, 0.40, 0.88, 1.36, 1.74):
        k.box(od, (1.72, 0.06, 0.06), (0, y, 0.81))
    for s in (-1, 1):
        k.box(od, (0.10, 1.90, 0.06), (s * 0.42, 0.82, 0.81))
        k.box(od, (0.04, 1.92, 0.20), (s * 0.84, 0.82, 1.02), ch=0.006)
        for z in (0.97, 1.07):
            k.box(od, (0.006, 1.90, 0.025), (s * 0.8625, 0.82, z))
        for y in (-0.10, 0.12, 1.04, 1.76):
            k.box(od, (0.012, 0.05, 0.20), (s * 0.8635, y, 1.02))
        for i in range(7):
            k.box(gm, (0.02, 0.03, 0.02), (s * 0.858, -0.05 + i * 0.30, 0.905))
        k.box(M["rubber"], (0.32, 0.015, 0.52), (s * 0.80, 1.80, 0.57))
    k.box(od, (1.72, 0.04, 0.20), (0, -0.12, 1.02), ch=0.006)
    k.box(od, (1.72, 0.04, 0.20), (0, 1.76, 1.02), ch=0.006)
    for x in (-0.6, -0.2, 0.2, 0.6):
        k.box(od, (0.05, 0.012, 0.20), (x, 1.783, 1.02))
    for s in (-1, 1):
        k.bar(gm, (s * 0.82, 1.79, 1.10), (s * 0.70, 1.79, 0.93), 0.006, n=4)
        k.box(od, (0.14, 0.05, 0.08), (s * 0.68, 1.76, 0.70), ch=0.01)
        k.box(M["red_lens"], (0.10, 0.01, 0.05), (s * 0.68, 1.787, 0.70))
    k.box(od, (1.30, 0.08, 0.10), (0, 1.78, 0.60), ch=0.01)
    shackle(k, (0, 1.86, 0.60), TO_X, R=0.05, r=0.016)

    # Canvas cover over hoops: it sags between them, so the hoops show as ridges.
    hoops = [-0.10, 0.365, 0.83, 1.295, 1.76]
    sections = []
    for i in range(len(hoops) - 1):
        for j in range(8):
            f = j / 8
            sections.append(canvas_section(hoops[i] + f * (hoops[i + 1] - hoops[i]), math.sin(math.pi * f) ** 0.8))
    sections.append(canvas_section(hoops[-1], 0.0))
    v, f = loft_geo(sections)
    k.add(M["canvas"], v, f)
    # Tie-down straps over the roof (kept clear of the emblem at y 0.2..0.9), buckles at the bottom.
    for y in (0.08, 1.07, 1.53):
        sec = canvas_section(y, 0.0)[1:-1]
        nrm = outline_normals(canvas_section(y, 0.0))[1:-1]
        sweep_rect(k, M["strap"], sec, nrm, (0, 1, 0), 0.024, 0.008)
        for s in (-1, 1):
            k.box(gm, (0.012, 0.06, 0.05), (s * 0.856, y, 1.18))
    for s in (-1, 1):  # lashing rope zig-zagging from the hooks under the rail up to the canvas skirt
        k.path(M["strap"], [Vector((s * 0.862, -0.05 + i * 0.15, 0.905 if i % 2 == 0 else 1.17)) for i in range(13)], 0.005, n=4)
    for x in (-0.45, 0.45):
        k.box(M["strap"], (0.05, 0.01, 0.60), (x, 1.765, 1.40))
    k.cyl(M["canvas"], 0.065, 1.66, (0, 1.80, 1.72), TO_X, n=14)

    k.build("TruckParts", r)
    return r


def build_aa_gun():
    """Twin-barrel AA gun on a four-wheel carriage braced on outrigger legs; the armoured mount turns."""
    r = root("Enemy_AAGun")
    k = Kit()
    o, od, gm = M["olive"], M["olive_dark"], M["gunmetal"]

    solid("Carriage", xprism([(-1.15, 0.47), (-1.04, 0.40), (1.04, 0.40), (1.15, 0.47), (1.15, 0.62), (-1.15, 0.62)], -0.52, 0.52), o, r, bevel=0.025)
    for s in (-1, 1):
        k.box(od, (0.08, 2.24, 0.12), (s * 0.56, 0, 0.46), ch=0.01)
        bolts(k, [Vector((s * 0.522, y, 0.58)) for y in (-0.9, -0.45, 0, 0.45, 0.9)], side_rot(s))
        # Stowage box hung on the frame between the wheels.
        k.box(od, (0.15, 0.62, 0.22), (s * 0.67, 0, 0.42), ch=0.015)
        k.box(od, (0.16, 0.63, 0.03), (s * 0.67, 0, 0.54), ch=0.006)
        for y in (-0.18, 0.18):
            k.box(gm, (0.012, 0.04, 0.05), (s * 0.748, y, 0.49))
        k.box(M["red_lens"], (0.07, 0.01, 0.04), (s * 0.40, 1.155, 0.55))
    for i in range(5):  # anti-slip ribs on the exposed deck ends
        for e in (-1, 1):
            k.box(od, (0.90, 0.025, 0.012), (0, e * (0.66 + i * 0.1), 0.626))
    shackle(k, (0, 1.20, 0.50), TO_X, R=0.05, r=0.015)
    for y in (-0.90, 0.90):
        k.box(od, (1.48, 0.12, 0.10), (0, y, 0.33), ch=0.01)
        for s in (-1, 1):
            wheel(k, (s * 0.80, y, 0.33), side_rot(s), 0.33, 0.22)
            # Mudguard over the wheel.
            k.box(od, (0.27, 0.62, 0.022), (s * 0.80, y, 0.715), ch=0.005)
            for e in (-1, 1):
                k.box(od, (0.27, 0.20, 0.022), (s * 0.80, y + e * 0.38, 0.66), (e * math.radians(-35), 0, 0), ch=0.005)
            k.bar(od, (s * 0.60, y, 0.55), (s * 0.70, y, 0.705), 0.015)

    # Outrigger legs with screw jacks and foot pads, and the towing drawbar.
    for sx in (-1, 1):
        for sy in (-1, 1):
            hinge = Vector((sx * 0.50, sy * 1.02, 0.52))
            foot = Vector((sx * 1.45, sy * 1.22, 0.47))
            solid(f"Leg_{sx}_{sy}", beam_loops(hinge, foot, 0.13, 0.15, 0.09, 0.10), o, r, bevel=0.012)
            k.cyl(gm, 0.05, 0.20, hinge, n=12)
            k.bar(od, hinge + Vector((0, 0, -0.10)), hinge.lerp(foot, 0.55) + Vector((0, 0, -0.04)), 0.022)
            jack(k, foot.x, foot.y, 0.62, pad_r=0.16)
            bolts(k, [hinge.lerp(foot, f) + Vector((0, 0, 0.065 - f * 0.02)) for f in (0.2, 0.45, 0.7)], None, r=0.012)
    for s in (-1, 1):
        k.bar(o, (s * 0.32, -1.12, 0.47), (0, -1.70, 0.42), 0.035, square=True)
    shackle(k, (0, -1.78, 0.42), None, R=0.06, r=0.018)
    k.box(od, (0.10, 0.12, 0.08), (0, -1.70, 0.42), ch=0.01)

    # ---- the turning mount ----
    t = pivot("Turret", (0, 0, 0.3), r)
    kt = Kit()
    kt.lathe(gm, [(0.62, 0.52), (0.68, 0.52)], (0, 0, 0), n=28)
    # Round top carriage: it clears the wheel mudguards (0.89 m out) as the mount turns.
    kt.lathe(od, [(0.68, 0), (0.68, 0.84), (0.695, 0.86), (0.735, 0.86), (0.75, 0.84), (0.75, 0)], (0, 0, 0), n=36)
    bolts(kt, [Vector((0.78 * math.cos(a), 0.78 * math.sin(a), 0.755)) for a in (2 * math.pi * (i + 0.5) / 16 for i in range(16))], None, r=0.014)
    for s in (-1, 1):
        x0, x1 = sorted((s * 0.27, s * 0.56))
        solid(f"Cheek_{s}", xprism([(-0.50, 0.75), (0.55, 0.75), (0.55, 1.28), (0.38, 1.42), (-0.20, 1.42), (-0.50, 1.04)], x0, x1), o, t, bevel=0.025)
        xo = s * 0.56
        kt.lathe(od, [(0, 0), (0, 0.12), (0.03, 0.11), (0.04, 0.06), (0.06, 0)], (xo, -0.10, 0.95), side_rot(s), n=16)
        bolts(kt, [Vector((xo + s * 0.03, -0.10 + 0.09 * math.cos(a), 0.95 + 0.09 * math.sin(a))) for a in (2 * math.pi * i / 6 for i in range(6))],
              side_rot(s), r=0.01)
        kt.box(o, (0.008, 0.48, 0.30), (s * 0.564, 0.22, 1.02), ch=0.003)
        bolts(kt, [Vector((s * 0.57, y, z)) for y in (0.0, 0.44) for z in (0.89, 1.15)], side_rot(s), r=0.011)
        kt.box(M["orange"], (0.006, 0.66, 0.05), (s * 0.566, 0.11, 1.30))
    handwheel(kt, (-0.64, 0.30, 0.98), TO_NX, R=0.10)
    handwheel(kt, (0.64, 0.30, 0.98), TO_X, R=0.10)
    # Gunner's seat at the back right; ammunition boxes at the back left.
    seat = Vector((0.36, 0.74, 0))
    kt.bar(gm, seat + Vector((0, 0, 0.75)), seat + Vector((0, 0, 1.0)), 0.03, n=10)
    kt.box(M["seat"], (0.30, 0.28, 0.05), seat + Vector((0, 0, 1.03)), ch=0.015)
    kt.box(gm, (0.32, 0.30, 0.02), seat + Vector((0, 0, 1.0)))
    kt.box(M["seat"], (0.30, 0.045, 0.24), seat + Vector((0, 0.155, 1.20)), (math.radians(-12), 0, 0), ch=0.015)
    for dx in (-0.13, 0.13):
        kt.bar(gm, seat + Vector((dx, 0.13, 1.02)), seat + Vector((dx, 0.19, 1.28)), 0.012)
        kt.bar(gm, seat + Vector((dx, -0.10, 1.10)), seat + Vector((dx, 0.12, 1.10)), 0.01)
    kt.bar(gm, seat + Vector((-0.13, -0.32, 0.82)), seat + Vector((0.13, -0.32, 0.82)), 0.012)
    for z in (0.83, 0.99):
        kt.box(od, (0.26, 0.40, 0.15), (-0.40, 0.66, z), ch=0.012)
        kt.bar(gm, (-0.53, 0.60, z), (-0.53, 0.72, z), 0.008)
    # Sight on the right cheek and a warning lamp on the left.
    kt.box(od, (0.06, 0.06, 0.14), (0.42, 0.05, 1.49), ch=0.01)
    kt.box(od, (0.15, 0.26, 0.15), (0.42, 0.02, 1.63), ch=0.02)
    kt.lathe(od, [(0, 0.06), (0.05, 0.065), (0.05, 0.05), (0.0, 0.045)], (0.42, -0.11, 1.64), TO_FWD, n=14, closed=True)
    kt.lathe(M["glass"], [(-0.01, 0), (-0.01, 0.048), (0.01, 0)], (0.42, -0.11, 1.64), TO_FWD, n=14)
    kt.box(od, (0.04, 0.08, 0.10), (0.51, 0.05, 1.63), ch=0.008)
    kt.lathe(od, [(0, 0), (0, 0.05), (0.04, 0.05), (0.04, 0)], (-0.42, 0.42, 1.42), n=10)
    kt.lathe(M["beacon"], [(0.04, 0), (0.04, 0.045), (0.07, 0.04), (0.10, 0.02), (0.105, 0)], (-0.42, 0.42, 1.42), n=12)

    # Elevating mass: cradle, two receivers with ammunition clips, cooled barrels with flash hiders, ring sight.
    e = pivot("Elevation", (0, -0.10, 0.95), t)
    ke = Kit()
    ke.box(od, (0.50, 0.56, 0.13), (0, -0.12, 0.96), ch=0.02)
    ke.cyl(gm, 0.055, 0.60, (0, -0.10, 0.95), TO_X, n=12)
    for s in (-1, 1):
        gx = s * 0.13
        ke.box(od, (0.15, 0.62, 0.15), (gx, -0.05, 1.08), ch=0.015)
        ke.box(gm, (0.10, 0.34, 0.06), (gx, -0.05, 1.18), ch=0.008)
        for i in range(4):
            ke.cyl(M["brass"], 0.022, 0.26, (gx, -0.05, 1.235 + i * 0.045), TO_BACK, n=8)
        ke.box(gm, (0.11, 0.06, 0.20), (gx, -0.20, 1.31))
        ke.bar(gm, (gx + s * 0.08, 0.10, 1.10), (gx + s * 0.08, 0.18, 1.10), 0.01)
        ke.lathe(gm, [(0, 0), (0, 0.062), (0.08, 0.062), (0.08, 0.052), (0.18, 0.052), (0.18, 0.06), (0.22, 0.06), (0.22, 0.052),
                      (0.36, 0.052), (0.36, 0.06), (0.40, 0.06), (0.40, 0.052), (0.55, 0.052), (0.58, 0.036),
                      (1.55, 0.031), (1.55, 0)], (gx, -0.35, 1.07), TO_FWD, n=14)
        fh = Vector((gx, -1.90, 1.07))
        ke.lathe(gm, [(0, 0.036), (0.20, 0.036)], fh, TO_FWD, n=10)
        for d0, d1 in ((0.0, 0.05), (0.09, 0.12), (0.16, 0.20)):
            ke.lathe(gm, [(d0, 0.036), (d0, 0.052), (d1, 0.052), (d1, 0.036)], fh, TO_FWD, n=12, closed=True)
        ke.box(gm, (0.02, 0.20, 0.03), fh + Vector((s * 0.04, -0.10, 0)))
        ke.cyl(gm, 0.022, 0.42, (gx, -0.56, 1.005), TO_FWD, n=8)
    ke.box(od, (0.50, 0.12, 0.24), (0, -0.40, 1.08), ch=0.02)
    ring = Vector((0, -0.58, 1.33))
    ke.bar(gm, (0, -0.44, 1.18), ring + Vector((0, 0, -0.09)), 0.01)
    ke.lathe(gm, torus_profile(0.09, 0.006, 5), ring, TO_FWD, n=20, closed=True)
    ke.bar(gm, ring + Vector((-0.09, 0, 0)), ring + Vector((0.09, 0, 0)), 0.003, n=4)
    ke.bar(gm, ring + Vector((0, 0, -0.09)), ring + Vector((0, 0, 0.09)), 0.003, n=4)
    ke.bar(gm, (0, -0.10, 1.18), (0, -0.10, 1.30), 0.008)

    k.build("CarriageParts", r)
    kt.build("MountParts", t)
    ke.build("GunParts", e)
    e.rotation_euler = (math.radians(-35), 0, 0)  # barrels raised
    return r


def lattice_reflector(kit, centre, half_w, half_h, focal, tilt, mesh_mat, frame_mat):
    """A curved mesh antenna: a parabolic trough curved across X with a rounded outline, its hollow facing -Y,
    leaned back by tilt (radians). Returns a function mapping (x, v) on the dish to world space."""
    c = Vector(centre)
    R = Matrix.Rotation(-tilt, 3, "X")

    def surf(x, v):
        return c + R @ Vector((x, -x * x / (4 * focal), v))

    def v_max(x):
        return half_h * max(0.0, 1 - abs(x / half_w) ** 4) ** 0.25

    def x_max(v):
        return half_w * max(0.0, 1 - abs(v / half_h) ** 4) ** 0.25
    nx = 19
    for i in range(nx):
        x = -half_w + (i + 0.5) * 2 * half_w / nx
        vm = v_max(x) - 0.01
        kit.bar(mesh_mat, surf(x, -vm), surf(x, vm), 0.008, square=True)
    nv = 11
    for j in range(nv):
        v = -half_h + (j + 0.5) * 2 * half_h / nv
        xm = x_max(v) - 0.01
        segs = max(4, round(2 * xm / 0.16))
        kit.path(mesh_mat, [surf(-xm + 2 * xm * q / segs, v) for q in range(segs + 1)], 0.008, n=4)
    rim_pts = []
    for q in range(65):
        a = 2 * math.pi * q / 64
        ca, sa = math.cos(a), math.sin(a)
        x = half_w * math.copysign(abs(ca) ** 0.5, ca)
        v = half_h * math.copysign(abs(sa) ** 0.5, sa)
        rim_pts.append(surf(x, v))
    kit.path(frame_mat, rim_pts, 0.018, n=6)
    return surf


def build_radar():
    """Search radar on a single-axle trailer: levelling jacks, a louvred generator, an equipment cabinet and a
    turning lattice antenna on a mast."""
    r = root("Enemy_Radar")
    k = Kit()
    o, od, gm = M["olive"], M["olive_dark"], M["gunmetal"]

    for s in (-1, 1):
        k.box(od, (0.10, 2.40, 0.16), (s * 0.45, 0.05, 0.54), ch=0.01)
    box("Deck", (1.64, 2.36, 0.06), (0, 0.05, 0.65), od, r, bevel=0.01)
    k.cyl(od, 0.05, 1.80, (0, 0.30, 0.36), TO_X, n=12)
    for s in (-1, 1):
        wheel(k, (s * 0.95, 0.30, 0.36), side_rot(s), 0.36, 0.22)
        k.box(od, (0.28, 0.66, 0.022), (s * 0.95, 0.30, 0.80), ch=0.005)
        for e in (-1, 1):
            k.box(od, (0.28, 0.20, 0.022), (s * 0.95, 0.30 + e * 0.40, 0.745), (e * math.radians(-35), 0, 0), ch=0.005)
        k.bar(od, (s * 0.80, 0.30, 0.66), (s * 0.84, 0.30, 0.79), 0.015)
        k.box(od, (0.12, 0.30, 0.10), (s * 0.55, 0.30, 0.42), ch=0.01)
    # Four levelling jacks on outriggers; drawbar with towing eye and a parking stand.
    for sx in (-1, 1):
        for y in (-1.02, 1.15):
            k.box(od, (0.55, 0.09, 0.09), (sx * 0.72, y, 0.55), ch=0.01)
            jack(k, sx * 0.98, y, 0.78, pad_r=0.13)
    for s in (-1, 1):
        k.bar(od, (s * 0.40, -1.15, 0.54), (0, -1.86, 0.48), 0.035, square=True)
    shackle(k, (0, -1.93, 0.48), None, R=0.06, r=0.018)
    k.cyl(od, 0.04, 0.46, (0, -1.62, 0.27), n=10)
    k.lathe(M["rust"], [(0, 0), (0, 0.08), (0.03, 0.08), (0.05, 0.03), (0.05, 0)], (0, -1.62, 0), n=12)

    # Generator set at the front: louvred side panels, control panel, exhaust and filler cap.
    box("Generator", (1.44, 0.72, 0.68), (0, -0.70, 1.02), od, r, bevel=0.03)
    for s in (-1, 1):
        louvres(k, (s * 0.725, -0.70, 1.02), s, 0.54, 0.50, 11)
        for z in (0.78, 1.26):
            for y in (-1.02, -0.38):
                k.box(gm, (0.01, 0.025, 0.04), (s * 0.723, y, z))
    k.box(od, (0.50, 0.03, 0.30), (0.20, -1.07, 1.06), ch=0.008)
    for x in (0.08, 0.22):
        k.cyl(M["glass"], 0.035, 0.012, (x, -1.088, 1.12), TO_FWD, n=12)
    for x in (0.08, 0.16, 0.24, 0.32):
        k.box(gm, (0.02, 0.02, 0.03), (x, -1.09, 0.97))
    for x in (-0.45, -0.15):
        k.bar(gm, (x, -1.07, 1.20), (x, -1.10, 1.20), 0.01)
        k.bar(gm, (x - 0.08, -1.10, 1.20), (x + 0.08, -1.10, 1.20), 0.01)
    k.bar(M["rust"], (-0.45, -0.85, 1.36), (-0.45, -0.85, 1.52), 0.035, n=10)
    k.cyl(M["rust"], 0.07, 0.36, (-0.32, -0.85, 1.56), TO_X, n=12)
    k.bar(M["rust"], (-0.16, -0.85, 1.56), (-0.12, -0.85, 1.70), 0.03, n=10)
    k.cyl(gm, 0.045, 0.05, (0.42, -0.48, 1.385), n=10)
    for x in (-0.66, 0.66):
        for y in (-1.0, -0.40):
            shackle(k, (x, y, 1.38), TO_BACK, R=0.03, r=0.009, mat=od)

    # Equipment cabinet: plain front half of each flank for the game's emblem, a door behind it, vents,
    # an orange band low down, and a ladder at the back.
    box("Cabinet", (1.60, 1.48, 0.90), (0, 0.45, 1.13), o, r, bevel=0.03)
    for s in (-1, 1):
        x = s * 0.80
        k.box(o, (0.012, 0.40, 0.78), (x, 0.95, 1.13), ch=0.004)
        k.bar(gm, (x + s * 0.02, 0.80, 1.05), (x + s * 0.02, 0.80, 1.20), 0.012)
        for z in (0.82, 1.44):
            k.cyl(gm, 0.016, 0.08, (x + s * 0.01, 1.15, z), n=8)
        louvres(k, (x + s * 0.004, -0.07, 1.38), s, 0.28, 0.22, 6)
        k.box(M["orange"], (0.006, 1.40, 0.05), (x + s * 0.008, 0.45, 0.785))
        k.box(gm, (0.03, 0.10, 0.10), (x + s * 0.015, -0.12, 0.85), ch=0.008)
    k.box(o, (0.80, 0.012, 0.78), (0.30, 1.19, 1.13), ch=0.004)
    k.bar(gm, (0.62, 1.21, 1.05), (0.62, 1.21, 1.20), 0.012)
    for x in (-0.68, -0.40):
        k.bar(gm, (x, 1.24, 0.66), (x, 1.24, 1.60), 0.015)
    for z in (0.80, 1.00, 1.20, 1.40):
        k.bar(gm, (-0.68, 1.24, z), (-0.40, 1.24, z), 0.012)
    k.bar(M["rubber"], (0.50, -0.44, 1.36), (0.50, -0.34, 1.46), 0.025, n=8)
    k.bar(M["rubber"], (0.50, -0.34, 1.46), (0.50, -0.29, 1.46), 0.025, n=8)

    # Mast: pedestal, tapered tube, braces and a cable; bearing at the top.
    k.box(od, (0.56, 0.56, 0.10), (0, 0, 1.63), ch=0.02)
    k.lathe(gm, [(1.68, 0), (1.68, 0.13), (1.78, 0.11), (2.50, 0.085), (2.55, 0.12), (2.68, 0.12), (2.68, 0)], (0, 0, 0), n=16)
    for a in (45, 135, 225, 315):
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        k.bar(od, (ca * 0.33, sa * 0.33, 1.68), (ca * 0.09, sa * 0.09, 2.20), 0.02)
    k.bar(M["rubber"], (0.14, 0.0, 1.68), (0.105, 0.0, 2.50), 0.016, n=6)

    # ---- the turning head ----
    t = pivot("Turret", (0, 0, 2.7), r)
    kt = Kit()
    kt.lathe(od, [(2.68, 0), (2.68, 0.17), (2.86, 0.17), (2.90, 0.14), (2.90, 0)], (0, 0, 0), n=20)
    bolts(kt, [Vector((0.15 * math.cos(a), 0.15 * math.sin(a), 2.905)) for a in (2 * math.pi * i / 10 for i in range(10))], None, r=0.012)
    kt.box(od, (0.16, 0.20, 0.16), (0.24, 0.05, 2.78), ch=0.015)
    kt.cyl(gm, 0.06, 0.16, (0.24, -0.11, 2.78), TO_FWD, n=12)
    surf = lattice_reflector(kt, (0, 0.30, 3.55), 1.40, 0.60, 1.2, math.radians(15), od, o)
    hub = Vector((0, 0.62, 3.32))
    kt.bar(o, (0, 0.05, 2.90), hub, 0.05, n=10)
    kt.lathe(od, [(-0.08, 0), (-0.08, 0.08), (0.08, 0.08), (0.08, 0)], hub, TO_BACK, n=12)
    for x, v in ((-1.2, 0.0), (1.2, 0.0), (-0.6, 0.42), (0.6, 0.42), (-0.6, -0.42), (0.6, -0.42), (0, 0.5), (0, -0.45)):
        kt.bar(o, hub, surf(x, v), 0.018, n=6)
    kt.bar(o, surf(-1.0, 0.0), surf(1.0, 0.0), 0.022, n=6)
    # Feed horn at the focus on a boom and two side struts, looking back into the dish.
    focus = surf(0, 0) + Matrix.Rotation(math.radians(-15), 3, "X") @ Vector((0, -1.2, 0))
    kt.bar(o, (0, -0.10, 2.90), focus + Vector((0, 0.08, -0.06)), 0.03, n=8)
    for x in (-0.7, 0.7):
        kt.bar(o, surf(x, -0.45), focus + Vector((0, 0.06, 0)), 0.012)
    horn = Matrix.Rotation(math.radians(-15), 3, "X") @ TO_BACK
    kt.lathe(od, [(-0.12, 0), (-0.12, 0.05), (0.0, 0.05), (0.06, 0.10), (0.06, 0)], focus, horn, n=4)
    kt.lathe(od, [(0, 0), (0, 0.02), (0.08, 0.02), (0.08, 0)], surf(0, 0.6) + Vector((0, 0.02, 0.0)), None, n=8)
    kt.lathe(M["beacon"], [(0.08, 0), (0.08, 0.05), (0.12, 0.045), (0.15, 0.02), (0.155, 0)], surf(0, 0.6) + Vector((0, 0.02, 0.0)), None, n=12)

    k.build("TrailerParts", r)
    kt.build("AntennaParts", t)
    return r


def build_artillery():
    """Towed howitzer dug in under a camouflage net: split trails with spades, two wheels, a gun shield and a
    long barrel. The tube (empty "Barrel", origin at the breech) recoils through its cradle (empty "Elevation")."""
    r = root("Enemy_Artillery")
    k = Kit()
    o, od, gm = M["olive"], M["olive_dark"], M["gunmetal"]

    for s in (-1, 1):
        wheel(k, (s * 1.05, 0.0, 0.5), side_rot(s), 0.5, 0.28)
        k.lathe(od, [(0, 0), (0, 0.10), (0.04, 0.10), (0.07, 0.06), (0.10, 0.05), (0.12, 0)], (s * 1.19, 0.0, 0.5), side_rot(s), n=12)
        k.box(od, (0.30, 0.16, 0.12), (s * 0.78, 0.10, 0.56), ch=0.015)
    k.cyl(od, 0.07, 1.90, (0, 0.0, 0.5), TO_X, n=14)
    box("Saddle", (0.80, 1.0, 0.32), (0, 0.12, 0.66), o, r, bevel=0.03)
    for s in (-1, 1):
        box(f"Cheek_{s}", (0.07, 0.75, 0.55), (s * 0.26, 0.08, 1.06), o, r, bevel=0.012)
        kt_c = Vector((s * 0.30, 0.10, 1.20))
        k.lathe(od, [(0, 0), (0, 0.11), (0.03, 0.10), (0.05, 0.06), (0.06, 0)], kt_c, side_rot(s), n=16)
        bolts(k, [kt_c + Vector((s * 0.03, 0.08 * math.cos(a), 0.08 * math.sin(a))) for a in (2 * math.pi * i / 6 for i in range(6))], side_rot(s), r=0.01)
        k.lathe(od, [(0, 0), (0, 0.07), (0.06, 0.07), (0.06, 0.055), (0.62, 0.055), (0.62, 0.065), (0.66, 0.065), (0.66, 0)],
                (s * 0.38, -0.28, 0.80), aim((0, 0.10, 1)), n=12)
        k.bar(gm, (s * 0.38, -0.21, 1.46), (s * 0.38, -0.18, 1.62), 0.025, n=8)
    handwheel(k, (-0.37, 0.45, 1.02), TO_NX, R=0.11)
    handwheel(k, (0.37, 0.45, 0.94), TO_X, R=0.11)
    for s in (-1, 1):
        k.box(od, (0.06, 0.10, 0.10), (s * 0.32, 0.45, 0.98 if s < 0 else 0.90), ch=0.01)

    # Gun shield: two wings either side of the barrel slot, folded back at the outer edge, leaning back 10 deg.
    # The game paints the emblem on the left wing's front face (Blender x -0.6, z 1.12).
    sc = Vector((0, -0.62, 1.15))
    sR = rotm((math.radians(-10), 0, 0))
    su, sv, sn = Vector((1, 0, 0)), sR @ Vector((0, 0, 1)), sR @ Vector((0, -1, 0))
    for s in (-1, 1):
        wing = [sc + su * s * a + sv * b for a, b in ((0.22, -0.52), (0.86, -0.52), (0.86, 0.40), (0.66, 0.52), (0.22, 0.52))]
        solid(f"Shield_{s}", plate_loops(wing, sn, 0.05), o, r, bevel=0.01)
        fold_back = Vector((s * 0.17, 0.22, 0))
        e0, e1 = sc + su * s * 0.86 + sv * -0.52, sc + su * s * 0.86 + sv * 0.40
        fold = [e0, e1, e1 + fold_back, e0 + fold_back]
        solid(f"ShieldFold_{s}", plate_loops(fold, quad_normal(fold, (s, -0.5, 0)), 0.05), o, r, bevel=0.01)
        front = sc + sn * 0.026
        k.box(od, (0.66, 0.035, 0.04), sc + su * s * 0.54 + sv * 0.52 + sn * 0.0, sR, ch=0.008)
        k.plate(M["orange"], [front + su * s * a + sv * b for a, b in ((0.24, -0.39), (0.84, -0.39), (0.84, -0.33), (0.24, -0.33))], sn, 0.006)
        bolts(k, [front + su * s * a + sv * -0.47 for a in (0.30, 0.48, 0.66, 0.82)] + [front + su * s * 0.82 + sv * b for b in (-0.2, 0.1, 0.35)],
              aim(sn), r=0.012)
        k.bar(od, sc - sn * 0.03 + su * s * 0.5 + sv * -0.3, (s * 0.28, 0.0, 0.92), 0.022)
    lower = [sc + su * a + sv * b for a, b in ((-0.22, -0.52), (0.22, -0.52), (0.22, -0.20), (-0.22, -0.20))]
    solid("ShieldLow", plate_loops(lower, sn, 0.05), o, r, bevel=0.01)

    # Split trails with handles, foot pads and spades dug into the ground.
    for s in (-1, 1):
        a, b = Vector((s * 0.22, 0.45, 0.66)), Vector((s * 1.30, 3.62, 0.20))
        solid(f"Trail_{s}", beam_loops(a, b, 0.20, 0.26, 0.13, 0.16), o, r, bevel=0.015)
        d = (b - a).normalized()
        side = d.cross(Vector((0, 0, 1))).normalized()
        k.box(od, (0.12, (b - a).length * 0.8, 0.02), a.lerp(b, 0.45) + Vector((0, 0, 0.115)), basis(side, d, side.cross(d)), ch=0.005)
        for f in (0.62, 0.82):
            p = a.lerp(b, f)
            h0, h1 = p + side * 0.09 * s, p + side * 0.09 * s + d * 0.18
            k.path(gm, [h0, h0 + side * 0.08 * s + Vector((0, 0, 0.05)), h1 + side * 0.08 * s + Vector((0, 0, 0.05)), h1], 0.012)
        k.lathe(M["rust"], [(0, 0), (0, 0.17), (0.03, 0.17), (0.06, 0.10), (0.06, 0)], (b.x, b.y, 0.0), n=16)
        k.bar(od, (b.x, b.y, 0.05), b, 0.05, n=10)
        dh = Vector((d.x, d.y, 0)).normalized()
        flat = basis(side, dh, (0, 0, 1))
        spade_c = b + dh * 0.12 + Vector((0, 0, -0.06))
        k.box(M["rust"], (0.62, 0.05, 0.50), spade_c, flat @ rotm((math.radians(20), 0, 0)), ch=0.01)
        for e in (-1, 1):
            k.box(M["rust"], (0.03, 0.22, 0.30), spade_c + side * e * 0.18 - dh * 0.10 + Vector((0, 0, 0.08)), flat, ch=0.006)

    # Cradle with recoil cylinders, elevating arc and sight; the tube and breech hang on Barrel inside it.
    e = pivot("Elevation", (0, 0.10, 1.20), r)
    ke = Kit()
    ke.lathe(od, [(0, 0), (0, 0.175), (0.06, 0.175), (0.08, 0.158), (1.40, 0.158), (1.45, 0.175), (1.48, 0.175), (1.48, 0)], (0, 0.24, 1.20), TO_FWD, n=20)
    for x in (-0.09, 0.09):
        ke.lathe(od, [(0, 0), (0, 0.065), (0.04, 0.08), (1.52, 0.08), (1.56, 0.065), (1.56, 0)], (x, 0.36, 1.44), TO_FWD, n=14)
    for y in (0.15, -0.45, -1.05):
        ke.box(od, (0.38, 0.06, 0.14), (0, y, 1.38), ch=0.01)
    arc_c = Vector((0, 0.10, 1.20))
    for i in range(12):
        a = math.radians(-120 + i * 6)
        p = arc_c + Vector((0, 0.36 * math.cos(a), 0.36 * math.sin(a)))
        ke.box(gm, (0.05, 0.035, 0.06), p, rotm((a + R90, 0, 0)))
    ke.loft(od, plate_loops([arc_c + Vector((0, 0.34 * math.cos(math.radians(a)), 0.34 * math.sin(math.radians(a)))) for a in range(-122, -50, 12)]
                            + [arc_c + Vector((0, -0.05, -0.05))], (1, 0, 0), 0.04))
    sight = Vector((-0.33, 0.02, 1.40))
    ke.box(od, (0.16, 0.08, 0.06), (-0.22, 0.02, 1.30), ch=0.01)
    ke.box(gm, (0.08, 0.16, 0.20), sight, ch=0.015)
    ke.cyl(gm, 0.035, 0.12, sight + Vector((0, -0.02, 0.15)), n=10)
    ke.box(gm, (0.06, 0.08, 0.05), sight + Vector((0, -0.04, 0.23)), ch=0.008)
    ke.box(M["glass"], (0.045, 0.006, 0.03), sight + Vector((0, -0.083, 0.23)))
    ke.cyl(M["rubber"], 0.022, 0.06, sight + Vector((-0.02, 0.10, 0.04)), TO_BACK, n=8)

    b = pivot("Barrel", (0, 0.25, 1.20), e)
    kb = Kit()
    box("Breech", (0.34, 0.34, 0.34), (0, 0.25, 1.20), od, b, bevel=0.03)
    kb.lathe(od, [(0, 0), (0, 0.18), (0.10, 0.18), (0.10, 0)], (0, 0.09, 1.20), TO_FWD, n=20)
    kb.bar(gm, (0.17, 0.30, 1.26), (0.32, 0.40, 1.36), 0.016, n=8)
    kb.cyl(M["rubber"], 0.02, 0.07, (0.32, 0.40, 1.40), n=8)
    kb.box(gm, (0.04, 0.10, 0.10), (-0.19, 0.25, 1.20), ch=0.01)
    kb.lathe(od, [(0.0, 0), (0.0, 0.15), (0.20, 0.14), (1.50, 0.122), (1.50, 0.13), (1.58, 0.13), (1.58, 0.12),
                  (3.0, 0.105), (3.70, 0.096), (3.80, 0.096), (3.80, 0)], (0, 0.08, 1.20), TO_FWD, n=20)
    mb = Vector((0, -3.70, 1.20))  # muzzle brake: its centre is 4.18 m ahead of the breech, where the game puts the flash
    kb.lathe(gm, [(0, 0.098), (0.45, 0.098)], mb, TO_FWD, n=18)
    for d0, d1 in ((0.0, 0.11), (0.21, 0.27), (0.38, 0.45)):
        kb.lathe(od, [(d0, 0.10), (d0, 0.17), (d1, 0.17), (d1, 0.10)], mb, TO_FWD, n=20, closed=True)
    for zz in (-1, 1):
        kb.box(od, (0.14, 0.45, 0.05), mb + Vector((0, -0.225, zz * 0.138)), ch=0.01)
    kb.cyl(M["rubber"], 0.07, 0.004, mb + Vector((0, -0.452, 0)), TO_FWD, n=14)

    # Camouflage net on four poles over the whole gun.
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"NetPole_{sx}_{sy}", (sx * 2.4, sy * 2.6 - 0.2, 0), (sx * 2.4, sy * 2.6 - 0.2, 2.3), 0.04, M["olive_dark"], r, verts=6)
    net("Net", (6.2, 6.8), 2.4, [M["camo_a"], M["camo_b"]], r, sag=0.9, seed=29)
    for obj in [o_ for o_ in r.children if o_.name.startswith("Net_")]:
        obj.location = (0, -0.2, 0)
    # Ammunition boxes and a couple of rounds by the right wheel.
    for y in (1.4, 2.0):
        k.box(od, (0.80, 0.50, 0.36), (1.6, y, 0.18), ch=0.02)
        k.box(od, (0.82, 0.52, 0.05), (1.6, y, 0.33), ch=0.01)
        for x in (1.25, 1.95):
            k.bar(gm, (x, y - 0.08, 0.26), (x, y + 0.08, 0.26), 0.012)
        for x in (1.42, 1.78):
            k.box(gm, (0.04, 0.012, 0.06), (x, y - 0.256, 0.28))
    for x, y in ((1.2, 2.45), (1.33, 2.5)):
        k.lathe(M["brass"], [(0, 0), (0, 0.06), (0.42, 0.06), (0.42, 0)], (x, y, 0), n=12)
        k.lathe(od, [(0.42, 0.06), (0.52, 0.058), (0.62, 0.035), (0.68, 0)], (x, y, 0), n=12)

    k.build("GunCarriageParts", r)
    ke.build("CradleParts", e)
    kb.build("TubeParts", b)
    e.rotation_euler = (math.radians(-14), 0, 0)  # elevated, muzzle up
    return r


# ---------- export ----------

MOVING_PARTS = ("Turret", "Barrel")


def join_objects(meshes, active):
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def flatten(root_obj):
    """Turns the model into the root empty plus a few meshes hanging straight off it: each moving part (an
    empty named Turret or Barrel, with everything built under it, nested pivots included) becomes one mesh of
    the same name with the empty's origin and rotation, and all the rest becomes <root>_Mesh.

    Moving parts must be single meshes directly under the root: with bake_space_transform, Blender's FBX
    exporter turns any object two levels below a baked parent by a stray 90 degrees about X."""
    bpy.context.view_layer.update()
    for empty in [o for o in root_obj.children_recursive if o.type == "EMPTY" and o.name in MOVING_PARTS]:
        name, target = empty.name, empty.matrix_world.copy()
        meshes = [o for o in empty.children_recursive if o.type == "MESH"]
        part = join_objects(meshes, meshes[0])
        bpy.context.view_layer.update()
        part.data.transform(target.inverted() @ part.matrix_world)
        part.parent = root_obj
        part.matrix_parent_inverse = root_obj.matrix_world.inverted()
        part.matrix_world = target
        for o in [empty] + [c for c in empty.children_recursive if c.type == "EMPTY"]:
            bpy.data.objects.remove(o, do_unlink=True)
        part.name = part.data.name = name
    bpy.context.view_layer.update()
    rest = [o for o in root_obj.children_recursive if o.type == "MESH" and o.name not in MOVING_PARTS]
    body = join_objects(rest, next(o for o in rest if o.parent == root_obj))
    bpy.context.view_layer.update()
    body.data.transform(root_obj.matrix_world.inverted() @ body.matrix_world)  # origin at the model's origin
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    body.name = body.data.name = root_obj.name + "_Mesh"
    for o in [c for c in root_obj.children_recursive if c.type == "EMPTY"]:
        bpy.data.objects.remove(o, do_unlink=True)


def hide_faces(root_obj, prefix):
    """Deletes the faces whose material name starts with prefix (preview only: the camo net hides the gun)."""
    for o in root_obj.children_recursive:
        if o.type != "MESH":
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        doomed = [f for f in bm.faces if o.data.materials[f.material_index].name.startswith(prefix)]
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        bm.to_mesh(o.data)
        bm.free()


def triangle_count(root_obj):
    total = 0
    for o in root_obj.children_recursive:
        if o.type == "MESH":
            o.data.calc_loop_triangles()
            total += len(o.data.loop_triangles)
    return total


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builds = [
        ("Enemy_Tank", build_tank, 7.0, 0.8),
        ("Enemy_Truck", build_truck, 6.5, 0.8),
        ("Enemy_AAGun", build_aa_gun, 6.0, 0.6),
        ("Enemy_Radar", build_radar, 8.5, 2.0),
        ("Enemy_Artillery", build_artillery, 10.0, 1.0),
    ]
    only = set(args[2].split(",")) if len(args) > 2 else None
    for name, build, cam_distance, target_z in builds:
        if only and name not in only:
            continue
        reset_scene()
        M.clear()
        make_materials()
        r = build()
        bpy.context.view_layer.update()
        box_uv_all(r)  # real-scale UVs for Unity's photo textures
        flatten(r)
        tris = triangle_count(r)
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name} ({tris} triangles)")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), cam_distance, target_z)
            if name == "Enemy_Artillery":  # a second look at the gun itself, under the net
                hide_faces(r, "CamoNet")
                bpy.context.scene.camera.location *= 0.8
                bpy.context.scene.render.filepath = os.path.join(PREVIEW_DIR, name + "_NoNet.png")
                bpy.ops.render.render(write_still=True)
            print(f"PREVIEW {name}")


main()
