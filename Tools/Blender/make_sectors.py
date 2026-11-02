"""Builds the models for the seeded sector generator and exports each as an FBX for Unity.

Dravin Coalition vehicles and targets (dark olive and grey, no markings: the game paints its emblems at runtime):
    Enemy_APC      8x8 armoured personnel carrier. "Turret" (one-man turret) carries "Barrel" (30 mm cannon).
    Enemy_Rocket   multiple rocket launcher truck. "Turret" is the 40-tube pack, its origin on the mounting trunnion.
    Enemy_Tanker   fuel tanker truck (no moving parts).
    Enemy_Jammer   electronic-warfare truck. "Turret" is the raised telescopic mast with its antennas and dish.
    Enemy_Depot    field ammunition depot under a camouflage net (static, one mesh).
Sector scenery (static, one mesh each, with real-metre UVs):
    Scenery_RailTrack, _RailWagon, _RailTankWagon, _Station, _Warehouse, _Factory, _Chimney, _Silo, _WaterTower,
    _Pylon, _DragonTeeth, _LogPile, _Hut

Run from the project root (Blender 4.x or 5.x):
    blender --background --factory-startup --python Tools/Blender/make_sectors.py -- Assets/DroneSandbox/Resources/Models [preview_dir] [names]

preview_dir is optional ("" skips the previews); names is an optional comma-separated filter such as
Enemy_APC,Scenery_Pylon. Keep preview paths short: Blender fails on very long ones.

Real metres, Z up, the ground at z = 0, each model centred on its footprint and facing Blender's -Y (Unity's +Z).
Unity mirrors Blender's X on import, so Blender (x, y, z) is Unity (-x, z, -y): "Unity +X" is Blender -X.

Railway: Scenery_RailTrack is a 6 m straight along Y (y -3..3) whose ends butt cleanly; rails are 1.435 m gauge
with their tops at RAIL_TOP (0.592 m). The wagons stand with their wheels on those rails, so put a wagon at the
same ground point as the track under it (on bare ground it would float 0.59 m up). The station's platform is on
its Unity +X side; lay its track along the model's Z with the centre line STATION_TRACK_X (7.6 m) out that way.

Moving parts work like make_world.py's: each is one mesh named "Turret" or "Barrel" whose origin is the pivot.
The APC's Barrel hangs under its Turret, so it follows the turret round; Blender's FBX exporter puts any object two
levels below a baked root 90 degrees out, so export_model() counter-rotates such objects for the export only.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from bpy_extras.io_utils import axis_conversion
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (box_uv_all, export, finish, join_meshes, material, pivot, render_preview,  # noqa: E402
                    reset_scene, root, set_parent, single_uv_layer)

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 and args[1] else None

R90 = math.radians(90)
M = {}
MARKS = []  # (label, Blender point, Blender normal or None): printed in Unity model space with each export


def mark(label, p, n=None):
    MARKS.append((label, Vector(p), Vector(n) if n is not None else None))

RAIL_TOP = 0.592          # top of the rail heads above the ground
RAIL_X = 0.7535           # rail centre lines: 1.435 m between the inner faces of 72 mm heads
WHEEL_R = 0.46            # wagon wheel tread radius
AXLE_Z = RAIL_TOP + WHEEL_R
BUFFER_Z = RAIL_TOP + 1.06
STATION_TRACK_X = 7.6     # track centre line from the station's centre, on its platform side (Unity +X)


def make_materials():
    # Photo-textured in Unity: ProjectSetup.PbrMaterials swaps in the material of the same name.
    M["olive"] = material("Olive", (0.16, 0.18, 0.10), roughness=0.8)
    M["olive_dark"] = material("OliveDark", (0.10, 0.11, 0.06), roughness=0.8)
    M["steel"] = material("SteelDark", (0.10, 0.095, 0.09), metallic=0.6, roughness=0.55)
    M["gunmetal"] = material("Gunmetal", (0.09, 0.09, 0.09), metallic=0.7, roughness=0.45)
    M["rust"] = material("RustySteel", (0.20, 0.11, 0.06), metallic=0.3, roughness=0.8)
    M["galv"] = material("Galvanized", (0.40, 0.42, 0.43), metallic=0.75, roughness=0.42)
    M["canvas"] = material("Canvas", (0.30, 0.29, 0.18), roughness=0.95)
    M["sand"] = material("Sandbag", (0.36, 0.30, 0.19), roughness=1.0)
    M["concrete"] = material("Concrete", (0.30, 0.29, 0.27), roughness=0.95)
    M["concrete_stained"] = material("ConcreteStained", (0.24, 0.23, 0.21), roughness=0.95)
    M["gravel"] = material("Gravel", (0.20, 0.19, 0.18), roughness=1.0)
    M["brick"] = material("Brick", (0.24, 0.07, 0.035), roughness=0.9)
    M["stone"] = material("Stone", (0.20, 0.19, 0.17), roughness=1.0)
    M["roof_tile"] = material("RoofTile", (0.32, 0.075, 0.025), roughness=0.85)
    M["roof_metal"] = material("RoofMetal", (0.22, 0.24, 0.25), metallic=0.6, roughness=0.45)
    M["roof_barn"] = material("RoofBarn", (0.12, 0.11, 0.10), metallic=0.3, roughness=0.8)
    M["container_rust"] = material("ContainerRust", (0.20, 0.045, 0.02), metallic=0.3, roughness=0.65)
    M["wood"] = material("Wood", (0.16, 0.10, 0.05), roughness=0.9)
    M["wood_light"] = material("WoodLight", (0.30, 0.21, 0.11), roughness=0.9)
    M["planks"] = material("WeatheredPlanks", (0.13, 0.10, 0.075), roughness=1.0)
    M["planks_dark"] = material("WeatheredPlanksDark", (0.07, 0.055, 0.04), roughness=1.0)
    M["door"] = material("DoorWood", (0.10, 0.05, 0.025), roughness=0.8)
    M["fence"] = material("FenceWood", (0.15, 0.11, 0.075), roughness=1.0)
    M["bark"] = material("Bark", (0.06, 0.045, 0.03), roughness=1.0)
    M["bark_pine"] = material("BarkPine", (0.16, 0.065, 0.025), roughness=1.0)
    # Flat colours, not in the table. WindowGlass becomes the window material that lights up after dark (Art.cs).
    M["rubber"] = material("Rubber", (0.025, 0.025, 0.025), roughness=0.9)
    M["glass"] = material("Glass", (0.02, 0.03, 0.04), metallic=0.3, roughness=0.08)
    M["win_glass"] = material("WindowGlass", (0.02, 0.025, 0.03), metallic=0.3, roughness=0.1)
    M["frame"] = material("WindowFrame", (0.65, 0.64, 0.60), roughness=0.6)
    M["interior"] = material("Interior", (0.01, 0.01, 0.01), roughness=1.0)
    M["lamp"] = material("Lamp", (1.0, 0.9, 0.6), emission=(1.0, 0.9, 0.6))
    M["red_lens"] = material("RedLens", (0.45, 0.02, 0.02), roughness=0.2)
    M["amber"] = material("AmberLens", (0.85, 0.42, 0.04), roughness=0.2)
    M["brass"] = material("Brass", (0.55, 0.40, 0.15), metallic=0.9, roughness=0.35)
    M["camo"] = material("CamoNet", (0.06, 0.075, 0.03), roughness=1.0)
    M["dirt"] = material("Dirt", (0.12, 0.09, 0.06), roughness=1.0)


# ---------- geometry kit ----------
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


def slab(k, mat, a, b, y0, y1, thick, up=1.0):
    """A sloped slab (roofs, canopies) whose underside runs from a to b, (x, z) points, along Y from y0 to y1."""
    a, b = Vector((a[0], 0, a[1])), Vector((b[0], 0, b[1]))
    d = b - a
    n = Vector((-d.z, 0, d.x)).normalized()
    if n.z * up < 0:
        n = -n
    pts = [Vector((p.x, y, p.z)) + n * thick / 2 for p, y in ((a, y0), (b, y0), (b, y1), (a, y1))]
    k.plate(mat, pts, n, thick)


# ---------- meshes with their own UVs ----------

def newell(pts):
    n = Vector((0, 0, 0))
    for i in range(len(pts)):
        n += pts[i].cross(pts[(i + 1) % len(pts)])
    return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))


def wrap_uvs(verts, faces, axis):
    """Per-face UVs in metres wrapped round a local axis (0 = X, 2 = Z) through the origin: u runs round (arc
    length), v along the axis. Faces looking along the axis get a flat projection. Box projection would put
    seams at 45 degrees round brick chimneys, silo walls and logs."""
    a = axis
    b, c = [i for i in range(3) if i != a]
    out = []
    for f in faces:
        pts = [Vector(verts[i]) for i in f]
        n = newell(pts)
        if abs(n[a]) > 0.7:
            out.append([(p[b], p[c]) for p in pts])
            continue
        angs = [math.atan2(p[c], p[b]) if math.hypot(p[b], p[c]) > 1e-6 else None for p in pts]
        known = [x for x in angs if x is not None]
        if max(known) - min(known) > math.pi:
            angs = [x + 2 * math.pi if (x is not None and x < 0) else x for x in angs]
            known = [x for x in angs if x is not None]
        mean = sum(known) / len(known)
        out.append([((ang if ang is not None else mean) * math.hypot(p[b], p[c]), p[a]) for p, ang in zip(pts, angs)])
    return out


def uv_mesh(name, verts, faces, uvs, mats, face_mats, parent, loc=(0, 0, 0), smooth=True):
    """A mesh with its UVs given per face corner (kept as they are when the model is joined)."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    for m in mats:
        mesh.materials.append(m)
    layer = mesh.uv_layers.new(name="UVMap")
    for poly, mi, fuv in zip(mesh.polygons, face_mats, uvs):
        poly.material_index = mi
        for li, uv in zip(poly.loop_indices, fuv):
            layer.data[li].uv = uv
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj["keep_uv"] = True
    single_uv_layer(obj)
    if smooth:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
        except (AttributeError, RuntimeError):
            bpy.ops.object.shade_smooth()
    set_parent(obj, parent)
    return obj


def lathe_uv_mesh(name, profile, n, mats, mat_of_face, parent, loc=(0, 0, 0), rot=None, axis=2, phase=0.0):
    """A lathe (about local Z, turned by rot) with UVs wrapped round it; mat_of_face(normal) picks the material."""
    v, f = lathe_geo(profile, n, caps=False, phase=phase)
    m = rotm(rot)
    v = [tuple(m @ Vector(p)) for p in v]
    uvs = wrap_uvs(v, f, axis)
    face_mats = [mat_of_face(newell([Vector(v[i]) for i in face])) for face in f]
    return uv_mesh(name, v, f, uvs, mats, face_mats, parent, loc)


def ground_patch(name, size, cells, top, mat, parent, rng, rim=0.2):
    """A low mound of earth: flat on top, sloping just below the ground at its ragged rim."""
    sx, sy = size
    nx, ny = cells
    verts = []
    for j in range(ny + 1):
        for i in range(nx + 1):
            u, v = i / nx - 0.5, j / ny - 0.5
            e = max(abs(u), abs(v)) * 2
            edge = i in (0, nx) or j in (0, ny)
            grow = 1.0 + (rng.uniform(-0.05, 0.05) if edge else 0.0)
            z = top * min(1.0, (1.0 - e) / rim) - (0.06 if edge else 0.0) + rng.uniform(-0.012, 0.012)
            verts.append((u * sx * grow, v * sy * grow, z))
    faces = [(j * (nx + 1) + i, j * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i + 1, (j + 1) * (nx + 1) + i)
             for j in range(ny) for i in range(nx)]
    return make_mesh(name, verts, faces, mat, parent, recalc=False)


# ---------- vehicle parts ----------

def wheel(k, c, s, r, width, hub=None):
    """A tyre on a dished steel wheel, its face looking out of side s (+1 / -1 along X)."""
    rot, c = side_rot(s), Vector(c)
    hw, rr = width / 2, r * 0.56
    k.lathe(M["rubber"], [(-hw, rr), (-hw, r * 0.84), (-hw * 0.7, r), (hw * 0.7, r), (hw, r * 0.84), (hw, rr)], c, rot,
            n=14, closed=True, recalc=False)
    k.lathe(hub or M["olive_dark"], [(hw * 0.92, rr), (hw * 0.55, rr * 0.45), (hw * 0.85, rr * 0.3), (hw * 0.85, 0)], c, rot,
            n=10, caps=False, recalc=False)


def lamp(k, c, r=0.07, housing=None):
    """A round headlamp shining toward -Y, its glass at c."""
    c = Vector(c)
    k.lathe(housing or M["olive_dark"], [(-0.12, 0), (-0.12, r * 0.8), (-0.03, r + 0.015), (0.0, r + 0.015), (0.0, 0)], c, TO_FWD, n=8)
    k.lathe(M["lamp"], [(0.004, r), (0.004, 0)], c, TO_FWD, n=8, caps=False, recalc=False)


def torus_profile(R, r, m=6):
    return [(r * math.sin(2 * math.pi * i / m), R + r * math.cos(2 * math.pi * i / m)) for i in range(m)]


# ---------- enemy vehicles ----------

