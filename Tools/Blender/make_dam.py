"""Builds the reservoir dam (Docs/Concept/dam_spillway.png) in Blender and exports it as an FBX for Unity.

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_dam.py -- Assets/DroneSandbox/Resources/Models [preview_dir]

A curved concrete gravity dam about 72 m long and 15 m high. The deck road bows upstream, an arcade of
arched bays runs under its downstream edge, and a stepped spillway in the middle carries a sheet of white
water from the lake down to the river. Stone towers with tiled roofs stand at both ends.

Axes: X along the dam, -Y downstream (Unity +Z), +Y towards the lake, Z up. The origin is the middle of the
dam's foot. The game places it so LAKE lines up with its lake level. Empties named "LampPoint_N" mark the
lamp heads, so the game can hang night lights there. The water sheet keeps its own UVs (u across, v
downhill, in metres) for the WhiteWater shader, and its vertex colour says how much foam there is: most where
the water falls off each step and lands on the next.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import SECOND_UV, box, cone, cylinder, export, hexa, join_meshes, material, mesh_object, render_preview, reset_scene, root, strut  # noqa: E402

import bpy  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 else None

H = 15.0          # top of the road deck, above the dam's foot
SPILL = 12.5      # spillway crest: the lake spills over here
LAKE = 12.9       # lake level the model is drawn for
A = 36.0          # half the dam's length
SAG = 5.0         # how far the dam bows towards the lake at its middle
SEG = 6.0         # width of one bay
SPILL_HALF = 6.0  # the spillway is the middle two bays
STEPS = 6
RISE = (SPILL - 0.6) / STEPS
TREAD = 9.0 / STEPS
ABUT = 8.0        # solid blocks past each end, buried in the hillside
SLOPE = 6.5 / (H - 1.0)  # how far the downstream face leans out per metre down
FOOT = -3.0       # the structure goes on below its nominal foot, so no gap shows where the riverbed dips

M = {}


def make_materials():
    M["concrete"] = material("DamConcrete", (0.55, 0.54, 0.5), roughness=0.85)  # weathered by height (ProjectSetup)
    M["deck"] = material("Concrete", (0.6, 0.6, 0.58), roughness=0.8)
    M["stone"] = material("Stone", (0.62, 0.58, 0.52), roughness=0.85)
    M["roof"] = material("RoofTile", (0.55, 0.22, 0.14), roughness=0.7)
    M["rail"] = material("Galvanized", (0.6, 0.62, 0.64), metallic=0.8, roughness=0.4)
    M["steel"] = material("SteelDark", (0.15, 0.16, 0.17), metallic=0.7, roughness=0.45)
    M["road"] = material("Asphalt", (0.12, 0.12, 0.12), roughness=0.9)
    M["window"] = material("Window", (0.02, 0.025, 0.03), metallic=0.3, roughness=0.1)
    M["door"] = material("DoorWood", (0.25, 0.17, 0.12), roughness=0.8)
    M["lamp"] = material("LampGlass", (1.0, 0.9, 0.7), emission=(1.0, 0.85, 0.6))
    M["water"] = material("WhiteWater", (0.82, 0.9, 0.92), roughness=0.6)     # churned foam where the water lands
    M["sheet"] = material("SpillSheet", (0.3, 0.45, 0.48), roughness=0.25)    # the falling sheets, streaked with foam
    M["flow"] = material("SpillWater", (0.03, 0.1, 0.11), roughness=0.05)      # dark water running over the treads


def yc(x):
    """How far the dam's centre line sits towards the lake at x along it (the arch)."""
    x = max(-A, min(A, x))
    return SAG * (1.0 - (x / A) ** 2)


def body(name, x0, x1, top, front_top, mat, parent):
    """A bay of the dam: upstream face upright, downstream face leaning out to the foot."""
    a, b = yc(x0), yc(x1)
    foot = front_top - SLOPE * (top - FOOT)
    corners = [(x0, a + foot, FOOT), (x1, b + foot, FOOT), (x1, b + 2.5, FOOT), (x0, a + 2.5, FOOT),
               (x0, a + front_top, top), (x1, b + front_top, top), (x1, b + 2.5, top), (x0, a + 2.5, top)]
    return hexa(name, corners, mat, parent, bevel=0.05)


