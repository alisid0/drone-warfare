"""Builds the countryside: trees, bushes, hay bales, village buildings, fences, the river bridge and boulders,
and exports each as an FBX for Unity. Modelled after the concept art in Docs/Concept (assets.png, map.png).

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_scenery.py -- Assets/DroneSandbox/Resources/Models [preview_dir] [only]

Each model is merged into one mesh so the hundreds of trees on the map stay cheap to draw.
Fronts (doors) face Blender's -Y, which is +Z in Unity.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (box, cylinder, export, gable_roof, join_meshes, leaf_cards, lumpy, material,  # noqa: E402
                    mesh_object, render_preview, reset_scene, root, strut)

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 else None

M = {}


def make_materials():
    # Linear colours, as Unity's URP Lit reads them.
    M["bark"] = material("Bark", (0.06, 0.045, 0.03), roughness=1.0)
    M["bark_pine"] = material("BarkPine", (0.16, 0.065, 0.025), roughness=1.0)
    M["bark_birch"] = material("BarkBirch", (0.55, 0.54, 0.50), roughness=0.9)
    M["pine"] = material("PineNeedles", (0.022, 0.05, 0.022), roughness=0.95)
    M["pine_light"] = material("PineNeedlesLight", (0.035, 0.075, 0.028), roughness=0.95)
    M["spruce"] = material("Spruce", (0.016, 0.038, 0.02), roughness=0.95)
    M["spruce_light"] = material("SpruceLight", (0.026, 0.056, 0.026), roughness=0.95)
    M["oak"] = material("OakLeaves", (0.04, 0.085, 0.018), roughness=0.9)
    M["oak_light"] = material("OakLeavesLight", (0.08, 0.15, 0.028), roughness=0.9)
    M["birch"] = material("BirchLeaves", (0.10, 0.19, 0.035), roughness=0.9)
    M["poplar"] = material("PoplarLeaves", (0.045, 0.10, 0.022), roughness=0.9)
    M["bush"] = material("BushLeaves", (0.05, 0.10, 0.02), roughness=0.9)
    M["bush_light"] = material("BushLeavesLight", (0.09, 0.16, 0.03), roughness=0.9)
    # Leaf cards: Unity swaps in alpha-cut leaf-cluster textures for these (ProjectSetup.LeafCardMaterials).
    M["oak_cards"] = material("OakCards", (0.06, 0.12, 0.025), roughness=0.9)
    M["birch_cards"] = material("BirchCards", (0.10, 0.19, 0.035), roughness=0.9)
    M["poplar_cards"] = material("PoplarCards", (0.05, 0.11, 0.025), roughness=0.9)
    M["bush_cards"] = material("BushCards", (0.07, 0.13, 0.025), roughness=0.9)
    M["pine_cards"] = material("PineCards", (0.03, 0.065, 0.025), roughness=0.9)
    M["hay"] = material("Hay", (0.40, 0.29, 0.10), roughness=1.0)
    M["hay_end"] = material("HayEnd", (0.30, 0.20, 0.07), roughness=1.0)
    M["plaster"] = material("PlasterWhite", (0.62, 0.60, 0.55), roughness=0.95)
    M["plaster_grey"] = material("PlasterGrey", (0.36, 0.36, 0.34), roughness=0.95)
    M["stone"] = material("Stone", (0.20, 0.19, 0.17), roughness=1.0)
    M["roof_tile"] = material("RoofTile", (0.32, 0.075, 0.025), roughness=0.85)
    M["roof_metal"] = material("RoofMetal", (0.22, 0.24, 0.25), metallic=0.6, roughness=0.45)
    M["roof_barn"] = material("RoofBarn", (0.12, 0.11, 0.10), metallic=0.3, roughness=0.8)
    M["spire"] = material("Spire", (0.06, 0.07, 0.08), metallic=0.6, roughness=0.4)
    M["brick"] = material("Brick", (0.24, 0.07, 0.035), roughness=0.9)
    M["frame"] = material("WindowFrame", (0.65, 0.64, 0.60), roughness=0.6)
    M["glass"] = material("WindowGlass", (0.02, 0.025, 0.03), metallic=0.3, roughness=0.1)
    M["door"] = material("DoorWood", (0.10, 0.05, 0.025), roughness=0.8)
    M["planks"] = material("WeatheredPlanks", (0.13, 0.10, 0.075), roughness=1.0)
    M["planks_dark"] = material("WeatheredPlanksDark", (0.07, 0.055, 0.04), roughness=1.0)
    M["fence"] = material("FenceWood", (0.15, 0.11, 0.075), roughness=1.0)
    M["concrete"] = material("Concrete", (0.30, 0.29, 0.27), roughness=0.95)
    M["asphalt"] = material("Asphalt", (0.05, 0.05, 0.05), roughness=0.9)
    M["railing"] = material("Railing", (0.22, 0.24, 0.26), metallic=0.7, roughness=0.4)
    M["rock"] = material("Rock", (0.075, 0.072, 0.066), roughness=0.95)
    M["moss"] = material("Moss", (0.05, 0.08, 0.015), roughness=1.0)
    M["interior"] = material("Interior", (0.01, 0.01, 0.01), roughness=1.0)


# ---------- trees and plants ----------

def crown(prefix, rng, count, center, spread, height, radius, mats, scale_z=0.8, strength=0.3,
          cards=None, per_clump=14, card_size=1.5):
    """Leafy clumps piled into a crown; the lighter material goes on top where the sun hits. With a card
    material, the blobs shrink into a dark inner mass and leaf cards give the crown a leafy edge."""
    cx, cy, cz = center
    clumps = []
    for k in range(count):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0, spread)
        clumps.append((math.cos(a) * d + cx, math.sin(a) * d + cy, cz + rng.uniform(-height / 2, height / 2)))
    clumps.sort(key=lambda p: p[2])
    placed = []
    for k, loc in enumerate(clumps):
        mat = mats[1] if k >= count * 0.55 else mats[0]
        rad = radius * rng.uniform(0.75, 1.15)
        core = 0.78 if cards else 1.0
        lumpy(f"{prefix}_{k}", rad * core, loc, mat, R[0], scale=(1, 1, scale_z), strength=strength * rad, subdiv=3, smooth=True)
        placed.append((loc[0], loc[1], loc[2], rad * 1.05))
    if cards:
        leaf_cards(f"{prefix}_Cards", placed, per_clump, card_size, cards, R[0], rng, center)


def build_pine(name, seed):
    """Scots pine: a tall bare orange trunk with flat clumps of needles at the top."""
    rng = random.Random(seed)
    r = root(name)
    R[0] = r
    top = rng.uniform(8.0, 9.5)
    clumps = []
    cylinder("Trunk", 0.2, top - 1.0, (0, 0, (top - 1.0) / 2), M["bark_pine"], r, verts=10)
    for k in range(6):
        a = rng.uniform(0, 2 * math.pi)
        z = rng.uniform(top * 0.55, top - 1.2)
        reach = rng.uniform(0.9, 1.7)
        end = (math.cos(a) * reach, math.sin(a) * reach, z + 0.5)
        strut(f"Branch_{k}", (0, 0, z), end, 0.06, M["bark_pine"], r)
        rad = rng.uniform(0.8, 1.2)
        lumpy(f"Needles_{k}", rad * 0.8, end, M["pine"] if k % 2 else M["pine_light"], r,
              scale=(1.2, 1.2, 0.55), strength=0.35, subdiv=3, smooth=True)
        clumps.append((*end, rad * 1.1))
    lumpy("Top", 0.85, (0, 0, top - 0.4), M["pine_light"], r, scale=(1.1, 1.1, 0.7), strength=0.3, subdiv=3, smooth=True)
    clumps.append((0, 0, top - 0.3, 1.0))
    leaf_cards("NeedleCards", clumps, 13, 1.3, M["pine_cards"], r, rng, (0, 0, top - 2.0), up=1.2)
    return r


def star_cone(name, radius, depth, z, mat, parent, points=9, droop=0.25):
    """A jagged cone, its rim alternating long and short points and drooping: one tier of a spruce."""
    verts = [(0, 0, z + depth)]
    for k in range(points * 2):
        a = math.pi * k / points
        rr = radius if k % 2 == 0 else radius * 0.72
        verts.append((math.cos(a) * rr, math.sin(a) * rr, z - (droop if k % 2 == 0 else 0)))
    faces = [(0, 1 + k, 1 + (k + 1) % (points * 2)) for k in range(points * 2)]
    faces.append(tuple(1 + k for k in reversed(range(points * 2))))
    return mesh_object(name, verts, faces, mat, parent, smooth=False)


def build_spruce(name, seed):
    """Spruce: a dark, dense cone of drooping tiers."""
    rng = random.Random(seed)
    r = root(name)
    height = rng.uniform(8.5, 10.0)
    cylinder("Trunk", 0.17, 2.0, (0, 0, 1.0), M["bark"], r, verts=8)
    tiers = 8
    for k in range(tiers):
        t = k / (tiers - 1)
        z = 1.0 + t * (height - 2.6)
        radius = 2.3 * (1 - t) + 0.35
        star_cone(f"Tier_{k}", radius * rng.uniform(0.9, 1.05), 2.2 - t * 0.6, z,
                  M["spruce"] if k % 2 == 0 else M["spruce_light"], r)
    return r


def build_oak(name, seed):
    """Oak: a thick trunk splitting into branches under a broad, round crown about 8 m across."""
    rng = random.Random(seed)
    r = root(name)
    R[0] = r
    cylinder("Trunk", 0.32, 3.2, (0, 0, 1.6), M["bark"], r, verts=10)
    for k in range(4):
        a = k * math.pi / 2 + rng.uniform(-0.4, 0.4)
        strut(f"Branch_{k}", (0, 0, 2.8), (math.cos(a) * 1.8, math.sin(a) * 1.8, 4.6), 0.14, M["bark"], r, verts=6)
    crown("Crown", rng, 10, (0, 0, 5.3), 2.4, 2.4, 1.9, [M["oak"], M["oak_light"]], scale_z=0.8,
          cards=M["oak_cards"], per_clump=16, card_size=1.6)
    return r


def build_birch(name, seed):
    """Birch: a slim white trunk and a light, airy crown."""
    rng = random.Random(seed)
    r = root(name)
    R[0] = r
    lean = rng.uniform(-0.3, 0.3)
    strut("Trunk", (0, 0, 0), (lean, 0, 7.5), 0.13, M["bark_birch"], r, verts=8)
    for k in range(5):
        z = 1.0 + k * 1.1
        box(f"Mark_{k}", (0.28, 0.28, 0.08), (lean * z / 7.5, 0, z), M["bark"], r, rot=(0, 0, rng.uniform(0, 3)), bevel=0)
    crown("Crown", rng, 7, (lean, 0, 5.6), 1.1, 3.2, 1.1, [M["birch"], M["birch"]], scale_z=1.2, strength=0.35,
          cards=M["birch_cards"], per_clump=14, card_size=1.2)
    return r


def build_poplar(name, seed):
    """Lombardy poplar: a tall, narrow column, planted in rows along roads and field edges."""
    rng = random.Random(seed)
    r = root(name)
    R[0] = r
    cylinder("Trunk", 0.2, 2.5, (0, 0, 1.25), M["bark"], r, verts=8)
    clumps = []
    for k in range(6):
        z = 2.6 + k * 1.45
        rad = 1.25 - abs(k - 2) * 0.12
        loc = (rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), z)
        lumpy(f"Column_{k}", rad * 0.8, loc, M["poplar"] if k < 4 else M["oak_light"], r, scale=(1, 1, 1.4),
              strength=0.3, subdiv=3, smooth=True)
        clumps.append((*loc, rad * 1.1))
    leaf_cards("Cards", clumps, 14, 1.1, M["poplar_cards"], r, rng, (0, 0, 6.0), up=0.1)
    return r


def build_bush(name, seed):
    """A round bush about 2.5 m across, for hedgerows and field corners."""
    rng = random.Random(seed)
    r = root(name)
    R[0] = r
    crown("Bush", rng, 6, (0, 0, 0.8), 0.8, 0.6, 0.8, [M["bush"], M["bush_light"]], scale_z=0.8, strength=0.4,
          cards=M["bush_cards"], per_clump=10, card_size=0.9)
    return r


def build_hay_bale():
    r = root("Scenery_HayBale")
    cylinder("Bale", 0.72, 1.2, (0, 0, 0.7), M["hay"], r, rot=(0, math.radians(90), 0), verts=20, bevel=0.06)
    for side in (-1, 1):
        cylinder(f"End_{side}", 0.6, 0.02, (side * 0.6, 0, 0.7), M["hay_end"], r, rot=(0, math.radians(90), 0), verts=20)
    return r


# ---------- buildings ----------

def window(prefix, loc, facing, size, parent):
    """A framed window on a wall facing 'x' or 'y' (sign of the outward direction in loc)."""
    x, y, z = loc
    w, h = size
    if facing == "y":
        box(f"{prefix}_Frame", (w + 0.16, 0.08, h + 0.16), (x, y, z), M["frame"], parent, bevel=0)
        box(f"{prefix}_Glass", (w, 0.1, h), (x, y + math.copysign(0.02, y), z), M["glass"], parent, bevel=0)
    else:
        box(f"{prefix}_Frame", (0.08, w + 0.16, h + 0.16), (x, y, z), M["frame"], parent, bevel=0)
        box(f"{prefix}_Glass", (0.1, w, h), (x + math.copysign(0.02, x), y, z), M["glass"], parent, bevel=0)


def build_cottage():
    """White plastered cottage with a red tile roof, a porch over the door and a brick chimney."""
    r = root("Scenery_House_A")
    W, D, H = 8.0, 6.0, 3.0
    box("Plinth", (W + 0.1, D + 0.1, 0.5), (0, 0, 0.25), M["stone"], r, bevel=0.02)
    box("Walls", (W, D, H), (0, 0, H / 2), M["plaster"], r, bevel=0.03)
    gable_roof("Roof", W, D, H, 2.3, M["roof_tile"], r, overhang=0.45, ridge_along_x=True)
    box("Ridge", (W + 0.9, 0.25, 0.18), (0, 0, H + 2.32), M["roof_tile"], r, bevel=0.03)
    box("Chimney", (0.55, 0.55, 1.6), (1.8, 0.6, H + 2.0), M["brick"], r, bevel=0.02)
    for k, x in enumerate((-2.9, 0.9, 2.8)):
        window(f"WinF_{k}", (x, -D / 2 - 0.02, 1.6), "y", (0.9, 1.1), r)
        window(f"WinB_{k}", (x, D / 2 + 0.02, 1.6), "y", (0.9, 1.1), r)
    for side in (-1, 1):
        window(f"WinS_{side}", (side * (W / 2 + 0.02), 0, 1.6), "x", (0.8, 1.0), r)
    box("Door", (1.0, 0.1, 2.1), (-1.1, -D / 2 - 0.03, 1.05), M["door"], r, bevel=0)
    # Porch: a little gable roof on two posts, and steps.
    gable_roof("PorchRoof", 2.0, 1.6, 2.5, 0.6, M["roof_tile"], r, overhang=0.1)
    for obj in [o for o in r.children if o.name == "PorchRoof"]:
        obj.location = (-1.1, -D / 2 - 0.8, 0)
    for side in (-1, 1):
        strut(f"PorchPost_{side}", (-1.1 + side * 0.9, -D / 2 - 1.5, 0), (-1.1 + side * 0.9, -D / 2 - 1.5, 2.5), 0.06, M["planks"], r)
    box("Step1", (1.6, 0.6, 0.25), (-1.1, -D / 2 - 0.35, 0.12), M["stone"], r, bevel=0.01)
    box("Step2", (1.6, 0.35, 0.45), (-1.1, -D / 2 - 0.2, 0.22), M["stone"], r, bevel=0.01)
    return r


def build_town_house():
    """Grey two-storey house with a metal roof, a balcony over the door and two chimneys."""
    r = root("Scenery_House_B")
    W, D, H = 8.5, 7.0, 6.0
    box("Plinth", (W + 0.1, D + 0.1, 0.6), (0, 0, 0.3), M["stone"], r, bevel=0.02)
    box("Walls", (W, D, H), (0, 0, H / 2), M["plaster_grey"], r, bevel=0.03)
    box("Band", (W + 0.06, D + 0.06, 0.15), (0, 0, 3.0), M["frame"], r, bevel=0)
    gable_roof("Roof", W, D, H, 2.6, M["roof_metal"], r, overhang=0.35, ridge_along_x=True)
    for k, x in enumerate((-2.2, 2.2)):
        box(f"Chimney_{k}", (0.5, 0.5, 1.8), (x, 0.4, H + 2.2), M["brick"], r, bevel=0.02)
    for f in range(2):
        z = 1.7 + f * 3.0
        for k, x in enumerate((-3.0, -1.0, 1.0, 3.0)):
            if f == 0 and k == 2:
                continue  # door
            window(f"WinF_{f}_{k}", (x, -D / 2 - 0.02, z), "y", (0.9, 1.3), r)
            window(f"WinB_{f}_{k}", (x, D / 2 + 0.02, z), "y", (0.9, 1.3), r)
        for side in (-1, 1):
            window(f"WinS_{f}_{side}", (side * (W / 2 + 0.02), -1.0, z), "x", (0.8, 1.2), r)
    box("Door", (1.0, 0.1, 2.2), (1.0, -D / 2 - 0.03, 1.4), M["door"], r, bevel=0)
    box("Steps", (1.8, 0.9, 0.6), (1.0, -D / 2 - 0.45, 0.3), M["stone"], r, bevel=0.01)
    box("Balcony", (2.2, 1.0, 0.15), (1.0, -D / 2 - 0.5, 3.4), M["concrete"], r, bevel=0.01)
    for k in range(7):
        strut(f"BalconyBar_{k}", (0.0 + k * 0.33, -D / 2 - 0.95, 3.45), (0.0 + k * 0.33, -D / 2 - 0.95, 4.35), 0.02, M["railing"], r, verts=4)
    strut("BalconyRail", (-0.1, -D / 2 - 0.95, 4.35), (2.1, -D / 2 - 0.95, 4.35), 0.03, M["railing"], r)
    return r


def build_barn():
    """Weathered plank barn on a stone base, big doors at the front, a lean-to log store at the side."""
    r = root("Scenery_Barn")
    W, D, H = 10.0, 7.0, 4.0
    box("Base", (W + 0.1, D + 0.1, 0.8), (0, 0, 0.4), M["stone"], r, bevel=0.03)
    box("Walls", (W, D, H), (0, 0, H / 2), M["planks"], r, bevel=0.02)
    rng = random.Random(5)
    for side in (-1, 1):
        for k in range(28):
            x = -W / 2 + 0.18 + k * (W - 0.36) / 27
            if side < 0 and abs(x) < 1.6:
                continue  # doorway
            box(f"Plank_{side}_{k}", (0.12, 0.04, H - 0.9), (x, side * (D / 2 + 0.02), 0.8 + (H - 0.9) / 2),
                M["planks_dark"] if rng.random() < 0.4 else M["planks"], r, bevel=0)
    gable_roof("Roof", W, D, H, 2.6, M["roof_barn"], r, overhang=0.4, ridge_along_x=True)
    box("Doorway", (3.0, 0.05, 3.0), (0, -D / 2 - 0.01, 1.5), M["interior"], r, bevel=0)
    box("DoorLeft", (1.5, 0.1, 3.0), (-1.55, -D / 2 - 0.1, 1.5), M["planks"], r, bevel=0)
    box("DoorRight", (0.1, 1.5, 3.0), (1.55, -D / 2 - 0.8, 1.5), M["planks"], r, bevel=0)  # swung open
    for k in range(2):
        box(f"Brace_{k}", (1.6, 0.06, 0.12), (-1.55, -D / 2 - 0.16, 0.8 + k * 1.4), M["planks_dark"], r, bevel=0)
    cylinder("Bale", 0.65, 1.1, (-0.4, -D / 2 + 1.0, 0.65), M["hay"], r, rot=(0, math.radians(90), 0), verts=16)
    # Lean-to on the right with stacked logs.
    gable_roof("LeanTo", 2.4, D - 1.0, 2.6, 0.01, M["roof_barn"], r, overhang=0.2)
    for obj in [o for o in r.children if o.name == "LeanTo"]:
        obj.location = (W / 2 + 1.2, 0, 0)
        obj.rotation_euler = (0, math.radians(-14), 0)
    for k in (-1, 1):
        strut(f"LeanPost_{k}", (W / 2 + 2.3, k * (D / 2 - 0.8), 0), (W / 2 + 2.3, k * (D / 2 - 0.8), 2.4), 0.07, M["planks"], r)
    for k in range(3):
        for j in range(6):
            cylinder(f"Log_{k}_{j}", 0.13, 1.0, (W / 2 + 1.2, -2.0 + j * 0.28 + (k % 2) * 0.14, 0.15 + k * 0.24),
                     M["planks"], r, rot=(0, math.radians(90), 0), verts=8)
    return r


def build_church():
    """Small village church: white nave with a red roof, a bell tower at the front and a dark spire."""
    r = root("Scenery_Church")
    W, D, H = 7.0, 13.0, 5.5
    box("Plinth", (W + 0.1, D + 0.1, 0.5), (0, 0, 0.25), M["stone"], r, bevel=0.02)
    box("Nave", (W, D, H), (0, 0, H / 2), M["plaster"], r, bevel=0.03)
    gable_roof("Roof", W, D, H, 3.2, M["roof_tile"], r, overhang=0.4)
    for side in (-1, 1):
        for k in range(4):
            window(f"Win_{side}_{k}", (side * (W / 2 + 0.02), -3.6 + k * 2.6, 2.8), "x", (0.8, 2.4), r)
    box("Apse", (4.0, 2.0, 4.0), (0, D / 2 + 1.0, 2.0), M["plaster"], r, bevel=0.03)
    gable_roof("ApseRoof", 4.0, 2.0, 4.0, 1.2, M["roof_tile"], r, overhang=0.2, ridge_along_x=True)
    for obj in [o for o in r.children if o.name == "ApseRoof"]:
        obj.location = (0, D / 2 + 1.0, 0)
    T, TH = 3.6, 13.0
    ty = -D / 2 - T / 2 + 0.3
    box("Tower", (T, T, TH), (0, ty, TH / 2), M["plaster"], r, bevel=0.03)
    box("TowerBand", (T + 0.1, T + 0.1, 0.2), (0, ty, TH - 2.6), M["stone"], r, bevel=0)
    for k, (dx, dy, axis) in enumerate(((0, -T / 2 - 0.02, "y"), (0, T / 2 + 0.02, "y"), (-T / 2 - 0.02, 0, "x"), (T / 2 + 0.02, 0, "x"))):
        window(f"Belfry_{k}", (dx, ty + dy, TH - 1.4), axis, (0.9, 1.4), r)
    box("Door", (1.4, 0.1, 2.6), (0, ty - T / 2 - 0.03, 1.3), M["door"], r, bevel=0)
    s = T / 2 + 0.2
    spire_v = [(-s, ty - s, TH), (s, ty - s, TH), (s, ty + s, TH), (-s, ty + s, TH), (0, ty, TH + 6.5)]
    mesh_object("Spire", spire_v, [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (3, 2, 1, 0)], M["spire"], r, smooth=False)
    strut("CrossV", (0, ty, TH + 6.4), (0, ty, TH + 7.6), 0.05, M["spire"], r, verts=4)
    strut("CrossH", (-0.35, ty, TH + 7.15), (0.35, ty, TH + 7.15), 0.05, M["spire"], r, verts=4)
    return r


def build_fence():
    """3 m of post-and-rail fence."""
    r = root("Scenery_Fence")
    rng = random.Random(2)
    for k, x in enumerate((-1.5, 0.0, 1.5)):
        cylinder(f"Post_{k}", 0.07, 1.25, (x, 0, 0.6), M["fence"], r, rot=(rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), 0), verts=7)
    for k, z in enumerate((0.45, 0.95)):
        strut(f"Rail_{k}", (-1.6, 0, z + rng.uniform(-0.04, 0.04)), (1.6, 0, z + rng.uniform(-0.04, 0.04)), 0.045, M["fence"], r, verts=6)
    return r


def build_bridge():
    """Concrete road bridge, 18 m long (along Y) and 6.4 m wide, the deck's top at z = 0.3, with steel railings
    and abutments that reach down into the riverbanks."""
    r = root("Scenery_Bridge")
    L, Wd = 18.0, 6.4
    box("Deck", (Wd, L, 0.6), (0, 0, 0.0), M["concrete"], r, bevel=0.03)
    box("Road", (Wd - 1.2, L, 0.02), (0, 0, 0.31), M["asphalt"], r, bevel=0)
    for side in (-1, 1):
        box(f"Kerb_{side}", (0.5, L, 0.25), (side * (Wd / 2 - 0.25), 0, 0.42), M["concrete"], r, bevel=0.02)
        for k in range(13):
            y = -L / 2 + 0.5 + k * (L - 1.0) / 12
            strut(f"Post_{side}_{k}", (side * (Wd / 2 - 0.2), y, 0.5), (side * (Wd / 2 - 0.2), y, 1.5), 0.04, M["railing"], r, verts=6)
        for z in (1.0, 1.5):
            strut(f"Rail_{side}_{z}", (side * (Wd / 2 - 0.2), -L / 2 + 0.4, z), (side * (Wd / 2 - 0.2), L / 2 - 0.4, z), 0.04, M["railing"], r, verts=6)
    for end in (-1, 1):
        box(f"Abutment_{end}", (Wd + 0.6, 2.4, 4.5), (0, end * (L / 2 - 0.6), -2.5), M["concrete"], r, bevel=0.03)
        for side in (-1, 1):
            box(f"Wing_{end}_{side}", (0.5, 3.0, 2.5), (side * (Wd / 2 + 0.3), end * (L / 2 + 0.6), -0.9), M["concrete"], r, bevel=0.02)
    for k in (-1, 1):
        box(f"Pier_{k}", (Wd - 0.6, 0.9, 4.0), (0, k * 3.2, -2.3), M["concrete"], r, bevel=0.03)
    return r


def build_rock(name, seed, scale):
    """A grey boulder with moss on top."""
    r = root(name)
    rng = random.Random(seed)
    lumpy("Rock", 1.0, (0, 0, 0.4), M["rock"], r, scale=scale, strength=0.55, subdiv=3)
    lumpy("Moss", 0.85, (rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 0.4 + scale[2] * 0.42), M["moss"], r,
          scale=(scale[0] * 0.95, scale[1] * 0.95, 0.4), strength=0.35, subdiv=3, smooth=True)
    return r


R = [None]  # the model being built, for crown()


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builds = [
        ("Scenery_Pine", lambda: build_pine("Scenery_Pine", 1), 16.0, 5.0),
        ("Scenery_Pine_B", lambda: build_pine("Scenery_Pine_B", 7), 16.0, 5.0),
        ("Scenery_Spruce", lambda: build_spruce("Scenery_Spruce", 3), 16.0, 4.5),
        ("Scenery_Tree", lambda: build_oak("Scenery_Tree", 4), 16.0, 4.0),
        ("Scenery_Tree_B", lambda: build_oak("Scenery_Tree_B", 9), 16.0, 4.0),
        ("Scenery_Birch", lambda: build_birch("Scenery_Birch", 5), 14.0, 4.0),
        ("Scenery_Poplar", lambda: build_poplar("Scenery_Poplar", 6), 17.0, 5.5),
        ("Scenery_Bush", lambda: build_bush("Scenery_Bush", 8), 6.0, 0.8),
        ("Scenery_HayBale", build_hay_bale, 4.5, 0.6),
        ("Scenery_House_A", build_cottage, 16.0, 2.5),
        ("Scenery_House_B", build_town_house, 18.0, 3.5),
        ("Scenery_Barn", build_barn, 19.0, 2.5),
        ("Scenery_Church", build_church, 28.0, 6.0),
        ("Scenery_Fence", build_fence, 5.0, 0.5),
        ("Scenery_Bridge", build_bridge, 26.0, 0.0),
        ("Scenery_Rock", lambda: build_rock("Scenery_Rock", 1, (1.3, 1.0, 0.75)), 5.0, 0.4),
        ("Scenery_Rock_B", lambda: build_rock("Scenery_Rock_B", 2, (1.0, 0.8, 0.9)), 5.0, 0.4),
    ]
    only = set(args[2].split(",")) if len(args) > 2 else None
    for name, build, cam_distance, target_z in builds:
        if only and name not in only:
            continue
        reset_scene()
        M.clear()
        make_materials()
        r = build()
        join_meshes(r)
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name}")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), cam_distance, target_z)
            print(f"PREVIEW {name}")


main()