def build_apc():
    """8x8 armoured personnel carrier, 7.5 x 2.9 x 2.4 m: a boat-shaped, sloped hull riding high over eight big
    tyres, with a small one-man turret ("Turret") whose 30 mm cannon ("Barrel", origin on the trunnions) elevates."""
    r = root("Enemy_APC")
    k = Kit()
    o, od, gm, st = M["olive"], M["olive_dark"], M["gunmetal"], M["steel"]
    WR, WW, TX = 0.55, 0.36, 1.10

    def section(y, zb, wb, zs, wf, zf, zf2, wr, zr):
        """Hull cross-section at y: belly, lower sides between the wheels, fender flare, sloped upper sides, roof."""
        c = min(0.12, (zs - zb) / 2)
        right = [(wb - c, zb), (wb, zb + c), (wb, zs), (wf, zf), (wf - 0.04, zf2), (wr, zr)]
        pts = [(0.0, zb)] + right + [(0.0, zr + 0.025)] + [(-x, z) for x, z in reversed(right)]
        return [Vector((x, y, z)) for x, z in pts]

    k.loft(o, [section(-3.75, 0.86, 0.80, 0.98, 1.22, 1.02, 1.07, 1.08, 1.12),
               section(-3.15, 0.45, 0.84, 1.10, 1.42, 1.20, 1.35, 1.14, 1.46),
               section(-2.35, 0.45, 0.86, 1.12, 1.45, 1.22, 1.38, 1.15, 1.80),
               section(-2.05, 0.45, 0.86, 1.12, 1.45, 1.22, 1.38, 1.15, 1.98),
               section(3.30, 0.45, 0.86, 1.12, 1.45, 1.22, 1.38, 1.15, 1.98),
               section(3.75, 0.78, 0.86, 1.12, 1.40, 1.22, 1.36, 1.12, 1.50)])

    # Eight wheels on independent arms.
    for y in (-2.35, -1.0, 0.85, 2.2):
        for s in (-1, 1):
            wheel(k, (s * TX, y, WR), s, WR, WW)
            k.box(od, (0.16, 0.24, 0.14), (s * 0.86, y, WR + 0.05))

    # Upper glacis: folded trim vane, headlamps on brackets; tow hooks on the lower nose plate.
    ga, gb = Vector((0, -3.75, 1.12)), Vector((0, -3.15, 1.46))
    gd = gb - ga
    gn = Vector((0, -gd.z, gd.y)).normalized()
    k.plate(od, [ga + gd * f + Vector((x, 0, 0)) + gn * 0.02 for x, f in ((-0.75, 0.2), (0.75, 0.2), (0.75, 0.8), (-0.75, 0.8))], gn, 0.04)
    for s in (-1, 1):
        p = ga + gd * 0.85 + Vector((s * 0.93, 0, 0))
        k.box(od, (0.2, 0.16, 0.12), p + Vector((0, 0.02, 0.04)), ch=0.02)
        lamp(k, p + Vector((0, -0.09, 0.13)), r=0.055)
        mark("headlight", p + Vector((0, -0.094, 0.13)), (0, -1, 0))
        k.box(od, (0.1, 0.16, 0.12), (s * 0.5, -3.56, 0.72), ch=0.02)

    # Driver's and commander's plate: two windows with their armoured shutters propped open.
    p0, p1 = Vector((0, -2.35, 1.80)), Vector((0, -2.05, 1.98))
    up = p1 - p0
    un = up.normalized()
    pn = Vector((0, -up.z, up.y)).normalized()
    for s in (-1, 1):
        c = p0 + up * 0.45 + Vector((s * 0.42, 0, 0))
        k.box(M["glass"], (0.5, 0.16, 0.02), c + pn * 0.008, basis((1, 0, 0), un, pn))
        h = p0 + up * 0.9 + Vector((s * 0.42, 0, 0))
        a = math.radians(115)
        ds = -un * math.cos(a) + pn * math.sin(a)
        k.box(od, (0.56, 0.26, 0.03), h + ds * 0.13 + pn * 0.015, basis((1, 0, 0), ds, Vector((1, 0, 0)).cross(ds)))
        # Roof hatches behind the plate.
        k.lathe(od, [(0.0, 0.21), (0.03, 0.21), (0.05, 0.16), (0.05, 0)], (s * 0.45, -1.86, 1.985), n=12, caps=False, recalc=False)

    # Flanks: side doors between the second and third axles, firing ports, an exhaust box on the right.
    def side_pt(s, y, f):
        return Vector((s * (1.41 - 0.26 * f), y, 1.38 + 0.60 * f))

    for s in (-1, 1):
        sn = Vector((s * 0.6, 0, 0.26)).normalized()
        k.plate(od, [side_pt(s, y, f) + sn * 0.015 for y, f in ((-0.40, 0.1), (0.30, 0.1), (0.30, 0.8), (-0.40, 0.8))], sn, 0.03)
        k.box(od, (0.03, 0.62, 0.42), (s * 0.875, -0.05, 0.86))
        k.box(st, (0.14, 0.5, 0.03), (s * 0.94, -0.05, 0.6))
        for y in (-0.33, 0.23):
            k.cyl(gm, 0.025, 0.12, side_pt(s, y, 0.45) + sn * 0.035, TO_BACK, n=6)
        for y in (-1.75, 1.05, 1.65):
            k.lathe(od, [(0.0, 0.075), (0.03, 0.07), (0.045, 0)], side_pt(s, y, 0.5), aim(sn), n=8, caps=False, recalc=False)
        mark("clear flank panel for an emblem (0.6 m)", side_pt(s, -1.0, 0.5) + sn * 0.005, sn)
        # Stowage bin on the flare above the rear wheels.
        k.box(od, (0.22, 0.9, 0.2), (s * 1.30, 1.55, 1.45), ch=0.02)
    k.box(M["rust"], (0.12, 0.8, 0.16), side_pt(1, 2.8, 0.15) + Vector((0.07, 0, 0.05)), ch=0.02)

    # Roof: troop hatches, engine deck grilles, antennas.
    for s in (-1, 1):
        k.box(od, (0.62, 0.78, 0.04), (s * 0.48, 0.75, 2.0), ch=0.01)
        k.cyl(gm, 0.02, 0.6, (s * 0.48, 0.34, 2.02), TO_X, n=6)
        cx = s * 0.52
        k.box(od, (0.72, 1.1, 0.03), (cx, 2.55, 1.99))
        for i in range(6):
            k.box(gm, (0.66, 0.05, 0.012), (cx, 2.1 + i * 0.18, 2.012), (math.radians(30), 0, 0))
        k.cyl(od, 0.04, 0.1, (s * 0.95, 3.15, 2.03), n=8)
        k.cyl(gm, 0.008, 2.2, (s * 0.95, 3.15, 3.18), n=4)

    # Rear plate: tail lights, a stowage box, tow hook.
    for s in (-1, 1):
        k.box(od, (0.16, 0.05, 0.1), (s * 1.0, 3.76, 1.22), ch=0.01)
        k.box(M["red_lens"], (0.12, 0.02, 0.06), (s * 1.0, 3.785, 1.22))
    k.box(od, (0.9, 0.14, 0.3), (0, 3.80, 1.15), ch=0.02)
    k.box(gm, (0.12, 0.16, 0.12), (0, 3.82, 0.86), ch=0.02)

    # ---- turret ----
    tp = Vector((0, -1.05, 2.0))
    t = pivot("Turret", tp, r)
    kt = Kit()
    kt.lathe(od, [(0.0, 0.70), (0.04, 0.70), (0.04, 0.0)], tp, n=16, caps=False, recalc=False)
    kt.lathe(o, [(0.04, 0.62), (0.10, 0.62), (0.36, 0.50), (0.42, 0.44), (0.42, 0.0)], tp, n=12, caps=False, recalc=False)
    kt.box(od, (0.2, 0.22, 0.18), tp + Vector((-0.24, -0.16, 0.50)), ch=0.02)
    kt.box(M["glass"], (0.14, 0.012, 0.07), tp + Vector((-0.24, -0.272, 0.52)))
    kt.lathe(od, [(0.42, 0.22), (0.45, 0.22), (0.47, 0.17), (0.47, 0)], tp + Vector((0.16, 0.12, 0)), n=12, caps=False, recalc=False)
    kt.box(od, (0.14, 0.16, 0.14), tp + Vector((0.40, -0.36, 0.34)), ch=0.02)
    kt.cyl(M["glass"], 0.05, 0.02, tp + Vector((0.40, -0.445, 0.34)), TO_FWD, n=10)
    for s in (-1, 1):
        base = tp + Vector((s * 0.50, 0.22, 0.22))
        kt.box(od, (0.1, 0.32, 0.1), base, ch=0.015)
        R = aim(Vector((s * 0.45, -1, 0.6)))
        for i in range(3):
            kt.lathe(od, [(0, 0), (0, 0.04), (0.2, 0.04), (0.2, 0)], base + Vector((s * 0.03, -0.1 + i * 0.1, 0.04)), R, n=6)
    kt.cyl(gm, 0.008, 1.4, tp + Vector((-0.3, 0.35, 1.12)), n=4)

    # ---- cannon: mantlet, recoil sleeve, barrel and muzzle brake; coaxial MG beside it ----
    bp = Vector((0, -1.56, 2.22))
    b = pivot("Barrel", bp, t)
    kb = Kit()
    kb.box(od, (0.40, 0.26, 0.26), bp + Vector((0, -0.04, 0)), ch=0.03)
    kb.lathe(gm, [(0, 0), (0, 0.065), (0.45, 0.06), (0.5, 0.034), (2.0, 0.03), (2.0, 0.046), (2.16, 0.046), (2.16, 0)],
             bp + Vector((0, -0.17, 0)), TO_FWD, n=8)
    kb.cyl(gm, 0.016, 0.5, bp + Vector((0.13, -0.42, -0.06)), TO_FWD, n=6)
    mark("cannon muzzle (2.33 m along the Barrel's forward)", bp + Vector((0, -2.33, 0)), (0, -1, 0))
    kb.box(od, (0.1, 0.36, 0.08), bp + Vector((0, -0.28, 0.1)), ch=0.015)

    k.build("HullParts", r)
    kt.build("TurretParts", t)
    kb.build("GunParts", b)
    return r


TRUCK_WR, TRUCK_WW, TRUCK_TX = 0.54, 0.36, 0.98


def truck_chassis(k, axles, y0, y1):
    """Big tyres on beam axles with leaf springs and differentials, and the ladder frame from y0 to y1."""
    od, st, gm = M["olive_dark"], M["steel"], M["gunmetal"]
    for y in axles:
        for s in (-1, 1):
            wheel(k, (s * TRUCK_TX, y, TRUCK_WR), s, TRUCK_WR, TRUCK_WW)
            k.box(st, (0.09, 0.9, 0.09), (s * 0.46, y, 0.80))
        k.cyl(od, 0.06, 2 * TRUCK_TX - 0.3, (0, y, TRUCK_WR), TO_X, n=8)
        k.lathe(od, [(-0.15, 0), (-0.15, 0.1), (0.0, 0.17), (0.14, 0.09), (0.14, 0)], (0, y, TRUCK_WR), TO_BACK, n=8)
    for s in (-1, 1):
        k.box(od, (0.12, y1 - y0, 0.24), (s * 0.46, (y0 + y1) / 2, 0.98))
    for y in (y0 + 0.1, (y0 + y1) / 2, y1 - 0.1):
        k.box(od, (0.84, 0.08, 0.12), (0, y, 0.98))
    k.bar(gm, (0, axles[0] + 0.2, TRUCK_WR + 0.06), (0, axles[1] - 0.2, TRUCK_WR + 0.06), 0.035, n=6)
    k.box(od, (2.0, 0.14, 0.18), (0, y1 + 0.05, 0.88), ch=0.015)
    for s in (-1, 1):
        k.box(od, (0.16, 0.05, 0.1), (s * 0.82, y1 + 0.13, 0.9), ch=0.01)
        k.box(M["red_lens"], (0.12, 0.02, 0.06), (s * 0.82, y1 + 0.16, 0.9))


def truck_cab(k):
    """The front of a bonneted army truck: bumper, grille, bonnet, wings with headlamps, and a flat-roofed cab
    with a raked two-pane windscreen. The cab's back is at y -1.0."""
    o, od, gm, st = M["olive"], M["olive_dark"], M["gunmetal"], M["steel"]
    k.box(od, (2.2, 0.16, 0.24), (0, -3.47, 0.92), ch=0.02)
    for s in (-1, 1):
        k.box(gm, (0.09, 0.12, 0.1), (s * 0.55, -3.58, 0.88), ch=0.01)
    k.hexa(o, [(-0.60, -3.40, 1.08), (0.60, -3.40, 1.08), (0.60, -2.28, 1.08), (-0.60, -2.28, 1.08),
               (-0.57, -3.37, 1.78), (0.57, -3.37, 1.78), (0.57, -2.28, 1.88), (-0.57, -2.28, 1.88)])
    k.box(M["rubber"], (0.92, 0.02, 0.5), (0, -3.405, 1.42))
    for i in range(7):
        k.box(od, (0.035, 0.04, 0.54), (-0.39 + i * 0.13, -3.42, 1.42))
    for z in (1.15, 1.69):
        k.box(od, (1.0, 0.05, 0.05), (0, -3.42, z))
    for s in (-1, 1):
        k.box(o, (0.62, 1.30, 0.04), (s * 0.92, -2.45, 1.32), ch=0.01)
        k.box(o, (0.62, 0.04, 0.34), (s * 0.92, -3.175, 1.18), (math.radians(-28), 0, 0), ch=0.01)
        k.box(o, (0.62, 0.035, 0.34), (s * 0.92, -1.80, 1.16), ch=0.008)
        lamp(k, (s * 0.92, -3.05, 1.45), r=0.08)
        mark("headlight", (s * 0.92, -3.054, 1.45), (0, -1, 0))
        k.box(M["amber"], (0.06, 0.08, 0.05), (s * 1.17, -2.95, 1.36))
    # Cab and windscreen.
    k.loft(o, xprism([(-2.28, 1.08), (-1.00, 1.08), (-1.00, 2.55), (-2.05, 2.55), (-2.28, 1.92)], -1.18, 1.18))
    w0, w1 = Vector((0, -2.28, 1.92)), Vector((0, -2.05, 2.55))
    wd = w1 - w0
    wn = Vector((0, -wd.z, wd.y)).normalized()
    for s in (-1, 1):
        k.plate(M["glass"], [w0 + wd * f + Vector((x, 0, 0)) + wn * 0.006 for x, f in ((s * 0.06, 0.1), (s * 1.06, 0.1), (s * 1.06, 0.9), (s * 0.06, 0.9))], wn, 0.012)
        k.box(M["glass"], (0.012, 0.62, 0.48), (s * 1.186, -1.62, 2.18))
        k.box(o, (0.012, 0.86, 1.2), (s * 1.184, -1.62, 1.66), ch=0.004)
        mark("cab door panel for an emblem (0.4 m)", (s * 1.191, -1.70, 1.52), (s, 0, 0))
        k.box(gm, (0.03, 0.12, 0.03), (s * 1.2, -1.30, 1.85))
        for z in (0.64, 0.88):
            k.box(st, (0.22, 0.34, 0.04), (s * 1.04, -1.62, z), ch=0.008)
        k.box(od, (0.03, 0.03, 0.5), (s * 1.12, -1.46, 0.86))
        k.bar(gm, (s * 1.18, -2.1, 2.25), (s * 1.30, -2.18, 2.30), 0.012, n=4)
        k.box(od, (0.04, 0.12, 0.24), (s * 1.31, -2.18, 2.30), ch=0.01)
        k.box(M["glass"], (0.03, 0.006, 0.2), (s * 1.31, -2.243, 2.3))
    k.bar(od, w0 + wd * 0.05 + wn * 0.01, w0 + wd * 0.95 + wn * 0.01, 0.025, n=4)
    k.box(M["glass"], (0.8, 0.012, 0.32), (0, -0.995, 2.2))
    k.lathe(od, [(0, 0.28), (0.03, 0.28), (0.05, 0.22), (0.05, 0)], (0.45, -1.55, 2.55), n=12, caps=False, recalc=False)
    # Fuel tank on the left, a battery box on the right, the exhaust up behind the cab.
    k.cyl(od, 0.22, 0.9, (-0.84, -0.45, 0.82), TO_BACK, n=12)
    for y in (-0.7, -0.2):
        k.cyl(M["rubber"], 0.225, 0.04, (-0.84, y, 0.82), TO_BACK, n=12)
    k.box(od, (0.4, 0.8, 0.45), (0.86, -0.45, 0.82), ch=0.02)
    k.bar(M["rust"], (0.95, -0.93, 1.1), (0.95, -0.93, 2.5), 0.05, n=8)
    k.bar(M["rust"], (0.95, -0.93, 2.5), (0.95, -0.85, 2.62), 0.045, n=8)