def cut(obj, cutter):
    """Boolean difference, then the cutter is thrown away."""
    mod = obj.modifiers.new("Cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)


def arcade(i, x0, x1, parent):
    """A wall in front of the leaning face, under the deck's overhang, pierced by one round arch."""
    mid = (yc(x0) + yc(x1)) / 2.0
    left, right = x0 + 0.7, x1 - 0.7
    bottom = H - 1.0 - (6.9 - 3.5) / SLOPE - 0.2  # where the wall meets the leaning face
    wall = box(f"Arcade_{i}", (right - left, 0.8, H - 1.0 - bottom), ((left + right) / 2, mid - 6.5, (H - 1.0 + bottom) / 2),
               M["concrete"], parent, bevel=0)
    radius = (right - left) / 2.0 - 0.45
    spring = H - 4.0
    hole = box(f"ArchHole_{i}", (radius * 2, 3.0, spring - bottom + 0.4), ((left + right) / 2, mid - 6.5, (spring + bottom) / 2 - 0.2),
               M["concrete"], None, bevel=0)
    top = cylinder(f"ArchTop_{i}", radius, 3.0, ((left + right) / 2, mid - 6.5, spring), M["concrete"], None,
                   rot=(math.radians(90), 0, 0), verts=32, bevel=0)
    cut(wall, hole)  # one cutter at a time: a joined, overlapping cutter isn't a clean solid
    cut(wall, top)
    # A keystone and a string course along the top of the arcade.
    box(f"Keystone_{i}", (0.7, 0.3, 0.9), ((left + right) / 2, mid - 6.95, spring + radius + 0.1), M["stone"], parent, bevel=0.03)


def pier(i, xb, parent):
    """A buttress at a bay joint, leaning with the face, its front flush with the arcade."""
    a = yc(xb)
    lean = SLOPE * -FOOT
    corners = [(xb - 0.7, a - 11.0 - lean, FOOT), (xb + 0.7, a - 11.0 - lean, FOOT), (xb + 0.7, a - 8.5 - lean, FOOT), (xb - 0.7, a - 8.5 - lean, FOOT),
               (xb - 0.7, a - 6.95, H - 1.0), (xb + 0.7, a - 6.95, H - 1.0), (xb + 0.7, a - 3.5, H - 1.0), (xb - 0.7, a - 3.5, H - 1.0)]
    hexa(f"Pier_{i}", corners, M["concrete"], parent, bevel=0.06)


def deck(i, x0, x1, parent):
    a, b = yc(x0), yc(x1)
    hexa(f"Deck_{i}", [(x0, a - 7.2, H - 1.0), (x1, b - 7.2, H - 1.0), (x1, b + 2.8, H - 1.0), (x0, a + 2.8, H - 1.0),
                       (x0, a - 7.2, H), (x1, b - 7.2, H), (x1, b + 2.8, H), (x0, a + 2.8, H)], M["deck"], parent, bevel=0.04)
    hexa(f"Road_{i}", [(x0, a - 2.0, H), (x1, b - 2.0, H), (x1, b + 2.2, H), (x0, a + 2.2, H),
                       (x0, a - 2.0, H + 0.04), (x1, b - 2.0, H + 0.04), (x1, b + 2.2, H + 0.04), (x0, a + 2.2, H + 0.04)],
         M["road"], parent, bevel=0)
    # A cornice under the downstream edge and railings along both edges.
    hexa(f"Cornice_{i}", [(x0, a - 7.45, H - 1.35), (x1, b - 7.45, H - 1.35), (x1, b - 7.0, H - 1.35), (x0, a - 7.0, H - 1.35),
                          (x0, a - 7.45, H - 0.95), (x1, b - 7.45, H - 0.95), (x1, b - 7.0, H - 0.95), (x0, a - 7.0, H - 0.95)],
         M["stone"], parent, bevel=0.03)
    for edge in (-7.0, 2.6):
        p0, p1 = (x0, a + edge, H), (x1, b + edge, H)
        cylinder(f"Post_{i}_{edge}", 0.05, 1.1, (x0, a + edge, H + 0.55), M["rail"], parent, verts=8, bevel=0)
        cylinder(f"PostMid_{i}_{edge}", 0.04, 1.1, ((x0 + x1) / 2, (a + b) / 2 + edge, H + 0.55), M["rail"], parent, verts=8, bevel=0)
        for z in (0.55, 1.05):
            strut(f"Rail_{i}_{edge}_{z}", (p0[0], p0[1], H + z), (p1[0], p1[1], H + z), 0.035, M["rail"], parent, verts=8)


def lamp(k, xb, parent):
    a = yc(xb) - 6.8
    cylinder(f"LampPole_{k}", 0.09, 6.0, (xb, a, H + 3.0), M["steel"], parent, verts=10, bevel=0)
    strut(f"LampArm_{k}", (xb, a, H + 5.8), (xb, a + 0.9, H + 6.1), 0.04, M["steel"], parent)
    box(f"LampHead_{k}", (0.35, 0.5, 0.18), (xb, a + 1.0, H + 6.05), M["steel"], parent, bevel=0.02)
    box(f"LampGlass_{k}", (0.28, 0.42, 0.04), (xb, a + 1.0, H + 5.95), M["lamp"], parent, bevel=0)
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=(xb, a + 1.0, H + 5.85))
    point = bpy.context.active_object
    point.name = f"LampPoint_{k}"
    point.parent = parent


