"""Builds the enemy vehicles and exports each as an FBX for Unity. (Scenery: make_scenery.py, bases: make_base.py.)

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_world.py -- Assets/DroneSandbox/Resources/Models [preview_dir]

These are stand-ins sized for the game. Any model dropped into Resources/Models with the same
name (e.g. an Asset Store tank saved as a prefab called Enemy_Tank) replaces them.
Rotating parts are empties named "Turret" (tank turret, AA mount, radar head).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (block, box, box_uv_all, cylinder, export, hexa, material, net, pivot,  # noqa: E402
                    render_preview, reset_scene, root, sphere, strut)

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
    M["camo_a"] = material("CamoNetA", (0.07, 0.09, 0.035), roughness=1.0)
    M["camo_b"] = material("CamoNetB", (0.20, 0.17, 0.08), roughness=1.0)


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


def build_artillery():
    """Towed howitzer dug in under a camouflage net: split trails, two wheels, a gun shield and a long barrel.
    The barrel hangs on an empty named "Barrel", which the game kicks back when the gun fires."""
    r = root("Enemy_Artillery")
    box("Carriage", (0.9, 1.3, 0.45), (0, 0.25, 0.78), M["olive"], r, bevel=0.04)
    box("Axle", (2.1, 0.18, 0.18), (0, 0.0, 0.5), M["olive_dark"], r, bevel=0.02)
    for side in (-1, 1):
        cylinder(f"Wheel_{side}", 0.5, 0.3, (side * 1.05, 0.0, 0.5), M["rubber"], r, rot=(0, R90, 0))
        cylinder(f"Hub_{side}", 0.22, 0.32, (side * 1.05, 0.0, 0.5), M["olive_dark"], r, rot=(0, R90, 0), verts=12)
        # Split trail running back and out to a spade dug into the ground.
        strut(f"Trail_{side}", (side * 0.3, 0.5, 0.6), (side * 1.35, 3.7, 0.12), 0.11, M["olive"], r, verts=8)
        box(f"Spade_{side}", (0.55, 0.1, 0.45), (side * 1.38, 3.78, 0.12), M["olive_dark"], r, rot=(0, 0, -side * 0.32))
    box("Shield", (2.2, 0.08, 1.05), (0, -0.6, 1.15), M["olive"], r, rot=(math.radians(-10), 0, 0), bevel=0.02)
    b = pivot("Barrel", (0, 0.1, 1.25), r)
    box("Cradle", (0.42, 1.6, 0.36), (0, 0.1, 1.25), M["olive_dark"], b, bevel=0.03)
    for side in (-1, 1):
        cylinder(f"Recuperator_{side}", 0.08, 1.4, (side * 0.16, -0.3, 1.47), M["olive_dark"], b, rot=(R90, 0, 0), verts=10)
    cylinder("Tube", 0.09, 4.4, (0, -2.0, 1.25), M["olive"], b, rot=(R90, 0, 0), verts=14)
    cylinder("MuzzleBrake", 0.15, 0.4, (0, -4.25, 1.25), M["olive_dark"], b, rot=(R90, 0, 0), verts=12)
    b.rotation_euler = (math.radians(-14), 0, 0)  # elevated, muzzle up
    # Camouflage net on four poles over the whole gun.
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"NetPole_{sx}_{sy}", (sx * 2.4, sy * 2.6 - 0.2, 0), (sx * 2.4, sy * 2.6 - 0.2, 2.3), 0.04, M["olive_dark"], r, verts=6)
    net("Net", (6.2, 6.8), 2.4, [M["camo_a"], M["camo_b"]], r, sag=0.9, seed=29)
    for obj in [o for o in r.children if o.name.startswith("Net_")]:
        obj.location = (0, -0.2, 0)
    box("Ammo", (0.8, 0.5, 0.4), (1.6, 1.4, 0.2), M["olive_dark"], r, bevel=0.03)
    box("Ammo2", (0.8, 0.5, 0.4), (1.6, 2.0, 0.2), M["olive_dark"], r, bevel=0.03)
    return r


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builds = [
        ("Enemy_Tank", build_tank, 7.0, 0.8),
        ("Enemy_Truck", build_truck, 6.5, 0.8),
        ("Enemy_AAGun", build_aa_gun, 6.0, 0.6),
        ("Enemy_Radar", build_radar, 7.5, 1.8),
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
        box_uv_all(r)  # real-scale UVs for Unity's photo textures; parts stay separate so turrets can turn
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name}")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), cam_distance, target_z)
            print(f"PREVIEW {name}")


main()