def build_rocket():
    """Multiple rocket launcher on a 6x6 truck, 7 x 2.5 x 2.8 m: 40 tubes in a 4 x 10 pack on a turntable behind
    the cab. "Turret" is the whole pack, its origin on the trunnion where it mounts on the turntable, so the game
    can turn it (about up) and raise it (about its X) round that point. Rest pose: level, muzzles forward."""
    r = root("Enemy_Rocket")
    k = Kit()
    o, od, gm = M["olive"], M["olive_dark"], M["gunmetal"]
    truck_chassis(k, (-2.45, 1.05, 2.45), -3.35, 3.30)
    truck_cab(k)
    # Launcher deck, mudguards, stowed rear jacks, a crew locker behind the cab and the turntable.
    k.box(od, (1.2, 4.25, 0.26), (0, 1.175, 1.23))
    k.box(o, (2.3, 4.25, 0.12), (0, 1.175, 1.42), ch=0.015)
    for s in (-1, 1):
        k.box(od, (0.46, 2.5, 0.03), (s * 0.98, 1.75, 1.20), ch=0.006)
        k.box(M["rubber"], (0.36, 0.015, 0.45), (s * 0.98, 3.05, 0.78))
        k.cyl(od, 0.07, 0.9, (s * 0.95, 3.15, 0.95), n=10)
        k.lathe(M["rust"], [(0, 0), (0, 0.14), (0.03, 0.14), (0.05, 0.06), (0.05, 0)], (s * 0.95, 3.15, 0.45), n=10)
        k.box(od, (0.04, 3.9, 0.12), (s * 1.13, 1.3, 1.42))
    k.box(od, (2.0, 0.42, 0.40), (0, -0.70, 1.68), ch=0.02)
    k.box(od, (2.02, 0.44, 0.04), (0, -0.70, 1.86), ch=0.01)
    for x in (-0.6, 0.6):
        k.box(gm, (0.14, 0.02, 0.04), (x, -0.925, 1.78))
    TP = Vector((0, 2.3, 1.95))
    k.lathe(od, [(0, 0.82), (0.24, 0.82), (0.28, 0.76), (0.28, 0.55), (0.40, 0.50), (0.47, 0.40), (0.47, 0)],
            (0, TP.y, 1.48), n=16, caps=False, recalc=False)

    # ---- the launcher pack ----
    t = pivot("Turret", TP, r)
    kt = Kit()
    kt.box(od, (0.56, 0.40, 0.14), TP, ch=0.02)
    for s in (-1, 1):
        kt.box(o, (0.06, 0.36, 0.30), TP + Vector((s * 0.26, 0, 0.10)), ch=0.01)
    kt.cyl(gm, 0.05, 0.62, TP, TO_X, n=8)
    rows, cols, pitch, rt, L = 4, 10, 0.15, 0.066, 3.0
    rear = TP.y + 0.08  # the pack's back sits just behind the trunnion, so raising it doesn't dip it into the turntable
    z0 = TP.z + 0.26
    kt.box(od, (0.36, 2.85, 0.10), Vector((0, rear - 1.425, z0 - rt - 0.085)))
    for i in range(cols):
        x = (i - (cols - 1) / 2) * pitch
        for j in range(rows):
            c = Vector((x, rear, z0 + j * pitch))
            kt.lathe(od, [(0.0, rt), (L, rt)], c, TO_FWD, n=8, caps=False, recalc=False)
            kt.lathe(M["rubber"], [(L - 0.004, rt), (L - 0.004, 0.0)], c, TO_FWD, n=8, caps=False, recalc=False)
    half_w = (cols - 1) / 2 * pitch + rt + 0.0175
    zc = z0 + (rows - 1) / 2 * pitch
    for yb in (rear - 0.15, rear - 1.5, rear - 2.85):
        kt.box(o, (2 * half_w + 0.035, 0.07, 0.035), (0, yb, z0 - rt - 0.0175))
        kt.box(o, (2 * half_w + 0.035, 0.07, 0.035), (0, yb, z0 + (rows - 1) * pitch + rt + 0.0175))
        for s in (-1, 1):
            kt.box(o, (0.035, 0.07, (rows - 1) * pitch + 2 * rt + 0.07), (s * half_w, yb, zc))
    kt.box(od, (2 * half_w, 0.04, (rows - 1) * pitch + 2 * rt), (0, rear + 0.02, zc))
    mark("centre of the tube muzzles (3.0 m ahead of the pack's back)", (0, rear - L, zc), (0, -1, 0))
    # Laying sight on the left and its cable.
    sight = Vector((-half_w - 0.12, rear - 0.5, zc - 0.05))
    kt.box(od, (0.14, 0.24, 0.2), sight, ch=0.02)
    kt.box(M["glass"], (0.1, 0.01, 0.06), sight + Vector((0, -0.125, 0.04)))
    kt.box(od, (0.12, 0.06, 0.06), sight + Vector((0.1, 0, -0.04)))
    kt.path(M["rubber"], [sight + Vector((0, 0.1, -0.1)), Vector((-0.4, TP.y + 0.15, TP.z + 0.12)), TP + Vector((-0.2, 0.15, 0.0))], 0.015, n=4)

    k.build("TruckParts", r)
    kt.build("LauncherParts", t)
    return r


def build_tanker():
    """Fuel tanker on a 6x6 truck, 8 x 2.5 x 3 m: a cylindrical tank with domed ends, a walkway and manholes on
    top, hose lockers and hose tubes on the sides and a hose reel at the back with a hose hanging off it."""
    r = root("Enemy_Tanker")
    k = Kit()
    o, od, gm, st = M["olive"], M["olive_dark"], M["gunmetal"], M["steel"]
    truck_chassis(k, (-2.45, 1.45, 2.85), -3.35, 4.20)
    truck_cab(k)
    # Tank and its saddles, bands, walkway, manholes, ladder.
    TZ, TR, y0, L = 2.04, 0.93, -0.80, 4.95
    k.lathe(od, [(0, 0), (0.03, 0.40), (0.10, 0.68), (0.19, 0.85), (0.28, TR), (L - 0.28, TR), (L - 0.19, 0.85),
                 (L - 0.10, 0.68), (L - 0.03, 0.40), (L, 0)], (0, y0, TZ), TO_BACK, n=20, recalc=False)
    for y in (-0.1, 1.65, 3.45):
        k.box(od, (1.1, 0.22, 0.16), (0, y, 1.17))
    for y in (-0.25, 1.65, 3.55):
        k.lathe(st, [(0, TR + 0.012), (0.07, TR + 0.012)], (0, y, TZ), TO_BACK, n=20, caps=False, recalc=False)
    for s in (-1, 1):
        mark("tank flank for an emblem (0.8 m, curved)", (s * (TR + 0.005), 2.6, TZ), (s, 0, 0))
    k.box(st, (0.46, 3.7, 0.04), (0, 1.65, TZ + TR + 0.035))
    for s in (-1, 1):
        k.bar(gm, (s * 0.26, 0.0, TZ + TR + 0.08), (s * 0.26, 3.3, TZ + TR + 0.08), 0.016, n=4)
    for y in (0.55, 2.75):
        k.lathe(gm, [(0, 0.27), (0.08, 0.27), (0.12, 0.22), (0.14, 0.0)], (0, y, TZ + TR - 0.02), n=12, caps=False, recalc=False)
    for x in (-0.2, 0.2):
        k.bar(gm, (x, 4.22, 0.95), (x, 4.0, TZ + 0.8), 0.018, n=4)
    for i in range(7):
        f = (i + 0.5) / 7
        k.bar(gm, (-0.2, 4.22 - 0.22 * f, 0.95 + (TZ - 0.15) * f), (0.2, 4.22 - 0.22 * f, 0.95 + (TZ - 0.15) * f), 0.012, n=4)
    # Hose lockers between the axles, hose tubes along the tank's lower flanks, mudguards.
    for s in (-1, 1):
        k.box(od, (0.42, 1.5, 0.55), (s * 0.97, 0.2, 0.92), ch=0.02)
        k.box(od, (0.01, 0.66, 0.46), (s * 1.18, -0.16, 0.93))
        k.box(od, (0.01, 0.66, 0.46), (s * 1.18, 0.56, 0.93))
        for y in (0.15, 0.25):
            k.box(gm, (0.03, 0.03, 0.12), (s * 1.19, y, 0.95))
        k.cyl(od, 0.085, 3.0, (s * 0.98, 2.3, 1.36), TO_BACK, n=8)
        k.cyl(gm, 0.095, 0.08, (s * 0.98, 3.82, 1.36), TO_BACK, n=8)
        k.box(od, (0.46, 2.3, 0.03), (s * 0.98, 2.15, 1.18), ch=0.006)
        k.box(M["rubber"], (0.36, 0.015, 0.45), (s * 0.98, 3.45, 0.78))
    # A coiled hose hung on the right locker.
    k.lathe(M["rubber"], torus_profile(0.24, 0.035), (1.23, 0.2, 1.0), TO_X, n=14, closed=True, recalc=False)
    # Hose reel behind the tank, its hose run down to a nozzle on the bumper.
    reel = Vector((0, 4.32, 1.12))
    for x in (-0.38, 0.38):
        k.cyl(od, 0.3, 0.03, reel + Vector((x, 0, 0)), TO_X, n=14)
    k.cyl(M["rubber"], 0.22, 0.74, reel, TO_X, n=14)
    k.box(od, (0.9, 0.3, 0.06), (0, 4.32, 0.80))
    hose = [reel + Vector((0.3, 0.2, -0.05)), Vector((0.45, 4.62, 0.75)), Vector((0.62, 4.66, 0.45)),
            Vector((0.80, 4.58, 0.40)), Vector((0.92, 4.42, 0.62)), Vector((0.92, 4.36, 0.86))]
    k.path(M["rubber"], hose, 0.035, n=6)
    k.cyl(gm, 0.045, 0.24, (0.92, 4.36, 0.98), n=8)
    # Fire extinguishers and a pump box behind the cab.
    k.box(od, (1.6, 0.25, 0.5), (0, -0.92, 1.38), ch=0.02)
    for x in (-0.55, 0.55):
        k.cyl(od, 0.08, 0.4, (x, -0.92, 1.85), n=8)

    k.build("TruckParts", r)
    return r


def build_jammer():
    """Electronic-warfare truck on a 6x6 chassis: a box body with a door, vents and a roof air conditioner, set on
    stabiliser jacks, and a telescopic mast raised 8 m above the roof ("Turret", origin at the mast's foot, so the
    game can turn it slowly) carrying two log-periodic arrays, panel antennas, whips and a small dish."""
    r = root("Enemy_Jammer")
    k = Kit()
    o, od, gm, st = M["olive"], M["olive_dark"], M["gunmetal"], M["steel"]
    truck_chassis(k, (-2.45, 1.05, 2.45), -3.35, 3.35)
    truck_cab(k)
    ROOF = 3.48
    k.box(od, (2.2, 4.3, 0.24), (0, 1.25, 1.24))
    k.box(o, (2.44, 4.30, ROOF - 1.36), (0, 1.25, (ROOF + 1.36) / 2), ch=0.04)
    for s in (-1, 1):
        k.box(od, (0.46, 2.5, 0.03), (s * 0.98, 1.75, 1.20), ch=0.006)
        # Window with a shutter, louvred vents, a side door with a folding step.
        k.box(M["glass"], (0.02, 0.5, 0.35), (s * 1.225, 0.1, 2.75))
        k.box(od, (0.03, 0.6, 0.06), (s * 1.24, 0.1, 2.98))
        for i in range(5):
            k.box(od, (0.03, 0.5, 0.025), (s * 1.235, 2.4, 2.6 + i * 0.07), (s * math.radians(25), 0, 0))
        # Stabiliser jacks down on the ground at the body's corners.
        for y in (-0.7, 3.2):
            k.box(od, (0.18, 0.18, 0.2), (s * 1.12, y, 1.3), ch=0.02)
            k.cyl(od, 0.07, 1.2, (s * 1.12, y, 0.75), n=10)
            k.cyl(gm, 0.05, 0.3, (s * 1.12, y, 0.2), n=8)
            k.lathe(M["rust"], [(0, 0), (0, 0.17), (0.03, 0.17), (0.05, 0.07), (0.05, 0)], (s * 1.12, y, 0), n=10)
    k.box(od, (0.03, 0.9, 1.9), (-1.235, 0.85, 2.33))
    k.box(gm, (0.04, 0.03, 0.18), (-1.25, 0.47, 2.3))
    k.box(st, (0.3, 0.7, 0.04), (-1.3, 0.85, 1.0), ch=0.008)
    for s in (-1, 1):
        mark("body panel for an emblem (0.6 m)", (s * 1.225, 1.75, 2.35), (s, 0, 0))
    # Rear door and ladder.
    k.box(od, (1.0, 0.03, 1.9), (0.35, 3.415, 2.33))
    k.box(gm, (0.18, 0.04, 0.03), (0.0, 3.43, 2.3))
    for x in (-0.6, -0.25):
        k.bar(gm, (x, 3.45, 0.4), (x, 3.45, ROOF + 0.4), 0.018, n=4)
    for i in range(9):
        k.bar(gm, (-0.6, 3.45, 0.55 + i * 0.36), (-0.25, 3.45, 0.55 + i * 0.36), 0.013, n=4)
    # Roof: air conditioner, rails, cable reels, two whips at the front.
    k.box(od, (1.2, 0.7, 0.42), (0, -0.4, ROOF + 0.21), ch=0.03)
    for i in range(6):
        k.box(gm, (1.0, 0.03, 0.025), (0, -0.765, ROOF + 0.08 + i * 0.055), (math.radians(30), 0, 0))
    for s in (-1, 1):
        k.box(od, (0.05, 4.1, 0.05), (s * 1.15, 1.25, ROOF + 0.06))
        k.cyl(od, 0.04, 0.12, (s * 0.95, -0.85, ROOF + 0.06), n=8)
        k.cyl(gm, 0.008, 2.0, (s * 0.95, -0.85, ROOF + 1.1), n=4)
        k.cyl(od, 0.28, 0.04, (s * 0.6, 1.4, ROOF + 0.02 + 0.15), TO_X, n=12)
    for x in (-0.6, 0.6):
        k.cyl(M["rubber"], 0.2, 0.3, (x, 1.4, ROOF + 0.17), TO_X, n=12)
    for x in (-0.75, -0.45, 0.45, 0.75):
        k.cyl(od, 0.28, 0.03, (x, 1.4, ROOF + 0.17), TO_X, n=12)

    # ---- the mast ----
    MP = Vector((0, 2.75, ROOF))
    t = pivot("Turret", MP, r)
    kt = Kit()
    kt.lathe(od, [(0.0, 0.32), (0.22, 0.32), (0.28, 0.24), (0.28, 0.0)], MP, n=12, caps=False, recalc=False)
    H = 8.0
    kt.lathe(gm, [(0.28, 0.15), (2.2, 0.15), (2.2, 0.175), (2.32, 0.175), (2.32, 0.125), (4.2, 0.125), (4.2, 0.15),
                  (4.32, 0.15), (4.32, 0.10), (6.1, 0.10), (6.1, 0.125), (6.2, 0.125), (6.2, 0.08), (H - 0.2, 0.08), (H - 0.2, 0)],
             MP, n=10, caps=False, recalc=False)
    kt.bar(M["rubber"], MP + Vector((0.17, 0, 0.3)), MP + Vector((0.12, 0, H - 0.3)), 0.012, n=4)
    for i in range(3):
        a = 2 * math.pi * i / 3 - R90
        d = Vector((math.cos(a), math.sin(a), 0))
        c = MP + d * 0.22 + Vector((0, 0, 5.3))
        kt.box(M["steel"], (0.26, 0.07, 0.9), c, basis(Vector((-d.y, d.x, 0)), d, (0, 0, 1)), ch=0.015)
    head = MP + Vector((0, 0, H))
    kt.box(od, (0.36, 0.36, 0.3), head + Vector((0, 0, -0.05)), ch=0.03)
    kt.box(gm, (2.6, 0.08, 0.08), head + Vector((0, 0, 0.12)))
    for s in (-1, 1):
        # Log-periodic arrays pointing forward: a boom with vertical elements shortening toward the front.
        bx = s * 1.25
        kt.bar(gm, head + Vector((bx, 0.55, 0.12)), head + Vector((bx, -0.95, 0.12)), 0.025, n=4)
        for e in range(9):
            y = 0.5 - e * 0.17
            half = 0.6 - e * 0.05
            kt.bar(M["steel"], head + Vector((bx, y, 0.12 - half)), head + Vector((bx, y, 0.12 + half)), 0.012, n=4, caps=False)
        kt.box(od, (0.12, 0.12, 0.12), head + Vector((bx, 0.0, 0.12)), ch=0.02)
    # Small dish on a stub above the head, looking forward and a little up; whips.
    kt.bar(od, head + Vector((0, 0, 0.1)), head + Vector((0, 0, 0.55)), 0.05, n=8)
    dc = head + Vector((0, -0.12, 0.62))
    R = aim(Vector((0, -1, 0.2)))
    kt.lathe(M["olive"], [(0.15, 0.34), (0.07, 0.21), (0.02, 0.1), (0.0, 0.0), (-0.03, 0.0), (-0.01, 0.1), (0.05, 0.21), (0.13, 0.35)],
             dc, R, n=14, closed=True, recalc=False)
    kt.bar(gm, dc, dc + R @ Vector((0, 0, 0.3)), 0.01, n=4)
    kt.box(od, (0.05, 0.05, 0.06), dc + R @ Vector((0, 0, 0.32)))
    for x in (-0.12, 0.12):
        kt.cyl(gm, 0.008, 1.5, head + Vector((x, 0.12, 0.95)), n=4)

    k.build("TruckParts", r)
    kt.build("MastParts", t)
    return r