def spillway(parent):
    """The middle two bays: a rounded crest the lake spills over, steps down the face, and side walls."""
    y0 = yc(SPILL_HALF / 2)
    box("SpillCrest", (2 * SPILL_HALF, 3.5, SPILL - FOOT), (0, y0 + 0.75, (SPILL + FOOT) / 2), M["concrete"], parent, bevel=0.3)
    for k in range(1, STEPS + 1):
        front = y0 - 1.0 - k * TREAD
        z = SPILL - k * RISE
        box(f"Step_{k}", (2 * SPILL_HALF, (y0 - 1.0) - front + 0.02, z - FOOT), (0, (front + y0 - 1.0) / 2, (z + FOOT) / 2), M["concrete"], parent, bevel=0.04)
    for side in (-1, 1):
        x_in, x_out = side * (SPILL_HALF - 0.4), side * (SPILL_HALF + 0.4)
        lo, hi = sorted((x_in, x_out))
        front = y0 - 1.0 - STEPS * TREAD - 0.6
        hexa(f"SpillWall_{side}", [(lo, front, FOOT), (hi, front, FOOT), (hi, y0 - 1.0, FOOT), (lo, y0 - 1.0, FOOT),
                                   (lo, front, 2.4), (hi, front, 2.4), (hi, y0 - 1.0, SPILL + 1.8), (lo, y0 - 1.0, SPILL + 1.8)],
             M["concrete"], parent, bevel=0.05)
        box(f"SpillWallTop_{side}", (0.8, 3.5, H - 1.0 - FOOT), (side * SPILL_HALF, y0 + 0.75, (H - 1.0 + FOOT) / 2), M["concrete"], parent, bevel=0.05)
    # Piers carrying the road over the spillway, with rounded noses facing the lake.
    for x in (-2.0, 2.0):
        box(f"SpillPier_{x}", (1.0, 3.5, H - 1.0 - SPILL + 0.2), (x, y0 + 0.75, (H - 1.0 + SPILL) / 2 - 0.1), M["concrete"], parent, bevel=0.05)
        cylinder(f"SpillNose_{x}", 0.5, H - 1.0 - SPILL + 0.2, (x, y0 + 2.5, (H - 1.0 + SPILL) / 2 - 0.1), M["concrete"], parent, verts=16)
    return y0


POOL = 0.3  # the river's surface at the dam's foot, above the model's foot (lake 12 m, river -0.6 m in MapLayout)


