"""Builds the enemy vehicles and the scenery (houses, trees, rocks) and exports each as an FBX for Unity.

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_world.py -- Assets/DroneSandbox/Resources/Models [preview_dir]

These are stand-ins sized for the game. Any model dropped into Resources/Models with the same
name (e.g. an Asset Store tank saved as a prefab called Enemy_Tank) replaces them.
Rotating parts are empties named "Turret" (tank turret, AA mount, radar head).
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (block, box, cone, cylinder, export, hexa, lumpy, material, pivot,  # noqa: E402
                    render_preview, reset_scene, root, set_parent, sphere)

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 else None

R90 = math.radians(90)
M = {}


def make_materials():
    M["olive"] = material("Olive", (0.16, 0.18, 0.10), roughness=0.8)
    M["olive_dark"] = material("OliveDark", (0.10, 0.11, 0.06), roughness=0.8)
    M["rubber"] = material("Rubber", (0.025, 0.025, 0.025), roughness=0.9)
    M["gunmetal"] = material("Gunmetal", (0.09, 0.09, 0.09), metallic=0.7, roughness=0.45)
    M["glass"] = material("Glass", (0.02, 0.03, 0.04), metallic=0.3, roughness=0.08)
    M["canvas"] = material("Canvas", (0.30, 0.29, 0.18), roughness=0.95)
    M["sand"] = material("Sandbag", (0.45, 0.40, 0.28), roughness=1.0)
    M["metal_light"] = material("MetalLight", (0.45, 0.47, 0.48), metallic=0.6, roughness=0.4)
    M["lamp"] = material("Lamp", (1.0, 0.9, 0.6), emission=(1.0, 0.9, 0.6))
    M["beacon"] = material("Beacon", (1.0, 0.1, 0.05), emission=(1.0, 0.1, 0.05))
    M["plaster"] = material("Plaster", (0.62, 0.58, 0.50), roughness=0.95)
    M["plaster_gray"] = material("PlasterGray", (0.48, 0.48, 0.47), roughness=0.95)
    M["roof_tile"] = material("RoofTile", (0.38, 0.15, 0.10), roughness=0.85)
    M["roof_metal"] = material("RoofMetal", (0.30, 0.32, 0.33), metallic=0.5, roughness=0.6)
    M["wood"] = material("Wood", (0.25, 0.16, 0.09), roughness=0.9)
    M["window"] = material("Window", (0.05, 0.06, 0.07), metallic=0.2, roughness=0.15)
    M["bark"] = material("Bark", (0.20, 0.14, 0.09), roughness=1.0)
    M["pine"] = material("PineNeedles", (0.07, 0.15, 0.07), roughness=0.9)
    M["leaves"] = material("Leaves", (0.13, 0.22, 0.08), roughness=0.9)
    M["leaves_light"] = material("LeavesLight", (0.20, 0.28, 0.10), roughness=0.9)
    M["rock"] = material("Rock", (0.32, 0.31, 0.29), roughness=0.95)


# ---------- enemy vehicles ----------

def build_tank():
    r = root("Enemy_Tank")
    # Hull between the tracks, with a sloped front glacis.
    hexa("Hull", block(0, 0, 0.35, 0.85, 1.3, 1.55, 1.3, 1.55, top_inset_front=0.55, top_inset_back=0.15), M["olive"], r)
    hexa("Deck", block(0, 0.05, 0.80, 0.98, 2.1, 1.25, 2.1, 1.55, top_inset_front=0.05), M["olive"], r)
    box("Exhaust", (1.2, 0.35, 0.08), (0, 1.35, 0.99), M["olive_dark"], r)
    for side in (-1, 1):
        x = side * 0.84
        hexa(f"Track_{side}", block(x, 0, 0.06, 0.74, 0.42, 1.35, 0.42, 1.35, top_inset_front=-0.32, top_inset_back=-0.28),
             M["rubber"], r, bevel=0.05)
        for k in range(6):
            y = -1.1 + k * 0.44
            cylinder(f"RoadWheel_{side}_{k}", 0.24, 0.1, (side * 1.07, y, 0.3), M["olive_dark"], r, rot=(0, R90, 0))
        cylinder(f"Sprocket_{side}", 0.2, 0.12, (side * 1.07, 1.5, 0.55), M["gunmetal"], r, rot=(0, R90, 0), verts=12)
        cylinder(f"Idler_{side}", 0.18, 0.12, (side * 1.07, -1.55, 0.5), M["olive_dark"], r, rot=(0, R90, 0))
        sphere(f"Headlight_{side}", 0.06, (side * 0.75, -1.3, 0.9), M["lamp"], r)
        box(f"Mudguard_{side}", (0.46, 0.25, 0.04), (x, -1.62, 0.78), M["olive"], r, rot=(math.radians(-25), 0, 0))

    t = pivot("Turret", (0, 0.15, 0.98), r)
    hexa("TurretBody", block(0, 0.15, 0.98, 1.45, 1.25, 0.9, 1.5, 1.05, top_inset_front=0.38, top_narrow=0.12), M["olive"], t)
    box("Bustle", (1.3, 0.45, 0.35), (0, 1.3, 1.2), M["olive_dark"], t, bevel=0.03)
    box("Mantlet", (0.55, 0.25, 0.32), (0, -0.82, 1.18), M["olive_dark"], t, bevel=0.04)
    cylinder("Barrel", 0.065, 2.3, (0, -2.0, 1.18), M["olive_dark"], t, rot=(R90, 0, 0))
    cylinder("Evacuator", 0.1, 0.35, (0, -1.7, 1.18), M["olive_dark"], t, rot=(R90, 0, 0))
    cylinder("Muzzle", 0.085, 0.15, (0, -3.1, 1.18), M["gunmetal"], t, rot=(R90, 0, 0))
    cylinder("Cupola", 0.2, 0.16, (0.35, 0.45, 1.52), M["olive_dark"], t)
    cylinder("Hatch", 0.17, 0.04, (-0.35, 0.35, 1.47), M["olive_dark"], t)
    box("RoofMG", (0.05, 0.6, 0.05), (0.35, 0.15, 1.66), M["gunmetal"], t, bevel=0)
    cylinder("Antenna", 0.012, 1.4, (-0.55, 1.1, 2.1), M["gunmetal"], t, verts=6)
    for side in (-1, 1):
        for k in range(3):
            cylinder(f"Smoke_{side}_{k}", 0.05, 0.18, (side * (0.5 + k * 0.08), -0.45 + k * 0.05, 1.42),
                     M["olive_dark"], t, rot=(math.radians(-60), 0, 0), verts=8)
    return r


def build_truck():
    r = root("Enemy_Truck")
    box("Chassis", (0.9, 3.3, 0.18), (0, 0, 0.55), M["olive_dark"], r)
    # Cab with a raked windscreen.
    hexa("Cab", block(0, -1.15, 0.62, 1.75, 1.7, 0.55, 1.7, 0.5, top_inset_front=0.25), M["olive"], r, bevel=0.05)
    hexa("Hood", block(0, -1.55, 0.62, 1.15, 1.5, 0.25, 1.5, 0.0, top_inset_front=0.08), M["olive"], r, bevel=0.04)
    box("Windscreen", (1.4, 0.04, 0.42), (0, -1.53, 1.45), M["glass"], r, rot=(math.radians(-12), 0, 0), bevel=0)
    for side in (-1, 1):
        box(f"SideWindow_{side}", (0.03, 0.55, 0.38), (side * 0.86, -1.12, 1.4), M["glass"], r, bevel=0)
        box(f"Mirror_{side}", (0.04, 0.06, 0.2), (side * 0.98, -1.5, 1.4), M["gunmetal"], r, bevel=0)
        sphere(f"Headlight_{side}", 0.07, (side * 0.6, -1.82, 0.95), M["lamp"], r)
    box("Grille", (1.2, 0.05, 0.35), (0, -1.8, 0.85), M["gunmetal"], r, bevel=0.01)
    box("Bumper", (1.8, 0.12, 0.14), (0, -1.86, 0.62), M["gunmetal"], r)
    # Cargo bed with a canvas cover.
    box("Bed", (1.75, 2.15, 0.12), (0, 0.55, 0.78), M["olive_dark"], r)
    box("CoverBox", (1.7, 2.1, 0.75), (0, 0.55, 1.2), M["canvas"], r, bevel=0.03)
    cover_top = cylinder("CoverTop", 0.85, 2.1, (0, 0.55, 1.57), M["canvas"], r, rot=(R90, 0, 0), verts=24)
    cover_top.scale = (1.0, 0.42, 1.0)  # flatten the arch: local Y points up after the 90° X rotation
    for side in (-1, 1):
        for k, y in enumerate((-1.2, 0.55, 1.25)):
            cylinder(f"Wheel_{side}_{k}", 0.38, 0.28, (side * 0.8, y, 0.38), M["rubber"], r, rot=(0, R90, 0))
            cylinder(f"Hub_{side}_{k}", 0.18, 0.3, (side * 0.8, y, 0.38), M["gunmetal"], r, rot=(0, R90, 0), verts=12)
        box(f"Fender_{side}", (0.34, 0.9, 0.05), (side * 0.8, -1.2, 0.82), M["olive"], r)
    box("FuelTank", (0.3, 0.6, 0.3), (-0.6, -0.35, 0.5), M["olive_dark"], r, bevel=0.06)
    return r


def build_aa_gun():
    r = root("Enemy_AAGun")
    # Sandbagged emplacement.
    for k in range(14):
        a = 2 * math.pi * k / 14
        for layer in range(2):
            ang = a + layer * math.pi / 14
            box(f"Sandbag_{k}_{layer}", (0.65, 0.32, 0.22), (math.cos(ang) * 1.65, math.sin(ang) * 1.65, 0.11 + layer * 0.21),
                M["sand"], r, rot=(0, 0, ang + R90), bevel=0.08)
    cylinder("Platform", 1.0, 0.22, (0, 0, 0.11), M["olive_dark"], r)

    t = pivot("Turret", (0, 0, 0.3), r)
    cylinder("Turntable", 0.65, 0.14, (0, 0, 0.37), M["olive"], t)
    for side in (-1, 1):
        box(f"Cradle_{side}", (0.12, 0.7, 0.55), (side * 0.32, 0.0, 0.7), M["olive"], t, bevel=0.03)
        box(f"AmmoBox_{side}", (0.22, 0.35, 0.3), (side * 0.5, 0.1, 0.85), M["olive_dark"], t, bevel=0.03)
    box("Seat", (0.35, 0.3, 0.08), (0, 0.55, 0.65), M["gunmetal"], t)
    e = pivot("Elevation", (0, -0.05, 0.95), t)
    for side in (-1, 1):
        x = side * 0.17
        box(f"Receiver_{side}", (0.14, 0.6, 0.16), (x, 0.0, 0.95), M["olive_dark"], e, bevel=0.02)
        cylinder(f"Barrel_{side}", 0.04, 1.9, (x, -1.2, 0.95), M["gunmetal"], e, rot=(R90, 0, 0), verts=12)
        cylinder(f"FlashHider_{side}", 0.06, 0.18, (x, -2.12, 0.95), M["gunmetal"], e, rot=(R90, 0, 0), verts=12)
    e.rotation_euler = (math.radians(-35), 0, 0)
    sphere("WarningLamp", 0.07, (0, 0.5, 0.95), M["beacon"], t)
    return r


def build_radar():
    r = root("Enemy_Radar")
    box("Shelter", (1.8, 2.4, 1.1), (0, 0.3, 0.55), M["olive"], r, bevel=0.04)
    box("Door", (0.04, 0.7, 0.85), (0.91, 0.6, 0.5), M["olive_dark"], r, bevel=0)
    box("Generator", (0.8, 0.7, 0.6), (-0.4, -1.4, 0.3), M["olive_dark"], r, bevel=0.04)
    cylinder("Mast", 0.12, 1.6, (0, 0, 1.9), M["gunmetal"], r, verts=12)
    for k in range(3):
        a = 2 * math.pi * k / 3
        cylinder(f"Strut_{k}", 0.03, 1.0, (math.cos(a) * 0.3, math.sin(a) * 0.3, 1.5), M["gunmetal"], r,
                 rot=(math.sin(a) * math.radians(18), -math.cos(a) * math.radians(18), 0), verts=6)

    t = pivot("Turret", (0, 0, 2.7), r)
    cylinder("Bearing", 0.25, 0.15, (0, 0, 2.75), M["olive_dark"], t)
    box("Boom", (0.12, 0.8, 0.12), (0, 0.2, 2.9), M["gunmetal"], t, bevel=0)
    box("Array", (2.6, 0.1, 1.1), (0, -0.1, 3.35), M["metal_light"], t, rot=(math.radians(-15), 0, 0), bevel=0.02)
    for k in range(7):
        box(f"Rib_{k}", (0.03, 0.04, 1.05), (-1.2 + k * 0.4, -0.17, 3.35), M["gunmetal"], t,
            rot=(math.radians(-15), 0, 0), bevel=0)
    sphere("Beacon", 0.08, (0, 0, 3.98), M["beacon"], t)
    return r


# ---------- scenery ----------

def gable_roof(name, w, d, z, rise, mat, parent, overhang=0.25):
    """A pitched roof whose ridge runs along Y."""
    w2, d2 = w / 2 + overhang, d / 2 + overhang
    corners = [(-w2, -d2, z), (w2, -d2, z), (w2, d2, z), (-w2, d2, z),
               (-0.01, -d2, z + rise), (0.01, -d2, z + rise), (0.01, d2, z + rise), (-0.01, d2, z + rise)]
    return hexa(name, corners, mat, parent, bevel=0)


def windows_on(parent, w, d, floors, mat):
    for f in range(floors):
        z = 1.0 + f * 2.4
        for side in (-1, 1):
            for k in range(max(1, int(d // 2))):
                y = -d / 2 + (k + 0.5) * d / max(1, int(d // 2))
                box(f"WinSide_{side}_{f}_{k}", (0.06, 0.6, 0.8), (side * (w / 2 + 0.02), y, z + 0.4), mat, parent, bevel=0)
            for k in range(max(1, int(w // 2.2))):
                x = -w / 2 + (k + 0.5) * w / max(1, int(w // 2.2))
                box(f"WinEnd_{side}_{f}_{k}", (0.6, 0.06, 0.8), (x, side * (d / 2 + 0.02), z + 0.4), mat, parent, bevel=0)


def build_house(name, w, d, floors, wall_mat, roof_mat, chimney=True):
    r = root(name)
    h = floors * 2.4 + 0.3
    box("Walls", (w, d, h), (0, 0, h / 2), wall_mat, r, bevel=0.03)
    gable_roof("Roof", w, d, h, w * 0.38, roof_mat, r)
    windows_on(r, w, d, floors, M["window"])
    box("Door", (0.9, 0.08, 1.9), (w * 0.2, -d / 2 - 0.02, 0.95), M["wood"], r, bevel=0)
    if chimney:
        box("Chimney", (0.5, 0.5, 1.4), (-w * 0.25, d * 0.2, h + w * 0.2), M["plaster_gray"], r, bevel=0.02)
    return r


def build_barn():
    r = root("Scenery_Barn")
    box("Walls", (5.5, 9.0, 3.6), (0, 0, 1.8), M["wood"], r, bevel=0.03)
    gable_roof("Roof", 5.5, 9.0, 3.6, 1.8, M["roof_metal"], r, overhang=0.3)
    box("BarnDoor", (2.6, 0.08, 2.8), (0, -4.52, 1.4), M["olive_dark"], r, bevel=0)
    return r


def build_pine():
    r = root("Scenery_Pine")
    cylinder("Trunk", 0.16, 2.0, (0, 0, 1.0), M["bark"], r, verts=10)
    for k in range(4):
        rad = 1.6 - k * 0.32
        cone(f"Needles_{k}", rad, 0.05, 1.9, (0, 0, 1.6 + k * 0.95), M["pine"], r, verts=10)
    return r


def build_broadleaf():
    r = root("Scenery_Tree")
    cylinder("Trunk", 0.2, 2.4, (0, 0, 1.2), M["bark"], r, verts=10)
    rng = random.Random(4)
    for k in range(6):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0.3, 0.9)
        loc = (math.cos(a) * d, math.sin(a) * d, rng.uniform(2.6, 3.6))
        mat = M["leaves"] if k % 2 else M["leaves_light"]
        lumpy(f"Leaves_{k}", rng.uniform(0.9, 1.3), loc, mat, r, strength=0.35)
    return r


def build_rock():
    r = root("Scenery_Rock")
    lumpy("Rock", 1.0, (0, 0, 0.35), M["rock"], r, scale=(1.3, 1.0, 0.6), strength=0.45)
    return r


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builds = [
        ("Enemy_Tank", build_tank, 7.0, 0.8),
        ("Enemy_Truck", build_truck, 6.5, 0.8),
        ("Enemy_AAGun", build_aa_gun, 6.0, 0.6),
        ("Enemy_Radar", build_radar, 7.5, 1.8),
        ("Scenery_House_A", lambda: build_house("Scenery_House_A", 5.0, 6.0, 1, M["plaster"], M["roof_tile"]), 13.0, 2.0),
        ("Scenery_House_B", lambda: build_house("Scenery_House_B", 6.0, 8.0, 2, M["plaster_gray"], M["roof_metal"]), 16.0, 3.0),
        ("Scenery_Barn", build_barn, 15.0, 2.5),
        ("Scenery_Pine", build_pine, 11.0, 2.8),
        ("Scenery_Tree", build_broadleaf, 10.0, 2.4),
        ("Scenery_Rock", build_rock, 5.0, 0.3),
    ]
    only = set(args[2].split(",")) if len(args) > 2 else None
    for name, build, cam_distance, target_z in builds:
        if only and name not in only:
            continue
        reset_scene()
        M.clear()
        make_materials()
        r = build()
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name}")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), cam_distance, target_z)
            print(f"PREVIEW {name}")


main()