def crate(k, mat, c, size, yaw=0.0, lid=None):
    R = rotm((0, 0, yaw))
    w, d, h = size
    c = Vector(c)
    k.box(mat, (w, d, h - 0.03), c + Vector((0, 0, -0.015)), R)
    k.box(lid or mat, (w + 0.02, d + 0.02, 0.035), c + Vector((0, 0, h / 2 - 0.0175)), R)


def crate_stack(k, mat, x, y, layers, size, rng, nx=2, ny=2, yaw=0.0, z0=0.04, pallet=True):
    """Crates in layers of nx by ny, on a pallet; the top layer is left part-used."""
    R = rotm((0, 0, yaw))
    w, d, h = size
    z = z0
    if pallet:
        k.box(M["wood_light"], (nx * w + 0.1, ny * d + 0.1, 0.13), Vector((x, y, z + 0.065)), R)
        z += 0.13
    for layer in range(layers):
        for i in range(nx):
            for j in range(ny):
                if layer == layers - 1 and layers > 1 and rng.random() < 0.35:
                    continue
                off = Vector(((i - (nx - 1) / 2) * (w + 0.01), (j - (ny - 1) / 2) * (d + 0.01), 0))
                crate(k, mat, Vector((x, y, z + h / 2)) + R @ off, size, yaw + rng.uniform(-0.04, 0.04))
        z += h


def shell_pallet(k, x, y, yaw):
    """Nine artillery rounds standing on a pallet in a timber frame."""
    R = rotm((0, 0, yaw))
    base = Vector((x, y, 0.04))
    k.box(M["wood_light"], (0.95, 0.95, 0.13), base + Vector((0, 0, 0.065)), R)
    for i in range(3):
        for j in range(3):
            p = base + R @ Vector(((i - 1) * 0.28, (j - 1) * 0.28, 0.13))
            k.lathe(M["brass"], [(0.0, 0.075), (0.42, 0.075)], p, n=6, caps=False, recalc=False)
            k.lathe(M["olive_dark"], [(0.42, 0.072), (0.6, 0.066), (0.78, 0)], p, n=6, caps=False, recalc=False)
    for e in (-1, 1):
        k.box(M["wood"], (0.92, 0.06, 0.05), base + R @ Vector((0, e * 0.43, 0.6)), R)
        k.box(M["wood"], (0.06, 0.92, 0.05), base + R @ Vector((e * 0.43, 0, 0.3)), R)
        for f in (-1, 1):
            k.box(M["wood"], (0.06, 0.06, 0.52), base + R @ Vector((e * 0.43, f * 0.43, 0.39)), R)


def camo_net(k, name, poles, size, cells, parent, rng, droop=0.5, holes=0.22):
    """A camouflage net draped over pole tops (x, y, height): it sags between them, droops at its ragged edges,
    is full of holes (so what it hides shows through in patches) and has scraps of garnish tied on. Faces look up."""
    sx, sy = size
    nx, ny = cells

    def height(x, y):
        e = max(abs(x / sx), abs(y / sy)) * 2
        return max(h - 0.13 * math.dist((x, y), (px, py)) ** 1.6 for px, py, h in poles) - droop * max(0.0, e - 0.78) / 0.22

    verts = []
    for j in range(ny + 1):
        for i in range(nx + 1):
            x, y = (i / nx - 0.5) * sx, (j / ny - 0.5) * sy
            verts.append((x + rng.uniform(-0.06, 0.06), y + rng.uniform(-0.06, 0.06), height(x, y) + rng.uniform(-0.04, 0.04)))
    faces = []
    for j in range(ny):
        for i in range(nx):
            edge = i in (0, nx - 1) or j in (0, ny - 1)
            if rng.random() < (0.3 if edge else holes):
                continue
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    for _ in range(70):
        x, y = rng.uniform(-0.42, 0.42) * sx, rng.uniform(-0.42, 0.42) * sy
        z = height(x, y) + 0.04
        w, d = rng.uniform(0.3, 0.6), rng.uniform(0.15, 0.3)
        R = rotm((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), rng.uniform(0, math.pi)))
        base = len(verts)
        for u, v in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            verts.append(tuple(Vector((x, y, z)) + R @ Vector((u * w / 2, v * d / 2, 0))))
        faces.append((base, base + 1, base + 2, base + 3))
    make_mesh(name, verts, faces, M["camo"], parent, recalc=False, smooth=False)


def bag(k, c, yaw, size=(0.6, 0.34, 0.18)):
    k.box(M["sand"], size, c, rotm((0, 0, yaw)), ch=0.07)


def build_depot():
    """Field ammunition depot, about 10 x 8 m: stacks of ammunition crates and pallets of shells on a dirt pad,
    under a camouflage net sagging between poles, with a low sandbag wall at the front. Static."""
    r = root("Enemy_Depot")
    k = Kit()
    rng = random.Random(7)
    ground_patch("Pad", (10.0, 8.0), (14, 12), 0.05, M["dirt"], r, rng)
    poles = [(sx * 4.4, sy * 3.4, 2.3) for sx in (-1, 1) for sy in (-1, 1)] + [(0.0, sy * 3.4, 2.5) for sy in (-1, 1)] \
        + [(sx * 1.9, 0.0, 3.0) for sx in (-1, 1)]
    for px, py, h in poles:
        k.bar(M["wood"], (px, py, -0.1), (px, py, h - 0.04), 0.05, n=6)
        if abs(px) > 4 and abs(py) > 3:
            peg = (px * 1.22, py * 1.28, 0.02)
            k.bar(M["canvas"], (px, py, h - 0.2), peg, 0.012, n=4)
            k.bar(M["wood"], (peg[0], peg[1], -0.1), (peg[0], peg[1], 0.12), 0.025, n=4)
    camo_net(k, "Net", poles, (10.0, 7.8), (20, 16), r, rng)
    # Crate stacks: wooden ammunition boxes and steel ones in olive.
    big, small = (0.75, 0.42, 0.30), (0.5, 0.3, 0.24)
    crate_stack(k, M["wood_light"], -3.3, -1.9, 3, big, rng, yaw=0.05)
    crate_stack(k, M["wood"], -3.4, 1.6, 4, big, rng, yaw=-0.04)
    crate_stack(k, M["olive_dark"], -0.6, 2.3, 3, small, rng, nx=3, ny=2, yaw=0.1)
    crate_stack(k, M["wood_light"], 1.3, 2.3, 2, big, rng, yaw=-0.08)
    crate_stack(k, M["wood"], 3.4, 1.4, 3, big, rng, yaw=1.57)
    crate_stack(k, M["wood_light"], 3.3, -1.7, 4, big, rng, yaw=0.02)
    for x, y, yaw in ((-1.8, -2.6, 0.4), (-1.3, -3.0, -0.3), (2.0, -3.1, 0.9), (0.2, -0.6, 0.2)):
        crate(k, M["wood_light"] if rng.random() < 0.6 else M["wood"], (x, y, 0.04 + big[2] / 2), big, yaw)
    shell_pallet(k, -0.7, -1.5, 0.1)
    shell_pallet(k, 0.7, -1.4, -0.15)
    # Sandbag wall along the front, and a few spare bags.
    for course in range(2):
        for i in range(8 - course):
            x = -2.15 + i * 0.62 + (0.31 if course else 0.0)
            bag(k, (x, -3.85 + rng.uniform(-0.03, 0.03), 0.04 + 0.09 + course * 0.165), rng.uniform(-0.06, 0.06))
    for x, y, yaw in ((-4.1, -0.3, 1.4), (-4.2, 0.35, 1.7), (4.1, -0.2, 1.5)):
        bag(k, (x, y, 0.13), yaw)
    k.build("DepotParts", r)
    return r


# ---------- railway ----------

def build_rail_track():
    """6 m of straight standard-gauge track along Y (y -3..3): a gravel bed about 3 m wide, wooden sleepers every
    0.6 m on steel baseplates, two flat-bottomed rails. The ends are cut square, so segments butt end to end; the
    gravel's UVs are stretched a little so its texture repeats a whole number of times per segment."""
    r = root("Scenery_RailTrack")
    k = Kit()
    L = 6.0
    prof = [(-1.55, -0.12), (1.55, -0.12), (1.22, 0.29), (1.10, 0.33), (-1.10, 0.33), (-1.22, 0.29)]
    v, f = loft_geo(yprism(prof, -L / 2, L / 2))
    stretch = 5.0 / 6.0  # Gravel tiles every 2.5 m: 6 m of track shows exactly two repeats
    uvs = []
    for face in f:
        pts = [Vector(v[i]) for i in face]
        n = newell(pts)
        axis = max(range(3), key=lambda i: abs(n[i]))
        uvs.append([(p.y * stretch, p.z) if axis == 0 else (p.x, p.z) if axis == 1 else (p.x, p.y * stretch) for p in pts])
    uv_mesh("Ballast", v, f, uvs, [M["gravel"]], [0] * len(f), r)
    for i in range(10):
        y = -L / 2 + 0.3 + i * 0.6
        k.box(M["wood"], (2.6, 0.25, 0.16), (0, y, 0.32))
        for s in (-1, 1):
            k.box(M["steel"], (0.34, 0.17, 0.02), (s * RAIL_X, y, 0.41))
    rail = [(-0.075, 0.0), (0.075, 0.0), (0.075, 0.011), (0.016, 0.03), (0.009, 0.045), (0.009, 0.118), (0.036, 0.131),
            (0.036, 0.172), (-0.036, 0.172), (-0.036, 0.131), (-0.009, 0.118), (-0.009, 0.045), (-0.016, 0.03), (-0.075, 0.011)]
    for s in (-1, 1):
        k.loft(M["steel"], yprism([(s * RAIL_X + x, 0.42 + z) for x, z in rail], -L / 2, L / 2))
    k.build("TrackParts", r)
    return r


WAGON_WHEEL = [(-0.07, 0.0), (-0.07, 0.49), (-0.045, 0.49), (-0.03, WHEEL_R), (0.065, WHEEL_R - 0.008), (0.065, 0.38),
               (0.035, 0.28), (0.06, 0.12), (0.06, 0.0)]


def bogie(k, yc):
    """A two-axle freight bogie centred at yc: wheelsets on the rails, cast side frames with axle boxes, and a
    bolster on coil springs."""
    st, gm = M["steel"], M["gunmetal"]
    for dy in (-0.9, 0.9):
        y = yc + dy
        for s in (-1, 1):
            k.lathe(M["rust"], WAGON_WHEEL, (s * (RAIL_X - 0.0175), y, AXLE_Z), side_rot(s), n=12, recalc=False)
            k.cyl(st, 0.06, 0.16, (s * 0.86, y, AXLE_Z), TO_X, n=8)
            k.box(st, (0.2, 0.28, 0.26), (s * 0.97, y, AXLE_Z), ch=0.02)
        k.cyl(st, 0.075, 1.36, (0, y, AXLE_Z), TO_X, n=8, caps=False)
    for s in (-1, 1):
        x = s * 0.97
        k.box(st, (0.14, 2.3, 0.14), (x, yc, AXLE_Z + 0.2), ch=0.02)
        k.box(st, (0.14, 0.9, 0.14), (x, yc, AXLE_Z - 0.16), ch=0.02)
        for e in (-1, 1):
            k.bar(st, (x, yc + e * 0.82, AXLE_Z + 0.12), (x, yc + e * 0.42, AXLE_Z - 0.16), 0.07, square=True)
            k.cyl(gm, 0.07, 0.22, (x, yc + e * 0.13, AXLE_Z + 0.02), n=8)
    k.box(st, (2.1, 0.36, 0.22), (0, yc, AXLE_Z + 0.16))


def underframe(k, half, sills=True):
    """Centre sill, side sills, headstocks with buffers and screw couplings, corner steps and grab handles, brake
    cylinder and air reservoir. half is the distance from the middle to the headstocks' faces."""
    st, gm, rust = M["steel"], M["gunmetal"], M["rust"]
    k.box(st, (0.40, 2 * half, 0.28), (0, 0, 1.46))
    if sills:
        for s in (-1, 1):
            k.box(st, (0.12, 2 * half, 0.30), (s * 1.30, 0, 1.47))
    for e in (-1, 1):
        y = e * (half - 0.1)
        k.box(rust, (2.8, 0.20, 0.38), (0, y, 1.6))
        for s in (-1, 1):
            k.box(st, (0.34, 0.06, 0.34), (s * 0.875, y + e * 0.12, BUFFER_Z))
            k.lathe(st, [(0.0, 0.09), (0.28, 0.09), (0.28, 0.06), (0.36, 0.06), (0.36, 0.19), (0.40, 0.19), (0.41, 0.15), (0.41, 0)],
                    (s * 0.875, y + e * 0.15, BUFFER_Z), TO_BACK if e > 0 else TO_FWD, n=10, caps=False, recalc=False)
            k.box(st, (0.4, 0.22, 0.03), (s * 1.18, y - e * 0.35, 1.02))
            k.bar(st, (s * 1.32, y - e * 0.35, 1.02), (s * 1.32, y - e * 0.35, 1.34), 0.015, n=4)
            k.bar(gm, (s * 1.42, y - e * 0.05, 1.5), (s * 1.42, y - e * 0.05, 2.1), 0.015, n=4)
        k.box(gm, (0.10, 0.30, 0.10), (0, y + e * 0.25, BUFFER_Z))
        k.bar(gm, (0, y + e * 0.38, BUFFER_Z), (0, y + e * 0.48, BUFFER_Z - 0.25), 0.02, n=4)
    k.cyl(st, 0.18, 0.5, (0.45, 0.8, 1.15), TO_BACK, n=10)
    k.cyl(st, 0.2, 1.0, (-0.35, -1.0, 1.15), TO_X, n=10)