def water_sheet(y0, parent):
    """The water from the lake over the crest and down the steps into the pool: one mesh with three materials, so
    it reads as water doing different things rather than a white staircase (UVs in metres, v down the flow).
    - SpillWater: dark, glassy water a few centimetres deep, over the crest and running across each tread,
      including the pocket of dark aerated water under each falling sheet.
    - SpillSheet: at every lip the water breaks into 9-14 overlapping strands of different widths, a few with gaps
      beside them where the wet concrete shows, each thrown its own distance out and curving down (a parabola)
      onto the next tread, its edges ragged and wobbling more as it falls. Each strand curls over the lip as glassy
      dark water, turns streaky as air gets into it, and is white churn by the time it lands: its second UV set
      (UVFall) runs down a picture of that change (v from 1 at the lip to 0 where it lands, u a random slice of
      it), so it changes smoothly and raggedly; the first UV set carries the streaks that move down the fall.
    - WhiteWater: churned foam where each strand lands, a low mound on the tread, and a boil of foam where the last
      fall hits the pool, sinking under the river's surface at its edges so it ends raggedly.
    Every strand, landing and step gets its own random numbers and texture offsets, so nothing repeats."""
    rng = random.Random(3)
    left, right = -SPILL_HALF + 0.4, SPILL_HALF - 0.4
    verts, faces, uvs, falls, kinds = [], [], [], [], []
    FLOW, SHEET, FOAM = 0, 1, 2

    def patch(kind, rows, u_offset=0.0, v_offset=0.0, fall=None):
        """A grid of quads through rows of points (each row runs across the flow), UVs in metres; fall(j, i) gives
        a vertex's second UVs (the falling sheets)."""
        start = len(verts)
        n = len(rows[0])
        along = [0.0]
        for j in range(1, len(rows)):
            a, b = rows[j - 1][n // 2], rows[j][n // 2]
            along.append(along[-1] + math.dist(a, b))
        for j, row in enumerate(rows):
            for i, p in enumerate(row):
                verts.append(p)
                uvs.append((p[0] + u_offset, along[j] + v_offset))
                falls.append(fall(j, i) if fall else (0.0, 0.0))
        for j in range(len(rows) - 1):
            for i in range(n - 1):
                a = start + j * n + i
                faces.append((a, a + n, a + n + 1, a + 1))
                kinds.append(kind)

    def lip(k):
        """Where lip k is: 0 is the crest, 1-6 the steps' front edges."""
        return y0 - 1.0 - k * TREAD, SPILL - k * RISE

    xs = [left + i * (right - left) / 12 for i in range(13)]

    # Over the crest: from the lake, down the rounded crest, thinning towards the lip.
    crest = [(y0 + 3.0, LAKE - 0.05), (y0 + 1.2, SPILL + 0.4), (y0 + 0.1, SPILL + 0.2), (y0 - 0.9, SPILL + 0.09)]
    patch(FLOW, [[(x, y, z) for x in xs] for y, z in crest])

    # Each tread's running water, from the foot of the riser (under the falling sheet) to just past the lip.
    for k in range(1, STEPS + 1):
        back, _ = lip(k - 1)
        front, top = lip(k)
        rows = []
        for t in (0.0, 0.3, 0.6, 0.85, 1.0):
            y = back - 0.02 + (front + 0.08 - back + 0.02) * t
            rows.append([(x, y, top + 0.05 + 0.03 * t) for x in xs])
        patch(FLOW, rows, rng.uniform(0, 20), rng.uniform(0, 20))

    # The falls: from every lip, strands of water thrown out and down onto the next tread (the last into the pool).
    for k in range(STEPS + 1):
        y_lip, z_lip = lip(k)
        last = k == STEPS
        land = POOL if last else z_lip - RISE + 0.05
        next_lip = None if last else lip(k + 1)[0]
        count = rng.randint(9, 14)
        weights = [rng.uniform(0.6, 1.4) for _ in range(count)]
        total = sum(weights)
        x = left
        for w in weights:
            xa, xb = x, x + (right - left) * w / total
            x = xb
            # Strands overlap their neighbours a little, except the few that leave a gap beside them, where the wet
            # concrete behind shows through.
            gap = rng.random() < 0.2
            xa -= 0.06
            xb = xb - rng.uniform(0.06, 0.16) if gap else xb + 0.06
            width = xb - xa
            throw = rng.uniform(0.25, 0.45) if last else rng.uniform(0.35, 0.8)
            drop = z_lip + 0.08 - land
            rows = []
            for j in range(13):
                t = j / 12
                y = y_lip + 0.12 - (0.12 + throw) * t
                z = z_lip + 0.08 - drop * t * t
                ragged = 0.05 + 0.1 * t  # the sheet frays as it falls
                neck = width * 0.08 * math.sin(math.pi * t) if gap else 0.0
                row = []
                for i in range(4):
                    f = i / 3
                    px = xa + width * f
                    if i == 0:
                        px += neck + rng.uniform(0, ragged)
                    elif i == 3:
                        px -= neck + rng.uniform(0, ragged)
                    # Rounded across (the middle bulges out a little) and wobbling as it falls.
                    bulge = 0.04 * math.sin(math.pi * f)
                    wobble = rng.uniform(-0.03, 0.03) * t
                    row.append((px, y - bulge + wobble, z + wobble))
                rows.append(row)
            # Where it turns from glassy to streaky to white differs from strand to strand.
            u0, stretch, shift = rng.uniform(0.0, 0.8), rng.uniform(0.85, 1.1), rng.uniform(-0.08, 0.06)
            patch(SHEET, rows, rng.uniform(0, 40), rng.uniform(0, 40),
                  fall=lambda j, i, u0=u0, stretch=stretch, shift=shift: (u0 + 0.2 * i / 3, 1.0 - (j / 12) * stretch - shift))

            # Churned foam where it lands: a low, lumpy mound across the strand, ending short of the next lip.
            if last:
                continue
            y_hit = y_lip - throw
            length = min(rng.uniform(0.45, 0.8), y_hit - 0.12 - (next_lip + 0.15))
            height = rng.uniform(0.15, 0.3)
            rows = []
            for j in range(5):
                v = j / 4
                y = y_hit + 0.15 - (0.15 + length) * v
                row = []
                for i in range(5):
                    px = xa - 0.12 + (width + 0.24) * i / 4
                    edge = 1.0 - abs(i / 2 - 1.0) * 0.6
                    z = land + height * math.sin(math.pi * min(1.0, v * 1.15)) ** 0.7 * edge + rng.uniform(-0.03, 0.04)
                    row.append((px, y + rng.uniform(-0.04, 0.04), z))
                rows.append(row)
            patch(FOAM, rows, rng.uniform(0, 30), rng.uniform(0, 30))

    # The boil where the last fall hits the pool: lumpy foam that sinks below the river's surface towards its
    # edges and downstream, so it ends in a ragged line.
    y_end = lip(STEPS)[0]
    rows = []
    for j in range(8):
        v = j / 7
        y = y_end - 0.15 - 4.8 * v
        row = []
        for i in range(15):
            u = i / 14
            px = left - 0.8 + (right - left + 1.6) * u
            side = abs(u - 0.5) * 2
            z = POOL + 0.22 - 0.4 * v ** 1.4 - 0.3 * side ** 3 + rng.uniform(-0.14, 0.14)
            row.append((px, y + rng.uniform(-0.15, 0.15), z))
        rows.append(row)
    patch(FOAM, rows, rng.uniform(0, 30), rng.uniform(0, 30))

    obj = mesh_object("SpillwayWater", verts, faces, M["flow"], parent, smooth=True)
    obj.data.materials.append(M["sheet"])
    obj.data.materials.append(M["water"])
    obj["keep_uv"] = True
    for poly, kind in zip(obj.data.polygons, kinds):
        poly.material_index = kind
    uv = obj.data.uv_layers.new(name="UVMap")
    fall_uv = obj.data.uv_layers.new(name=SECOND_UV)
    for poly in obj.data.polygons:
        for li in poly.loop_indices:
            vi = obj.data.loops[li].vertex_index
            uv.data[li].uv = uvs[vi]
            fall_uv.data[li].uv = falls[vi]
    obj.data.uv_layers.active = uv
    return obj


def tower(name, x, size, height, parent):
    """A stone gatehouse at the end of the dam, beside the road, with a tiled hip roof."""
    y = yc(x) - 4.5
    base = 4.0
    box(f"{name}_Body", (size, size, height - base), (x, y, (height + base) / 2), M["stone"], parent, bevel=0.06)
    box(f"{name}_Plinth", (size + 0.4, size + 0.4, 1.2), (x, y, H + 0.6), M["stone"], parent, bevel=0.05)
    box(f"{name}_Cornice", (size + 0.6, size + 0.6, 0.45), (x, y, height + 0.2), M["stone"], parent, bevel=0.05)
    roof = cone(f"{name}_Roof", size * 0.82, 0.15, size * 0.5, (x, y, height + 0.42 + size * 0.25), M["roof"], parent, verts=4)
    roof.rotation_euler[2] = math.radians(45)
    half = size / 2
    for floor in (H + 2.2, H + 5.4):
        if floor + 1.2 > height:
            continue
        for k, (dx, dy, rx) in enumerate([(0, -half, 0), (0, half, 0), (-half, 0, 1), (half, 0, 1)]):
            for off in (-size * 0.22, size * 0.22):
                px = x + dx + (off if rx == 0 else 0)
                py = y + dy + (off if rx == 1 else 0)
                w = (1.0, 0.12, 1.5) if rx == 0 else (0.12, 1.0, 1.5)
                box(f"{name}_Win_{floor}_{k}_{off}", w, (px, py, floor + 0.75), M["window"], parent, bevel=0)
                sill = (1.3, 0.3, 0.15) if rx == 0 else (0.3, 1.3, 0.15)
                box(f"{name}_Sill_{floor}_{k}_{off}", sill, (px, py, floor - 0.05), M["stone"], parent, bevel=0.02)
    box(f"{name}_Door", (1.4, 0.15, 2.4), (x, y + half, H + 1.2), M["door"], parent, bevel=0)


def build_dam():
    r = root("Scenery_Dam")
    # Bays left of the spillway, the spillway, bays right of it, then the buried abutments.
    edges = [-A + i * SEG for i in range(int(2 * A / SEG) + 1)]
    for i in range(len(edges) - 1):
        x0, x1 = edges[i], edges[i + 1]
        deck(i, x0, x1, r)
        if abs((x0 + x1) / 2) < SPILL_HALF:
            continue
        body(f"Body_{i}", x0, x1, H - 1.0, -3.5, M["concrete"], r)
        arcade(i, x0, x1, r)
    for k, xb in enumerate(edges):
        if abs(xb) < SPILL_HALF - 0.1:
            continue  # nothing standing in the spillway's water
        pier(k, xb, r)
    for side in (-1, 1):
        x0, x1 = sorted((side * A, side * (A + ABUT)))
        body(f"Abutment_{side}", x0, x1, H - 1.0, -3.5, M["concrete"], r)
        deck(f"A{side}", x0, x1, r)
    y0 = spillway(r)
    for k, xb in enumerate(edges[1:-1:2]):
        lamp(k, xb, r)
    tower("TowerWest", -(A + 4.0), 6.5, H + 7.5, r)
    tower("TowerEast", A + 4.5, 8.0, H + 9.5, r)
    water_sheet(y0, r)
    return r


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    reset_scene()
    M.clear()
    make_materials()
    r = build_dam()
    join_meshes(r)
    export(r, os.path.join(OUT_DIR, "Scenery_Dam.fbx"))
    print("EXPORTED Scenery_Dam")
    if PREVIEW_DIR:
        os.makedirs(PREVIEW_DIR, exist_ok=True)
        render_preview(os.path.join(PREVIEW_DIR, "Scenery_Dam.png"), 62.0, 7.0)
        print("PREVIEW Scenery_Dam")


main()
