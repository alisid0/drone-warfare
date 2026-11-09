"""Builds the wildlife for the Mimic hunt in Blender and exports each as an FBX for Unity: a crow-sized bird and its
robot twin, the Dravin spy drone built to pass for one.

Run from the project root (Blender 4.x or 5.x):
    blender --background --factory-startup --python Tools/Blender/make_wildlife.py -- Assets/DroneSandbox/Resources/Models [preview_dir]

Real metres, Z up, beak down Blender's -Y (which becomes +Z, forward, in Unity). About 0.45 m long with a 1 m
wingspan, a little big for a crow so it reads from a drone's camera.

Each model is a root empty with:
- the body ("Body_<material>": body, head, beak, tail, tucked feet);
- "Wing_L" and "Wing_R": pivots at the shoulders, each holding the inner wing and "Hand_L"/"Hand_R", the outer
  wing (the primaries) with its origin at the wrist, so the game can flap them and flex the wing as it beats (the
  hand is a mesh, not a pivot holding one: three levels down, the FBX export would turn it on its side);
- on the robot only, what gives it away at close range: "Servo_L"/"Servo_R" housings at the shoulders (material
  Servo, which the game makes glint), a seam down the back and lens eyes. Everything else, the silhouette and the
  colours, is the same as the real bird's, because from any distance the two must look alike.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (box, cone, export_model, material, mesh_object, pivot, render_preview, reset_scene, root,  # noqa: E402
                    set_parent, sphere)

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 and args[1] else None

M = {}


def make_materials():
    M["feather"] = material("Feather", (0.030, 0.030, 0.036), roughness=0.55)
    M["beak"] = material("Beak", (0.020, 0.020, 0.022), roughness=0.35)
    M["servo"] = material("Servo", (0.30, 0.31, 0.32), metallic=1.0, roughness=0.28)
    M["lens"] = material("Lens", (0.010, 0.010, 0.012), roughness=0.05)
    M["eye"] = material("Eye", (0.010, 0.008, 0.006), roughness=0.15)


def solidify(obj, thickness):
    mod = obj.modifiers.new("Solidify", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = 0.0
    return obj


# One wing, spreading along +X from the shoulder, its leading edge forward (-Y), in two parts that meet at the
# wrist (x = 0.22): the broad arm, and the hand with the long fingered primaries a crow spreads at the tip.
# Slightly cambered (raised along the middle of the chord).
WRIST = 0.22
ARM = [
    (0.00, -0.055), (0.10, -0.075), (WRIST, -0.075), (WRIST, 0.135), (0.190, 0.138), (0.090, 0.128),
    (0.020, 0.100), (0.00, 0.070),
]
HAND = [
    (WRIST, -0.075), (0.33, -0.060), (0.41, -0.035), (0.47, -0.005), (0.445, 0.015), (0.485, 0.030),
    (0.445, 0.048), (0.475, 0.066), (0.425, 0.080), (0.445, 0.100), (0.380, 0.112), (0.300, 0.128), (WRIST, 0.135),
]


def wing_surface(name, outline, middle, side, shoulder, parent):
    """A fan of triangles round `middle`, spanning the outline, as a thin solid."""
    sx, sy, sz = shoulder
    def lift(x, y):
        camber = 0.012 * math.sin(math.pi * min(1.0, max(0.0, (y + 0.075) / 0.21)))
        return sz + camber + 0.02 * (x / 0.48) ** 2  # bends up a little to the tip
    verts = [(sx + side * x, sy + y, lift(x, y)) for x, y in outline]
    mx, my = middle
    verts.append((sx + side * mx, sy + my, lift(mx, my) + 0.004))
    centre = len(verts) - 1
    faces = []
    for i in range(len(outline)):
        a, b = i, (i + 1) % len(outline)
        faces.append((centre, a, b) if side > 0 else (centre, b, a))
    obj = mesh_object(name, verts, faces, M["feather"], parent, smooth=True)
    return solidify(obj, 0.008)


def wing(label, side, shoulder, parent):
    """The inner wing on the shoulder pivot, and the outer wing beside it with its origin at the wrist."""
    sx, sy, sz = shoulder
    wing_surface(f"Feathers_{label}", ARM, (0.11, 0.03), side, shoulder, parent)
    hand = wing_surface(f"Hand_{label}", HAND, (0.33, 0.04), side, shoulder, parent)
    wrist = Vector((sx + side * WRIST, sy + 0.03, sz + 0.012 + 0.02 * (WRIST / 0.48) ** 2))
    hand.data.transform(Matrix.Translation(-wrist))
    hand.location = wrist
    return hand


def tail(parent):
    verts = [(-0.022, 0.17, 0.0), (0.022, 0.17, 0.0), (0.050, 0.34, -0.012), (0.0, 0.355, -0.012), (-0.050, 0.34, -0.012)]
    obj = mesh_object("Tail", verts, [(0, 1, 2, 3, 4)], M["feather"], parent, smooth=True)
    return solidify(obj, 0.01)


def build_bird(name, robot):
    rig = root(name)
    # Body, head and beak: a long, sleek teardrop, a small head a little raised and drawn forward, a stout beak.
    sphere("Body", 1.0, (0, 0.03, 0), M["feather"], rig, scale=(0.072, 0.19, 0.067))
    sphere("Head", 0.042, (0, -0.18, 0.03), M["feather"], rig, scale=(0.9, 1.2, 0.9))
    beak = cone("Beak", 0.017, 0.003, 0.072, (0, -0.258, 0.027), M["beak"], rig, verts=12)
    beak.rotation_euler = (math.radians(90), 0, 0)
    tail(rig)
    for side in (-1, 1):
        sphere(f"Foot_{'L' if side > 0 else 'R'}", 0.014, (side * 0.025, 0.07, -0.06), M["beak"], rig, scale=(1, 1.8, 0.7))
        eye_mat = M["lens"] if robot else M["eye"]
        sphere(f"Eye_{'L' if side > 0 else 'R'}", 0.008 if robot else 0.006, (side * 0.032, -0.2, 0.044), eye_mat, rig)
    for side, label in ((1, "L"), (-1, "R")):
        at = (side * 0.05, -0.035, 0.03)
        shoulder = pivot(f"Wing_{label}", at, rig)
        wing(label, side, at, shoulder)
        if robot:
            # A servo housing where the wing meets the body, mostly hidden under the feathers.
            bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.012, depth=0.022,
                                                      location=(side * 0.05, -0.035, 0.036),
                                                      rotation=(0, math.radians(90), 0))
            servo = bpy.context.active_object
            servo.name = f"Servo_{label}"
            servo.data.materials.append(M["servo"])
            set_parent(servo, rig)
    if robot:
        box("Seam", (0.004, 0.18, 0.004), (0, 0.02, 0.064), M["servo"], rig, bevel=0.0)
    return rig


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, robot in (("Wildlife_Crow", False), ("Wildlife_CrowRobot", True)):
        reset_scene()
        make_materials()
        rig = build_bird(name, robot)
        export_model(rig, os.path.join(OUT_DIR, name + ".fbx"))
        print("exported", name)
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), 0.9)


main()