def build_wagon():
    """Covered goods wagon, 14 x 2.9 m over buffers and 3.9 m from the rail to the roof, on two bogies: a ribbed
    steel body in faded oxide with a sliding door each side (part open), vents and an arched roof."""
    r = root("Scenery_RailWagon")
    k = Kit()
    body, st = M["container_rust"], M["steel"]
    underframe(k, 6.6)
    for yc in (-4.6, 4.6):
        bogie(k, yc)
    Z0, Z1, HB = 1.62, 4.15, 6.5
    k.box(body, (2.80, 2 * HB, Z1 - Z0), (0, 0, (Z0 + Z1) / 2))
    arc = [(-1.46 + 2.92 * i / 12, Z1 + 0.34 * (1 - ((-1.46 + 2.92 * i / 12) / 1.46) ** 2)) for i in range(13)]
    k.loft(st, yprism(arc + [(x, z - 0.06) for x, z in reversed(arc)], -HB - 0.06, HB + 0.06))
    for s in (-1, 1):
        x = s * 1.425
        for i in range(13):
            y = -6.0 + i * 1.0
            if abs(y) < 1.4:
                continue
            k.box(body, (0.05, 0.08, Z1 - Z0 - 0.1), (x, y, (Z0 + Z1) / 2))
        for z in (Z0 + 0.04, Z1 - 0.04):
            k.box(body, (0.06, 2 * HB, 0.08), (x, 0, z))
        # Sliding door, pushed 0.3 m back: a dark slit shows at the front of the doorway.
        k.box(body, (0.06, 2.6, 2.35), (s * 1.45, 0.3, 2.85))
        for dy in (-0.9, 0.0, 0.9):
            k.box(body, (0.05, 0.06, 2.3), (s * 1.48, 0.3 + dy, 2.85))
        k.box(M["interior"], (0.02, 0.3, 2.3), (s * 1.405, -1.15, 2.85))
        for z in (1.72, 4.05):
            k.box(st, (0.06, 5.4, 0.06), (s * 1.47, 0.6, z))
        k.box(M["gunmetal"], (0.04, 0.04, 0.4), (s * 1.5, -0.85, 2.8))
        for e in (-1, 1):
            k.box(st, (0.04, 0.6, 0.35), (s * 1.43, e * 5.0, 3.75))
    for e in (-1, 1):
        for z in (2.3, 3.0, 3.7):
            k.box(body, (2.7, 0.05, 0.07), (0, e * (HB + 0.02), z))
    for x in (-0.95, -0.6):
        k.bar(M["gunmetal"], (x, -HB - 0.06, 1.8), (x, -HB - 0.06, Z1 + 0.2), 0.016, n=4)
    for i in range(7):
        k.bar(M["gunmetal"], (-0.95, -HB - 0.06, 2.1 + i * 0.32), (-0.6, -HB - 0.06, 2.1 + i * 0.32), 0.012, n=4)
    k.build("WagonParts", r)
    return r


def build_tank_wagon():
    """Four-axle tank wagon, 12 x 2.9 m over buffers and 3.8 m from the rail to the dome: a dark steel tank with
    dished ends on saddles, three bands, a top platform round the filling dome, side ladders with grab rails over
    the top, end platforms with railings and a handbrake wheel, and the bottom outlet valve."""
    r = root("Scenery_RailTankWagon")
    k = Kit()
    st, gm = M["steel"], M["gunmetal"]
    underframe(k, 5.65, sills=True)
    for yc in (-3.9, 3.9):
        bogie(k, yc)
    TZ, TR, L = 2.84, 1.30, 10.0
    k.lathe(st, [(0, 0), (0.04, 0.55), (0.12, 0.92), (0.24, 1.18), (0.36, TR), (L - 0.36, TR), (L - 0.24, 1.18), (L - 0.12, 0.92),
                 (L - 0.04, 0.55), (L, 0)], (0, -L / 2, TZ), TO_BACK, n=24, recalc=False)
    for y in (-3.9, 0.0, 3.9):
        k.box(st, (2.0, 0.3, 0.32), (0, y, 1.72))
    for y in (-2.6, 2.6):
        k.lathe(gm, [(0, TR + 0.015), (0.08, TR + 0.015)], (0, y, TZ), TO_BACK, n=24, caps=False, recalc=False)
    top = TZ + TR
    k.lathe(st, [(0, 0.48), (0.20, 0.48), (0.23, 0.42), (0.25, 0.30), (0.25, 0)], (0, 0, top - 0.02), n=16, caps=False, recalc=False)
    k.box(M["wood_light"], (1.5, 1.5, 0.04), (0, 0, top + 0.02))
    for sx in (-1, 1):
        for sy in (-1, 1):
            k.bar(st, (sx * 0.6, sy * 0.6, top - 0.15), (sx * 0.6, sy * 0.6, top), 0.03, n=4)
    for s in (-1, 1):
        for dy in (-0.25, 0.25):
            k.bar(gm, (s * 1.42, dy, 1.62), (s * 1.42, dy, 3.25), 0.016, n=4)
            arc = [Vector((s * (TR + 0.12) * math.cos(a), dy, TZ + (TR + 0.12) * math.sin(a))) for a in (math.radians(d) for d in (18, 35, 52, 66))]
            k.path(gm, [Vector((s * 1.42, dy, 3.25))] + arc + [Vector((s * 0.72, dy, top + 0.05))], 0.016, n=4)
        for i in range(5):
            z = 1.85 + i * 0.32
            k.bar(gm, (s * 1.42, -0.25, z), (s * 1.42, 0.25, z), 0.012, n=4)
    for e in (-1, 1):
        y = e * 5.25
        k.box(M["wood_light"], (2.7, 0.55, 0.05), (0, y, 1.64))
        for s in (-1, 1):
            k.bar(gm, (s * 1.3, y + e * 0.2, 1.66), (s * 1.3, y + e * 0.2, 2.65), 0.02, n=4)
            k.bar(gm, (s * 1.3, y - e * 0.22, 1.66), (s * 1.3, y - e * 0.22, 2.65), 0.02, n=4)
            k.bar(gm, (s * 1.3, y - e * 0.22, 2.65), (s * 1.3, y + e * 0.2, 2.65), 0.018, n=4)
        k.bar(gm, (-1.3, y + e * 0.2, 2.65), (1.3, y + e * 0.2, 2.65), 0.018, n=4)
    k.bar(gm, (0.9, -5.4, 1.66), (0.9, -5.4, 2.4), 0.03, n=6)
    k.lathe(gm, torus_profile(0.2, 0.015, 5), (0.9, -5.42, 2.4), TO_FWD, n=12, closed=True, recalc=False)
    k.cyl(st, 0.1, 0.42, (0, 0, TZ - TR - 0.12), n=8)
    k.box(st, (0.3, 0.2, 0.16), (0, 0, TZ - TR - 0.38))
    k.build("TankWagonParts", r)
    return r


# ---------- buildings ----------

def wall_window(k, c, face, w, h, sill=None, lintel=None, bars=True):
    """A window on a wall whose outward normal is face ((+-1, 0) or (0, +-1)); c is its centre on the wall surface.
    The glass stands a little proud of the frame so both read from a distance."""
    nx, ny = face
    c = Vector(c)

    def size(across, out, up):
        return (out, across, up) if nx else (across, out, up)

    def at(out, across=0.0, dz=0.0):
        return c + Vector((nx * out + (0 if nx else across), ny * out + (across if nx else 0), dz))

    k.box(M["frame"], size(w + 0.16, 0.08, h + 0.16), at(0.0))
    k.box(M["win_glass"], size(w, 0.10, h), at(0.0))
    if bars:
        k.box(M["frame"], size(0.05, 0.03, h), at(0.06))
        k.box(M["frame"], size(w, 0.03, 0.05), at(0.06, 0, h * 0.2))
    if sill:
        k.box(sill, size(w + 0.3, 0.16, 0.08), at(0.06, 0, -h / 2 - 0.12))
    if lintel:
        k.box(lintel, size(w + 0.3, 0.06, 0.22), at(0.02, 0, h / 2 + 0.2))


def wall_door(k, c, face, w, h, mat=None, glass=True):
    """A door whose bottom middle is c, on a wall facing face."""
    nx, ny = face
    c = Vector(c)

    def size(across, out, up):
        return (out, across, up) if nx else (across, out, up)

    def at(out, across=0.0, dz=0.0):
        return c + Vector((nx * out + (0 if nx else across), ny * out + (across if nx else 0), dz))

    k.box(M["frame"], size(w + 0.18, 0.08, h + 0.12), at(0.0, 0, (h + 0.12) / 2))
    k.box(mat or M["door"], size(w, 0.1, h), at(0.0, 0, h / 2))
    if glass:
        k.box(M["win_glass"], size(w * 0.6, 0.12, h * 0.25), at(0.0, 0, h * 0.75))
    k.box(M["gunmetal"], size(0.04, 0.14, 0.12), at(0.0, w * 0.35, h * 0.48))


def build_station():
    """Small rural station: a two-storey brick building (14 m along the track, 8 m deep) under a tiled roof, and a
    platform 0.95 m high along its track side, covered by a canopy on iron columns beside the building, with benches,
    lamps, blank name boards and a fence. Drawn with the platform at +X and mirrored, so it ends up on Unity's +X;
    the track runs along the model's Z with its centre line STATION_TRACK_X out on that side."""
    r = root("Scenery_Station")
    k = Kit(mirror_x=True)
    B0, B1, HY, F0 = -6.0, 2.0, 7.0, 0.95
    H = F0 + 6.4
    cx = (B0 + B1) / 2
    k.box(M["stone"], (8.2, 14.2, F0 + 0.25), (cx, 0, (F0 + 0.25) / 2 - 0.15))
    k.box(M["brick"], (8.0, 14.0, H - F0), (cx, 0, (F0 + H) / 2))
    k.box(M["stone"], (8.12, 14.12, 0.18), (cx, 0, F0 + 3.2))
    k.box(M["stone"], (8.2, 14.2, 0.24), (cx, 0, H - 0.06))
    rise, ov = 3.0, 0.45
    for s in (-1, 1):
        slab(k, M["roof_tile"], (cx + s * (4.0 + ov), H - ov * rise / 4.0), (cx, H + rise), -HY - ov, HY + ov, 0.16)
        k.box(M["steel"], (0.14, 2 * HY + 0.9, 0.12), (cx + s * (4.0 + ov - 0.05), 0, H - ov * rise / 4.0 - 0.08))
    k.box(M["roof_tile"], (0.34, 2 * HY + 0.9, 0.2), (cx, 0, H + rise + 0.08))
    for e in (-1, 1):
        k.loft(M["brick"], yprism([(cx - 4.0, H), (cx + 4.0, H), (cx, H + rise)], e * (HY - 0.3), e * HY))
        k.box(M["brick"], (0.8, 0.6, 2.4), (cx + 1.0, e * 4.6, H + rise - 0.1))
        k.box(M["stone"], (0.92, 0.72, 0.12), (cx + 1.0, e * 4.6, H + rise + 1.12))
        for sx in (B0, B1):
            k.cyl(M["steel"], 0.05, H - 0.2, (sx + (0.1 if sx > cx else -0.1), e * (HY + 0.1), (H - 0.2) / 2), n=6)
    z1, z2 = F0 + 1.55, F0 + 3.2 + 1.55
    sill, lintel = M["stone"], M["stone"]
    # Platform side (+X): two doors and three windows below, five windows above.
    for y in (-5.2, 0.0, 5.2):
        wall_window(k, (B1, y, z1), (1, 0), 1.0, 1.7, sill, lintel)
    for y in (-2.6, 2.6):
        wall_door(k, (B1, y, F0), (1, 0), 1.1, 2.3)
    for y in (-5.2, -2.6, 0.0, 2.6, 5.2):
        wall_window(k, (B1, y, z2), (1, 0), 1.0, 1.5, sill, lintel)
    # Road side (-X): the entrance with steps and a little canopy.
    for y in (-5.2, -2.6, 2.6, 5.2):
        wall_window(k, (B0, y, z1), (-1, 0), 1.0, 1.7, sill, lintel)
    for y in (-5.2, -2.6, 0.0, 2.6, 5.2):
        wall_window(k, (B0, y, z2), (-1, 0), 1.0, 1.5, sill, lintel)
    wall_door(k, (B0, 0, F0), (-1, 0), 1.4, 2.4)
    for i in range(3):
        k.box(M["stone"], (0.35, 2.2, F0 / 3 * (i + 1)), (B0 - 1.05 + i * 0.35, 0, F0 / 6 * (i + 1) - 0.0))
    k.box(M["roof_metal"], (1.3, 2.6, 0.08), (B0 - 0.62, 0, F0 + 2.8), (0, math.radians(-8), 0))
    for y in (-1.1, 1.1):
        k.bar(M["steel"], (B0 - 0.02, y, F0 + 2.3), (B0 - 1.1, y, F0 + 2.72), 0.03, n=4)
    # Ends: windows, and a side door on the -Y end.
    for e in (-1, 1):
        for x in (cx - 1.6, cx + 1.6):
            wall_window(k, (x, e * HY, z2), (0, e), 1.0, 1.5, sill, lintel)
        wall_window(k, (cx - 1.6, e * HY, z1), (0, e), 1.0, 1.7, sill, lintel)
    wall_door(k, (cx + 1.8, -HY, F0), (0, -1), 1.0, 2.2)
    wall_window(k, (cx + 1.8, HY, z1), (0, 1), 1.0, 1.7, sill, lintel)
    # Platform: concrete with a stone coping and a brick face, ramps at both ends.
    PX0, PX1, PY = B1, B1 + 4.0, 11.0
    k.box(M["concrete"], (PX1 - PX0, 2 * PY, F0 + 0.15), ((PX0 + PX1) / 2, 0, (F0 - 0.15) / 2))
    k.box(M["stone"], (0.45, 2 * PY, 0.08), (PX1 - 0.2, 0, F0 + 0.02))
    k.box(M["brick"], (0.1, 2 * PY, F0 + 0.1), (PX1, 0, (F0 - 0.1) / 2))
    for e in (-1, 1):
        ya, yb = e * PY, e * (PY + 2.5)
        k.hexa(M["concrete"], [(PX0, ya, -0.15), (PX1, ya, -0.15), (PX1, yb, -0.15), (PX0, yb, -0.15),
                               (PX0, ya, F0), (PX1, ya, F0), (PX1, yb, 0.03), (PX0, yb, 0.03)])
    # Canopy along the building on iron columns, with a timber valance.
    CX = PX1 - 0.75
    slab(k, M["roof_metal"], (B1 - 0.05, F0 + 3.12), (PX1 - 0.25, F0 + 2.86), -HY - 0.6, HY + 0.6, 0.08)
    k.box(M["wood_light"], (0.04, 2 * HY + 1.2, 0.42), (PX1 - 0.23, 0, F0 + 2.64))
    k.box(M["steel"], (0.14, 2 * HY + 1.0, 0.18), (CX, 0, F0 + 2.8))
    for y in (-6.0, -3.0, 0.0, 3.0, 6.0):
        k.cyl(M["steel"], 0.075, 2.8, (CX, y, F0 + 1.4), n=8)
        k.box(M["steel"], (0.24, 0.24, 0.2), (CX, y, F0 + 0.1))
        k.bar(M["steel"], (B1 + 0.02, y, F0 + 3.04), (CX, y, F0 + 3.12 - 0.26 * (CX - B1 + 0.05) / (PX1 - 0.25 - B1 + 0.05) - 0.07), 0.06, square=True)
        for e in (-1, 1):
            k.bar(M["steel"], (CX, y, F0 + 2.2), (CX, y + e * 0.55, F0 + 2.75), 0.025, n=4)
        k.bar(M["steel"], (CX, y, F0 + 2.2), (CX - 0.55, y, F0 + 2.88), 0.025, n=4)
    # Benches against the wall, lamps and blank name boards along the edge, a fence behind the open platform.
    for y in (-4.0, 4.0):
        k.box(M["wood_light"], (0.45, 1.8, 0.06), (B1 + 0.45, y, F0 + 0.45))
        k.box(M["wood_light"], (0.06, 1.8, 0.4), (B1 + 0.24, y, F0 + 0.78), (0, math.radians(-10), 0))
        for e in (-1, 1):
            k.box(M["steel"], (0.45, 0.06, 0.44), (B1 + 0.42, y + e * 0.75, F0 + 0.22))
    for y in (-9.5, 9.5):
        k.cyl(M["steel"], 0.06, 3.2, (PX1 - 0.6, y, F0 + 1.6), n=8)
        k.bar(M["steel"], (PX1 - 0.6, y, F0 + 3.1), (PX1 - 0.2, y, F0 + 3.2), 0.03, n=4)
        k.box(M["steel"], (0.3, 0.3, 0.22), (PX1 - 0.2, y, F0 + 3.15), ch=0.03)
        k.box(M["lamp"], (0.22, 0.22, 0.02), (PX1 - 0.2, y, F0 + 3.035))
    for y in (-8.2, 8.2):
        for e in (-1, 1):
            k.box(M["steel"], (0.08, 0.08, 2.3), (PX1 - 1.2, y + e * 0.9, F0 + 1.15))
        k.box(M["steel"], (0.1, 2.1, 0.56), (PX1 - 1.2, y, F0 + 2.1))
        k.box(M["frame"], (0.12, 1.94, 0.42), (PX1 - 1.2, y, F0 + 2.1))
    for e in (-1, 1):
        for i in range(5):
            y = e * (HY + 0.4 + i * 0.9)
            k.box(M["fence"], (0.08, 0.08, 1.1), (B1 + 0.12, y, F0 + 0.5))
        for z in (0.45, 0.85):
            k.box(M["fence"], (0.04, 3.7, 0.1), (B1 + 0.16, e * (HY + 2.2), F0 + z))
    k.build("StationParts", r)
    return r


