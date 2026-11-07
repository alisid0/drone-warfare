"""Geometry kit shared by the Blender model scripts: parts written as raw vertices and faces, collected one mesh
per material by a Kit and built in one go (hundreds of small parts become a few objects).
"""
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

from common import finish

R90 = math.radians(90)


# Rotations are Euler tuples or 3x3 matrices. Cylinders and lathes run along local +Z; a lathe profile is a list
# of (z, radius) points, and a radius of 0 closes that end on the axis. Lathe faces look out to the left of the
# profile's direction: (dz, dr) -> (-dr, dz), so walk the profile with the solid on your right.

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
    """Local +Z pointing out of the flank on side s (+1 / -1 along X)."""
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


def lathe_geo(profile, n, closed=False, caps=True, phase=0.0):
    """A surface of revolution about local Z. Open profiles are capped at both ends unless caps is False."""
    verts, faces, rings = [], [], []
    for z, r in profile:
        if r <= 1e-6:
            rings.append([len(verts)])
            verts.append((0.0, 0.0, z))
        else:
            rings.append(list(range(len(verts), len(verts) + n)))
            verts += [(r * math.cos(2 * math.pi * (k + phase) / n), r * math.sin(2 * math.pi * (k + phase) / n), z)
                      for k in range(n)]
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
    if not closed and caps:
        if len(rings[0]) > 1:
            faces.append(tuple(reversed(rings[0])))
        if len(rings[-1]) > 1:
            faces.append(tuple(rings[-1]))
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


def yprism(profile, y0, y1):
    """Loops for a solid whose (x, z) end profile is pushed along Y from y0 to y1."""
    return [[Vector((x, y0, z)) for x, z in profile], [Vector((x, y1, z)) for x, z in profile]]


def plate_loops(pts, n, thick):
    n = Vector(n).normalized() * thick / 2
    return [[Vector(p) - n for p in pts], [Vector(p) + n for p in pts]]


def make_mesh(name, verts, faces, mat, parent, recalc=True, smooth=True):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.validate()
    mesh.update()
    if recalc:
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, mat, parent, 0.0, smooth)


class Kit:
    """Collects parts as raw geometry, one mesh per material, built in one go. Closed solids get their normals
    recalculated; open surfaces (recalc=False) keep the winding they were made with. mirror_x draws the whole kit
    mirrored across X (for the station, drawn with its platform at +X and delivered with it at Blender -X)."""

    def __init__(self, mirror_x=False):
        self.parts = {}
        self.mirror_x = mirror_x

    def add(self, mat, verts, faces, loc=(0, 0, 0), rot=None, recalc=True):
        m, o = rotm(rot), Vector(loc)
        entry = self.parts.setdefault((mat.name, recalc), [mat, [], []])
        base = len(entry[1])
        pts = [o + m @ Vector(p) for p in verts]
        if self.mirror_x:
            pts = [Vector((-p.x, p.y, p.z)) for p in pts]
            faces = [tuple(reversed(f)) for f in faces]
        entry[1].extend(tuple(p) for p in pts)
        entry[2].extend(tuple(base + i for i in f) for f in faces)

    def box(self, mat, size, loc, rot=None, ch=0.0):
        if min(size) < 0.1 or max(size) < 0.35:
            ch = 0.0  # a chamfer on something this small costs 32 triangles and can't be seen
        v, f = box_geo(size, ch)
        self.add(mat, v, f, loc, rot)

    def cyl(self, mat, r, depth, loc, rot=None, n=12, r2=None, caps=True):
        v, f = lathe_geo([(-depth / 2, r), (depth / 2, r if r2 is None else r2)], n, caps=caps)
        self.add(mat, v, f, loc, rot, recalc=caps)

    def lathe(self, mat, profile, loc, rot=None, n=16, closed=False, caps=True, recalc=True, phase=0.0):
        v, f = lathe_geo(profile, n, closed, caps, phase)
        self.add(mat, v, f, loc, rot, recalc=recalc)

    def bar(self, mat, p1, p2, r, n=6, caps=True, square=False):
        a, b = Vector(p1), Vector(p2)
        d = b - a
        if d.length < 1e-5:
            return
        if square:
            self.box(mat, (2 * r, 2 * r, d.length), (a + b) / 2, aim(d))
        else:
            self.cyl(mat, r, d.length, (a + b) / 2, aim(d), n, caps=caps)

    def path(self, mat, pts, r, n=6, caps=True):
        for a, b in zip(pts, pts[1:]):
            self.bar(mat, a, b, r, n, caps)

    def loft(self, mat, loops, caps=True):
        v, f = loft_geo(loops, caps)
        self.add(mat, v, f, recalc=caps)

    def plate(self, mat, pts, n, thick):
        self.loft(mat, plate_loops(pts, n, thick))

    def hexa(self, mat, corners):
        self.add(mat, [Vector(c) for c in corners], HEXA_FACES)

    def build(self, name, parent):
        if os.environ.get("SECTORS_STATS"):
            for (mname, recalc), (mat, verts, faces) in self.parts.items():
                print(f"  STATS {name} {mname}{'' if recalc else ' (open)'}: {sum(len(f) - 2 for f in faces)} triangles")
        for (mname, recalc), (mat, verts, faces) in self.parts.items():
            make_mesh(f"{name}_{mname}" + ("" if recalc else "_open"), verts, faces, mat, parent, recalc=recalc)
        self.parts = {}
