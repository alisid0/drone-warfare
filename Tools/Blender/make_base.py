"""Builds your drone base, the enemy command post and their props, and exports each as an FBX for Unity.
Modelled after the concept art in Docs/Concept (base.png, hq.png).

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_base.py -- Assets/DroneSandbox/Resources/Models [preview_dir] [only]

The game lays these out itself (see BaseBuilder.cs): a ring of HESCO barriers with a gate where each road
comes in, the hangar, launch pads and the rest inside. Fronts face Blender's -Y, which is +Z in Unity.
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (box, cone, cylinder, export, gable_roof, join_meshes, material, mesh_object, net,  # noqa: E402
                    render_preview, reset_scene, ring, root, sandbag_wall, set_parent, sphere, strut)

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 else None

R90 = math.radians(90)
M = {}


def make_materials():
    # Colours are linear (as Unity's URP Lit reads them), so they look lighter than the numbers suggest.
    M["concrete"] = material("Concrete", (0.30, 0.29, 0.27), roughness=0.95)
    M["concrete_stained"] = material("ConcreteStained", (0.24, 0.23, 0.21), roughness=0.95)
    M["yellow"] = material("PaintYellow", (0.62, 0.38, 0.02), roughness=0.7)
    M["galvanized"] = material("Galvanized", (0.40, 0.42, 0.43), metallic=0.75, roughness=0.42)
    M["steel"] = material("SteelDark", (0.06, 0.06, 0.06), metallic=0.6, roughness=0.5)
    M["rust"] = material("RustySteel", (0.11, 0.06, 0.04), metallic=0.4, roughness=0.8)
    M["hesco"] = material("HescoFabric", (0.33, 0.26, 0.15), roughness=1.0)
    M["hesco_fill"] = material("HescoFill", (0.20, 0.15, 0.09), roughness=1.0)
    M["wire"] = material("HescoWire", (0.12, 0.11, 0.09), metallic=0.4, roughness=0.7)
    M["sand"] = material("Sandbag", (0.36, 0.30, 0.19), roughness=1.0)
    M["canvas"] = material("TentCanvas", (0.075, 0.09, 0.04), roughness=0.95)
    M["canvas_dark"] = material("TentCanvasDark", (0.04, 0.05, 0.025), roughness=0.95)
    M["camo_a"] = material("CamoNetA", (0.07, 0.09, 0.035), roughness=1.0)
    M["camo_b"] = material("CamoNetB", (0.20, 0.17, 0.08), roughness=1.0)
    M["olive"] = material("Olive", (0.08, 0.10, 0.04), roughness=0.8)
    M["container_olive"] = material("ContainerOlive", (0.07, 0.10, 0.04), metallic=0.3, roughness=0.65)
    M["container_rust"] = material("ContainerRust", (0.20, 0.045, 0.02), metallic=0.3, roughness=0.65)
    M["tank_white"] = material("TankWhite", (0.62, 0.62, 0.60), metallic=0.5, roughness=0.35)
    M["hazard"] = material("HazardOrange", (0.8, 0.25, 0.02), roughness=0.6)
    M["wood"] = material("Wood", (0.16, 0.10, 0.05), roughness=0.9)
    M["wood_light"] = material("WoodLight", (0.30, 0.21, 0.11), roughness=0.9)
    M["window"] = material("Window", (0.02, 0.025, 0.03), metallic=0.3, roughness=0.12)
    M["dark"] = material("Interior", (0.01, 0.01, 0.01), roughness=1.0)
    M["lamp"] = material("Lamp", (1.0, 0.92, 0.7), emission=(1.0, 0.92, 0.7))
    M["beacon"] = material("Beacon", (1.0, 0.1, 0.05), emission=(1.0, 0.1, 0.05))
    M["white"] = material("PaintWhite", (0.7, 0.7, 0.68), roughness=0.5)
    M["drum_green"] = material("DrumGreen", (0.06, 0.09, 0.03), metallic=0.3, roughness=0.6)
    M["drum_orange"] = material("DrumOrange", (0.32, 0.08, 0.02), metallic=0.3, roughness=0.6)
    M["roof_felt"] = material("RoofFelt", (0.06, 0.06, 0.06), roughness=0.95)


# ---------- your base ----------

def build_hangar():
    """Arched corrugated-steel hangar, 11 m wide and 14 m long, doors open at the front (-Y)."""
    r = root("Base_Hangar")
    radius, length = 5.5, 14.0
    around, along = 40, 140
    verts, faces = [], []
    for j in range(along + 1):
        y = -length / 2 + length * j / along
        ripple = 0.035 * math.sin(2 * math.pi * y / 0.2)  # corrugation ridges run round the arch
        for i in range(around + 1):
            a = math.pi * i / around
            rr = radius + ripple
            verts.append((math.cos(a) * rr, y, math.sin(a) * rr))
    for j in range(along):
        for i in range(around):
            a = j * (around + 1) + i
            faces.append((a, a + around + 1, a + around + 2, a + 1))
    shell = mesh_object("Shell", verts, faces, M["galvanized"], r)
    solid = shell.modifiers.new("Solidify", "SOLIDIFY")
    solid.thickness = 0.06
    # Seams every 2.8 m and a ridge cap.
    for k in range(6):
        y = -length / 2 + k * length / 5
        rib_v, rib_f = [], []
        for i in range(around + 1):
            a = math.pi * i / around
            for dy in (-0.05, 0.05):
                rib_v.append((math.cos(a) * (radius + 0.07), y + dy, math.sin(a) * (radius + 0.07)))
        for i in range(around):
            b = 2 * i
            rib_f.append((b, b + 1, b + 3, b + 2))
        mesh_object(f"Seam_{k}", rib_v, rib_f, M["steel"], r, smooth=False)

    # End walls: half-discs, the front one with a dark opening and two door leaves slid aside.
    for name, y, mat in (("BackWall", length / 2 - 0.05, M["galvanized"]), ("FrontWall", -length / 2 + 0.1, M["galvanized"])):
        wv = [(0, y, 0)] + [(math.cos(math.pi * i / 32) * radius, y, math.sin(math.pi * i / 32) * radius) for i in range(33)]
        wf = [(0, i + 1, i + 2) if name == "FrontWall" else (0, i + 2, i + 1) for i in range(32)]
        mesh_object(name, wv, wf, mat, r, smooth=False)
    box("Opening", (6.2, 0.05, 4.3), (0, -length / 2 + 0.05, 2.15), M["dark"], r, bevel=0)
    box("LintelLight", (5.6, 0.1, 0.12), (0, -length / 2 + 0.2, 4.1), M["lamp"], r, bevel=0)
    for side in (-1, 1):
        box(f"Door_{side}", (2.4, 0.12, 4.3), (side * 4.1, -length / 2 - 0.12, 2.15), M["galvanized"], r, bevel=0.02)
        for k in range(5):
            box(f"DoorRib_{side}_{k}", (0.06, 0.05, 4.2), (side * (3.1 + k * 0.5), -length / 2 - 0.2, 2.15), M["steel"], r, bevel=0)
        box(f"DoorRail_{side}", (2.6, 0.1, 0.1), (side * 4.1, -length / 2 - 0.15, 4.35), M["steel"], r, bevel=0)
    # A drone parked inside, half visible.
    box("DroneBody", (0.5, 2.2, 0.35), (0, -length / 2 + 2.4, 0.9), M["white"], r)
    box("DroneWing", (5.0, 0.6, 0.06), (0, -length / 2 + 2.6, 1.05), M["white"], r)
    for side in (-1, 1):
        box(f"Vent_{side}", (0.7, 0.7, 0.4), (side * 1.6, side * 2.0, radius - 0.1), M["steel"], r)
    box("Apron", (8.0, 3.5, 0.12), (0, -length / 2 - 1.8, 0.06), M["concrete"], r, bevel=0)
    for k in range(3):
        box(f"Crate_{k}", (0.8, 0.6, 0.6), (-radius - 0.6, -3 + k * 0.9, 0.3), M["olive"], r, bevel=0.03)
    return r


def build_launch_pad():
    """7 m square concrete pad with a yellow circle and corner marks."""
    r = root("Base_LaunchPad")
    box("Slab", (7.0, 7.0, 0.16), (0, 0, 0.08), M["concrete"], r, bevel=0.03)
    ring("Circle", 2.35, 2.6, 0.165, M["yellow"], r)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"CornerA_{sx}_{sy}", (0.9, 0.16, 0.01), (sx * 2.95, sy * 3.25, 0.165), M["yellow"], r, bevel=0)
            box(f"CornerB_{sx}_{sy}", (0.16, 0.9, 0.01), (sx * 3.25, sy * 2.95, 0.165), M["yellow"], r, bevel=0)
            cylinder(f"Light_{sx}_{sy}", 0.07, 0.12, (sx * 3.6, sy * 3.6, 0.1), M["lamp"], r, verts=8)
    box("Case", (0.7, 0.45, 0.4), (-3.9, 2.2, 0.2), M["olive"], r, bevel=0.03)
    box("Case2", (0.5, 0.4, 0.3), (-3.8, 1.5, 0.15), M["olive"], r, bevel=0.03)
    return r


def build_fuel_tanks():
    """Two horizontal fuel tanks on saddles inside a low concrete bund."""
    r = root("Base_FuelTanks")
    for k, x in enumerate((-1.1, 1.1)):
        cylinder(f"Tank_{k}", 0.9, 4.0, (x, 0, 1.25), M["tank_white"], r, rot=(R90, 0, 0), verts=28)
        for end in (-1, 1):
            sphere(f"Cap_{k}_{end}", 0.9, (x, end * 2.0, 1.25), M["tank_white"], r, scale=(1, 0.25, 1))
            box(f"Saddle_{k}_{end}", (1.5, 0.25, 0.7), (x, end * 1.3, 0.4), M["steel"], r)
        box(f"Hazard_{k}", (0.35, 0.03, 0.35), (x, -2.24, 1.3), M["hazard"], r, rot=(0, math.radians(45), 0), bevel=0)
    box("Walkway", (0.6, 4.2, 0.06), (0, 0, 2.25), M["steel"], r, bevel=0)
    for y in (-2, -1, 0, 1, 2):
        strut(f"Rail_{y}", (0.3, y, 2.25), (0.3, y, 3.0), 0.02, M["yellow"], r)
    strut("RailTop", (0.3, -2.1, 3.0), (0.3, 2.1, 3.0), 0.02, M["yellow"], r)
    strut("Pipe", (-1.1, -2.2, 0.5), (2.6, -2.2, 0.5), 0.06, M["steel"], r)
    # Bund wall.
    for name, size, loc in (("BundN", (5.4, 0.25, 0.35), (0, 2.8, 0.17)), ("BundS", (5.4, 0.25, 0.35), (0, -2.8, 0.17)),
                            ("BundE", (0.25, 5.4, 0.35), (2.6, 0, 0.17)), ("BundW", (0.25, 5.4, 0.35), (-2.6, 0, 0.17))):
        box(name, size, loc, M["concrete"], r)
    box("Pump", (0.6, 0.5, 0.8), (2.1, -2.0, 0.4), M["olive"], r, bevel=0.03)
    return r


def build_mast():
    """A 15 m lattice radio mast with antenna panels and a red beacon."""
    r = root("Base_Mast")
    height, levels = 15.0, 10
    legs = []
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 2
        legs.append(lambda z, a=a: (math.cos(a) * (0.75 - 0.4 * z / height), math.sin(a) * (0.75 - 0.4 * z / height), z))
    for k, leg in enumerate(legs):
        strut(f"Leg_{k}", leg(0), leg(height), 0.045, M["galvanized"], r)
    for lv in range(levels):
        z0, z1 = height * lv / levels, height * (lv + 1) / levels
        for k in range(3):
            a, b = legs[k], legs[(k + 1) % 3]
            strut(f"Brace_{lv}_{k}", a(z0), b(z1), 0.018, M["galvanized"], r, verts=4)
            strut(f"Ring_{lv}_{k}", a(z1), b(z1), 0.018, M["galvanized"], r, verts=4)
    for k in range(3):
        a = 2 * math.pi * k / 3
        box(f"Panel_{k}", (0.3, 0.12, 1.2), (math.cos(a) * 0.55, math.sin(a) * 0.55, height - 1.0), M["white"], r,
            rot=(0, 0, a + R90), bevel=0.02)
    cylinder("Whip", 0.02, 2.5, (0, 0, height + 1.2), M["steel"], r, verts=6)
    sphere("Beacon", 0.12, (0, 0, height + 0.15), M["beacon"], r)
    box("Footing", (2.0, 2.0, 0.3), (0, 0, 0.15), M["concrete"], r)
    box("Cabinet", (0.8, 0.5, 1.4), (1.4, 0.4, 0.7), M["galvanized"], r, bevel=0.03)
    box("Cabinet2", (0.8, 0.5, 1.2), (1.4, -0.4, 0.6), M["galvanized"], r, bevel=0.03)
    return r


def build_dish():
    """Satellite dish on a post, tilted up toward the sky."""
    r = root("Base_Dish")
    box("Base", (1.0, 1.0, 0.3), (0, 0, 0.15), M["concrete"], r)
    cylinder("Post", 0.1, 1.6, (0, 0, 1.0), M["steel"], r, verts=10)
    tilt = math.radians(-35)
    dish = cone("Dish", 1.1, 0.2, 0.4, (0, -0.15, 2.0), M["white"], r, verts=28)
    dish.rotation_euler = (math.radians(90) + tilt, 0, 0)
    strut("FeedArm", (0, -0.2, 1.85), (0, -1.0, 2.6), 0.025, M["steel"], r)
    box("Feed", (0.12, 0.2, 0.12), (0, -1.0, 2.6), M["steel"], r, bevel=0)
    return r


def build_tent():
    """Large olive command tent with a sandbag row in front."""
    r = root("Base_Tent")
    w, d, wall = 5.0, 6.5, 1.9
    box("Walls", (w, d, wall), (0, 0, wall / 2), M["canvas"], r, bevel=0.05)
    gable_roof("Roof", w, d, wall, 1.5, M["canvas"], r, overhang=0.35)
    box("Door", (1.3, 0.04, 1.7), (0, -d / 2 - 0.03, 0.85), M["canvas_dark"], r, bevel=0)
    for side in (-1, 1):
        for k in range(2):
            box(f"Window_{side}_{k}", (0.04, 0.9, 0.5), (side * (w / 2 + 0.02), -1.2 + k * 2.4, 1.35), M["window"], r, bevel=0)
        for k in range(4):
            strut(f"Guy_{side}_{k}", (side * (w / 2 + 0.35), -d / 2 + 0.6 + k * (d - 1.2) / 3, wall),
                  (side * (w / 2 + 1.2), -d / 2 + 0.6 + k * (d - 1.2) / 3, 0), 0.012, M["wood"], r, verts=4)
    sandbag_wall("FrontBags", (-2.4, -d / 2 - 0.7), (-0.9, -d / 2 - 0.7), 2, M["sand"], r)
    sandbag_wall("FrontBags2", (0.9, -d / 2 - 0.7), (2.4, -d / 2 - 0.7), 2, M["sand"], r)
    box("Generator", (1.0, 0.7, 0.8), (w / 2 + 0.9, d / 2 - 0.8, 0.4), M["olive"], r, bevel=0.04)
    return r


# ---------- shared props ----------

def build_container(name, mat):
    """A 20 ft shipping container, doors at the back (+Y)."""
    r = root(name)
    L, W, H = 6.06, 2.44, 2.59
    box("Body", (W, L, H), (0, 0, H / 2), mat, r, bevel=0.02)
    for side in (-1, 1):
        for k in range(18):
            y = -L / 2 + 0.3 + k * (L - 0.6) / 17
            box(f"Rib_{side}_{k}", (0.05, 0.12, H - 0.3), (side * (W / 2 + 0.02), y, H / 2), mat, r, bevel=0)
    for k in range(4):
        x = -0.85 + k * 0.57
        strut(f"LockBar_{k}", (x, L / 2 + 0.06, 0.2), (x, L / 2 + 0.06, H - 0.2), 0.025, M["steel"], r)
    box("DoorSeam", (0.03, 0.03, H - 0.2), (0, L / 2 + 0.02, H / 2), M["steel"], r, bevel=0)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"Corner_{sx}_{sy}", (0.18, 0.18, H), (sx * (W / 2 - 0.08), sy * (L / 2 - 0.08), H / 2), M["steel"], r, bevel=0)
    return r


def build_hesco():
    """One HESCO barrier unit: a wire cage lined with fabric and filled with earth, 1.1 x 1.1 x 1.35 m."""
    r = root("Prop_Hesco")
    s, h = 1.1, 1.35
    box("Fabric", (s - 0.04, s - 0.04, h - 0.05), (0, 0, (h - 0.05) / 2), M["hesco"], r, bevel=0.08)
    box("Fill", (s - 0.16, s - 0.16, 0.05), (0, 0, h - 0.04), M["hesco_fill"], r, bevel=0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"Post_{sx}_{sy}", (0.04, 0.04, h), (sx * s / 2, sy * s / 2, h / 2), M["wire"], r, bevel=0)
    for k in range(4):
        z = 0.15 + k * (h - 0.25) / 3
        for name, size, loc in (("N", (s, 0.025, 0.025), (0, s / 2, z)), ("S", (s, 0.025, 0.025), (0, -s / 2, z)),
                                ("E", (0.025, s, 0.025), (s / 2, 0, z)), ("W", (0.025, s, 0.025), (-s / 2, 0, z))):
            box(f"Wire_{k}_{name}", size, loc, M["wire"], r, bevel=0)
    for k in range(1, 4):
        for sy in (-1, 1):
            box(f"WireV_{k}_{sy}", (0.025, 0.025, h), (-s / 2 + k * s / 4, sy * s / 2, h / 2), M["wire"], r, bevel=0)
    return r


def build_gate():
    """Steel mesh gate, two leaves swung open inward (+Y), posts 5 m apart."""
    r = root("Prop_Gate")
    for side in (-1, 1):
        box(f"Post_{side}", (0.18, 0.18, 2.4), (side * 2.6, 0, 1.2), M["steel"], r)
        sphere(f"Lamp_{side}", 0.1, (side * 2.6, 0, 2.5), M["lamp"], r)
        box(f"Block_{side}", (0.8, 0.8, 0.8), (side * 3.4, -1.4, 0.4), M["concrete"], r, bevel=0.03)
        # Each leaf hangs on its post and is swung 70 degrees in; u runs along the leaf from the hinge.
        hinge = (side * 2.5, 0)
        a = math.radians(180 - 70) if side > 0 else math.radians(70)
        along = (math.cos(a), math.sin(a))

        def at(u, z):
            return (hinge[0] + along[0] * u, hinge[1] + along[1] * u, z)

        yaw = a
        for z in (0.15, 1.0, 1.9):
            box(f"Rail_{side}_{z}", (2.4, 0.06, 0.06), at(1.2, z), M["galvanized"], r, rot=(0, 0, yaw), bevel=0)
        for k in range(7):
            box(f"Bar_{side}_{k}", (0.03, 0.03, 1.8), at(0.1 + k * 0.37, 1.05), M["galvanized"], r, rot=(0, 0, yaw), bevel=0)
        box(f"Mesh_{side}", (2.4, 0.01, 1.75), at(1.2, 1.05), M["wire"], r, rot=(0, 0, yaw), bevel=0)
    return r


def build_hedgehog():
    """Czech hedgehog anti-vehicle obstacle."""
    r = root("Prop_Hedgehog")
    for k, rot in enumerate(((math.radians(55), 0, 0), (math.radians(-55), 0, math.radians(90)),
                             (0, math.radians(55), math.radians(45)))):
        box(f"Beam_{k}", (0.12, 0.12, 2.0), (0, 0, 0.6), M["rust"], r, rot=rot, bevel=0.01)
    return r


def build_bunker():
    """Sandbagged fighting position under a camouflage net, open at the back (+Y)."""
    r = root("Prop_Bunker")
    half = 1.7
    for name, a, b in (("F", (-half, -half), (half, -half)), ("L", (-half, -half), (-half, half - 0.8)),
                       ("R", (half, -half), (half, half - 0.8))):
        sandbag_wall(f"Bags{name}", a, b, 4, M["sand"], r)
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"Pole_{sx}_{sy}", (sx * 1.9, sy * 1.9, 0), (sx * 1.9, sy * 1.9, 1.9), 0.04, M["wood"], r)
    net("Net", (4.6, 4.6), 2.0, [M["camo_a"], M["camo_b"]], r, sag=0.45, seed=3)
    box("Gun", (0.12, 1.0, 0.12), (0, -half + 0.2, 0.95), M["steel"], r, bevel=0)
    box("Crate", (0.6, 0.4, 0.35), (0.8, 0.6, 0.18), M["olive"], r, bevel=0.03)
    return r


def build_floodlight():
    """8 m light pole with four lamps and a small generator at its foot."""
    r = root("Prop_Floodlight")
    cylinder("Pole", 0.08, 8.0, (0, 0, 4.0), M["galvanized"], r, verts=10)
    box("Bar", (1.4, 0.1, 0.1), (0, 0, 8.0), M["steel"], r, bevel=0)
    for k in range(4):
        x = -0.55 + k * 0.37
        z = 8.25 if k % 2 else 7.8
        box(f"Housing_{k}", (0.32, 0.22, 0.3), (x, -0.05, z), M["steel"], r, bevel=0.02)
        box(f"Lens_{k}", (0.26, 0.02, 0.24), (x, -0.17, z), M["lamp"], r, bevel=0)
    box("Generator", (0.9, 0.6, 0.7), (0.6, 0.4, 0.35), M["olive"], r, bevel=0.04)
    return r


def build_crates(name, mat, size=(0.7, 0.45, 0.4)):
    """A pallet stacked with crates."""
    r = root(name)
    box("Pallet", (1.6, 1.2, 0.14), (0, 0, 0.07), M["wood_light"], r, bevel=0.01)
    rng = random.Random(len(name))
    for layer in range(3):
        for i in range(2):
            for j in range(2):
                if layer == 2 and rng.random() < 0.5:
                    continue
                box(f"Crate_{layer}_{i}_{j}", size, (-0.38 + i * 0.76, -0.28 + j * 0.54, 0.14 + size[2] / 2 + layer * size[2]),
                    mat, r, rot=(0, 0, rng.uniform(-0.05, 0.05)), bevel=0.02)
    return r


def build_drums():
    """Fuel drums on a pallet, some green, some orange."""
    r = root("Prop_Drums")
    box("Pallet", (1.8, 1.2, 0.14), (0, 0, 0.07), M["wood_light"], r, bevel=0.01)
    for i in range(3):
        for j in range(2):
            mat = M["drum_orange"] if (i + j) % 3 == 0 else M["drum_green"]
            cylinder(f"Drum_{i}_{j}", 0.29, 0.88, (-0.6 + i * 0.6, -0.3 + j * 0.6, 0.58), mat, r, verts=16)
    return r


def build_generator():
    r = root("Prop_Generator")
    box("Body", (1.0, 2.0, 1.1), (0, 0, 0.6), M["olive"], r, bevel=0.04)
    box("Skid", (1.1, 2.2, 0.1), (0, 0, 0.05), M["steel"], r, bevel=0)
    for k in range(6):
        box(f"Louver_{k}", (1.02, 0.05, 0.5), (0, -0.7 + k * 0.12, 0.7), M["steel"], r, bevel=0)
    cylinder("Exhaust", 0.06, 0.6, (0.3, 0.7, 1.4), M["steel"], r, verts=8)
    return r


def build_sandbag_wall():
    r = root("Prop_SandbagWall")
    sandbag_wall("Bags", (-1.5, 0), (1.5, 0), 3, M["sand"], r)
    return r


def build_trench():
    """A 4 m stretch of trench: sandbag lips either side of a dark plank floor, posts holding the sides."""
    r = root("Prop_Trench")
    box("Floor", (1.3, 4.0, 0.04), (0, 0, 0.02), M["dark"], r, bevel=0)
    for k in range(9):
        box(f"Plank_{k}", (1.2, 0.3, 0.03), (0, -1.8 + k * 0.45, 0.05), M["wood"], r, bevel=0)
    for side in (-1, 1):
        sandbag_wall(f"Lip_{side}", (side * 1.0, -2.0), (side * 1.0, 2.0), 2, M["sand"], r)
        box(f"Revetment_{side}", (0.06, 4.0, 0.35), (side * 0.68, 0, 0.18), M["wood"], r, bevel=0)
        for k in range(5):
            box(f"Post_{side}_{k}", (0.1, 0.1, 0.55), (side * 0.7, -1.8 + k * 0.9, 0.27), M["wood"], r, bevel=0)
    return r


# ---------- enemy command post ----------

def build_hq():
    """Two-storey concrete command building: flat roof with a sandbag parapet, antennas, a dish and a camo
    net, an outside staircase and a door with an awning at the front (-Y)."""
    r = root("HQ_Building")
    W, D, floor = 12.0, 9.0, 3.1
    H = floor * 2
    box("Walls", (W, D, H), (0, 0, H / 2), M["concrete"], r, bevel=0.04)
    box("Plinth", (W + 0.2, D + 0.2, 0.4), (0, 0, 0.2), M["concrete_stained"], r, bevel=0.02)
    box("FloorBand", (W + 0.08, D + 0.08, 0.2), (0, 0, floor), M["concrete_stained"], r, bevel=0)
    box("Roof", (W - 0.3, D - 0.3, 0.05), (0, 0, H + 0.02), M["roof_felt"], r, bevel=0)
    box("ParapetFront", (W + 0.1, 0.3, 0.5), (0, -D / 2, H + 0.25), M["concrete"], r)
    box("ParapetBack", (W + 0.1, 0.3, 0.5), (0, D / 2, H + 0.25), M["concrete"], r)
    box("ParapetL", (0.3, D, 0.5), (-W / 2, 0, H + 0.25), M["concrete"], r)
    box("ParapetR", (0.3, D, 0.5), (W / 2, 0, H + 0.25), M["concrete"], r)
    # Windows with dark glass and bars.
    for f in range(2):
        z = f * floor + 1.6
        for k in range(5):
            x = -W / 2 + 1.3 + k * (W - 2.6) / 4
            if f == 0 and k == 2:
                continue  # the door goes here
            for side in (-1, 1):
                box(f"Win_{f}_{k}_{side}", (1.1, 0.06, 1.0), (x, side * (D / 2 + 0.01), z), M["window"], r, bevel=0)
                box(f"Sill_{f}_{k}_{side}", (1.25, 0.15, 0.08), (x, side * (D / 2 + 0.06), z - 0.55), M["concrete_stained"], r, bevel=0)
        for k in range(3):
            y = -D / 2 + 1.5 + k * (D - 3) / 2
            box(f"WinSide_{f}_{k}", (0.06, 1.0, 1.0), (-W / 2 - 0.01, y, z), M["window"], r, bevel=0)
    box("Door", (1.8, 0.08, 2.3), (0, -D / 2 - 0.02, 1.15), M["steel"], r, bevel=0)
    box("Awning", (2.8, 1.2, 0.08), (0, -D / 2 - 0.6, 2.6), M["galvanized"], r, rot=(math.radians(-8), 0, 0), bevel=0)
    for side in (-1, 1):
        sphere(f"DoorLamp_{side}", 0.09, (side * 1.3, -D / 2 - 0.1, 2.3), M["lamp"], r)
        box(f"AC_{side}", (0.8, 0.3, 0.55), (W / 2 + 0.16, side * 2.5, 1.6 + (side + 1) * 1.6), M["white"], r, bevel=0.02)
    # Outside staircase up the right-hand wall to the roof.
    steps = 16
    for k in range(steps):
        y = -D / 2 + 0.8 + k * 0.42
        z = 0.2 + k * (H - 0.2) / steps
        box(f"Step_{k}", (1.0, 0.32, 0.05), (W / 2 + 0.75, y, z), M["steel"], r, bevel=0)
    strut("StringerA", (W / 2 + 0.25, -D / 2 + 0.6, 0), (W / 2 + 0.25, -D / 2 + 0.8 + steps * 0.42, H), 0.05, M["steel"], r)
    strut("StringerB", (W / 2 + 1.25, -D / 2 + 0.6, 0), (W / 2 + 1.25, -D / 2 + 0.8 + steps * 0.42, H), 0.05, M["steel"], r)
    strut("Handrail", (W / 2 + 1.25, -D / 2 + 0.6, 1.0), (W / 2 + 1.25, -D / 2 + 0.8 + steps * 0.42, H + 1.0), 0.03, M["steel"], r)
    # Roof: sandbag parapet at the front, a camo-netted shelter at the back left, kit, antennas and a dish.
    sandbag_wall("RoofBags", (-W / 2 + 0.5, -D / 2 + 0.5), (W / 2 - 0.5, -D / 2 + 0.5), 2, M["sand"], r)
    for obj in [o for o in r.children if o.name.startswith("RoofBags")]:
        obj.location.z += H + 0.05
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"NetPole_{sx}_{sy}", (-W / 4 + sx * 2.6, D / 4 + sy * 1.8 - 0.4, H), (-W / 4 + sx * 2.6, D / 4 + sy * 1.8 - 0.4, H + 2.0), 0.04, M["wood"], r)
    net("RoofNet", (6.4, 4.6), H + 2.1, [M["camo_a"], M["camo_b"]], r, sag=0.5, seed=7)
    for obj in [o for o in r.children if o.name.startswith("RoofNet")]:
        obj.location = (-W / 4, D / 4 - 0.4, 0)
    for k in range(3):
        box(f"RoofCrate_{k}", (0.8, 0.6, 0.5), (-W / 4 - 1.5 + k * 1.1, D / 4, H + 0.3), M["olive"], r, bevel=0.03)
        box(f"RoofUnit_{k}", (0.9, 0.9, 0.7), (W / 4 - 1.2 + k * 1.2, -0.6, H + 0.4), M["galvanized"], r, bevel=0.03)
    for k, (x, y, h) in enumerate(((W / 4 - 0.5, D / 4 + 0.5, 5.0), (W / 4 + 1.2, D / 4 + 1.0, 6.5), (W / 4 + 2.6, D / 4 + 0.3, 4.5))):
        strut(f"Antenna_{k}", (x, y, H), (x, y, H + h), 0.05, M["steel"], r)
        for m in range(3):
            a = 2 * math.pi * m / 3
            box(f"AntPanel_{k}_{m}", (0.18, 0.08, 0.8), (x + math.cos(a) * 0.15, y + math.sin(a) * 0.15, H + h - 0.6), M["white"], r,
                rot=(0, 0, a + R90), bevel=0)
            strut(f"Guy_{k}_{m}", (x, y, H + h * 0.7), (x + math.cos(a) * 1.4, y + math.sin(a) * 1.4, H), 0.008, M["steel"], r, verts=4)
    dish = cone("Dish", 0.75, 0.15, 0.28, (W / 2 - 1.3, D / 2 - 1.2, H + 1.2), M["white"], r, verts=24)
    dish.rotation_euler = (math.radians(55), 0, math.radians(200))
    strut("DishPost", (W / 2 - 1.3, D / 2 - 1.2, H), (W / 2 - 1.3, D / 2 - 1.2, H + 1.0), 0.05, M["steel"], r)
    return r


def build_watchtower():
    """Wooden watchtower: four splayed legs with X braces, a walled platform at 6 m and a pyramid roof."""
    r = root("HQ_Watchtower")
    top, base_half, top_half = 6.0, 1.6, 1.2
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"Leg_{sx}_{sy}", (sx * base_half, sy * base_half, 0), (sx * top_half, sy * top_half, top + 2.4), 0.09, M["wood"], r)
    for lv, (z0, z1) in enumerate(((0.3, 3.0), (3.0, 5.8))):
        hw0 = base_half - (base_half - top_half) * z0 / (top + 2.4)
        hw1 = base_half - (base_half - top_half) * z1 / (top + 2.4)
        for name, a0, a1, b0, b1 in (("N", (-hw0, hw0), (hw1, hw1), (hw0, hw0), (-hw1, hw1)),
                                     ("S", (-hw0, -hw0), (hw1, -hw1), (hw0, -hw0), (-hw1, -hw1)),
                                     ("E", (hw0, -hw0), (hw1, hw1), (hw0, hw0), (hw1, -hw1)),
                                     ("W", (-hw0, -hw0), (-hw1, hw1), (-hw0, hw0), (-hw1, -hw1))):
            strut(f"X1_{lv}_{name}", (*a0, z0), (*a1, z1), 0.05, M["wood"], r, verts=4)
            strut(f"X2_{lv}_{name}", (*b0, z0), (*b1, z1), 0.05, M["wood"], r, verts=4)
    box("Platform", (3.0, 3.0, 0.15), (0, 0, top), M["wood_light"], r, bevel=0.01)
    for name, size, loc in (("WallN", (3.0, 0.08, 1.1), (0, 1.5, top + 0.6)), ("WallS", (3.0, 0.08, 1.1), (0, -1.5, top + 0.6)),
                            ("WallE", (0.08, 3.0, 1.1), (1.5, 0, top + 0.6)), ("WallW", (0.08, 3.0, 1.1), (-1.5, 0, top + 0.6))):
        box(name, size, loc, M["wood_light"], r, bevel=0)
    roof_v = [(-1.8, -1.8, top + 2.4), (1.8, -1.8, top + 2.4), (1.8, 1.8, top + 2.4), (-1.8, 1.8, top + 2.4), (0, 0, top + 3.4)]
    roof_f = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4), (3, 2, 1, 0)]
    mesh_object("Roof", roof_v, roof_f, M["wood"], r, smooth=False)
    sandbag_wall("TopBags", (-1.3, -1.25), (1.3, -1.25), 1, M["sand"], r)
    for obj in [o for o in r.children if o.name.startswith("TopBags")]:
        obj.location.z += top + 1.15
    for k in range(14):
        z = 0.3 + k * 0.42
        box(f"Rung_{k}", (0.6, 0.05, 0.05), (0, -base_half - 0.25 + k * 0.012, z), M["wood"], r, bevel=0)
    strut("LadderL", (-0.3, -base_half - 0.25, 0), (-0.3, -base_half - 0.08, top), 0.035, M["wood"], r, verts=4)
    strut("LadderR", (0.3, -base_half - 0.25, 0), (0.3, -base_half - 0.08, top), 0.035, M["wood"], r, verts=4)
    box("Searchlight", (0.4, 0.5, 0.35), (1.0, -1.1, top + 1.35), M["steel"], r, bevel=0.02)
    box("SearchlightLens", (0.32, 0.02, 0.28), (1.0, -1.36, top + 1.35), M["lamp"], r, bevel=0)
    return r


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builds = [
        ("Base_Hangar", build_hangar, 22.0, 2.5),
        ("Base_LaunchPad", build_launch_pad, 11.0, 0.0),
        ("Base_FuelTanks", build_fuel_tanks, 9.0, 1.0),
        ("Base_Mast", build_mast, 20.0, 7.0),
        ("Base_Dish", build_dish, 5.0, 1.5),
        ("Base_Tent", build_tent, 12.0, 1.5),
        ("Prop_Container_Olive", lambda: build_container("Prop_Container_Olive", M["container_olive"]), 10.0, 1.2),
        ("Prop_Container_Rust", lambda: build_container("Prop_Container_Rust", M["container_rust"]), 10.0, 1.2),
        ("Prop_Hesco", build_hesco, 3.5, 0.6),
        ("Prop_Gate", build_gate, 9.0, 1.0),
        ("Prop_Hedgehog", build_hedgehog, 4.0, 0.5),
        ("Prop_Bunker", build_bunker, 8.0, 0.8),
        ("Prop_Floodlight", build_floodlight, 11.0, 4.0),
        ("Prop_Crates", lambda: build_crates("Prop_Crates", M["olive"]), 4.0, 0.6),
        ("Prop_WoodCrates", lambda: build_crates("Prop_WoodCrates", M["wood_light"], (0.7, 0.5, 0.5)), 4.0, 0.6),
        ("Prop_Drums", build_drums, 4.0, 0.5),
        ("Prop_Generator", build_generator, 4.5, 0.6),
        ("Prop_SandbagWall", build_sandbag_wall, 5.0, 0.3),
        ("Prop_Trench", build_trench, 6.0, 0.2),
        ("HQ_Building", build_hq, 24.0, 3.5),
        ("HQ_Watchtower", build_watchtower, 14.0, 4.0),
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