def build_warehouse():
    """Industrial hall, 18 m wide, 30 m long, 9 m to the ridge: a steel portal frame showing on the outside,
    corrugated walls over a concrete dado, a corrugated roof with a ridge vent, two roller doors in the front gable
    (one half up, showing the dark inside) and one in the side, personnel doors and high strip windows."""
    r = root("Scenery_Warehouse")
    k = Kit()
    W, L, E, RIDGE = 18.0, 30.0, 7.5, 9.0
    hw, hl = W / 2, L / 2
    st, galv = M["steel"], M["galv"]
    k.box(M["concrete"], (W + 0.1, L + 0.1, 1.15), (0, 0, 0.425))
    for s in (-1, 1):
        k.box(galv, (0.12, L, E - 1.0), (s * (hw - 0.04), 0, (1.0 + E) / 2))
    for e in (-1, 1):
        k.loft(galv, yprism([(-hw, 1.0), (hw, 1.0), (hw, E), (0, RIDGE), (-hw, E)], e * (hl - 0.12), e * hl))
    pitch = (RIDGE - E) / hw
    for s in (-1, 1):
        slab(k, M["roof_metal"], (s * (hw + 0.45), E - 0.45 * pitch), (0, RIDGE), -hl - 0.3, hl + 0.3, 0.12)
        k.box(st, (0.2, L + 0.6, 0.16), (s * (hw + 0.42), 0, E - 0.45 * pitch - 0.1))
        for y in (-hl + 0.3, 0.0, hl - 0.3):
            k.cyl(st, 0.06, E - 0.6, (s * (hw + 0.42), y, (E - 0.6) / 2), n=6)
        # Portal frame columns and the eaves beam, proud of the cladding.
        for i in range(6):
            y = -hl + i * 6.0
            k.box(st, (0.25, 0.35, E), (s * (hw + 0.12), y if i else -hl + 0.18, E / 2))
        k.box(st, (0.25, 0.35, E), (s * (hw + 0.12), hl - 0.18, E / 2))
        k.box(st, (0.25, L + 0.2, 0.3), (s * (hw + 0.12), 0, E - 0.15))
        # High strip windows in each bay.
        for i in range(5):
            y = -hl + 3.0 + i * 6.0
            k.box(st, (0.06, 5.1, 0.9), (s * (hw + 0.03), y, 5.9))
            k.box(M["win_glass"], (0.08, 4.9, 0.7), (s * (hw + 0.03), y, 5.9))
    k.box(M["roof_metal"], (0.8, L - 4, 0.35), (0, 0, RIDGE + 0.12))
    for e in (-1, 1):
        y = e * (hl + 0.12)
        for x in (-hw / 2, 0.0, hw / 2):
            h = E + (RIDGE - E) * (1 - abs(x) / hw)
            k.box(st, (0.3, 0.25, h), (x, y, h / 2))
        for s in (-1, 1):
            k.bar(st, (s * hw, y, E), (0, y, RIDGE), 0.15, square=True)
    # Front gable roller doors: the left one shut, the right one half up.
    y = -hl
    for x, open_h in ((-4.5, 0.0), (4.5, 3.0)):
        k.box(st, (5.4, 0.22, 0.25), (x, y - 0.1, 5.75))
        for jx in (-2.7, 2.7):
            k.box(st, (0.2, 0.22, 5.6), (x + jx, y - 0.1, 2.8))
        k.box(galv, (5.4, 0.6, 0.6), (x, y - 0.35, 6.2))
        if open_h:
            k.box(M["interior"], (5.0, 0.04, open_h), (x, y - 0.02, open_h / 2))
        dh = 5.6 - open_h
        k.box(galv, (5.0, 0.1, dh), (x, y - 0.07, open_h + dh / 2))
        for i in range(int(dh / 0.6)):
            k.box(st, (5.0, 0.02, 0.04), (x, y - 0.125, open_h + 0.3 + i * 0.6))
        k.box(st, (5.0, 0.12, 0.08), (x, y - 0.08, open_h + 0.04))
        for jx in (-2.9, 2.9):
            k.cyl(M["concrete"], 0.14, 1.0, (x + jx, y - 0.5, 0.5), n=8)
    wall_door(k, (-8.2, -hl - 0.02, 0.0), (0, -1), 1.0, 2.15, M["steel"], glass=False)
    wall_door(k, (8.2, -hl - 0.02, 0.0), (0, -1), 1.0, 2.15, M["steel"], glass=False)
    # Side roller door with its own personnel door.
    x = hw + 0.06
    k.box(galv, (0.1, 4.5, 4.8), (x + 0.04, 6.0, 2.4))
    for i in range(8):
        k.box(st, (0.02, 4.5, 0.04), (x + 0.1, 6.0, 0.3 + i * 0.6))
    k.box(galv, (0.6, 4.9, 0.6), (x + 0.3, 6.0, 5.1))
    for jy in (-2.35, 2.35):
        k.box(st, (0.2, 0.2, 4.9), (x + 0.08, 6.0 + jy, 2.45))
    wall_door(k, (x, 2.5, 0.0), (1, 0), 1.0, 2.15, M["steel"], glass=False)
    k.build("WarehouseParts", r)
    return r


def build_factory():
    """Brick factory hall, 24 x 16 m: walls 6 m high with pilasters and tall windows, a north-light sawtooth roof
    of four bays (glazing on the steep faces, which look toward Blender +Y, Unity -Z), stepped gable walls that
    follow the teeth, big double doors at the front and a smaller one on the east gable."""
    r = root("Scenery_Factory")
    k = Kit()
    W, D, H = 24.0, 16.0, 6.0
    hw, hd = W / 2, D / 2
    brick, stone, st = M["brick"], M["stone"], M["steel"]
    k.box(M["concrete"], (W + 0.2, D + 0.2, 0.6), (0, 0, 0.15))
    for e in (-1, 1):
        k.box(brick, (W, 0.4, H), (0, e * (hd - 0.2), H / 2))
        k.box(stone, (W + 0.3, 0.55, 0.2), (0, e * (hd - 0.2), H + 0.05))
    teeth, PEAK = 4, 8.4
    span = D / teeth
    outline = [(-hd, 0.0), (hd, 0.0)]
    for i in reversed(range(teeth)):
        y0 = -hd + i * span
        outline += [(y0 + span, H), (y0 + span - 0.7, PEAK)]
    outline.append((-hd, H))
    for s in (-1, 1):
        k.loft(brick, xprism(outline, s * (hw - 0.4), s * hw))
    for i in range(teeth):
        y0 = -hd + i * span
        top = (y0 + span - 0.7, PEAK)
        # Roof slope (faces -Y and up) and the glazed north light (faces +Y).
        a, b = Vector((0, y0 - 0.1, H - 0.06)), Vector((0, top[0], top[1]))
        d = b - a
        n = Vector((0, -d.z, d.y)).normalized()
        k.plate(M["roof_barn"], [Vector((x, p.y, p.z)) + n * 0.05 for x, p in ((-hw - 0.25, a), (hw + 0.25, a), (hw + 0.25, b), (-hw - 0.25, b))], n, 0.1)
        g0, g1 = Vector((0, top[0], top[1])), Vector((0, y0 + span, H))
        gd = g1 - g0
        gn = Vector((0, gd.z, -gd.y)).normalized()
        if gn.y < 0:
            gn = -gn
        k.plate(M["glass"], [Vector((x, p.y, p.z)) for x, p in ((-hw + 0.4, g0), (hw - 0.4, g0), (hw - 0.4, g1), (-hw + 0.4, g1))], gn, 0.05)
        for j in range(13):
            x = -hw + 0.4 + j * (W - 0.8) / 12
            k.bar(st, Vector((x, g0.y, g0.z)) + gn * 0.04, Vector((x, g1.y, g1.z)) + gn * 0.04, 0.035, n=4)
        k.bar(st, Vector((-hw + 0.4, g0.y, g0.z - 0.05)) + gn * 0.04, Vector((hw - 0.4, g0.y, g0.z - 0.05)) + gn * 0.04, 0.05, n=4)
        if i:
            k.box(st, (W, 0.35, 0.12), (0, y0, H))
        # Roof ventilators on the slopes.
        for x in (-6.0, 6.0):
            p = a + d * 0.6
            k.cyl(st, 0.25, 0.6, (x, p.y, p.z + 0.3), n=8)
            k.cyl(st, 0.36, 0.06, (x, p.y, p.z + 0.62), n=8)
    # Pilasters and tall windows with brick arches along both long walls.
    for e in (-1, 1):
        y = e * hd
        for i in range(7):
            x = -hw + i * 4.0
            k.box(brick, (0.5, 0.3, H), (max(-hw + 0.25, min(hw - 0.25, x)), y + e * 0.1, H / 2))
        for i in range(6):
            x = -hw + 2.0 + i * 4.0
            if e < 0 and i in (2, 3):
                continue
            wall_window(k, (x, y, 3.0), (0, e), 2.2, 3.0, stone, brick)
    # Front doors in the middle two bays, an office door, a gable door, windows in the gables.
    y = -hd
    for x in (-1.15, 1.15):
        k.box(M["door"], (2.2, 0.12, 4.4), (x, y - 0.03, 2.2))
        k.box(M["planks_dark"], (2.0, 0.14, 0.12), (x, y - 0.04, 1.2))
        k.box(M["planks_dark"], (2.0, 0.14, 0.12), (x, y - 0.04, 3.4))
    k.box(stone, (5.0, 0.3, 0.5), (0, y - 0.05, 4.7))
    k.box(M["roof_metal"], (5.6, 1.4, 0.1), (0, y - 0.7, 5.15), (math.radians(6), 0, 0))
    wall_door(k, (-4.6, y - 0.02, 0.3), (0, -1), 1.0, 2.2)
    for s in (-1, 1):
        x = s * hw
        for yy in (-4.0, 4.0):
            wall_window(k, (x, yy, 3.0), (s, 0), 2.2, 3.0, stone, brick)
    k.box(M["door"], (0.12, 3.0, 3.6), (hw + 0.03, 0.0, 1.8))
    k.box(stone, (0.3, 3.6, 0.4), (hw + 0.05, 0.0, 3.85))
    k.build("FactoryParts", r)
    return r


def build_chimney():
    """Brick factory chimney, 28 m: a square base with a stone plinth, cornice and a bricked-up flue arch, a round
    shaft tapering from 1.45 m to 0.92 m radius with iron bands, a corbelled crown, a lightning conductor and iron
    rungs up the back. The round brickwork has its UVs wrapped round it."""
    r = root("Scenery_Chimney")
    k = Kit()
    k.box(M["stone"], (4.0, 4.0, 0.8), (0, 0, 0.25))
    k.box(M["brick"], (3.6, 3.6, 3.6), (0, 0, 0.65 + 1.8))
    k.box(M["stone"], (3.9, 3.9, 0.3), (0, 0, 4.4))
    k.box(M["interior"], (1.0, 0.04, 1.5), (0, -1.81, 1.6))
    k.box(M["brick"], (1.5, 0.08, 0.3), (0, -1.82, 2.5))
    k.box(M["brick"], (1.0, 0.06, 1.4), (0, -1.79, 1.55))

    def radius(z):
        return 1.45 + (0.92 - 1.45) * (z - 4.55) / (25.6 - 4.55)

    prof = [(4.4, 1.48), (4.55, 1.45), (25.6, 0.92), (25.8, 1.05), (26.6, 1.08), (26.75, 1.0), (27.6, 0.98), (27.6, 0.80), (27.0, 0.80)]
    lathe_uv_mesh("Shaft", prof, 24, [M["brick"]], lambda n: 0, r)
    k.lathe(M["interior"], [(27.0, 0.80), (27.0, 0.0)], (0, 0, 0), n=24, caps=False, recalc=False)
    for z in (9.0, 14.0, 19.0, 23.0):
        k.lathe(M["steel"], [(z, radius(z) + 0.025), (z + 0.12, radius(z + 0.12) + 0.025)], (0, 0, 0), n=24, caps=False, recalc=False)
    k.bar(M["gunmetal"], (0, radius(4.6) + 0.04, 4.6), (0, radius(25.5) + 0.04, 25.5), 0.015, n=4)
    k.path(M["gunmetal"], [Vector((0, radius(25.5) + 0.04, 25.5)), Vector((0, 1.12, 25.9)), Vector((0, 1.12, 26.7)),
                           Vector((0, 1.02, 27.7)), Vector((0, 0.9, 28.6))], 0.015, n=4)
    for i in range(30):
        z = 5.2 + i * 0.68
        rr = radius(z) + 0.1
        k.bar(M["gunmetal"], (-0.2, -rr, z), (0.2, -rr, z), 0.015, n=4)
        if i % 10 == 0:
            k.bar(M["gunmetal"], (-0.2, -rr + 0.1, z), (-0.2, -rr, z), 0.015, n=4)
    k.build("ChimneyParts", r)
    return r


def build_silo():
    """Two tall corrugated-steel grain silos (6 m across, 16 m to their roof caps) on concrete rings, with stiffeners,
    a ladder and a catwalk between their roofs, and a lattice bucket-elevator tower in front topped by a head house
    (its roof at 18.2 m) whose spouts run down to each silo."""
    r = root("Scenery_Silo")
    k = Kit()
    galv, st = M["galv"], M["steel"]
    SR, H0, H1, PEAK = 3.0, 0.45, 13.8, 16.2
    tops = []
    for sx in (-1, 1):
        cx, cy = sx * 3.35, 1.0
        k.lathe(M["concrete"], [(-0.15, SR + 0.25), (H0, SR + 0.25), (H0, 0)], (cx, cy, 0), n=28, caps=False, recalc=False)
        lathe_uv_mesh(f"Wall_{sx}", [(H0, SR), (H1, SR)], 32, [galv], lambda n: 0, r, loc=(cx, cy, 0), phase=0.25)
        k.lathe(galv, [(H1 - 0.05, SR + 0.1), (H1 + 0.08, SR + 0.12), (PEAK, 0.5), (PEAK, 0.42), (PEAK + 0.4, 0.42), (PEAK + 0.5, 0.58),
                       (PEAK + 0.62, 0.3), (PEAK + 0.66, 0.0)], (cx, cy, 0), n=28, caps=False, recalc=False)
        for i in range(16):
            a = 2 * math.pi * (i + 0.5) / 16
            d = Vector((math.cos(a), math.sin(a), 0))
            k.box(st, (0.06, 0.08, H1 - H0), Vector((cx, cy, (H0 + H1) / 2)) + d * (SR + 0.03), basis(d, Vector((-d.y, d.x, 0)), (0, 0, 1)))
        k.lathe(st, [(H1 - 0.3, SR + 0.05), (H1 - 0.1, SR + 0.05)], (cx, cy, 0), n=28, caps=False, recalc=False)
        tops.append(Vector((cx, cy, PEAK + 0.6)))
    # Ladder up the right-hand silo's outside, and a hand rail up its roof.
    lx, ly = 3.35 + SR + 0.25, 1.0
    for dy in (-0.22, 0.22):
        k.bar(st, (lx, ly + dy, 0.4), (lx, ly + dy, H1 + 0.9), 0.025, n=4)
    for i in range(33):
        k.bar(st, (lx, ly - 0.22, 0.6 + i * 0.4), (lx, ly + 0.22, 0.6 + i * 0.4), 0.014, n=4, caps=False)
    for i in range(6):
        z = 3.0 + i * 2.0
        k.path(st, [Vector((lx - 0.15, ly - 0.25, z)), Vector((lx + 0.25, ly - 0.35, z)), Vector((lx + 0.45, ly, z)),
                    Vector((lx + 0.25, ly + 0.35, z)), Vector((lx - 0.15, ly + 0.25, z))], 0.015, n=4, caps=False)
    k.path(st, [Vector((lx - 0.1, ly, H1 + 0.9)), Vector((3.35 + 0.8, ly, PEAK + 0.1))], 0.025, n=4)
    # Catwalk between the roof caps.
    k.box(st, (6.7 - 1.0, 0.8, 0.06), (0, 1.0, PEAK + 0.2))
    for dy in (-0.4, 0.4):
        k.bar(st, (-2.8, 1.0 + dy, PEAK + 1.2), (2.8, 1.0 + dy, PEAK + 1.2), 0.02, n=4)
        for x in (-2.8, -1.4, 0.0, 1.4, 2.8):
            k.bar(st, (x, 1.0 + dy, PEAK + 0.2), (x, 1.0 + dy, PEAK + 1.2), 0.02, n=4)
    # Elevator tower: four legs, braced faces, the elevator casing inside, the head house and its spouts.
    tx, ty, TH, w = 0.0, -3.5, 16.0, 0.8
    levels = [0.0, 2.3, 4.6, 6.9, 9.2, 11.5, 13.8, TH]
    for sx in (-1, 1):
        for sy in (-1, 1):
            k.bar(st, (tx + sx * w, ty + sy * w, -0.1), (tx + sx * w, ty + sy * w, TH), 0.07, n=4)
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for z0, z1 in zip(levels, levels[1:]):
        for (ax, ay), (bx, by) in zip(corners, corners[1:] + corners[:1]):
            pa0, pb0 = Vector((tx + ax * w, ty + ay * w, z0)), Vector((tx + bx * w, ty + by * w, z0))
            pa1, pb1 = Vector((tx + ax * w, ty + ay * w, z1)), Vector((tx + bx * w, ty + by * w, z1))
            k.bar(st, pa0, pb1, 0.03, n=4, caps=False)
            k.bar(st, pa1, pb1, 0.035, n=4, caps=False)
    k.box(galv, (0.4, 0.5, TH), (tx - 0.25, ty, TH / 2))
    k.box(galv, (0.4, 0.5, TH), (tx + 0.25, ty, TH / 2))
    k.box(galv, (2.6, 2.6, 1.7), (tx, ty, TH + 0.85))
    k.hexa(M["roof_metal"], [(tx - 1.45, ty - 1.45, TH + 1.7), (tx + 1.45, ty - 1.45, TH + 1.7), (tx + 1.45, ty + 1.45, TH + 1.7),
                             (tx - 1.45, ty + 1.45, TH + 1.7), (tx - 0.05, ty - 1.45, TH + 2.2), (tx + 0.05, ty - 1.45, TH + 2.2),
                             (tx + 0.05, ty + 1.45, TH + 2.2), (tx - 0.05, ty + 1.45, TH + 2.2)])
    k.box(st, (0.8, 0.6, 0.6), (tx + 0.6, ty - 1.5, TH + 1.0))
    for top in tops:
        start = Vector((tx + math.copysign(1.0, top.x), ty + 1.0, TH + 1.3))
        k.bar(galv, start, top + Vector((0, 0, 0.05)), 0.13, n=8)
    k.box(M["concrete"], (3.4, 3.4, 0.3), (tx, ty, 0.0))
    k.box(st, (1.6, 2.2, 0.04), (tx, ty - 0.3, 0.16))
    k.build("SiloParts", r)
    return r


def build_water_tower():
    """Steel water tower, 16 m: four splayed tubular legs with ring girders and cross-bracing, a central riser pipe,
    a ladder to the balcony round the tank, and a riveted tank with a conical bottom and roof."""
    r = root("Scenery_WaterTower")
    k = Kit()
    st, gm = M["steel"], M["gunmetal"]
    TOP = 10.8

    def leg(sx, sy, z):
        w = 2.7 - (2.7 - 1.85) * z / TOP
        return Vector((sx * w, sy * w, z))

    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in corners:
        k.bar(st, leg(sx, sy, -0.1), leg(sx, sy, TOP + 0.05), 0.11, n=8)
        f = leg(sx, sy, 0)
        k.box(M["concrete"], (0.9, 0.9, 0.5), (f.x, f.y, 0.1))
    levels = [0.25, 3.7, 7.2, TOP - 0.05]
    for z0, z1 in zip(levels, levels[1:]):
        for (ax, ay), (bx, by) in zip(corners, corners[1:] + corners[:1]):
            k.bar(gm, leg(ax, ay, z0), leg(bx, by, z1), 0.025, n=4, caps=False)
            k.bar(gm, leg(bx, by, z0), leg(ax, ay, z1), 0.025, n=4, caps=False)
    for z in levels[1:]:
        for (ax, ay), (bx, by) in zip(corners, corners[1:] + corners[:1]):
            k.bar(st, leg(ax, ay, z), leg(bx, by, z), 0.06, n=6, caps=False)
    k.cyl(st, 0.3, 10.4, (0, 0, 5.2), n=10)
    k.lathe(M["rust"], [(10.3, 0.0), (10.75, 1.4), (11.2, 2.6), (14.6, 2.6), (14.62, 2.68), (14.7, 2.68), (15.9, 0.3), (16.0, 0.3),
                        (16.0, 0.0)], (0, 0, 0), n=24, recalc=False)
    for z in (12.3, 13.5):
        k.lathe(st, [(z, 2.62), (z + 0.06, 2.62)], (0, 0, 0), n=24, caps=False, recalc=False)
    k.lathe(st, [(11.0, 2.55), (11.0, 3.25), (11.08, 3.25), (11.08, 2.55)], (0, 0, 0), n=24, closed=True, recalc=False)
    ring = [Vector((3.2 * math.cos(2 * math.pi * i / 12), 3.2 * math.sin(2 * math.pi * i / 12), 12.1)) for i in range(13)]
    k.path(gm, ring, 0.025, n=4, caps=False)
    for p in ring[:-1]:
        k.bar(gm, (p.x, p.y, 11.08), p, 0.022, n=4, caps=False)
    for (ax, ay) in corners:
        p = leg(ax, ay, TOP)
        k.bar(gm, p, Vector((ax * 2.25, ay * 2.25, 11.0)), 0.04, n=4)
    # Ladders: up the front to the balcony, then up the tank.
    for x in (-0.22, 0.22):
        k.bar(gm, (x, -3.0, 0.0), (x, -3.0, 12.0), 0.022, n=4)
        k.bar(gm, (x, -2.72, 11.1), (x, -2.72, 14.7), 0.02, n=4)
    for i in range(32):
        z = 0.35 + i * 0.34
        k.bar(gm, (-0.22, -3.0, z), (0.22, -3.0, z), 0.014, n=4, caps=False)
    for i in range(10):
        z = 11.4 + i * 0.33
        k.bar(gm, (-0.22, -2.72, z), (0.22, -2.72, z), 0.014, n=4, caps=False)
    k.build("TowerParts", r)
    return r


def build_pylon():
    """High-voltage lattice pylon, 24 m: four legs tapering from a 5.6 m square base to a slim waist, X-braced
    faces, three pairs of lattice cross-arms (the middle pair longest) with insulator strings, and an earth-wire
    peak. Square angle members, no end caps, so it stays light."""
    r = root("Scenery_Pylon")
    k = Kit()
    g, gm = M["galv"], M["gunmetal"]
    WAIST, TOPZ = 15.4, 22.4

    def w(z):
        return 2.8 - 1.8 * z / WAIST if z <= WAIST else 1.0 - 0.3 * (z - WAIST) / (TOPZ - WAIST)

    def leg(sx, sy, z):
        return Vector((sx * w(z), sy * w(z), z))

    levels = [0.0, 3.2, 6.2, 8.9, 11.4, 13.6, WAIST, 17.0, 18.9, 20.6, TOPZ]
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in corners:
        k.path(g, [leg(sx, sy, z) for z in [-0.2] + levels[1:]], 0.075, n=4, caps=False)
        f = leg(sx, sy, 0.0)
        k.box(M["concrete"], (0.7, 0.7, 0.5), (f.x, f.y, 0.05))
    for z0, z1 in zip(levels, levels[1:]):
        for (ax, ay), (bx, by) in zip(corners, corners[1:] + corners[:1]):
            k.bar(g, leg(ax, ay, z0), leg(bx, by, z1), 0.032, n=4, caps=False)
            k.bar(g, leg(bx, by, z0), leg(ax, ay, z1), 0.032, n=4, caps=False)
    for z in levels[1:]:
        for (ax, ay), (bx, by) in zip(corners, corners[1:] + corners[:1]):
            k.bar(g, leg(ax, ay, z), leg(bx, by, z), 0.035, n=4, caps=False)
    for z in (WAIST, 18.9, TOPZ):
        k.bar(g, leg(-1, -1, z), leg(1, 1, z), 0.03, n=4, caps=False)
    # Cross-arms: two top chords and two bottom chords from the body to the tip, laced together.
    for z, reach in ((WAIST, 5.0), (18.9, 5.8), (TOPZ, 4.6)):
        zb = z - 1.5
        for s in (-1, 1):
            tip = Vector((s * reach, 0.0, z))
            tops = [leg(s, sy, z) for sy in (-1, 1)]
            bots = [leg(s, sy, zb) for sy in (-1, 1)]
            for p in tops + bots:
                k.bar(g, p, tip, 0.04, n=4, caps=False)
            for f in (0.35, 0.68):
                for tp, bp in zip(tops, bots):
                    a, b = tp.lerp(tip, f), bp.lerp(tip, f)
                    k.bar(g, a, b, 0.025, n=4, caps=False)
                    k.bar(g, a, bp.lerp(tip, f - 0.3), 0.022, n=4, caps=False)
                k.bar(g, tops[0].lerp(tip, f), tops[1].lerp(tip, f), 0.022, n=4, caps=False)
            # Insulator string and conductor clamp hanging from the tip.
            k.bar(gm, tip, tip + Vector((0, 0, -0.35)), 0.02, n=4)
            k.cyl(M["glass"], 0.13, 1.5, tip + Vector((0, 0, -1.15)), n=6)
            for dz in (-0.5, -0.9, -1.3, -1.7):
                k.cyl(M["glass"], 0.17, 0.05, tip + Vector((0, 0, dz)), n=6)
            k.box(gm, (0.12, 0.3, 0.1), tip + Vector((0, 0, -2.0)))
    for sx, sy in corners:
        k.bar(g, leg(sx, sy, TOPZ), Vector((0, 0, 24.0)), 0.04, n=4, caps=False)
    k.box(gm, (0.15, 0.4, 0.15), (0, 0, 24.0))
    k.build("PylonParts", r)
    return r


# ---------- props ----------

def build_dragon_teeth():
    """10 m of concrete anti-tank pyramids, 1.2 m tall, in two staggered rows on a shared footing."""
    r = root("Scenery_DragonTeeth")
    k = Kit()
    rng = random.Random(4)
    k.box(M["concrete_stained"], (10.4, 2.9, 0.35), (0, 0, -0.08))
    for y, count, x0 in ((-0.68, 8, -4.375), (0.68, 7, -3.75)):
        for i in range(count):
            s = rng.uniform(0.94, 1.05)
            base, top, h = 1.05 * s, 0.3 * s, 1.2 * s
            c = Vector((x0 + i * 1.25 + rng.uniform(-0.05, 0.05), y + rng.uniform(-0.05, 0.05), 0.08))
            R = rotm((rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03), rng.uniform(-0.08, 0.08)))
            pts = [(-base / 2, -base / 2, -0.18), (base / 2, -base / 2, -0.18), (base / 2, base / 2, -0.18), (-base / 2, base / 2, -0.18),
                   (-top / 2, -top / 2, h - 0.08), (top / 2, -top / 2, h - 0.08), (top / 2, top / 2, h - 0.08), (-top / 2, top / 2, h - 0.08)]
            k.hexa(M["concrete"], [c + R @ Vector(p) for p in pts])
    k.build("TeethParts", r)
    return r


def build_log_pile():
    """Felled logs, about 6 m long, stacked four high on two cross skids between stakes. Bark has its UVs wrapped
    round each log and running along it; the sawn ends are wood."""
    r = root("Scenery_LogPile")
    k = Kit()
    rng = random.Random(12)
    skid_r = 0.12
    for x in (-1.9, 1.9):
        k.cyl(M["bark"], skid_r, 3.6, (x, 0, skid_r), TO_BACK, n=8)
    logs, row = [], []
    y = 0.0
    for i in range(7):
        rr = rng.uniform(0.17, 0.24)
        if row:
            y += row[-1][2] + rr + 0.01
        row.append([y, 2 * skid_r + rr - 0.02, rr])
    shift = (row[0][0] + row[-1][0]) / 2
    for lg in row:
        lg[0] -= shift
    logs += row
    for _ in range(3):
        nxt = []
        for a, b in zip(row, row[1:]):
            rr = rng.uniform(0.16, 0.23)
            (ya, za, ra), (yb, zb, rb) = a, b
            d = math.dist((ya, za), (yb, zb))
            la, lb = ra + rr, rb + rr
            t = (la * la - lb * lb + d * d) / (2 * d)
            hgt = math.sqrt(max(0.0, la * la - t * t))
            ux, uz = (yb - ya) / d, (zb - za) / d
            nxt.append([ya + ux * t - uz * hgt, za + uz * t + ux * hgt, rr])
        row = nxt
        logs += row
    for i, (y, z, rr) in enumerate(logs):
        length = rng.uniform(5.6, 6.2)
        x0 = -length / 2 + rng.uniform(-0.2, 0.2)
        bark = M["bark_pine"] if rng.random() < 0.7 else M["bark"]
        prof = [(x0, 0.0), (x0, rr), (x0 + length, rr * rng.uniform(0.9, 1.0)), (x0 + length, 0.0)]
        lathe_uv_mesh(f"Log_{i}", prof, 8, [bark, M["wood_light"]], lambda n: 1 if abs(n.x) > 0.7 else 0, r,
                      loc=(0, y, z), rot=TO_X, axis=0, phase=rng.random())
    ymax = max(abs(y) + rr for y, z, rr in logs[:7])
    for e in (-1, 1):
        for x in (-1.9, 1.9):
            k.bar(M["wood"], (x, e * (ymax + 0.08), -0.2), (x + rng.uniform(-0.05, 0.05), e * (ymax + 0.1), 1.55), 0.06, n=6)
    k.build("PileParts", r)
    return r


def build_hut():
    """Small plank shed, 4 x 3 m, on stone blocks: battened walls, a corrugated pitched roof, a ledged door and a
    window at the front (-Y), a stove pipe, a step, firewood stacked along the east wall and a water butt."""
    r = root("Scenery_Hut")
    k = Kit()
    rng = random.Random(3)
    W, D, HW, F = 4.0, 3.0, 2.2, 0.35
    for sx in (-1, 0, 1):
        for sy in (-1, 1):
            k.box(M["stone"], (0.35, 0.35, 0.5), (sx * 1.75, sy * 1.25, 0.1))
    k.box(M["planks_dark"], (W + 0.1, D + 0.1, 0.14), (0, 0, F - 0.07))
    k.box(M["planks"], (W, D, HW), (0, 0, F + HW / 2))
    for i in range(10):
        x = -W / 2 + 0.2 + i * (W - 0.4) / 9
        for e in (-1, 1):
            if e < 0 and -1.3 < x < -0.3:
                continue
            k.box(M["planks_dark"], (0.06, 0.03, HW - 0.05), (x, e * (D / 2 + 0.015), F + HW / 2))
    for i in range(7):
        y = -D / 2 + 0.2 + i * (D - 0.4) / 6
        for s in (-1, 1):
            k.box(M["planks_dark"], (0.03, 0.06, HW - 0.05), (s * (W / 2 + 0.015), y, F + HW / 2))
    for sx in (-1, 1):
        for sy in (-1, 1):
            k.box(M["wood"], (0.12, 0.12, HW + 0.05), (sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), F + HW / 2))
    rise, ov = 0.9, 0.35
    top = F + HW
    for e in (-1, 1):
        a = (e * (D / 2 + ov), top - ov * rise / (D / 2))
        b = (0.0, top + rise)
        A, B = Vector((0, a[0], a[1])), Vector((0, b[0], b[1]))
        d = B - A
        n = Vector((0, -d.z, d.y)).normalized()
        if n.z < 0:
            n = -n
        k.plate(M["roof_barn"], [Vector((x, p.y, p.z)) + n * 0.03 for x, p in ((-W / 2 - ov, A), (W / 2 + ov, A), (W / 2 + ov, B), (-W / 2 - ov, B))], n, 0.06)
    for s in (-1, 1):
        k.loft(M["planks"], xprism([(-D / 2, top), (D / 2, top), (0, top + rise)], s * (W / 2 - 0.05), s * W / 2))
        for e in (-1, 1):  # barge boards up the gable's edges
            eave = Vector((s * (W / 2 + ov - 0.03), e * (D / 2 + ov), top - ov * rise / (D / 2) - 0.02))
            k.bar(M["wood"], eave, Vector((s * (W / 2 + ov - 0.03), 0, top + rise - 0.02)), 0.05, square=True)
    k.box(M["wood"], (W + 2 * ov, 0.12, 0.12), (0, 0, top + rise + 0.03))
    # Ledged door with a Z brace, a window, the step.
    k.box(M["planks_dark"], (0.95, 0.06, 1.9), (-0.8, -D / 2 - 0.04, F + 0.95))
    for z in (0.3, 1.6):
        k.box(M["wood"], (0.9, 0.04, 0.12), (-0.8, -D / 2 - 0.08, F + z))
    k.box(M["wood"], (0.08, 0.04, 1.4), (-0.8, -D / 2 - 0.08, F + 0.95), (0, math.radians(-30), 0))
    k.box(M["gunmetal"], (0.04, 0.05, 0.12), (-0.45, -D / 2 - 0.1, F + 1.0))
    wall_window(k, (0.9, -D / 2 - 0.03, F + 1.35), (0, -1), 0.7, 0.6, M["wood"], M["wood"])
    k.box(M["stone"], (1.0, 0.45, 0.3), (-0.8, -D / 2 - 0.3, 0.12))
    # Stove pipe through the roof.
    k.cyl(M["steel"], 0.07, 1.4, (1.1, 0.75, top + rise * 0.5 + 0.35), n=8)
    k.cyl(M["steel"], 0.13, 0.05, (1.1, 0.75, top + rise * 0.5 + 1.05), n=8)
    # Firewood along the east wall and a water butt at the back.
    for row in range(4):
        for i in range(6):
            y = -1.1 + i * 0.42 + (0.21 if row % 2 else 0)
            if y > 1.2:
                continue
            k.cyl(M["bark"], rng.uniform(0.08, 0.11), 0.5, (W / 2 + 0.3, y, 0.11 + row * 0.18), TO_X, n=6)
    slab(k, M["roof_barn"], (W / 2 - 0.02, 1.45), (W / 2 + 0.8, 1.12), -1.45, 1.45, 0.04)
    for e in (-1, 1):
        k.box(M["wood"], (0.07, 0.07, 1.1), (W / 2 + 0.72, e * 1.38, 0.55))
    k.cyl(M["wood"], 0.3, 0.9, (-W / 2 - 0.35, D / 2 - 0.2, 0.45), n=10)
    k.cyl(M["steel"], 0.31, 0.04, (-W / 2 - 0.35, D / 2 - 0.2, 0.75), n=10)
    k.build("HutParts", r)
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
    """Turns the model into the root empty plus a few meshes: each moving part (an empty named Turret or Barrel
    with everything built under it) becomes one mesh of the same name with the empty's origin and rotation, kept
    under the moving part it was built under (the APC's Barrel under its Turret) or straight under the root; all the
    rest becomes <root>_Mesh."""
    bpy.context.view_layer.update()

    def mover(o):
        p = o.parent
        while p is not None and p is not root_obj:
            if p.type == "EMPTY" and p.name in MOVING_PARTS:
                return p.name
            p = p.parent
        return None

    groups = {}
    for o in root_obj.children_recursive:
        if o.type == "MESH":
            groups.setdefault(mover(o), []).append(o)
    empties = [o for o in root_obj.children_recursive if o.type == "EMPTY" and o.name in MOVING_PARTS]
    parts = {}
    for e in empties:
        mine = groups[e.name]
        target = e.matrix_world.copy()
        part = join_objects(mine, mine[0])
        bpy.context.view_layer.update()
        part.data.transform(target.inverted() @ part.matrix_world)
        parts[e.name] = (part, target, mover(e))
    rest = groups[None]
    body = join_objects(rest, rest[0])
    bpy.context.view_layer.update()
    body.data.transform(root_obj.matrix_world.inverted() @ body.matrix_world)
    body.parent = root_obj
    body.matrix_parent_inverse = Matrix.Identity(4)
    body.matrix_basis = Matrix.Identity(4)
    for name, (part, target, up) in parts.items():
        parent, parent_world = (parts[up][0], parts[up][1]) if up else (root_obj, root_obj.matrix_world.copy())
        part.parent = parent
        part.matrix_parent_inverse = Matrix.Identity(4)
        part.matrix_basis = parent_world.inverted() @ target
    for o in [c for c in root_obj.children_recursive if c.type == "EMPTY"]:
        bpy.data.objects.remove(o, do_unlink=True)
    for name, (part, target, up) in parts.items():
        part.name = part.data.name = name
    body.name = body.data.name = root_obj.name + "_Mesh"
    bpy.context.view_layer.update()


def export_model(root_obj, path):
    """common.export, with objects two levels below the root counter-rotated for the export. With
    bake_space_transform, Blender's FBX exporter writes such an object's local transform as
    G T^-1 G^-1 T B G^-1 instead of G B G^-1 (T the parent's local matrix, B the object's, G the axis conversion),
    which turns it 90 degrees about X; giving the object T^-1 G T B for the export cancels that out."""
    G = axis_conversion(to_forward="-Z", to_up="Y").to_4x4()
    saved = []
    for o in root_obj.children_recursive:
        if o.parent is not None and o.parent is not root_obj:
            T, B = o.parent.matrix_local.copy(), o.matrix_local.copy()
            saved.append((o, o.matrix_parent_inverse.copy(), o.matrix_basis.copy()))
            o.matrix_parent_inverse = Matrix.Identity(4)
            o.matrix_basis = T.inverted() @ G @ T @ B
    bpy.context.view_layer.update()
    export(root_obj, path)
    for o, pinv, basis_ in saved:
        o.matrix_parent_inverse = pinv
        o.matrix_basis = basis_
    bpy.context.view_layer.update()


def mesh_bounds(objs):
    lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
    for o in objs:
        mw = o.matrix_world
        for v in o.data.vertices:
            p = mw @ v.co
            lo = Vector((min(lo[i], p[i]) for i in range(3)))
            hi = Vector((max(hi[i], p[i]) for i in range(3)))
    return lo, hi


def centre_footprint(root_obj, along_x=True):
    """Moves the model so its footprint (ignoring moving parts) is centred on the origin in Y, and in X too unless
    along_x is False (vehicles, built symmetric about X = 0, where a lopsided exhaust shouldn't move the pivots)."""
    bpy.context.view_layer.update()

    def moving(o):
        p = o.parent
        while p is not None:
            if p.name in MOVING_PARTS:
                return True
            p = p.parent
        return False

    objs = [o for o in root_obj.children_recursive if o.type == "MESH" and not moving(o)]
    lo, hi = mesh_bounds(objs)
    shift = Vector((-(lo.x + hi.x) / 2 if along_x else 0.0, -(lo.y + hi.y) / 2, 0))
    if shift.length < 0.005:
        return Vector((0, 0, 0))
    for o in root_obj.children:
        o.location += shift
    bpy.context.view_layer.update()
    return shift


def triangle_count(root_obj):
    total = 0
    for o in root_obj.children_recursive:
        if o.type == "MESH":
            o.data.calc_loop_triangles()
            total += len(o.data.loop_triangles)
    return total


def unity(p):
    return Vector((-p.x, p.z, -p.y))


def report(name, root_obj):
    meshes = [o for o in root_obj.children_recursive if o.type == "MESH"]
    lo, hi = mesh_bounds(meshes)
    size = hi - lo
    print(f"EXPORTED {name} ({triangle_count(root_obj)} triangles, {size.x:.2f} x {size.y:.2f} x {size.z:.2f} m, "
          f"z {lo.z:.2f}..{hi.z:.2f}, x {lo.x:.2f}..{hi.x:.2f}, y {lo.y:.2f}..{hi.y:.2f})")
    for o in meshes:
        if o.name in MOVING_PARTS:
            p = o.matrix_world.translation
            lp = o.matrix_local.translation
            u = unity(p)
            print(f"  PIVOT {o.name} under {o.parent.name}: Blender world ({p.x:.3f}, {p.y:.3f}, {p.z:.3f}), "
                  f"Unity model space ({u.x:.3f}, {u.y:.3f}, {u.z:.3f}), local to parent Unity ({-lp.x:.3f}, {lp.z:.3f}, {-lp.y:.3f})")
    for o in meshes:
        blo, bhi = mesh_bounds([o])
        print(f"  BOUNDS {o.name}: ({blo.x:.3f}, {blo.y:.3f}, {blo.z:.3f}) .. ({bhi.x:.3f}, {bhi.y:.3f}, {bhi.z:.3f})")
    return lo, hi


def previews(name, dist, tz, lo, hi):
    """The usual front-right preview, plus a back-left one (<name>_B.png). An orange 1.8 m post stands at the
    front-right corner for scale (preview only)."""
    fig = bpy.data.meshes.new("Figure")
    v, f = box_geo((0.45, 0.3, 1.8))
    fig.from_pydata(v, [], f)
    figure = bpy.data.objects.new("ScaleFigure", fig)
    bpy.context.collection.objects.link(figure)
    figure.location = (hi.x + 0.6, lo.y - 0.6, 0.9)
    figure.data.materials.append(material("PreviewFigure", (0.9, 0.3, 0.02), roughness=0.6))
    for m in bpy.data.materials:
        m.use_backface_culling = True  # as Unity draws them, so faces that look the wrong way show up as holes
    render_preview(os.path.join(PREVIEW_DIR, name + ".png"), dist, tz)
    cam = bpy.context.scene.camera
    cam.location = (-dist * 0.8, dist * 0.85, dist * 0.65 + tz)
    cam.rotation_euler = (Vector((0, 0, tz)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.render.filepath = os.path.join(PREVIEW_DIR, name + "_B.png")
    bpy.ops.render.render(write_still=True)


BUILDS = [
    ("Enemy_APC", build_apc, 11.0, 1.0),
    ("Enemy_Rocket", build_rocket, 11.0, 1.2),
    ("Enemy_Tanker", build_tanker, 12.0, 1.3),
    ("Enemy_Jammer", build_jammer, 22.0, 6.0),
    ("Enemy_Depot", build_depot, 14.0, 1.0),
    ("Scenery_RailTrack", build_rail_track, 6.5, 0.3),
    ("Scenery_RailWagon", build_wagon, 16.0, 2.2),
    ("Scenery_RailTankWagon", build_tank_wagon, 15.0, 2.2),
    ("Scenery_Station", build_station, 30.0, 3.0),
    ("Scenery_Warehouse", build_warehouse, 44.0, 3.0),
    ("Scenery_Factory", build_factory, 30.0, 4.0),
    ("Scenery_Chimney", build_chimney, 42.0, 14.0),
    ("Scenery_Silo", build_silo, 36.0, 9.0),
    ("Scenery_WaterTower", build_water_tower, 27.0, 8.0),
    ("Scenery_Pylon", build_pylon, 38.0, 12.0),
    ("Scenery_DragonTeeth", build_dragon_teeth, 10.0, 0.5),
    ("Scenery_LogPile", build_log_pile, 7.5, 0.6),
    ("Scenery_Hut", build_hut, 8.0, 1.5),
]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    only = set(args[2].split(",")) if len(args) > 2 and args[2] else None
    for name, build, cam_distance, target_z in BUILDS:
        if only and name not in only:
            continue
        reset_scene()
        M.clear()
        MARKS.clear()
        make_materials()
        r = build()
        moving = any(o.type == "EMPTY" and o.name in MOVING_PARTS for o in r.children_recursive)
        # Vehicles are built symmetric about X = 0; the station is centred on its building and platform (its
        # entrance steps stick out), so its track line stays at STATION_TRACK_X.
        shift = centre_footprint(r, along_x=not moving and name not in ("Enemy_Tanker", "Scenery_Station"))
        if moving:
            box_uv_all(r)  # real-scale UVs for Unity's photo textures
            flatten(r)
        else:
            join_meshes(r)
        bpy.context.view_layer.update()
        lo, hi = report(name, r)
        for label, p, n in MARKS:
            u = unity(p + shift)
            text = f"  MARK {label}: Unity ({u.x:.2f}, {u.y:.2f}, {u.z:.2f})"
            if n is not None:
                un = unity(n)
                text += f" facing ({un.x:.2f}, {un.y:.2f}, {un.z:.2f})"
            print(text + ("  [Turret/Barrel rest pose]" if "muzzle" in label else ""))
        export_model(r, os.path.join(OUT_DIR, name + ".fbx"))
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            previews(name, cam_distance, target_z, lo, hi)
            print(f"PREVIEW {name}")


main()
