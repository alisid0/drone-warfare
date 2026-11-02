"""Builds the player drones in Blender and exports each one as an FBX for Unity.

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_drones.py -- Assets/DroneSandbox/Resources/Models [preview_dir] [names]

Names, comma-separated (e.g. Drone_Nightjar), rebuild just those models. Each drone is a root empty with its parts as children.
Propellers are separate objects named "Prop_N" with their origin at the hub, so the game can spin them, and the
Nightjar's bombs are objects named "Bomb_N" so the game can hide each one as it drops. Noses point down
Blender's -Y, which becomes +Z (forward) in Unity.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import box, box_uv_all, cylinder, export, material, render_preview, reset_scene, root, sphere, strut  # noqa: E402

import bpy  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 else None

# ---------- shared parts ----------

M = {}


def make_materials():
    M["carbon"] = material("Carbon", (0.025, 0.025, 0.028), roughness=0.35)
    M["shell_gray"] = material("ShellGray", (0.10, 0.105, 0.11), roughness=0.5)
    M["olive"] = material("Olive", (0.13, 0.15, 0.08), roughness=0.75)
    M["motor"] = material("MotorMetal", (0.35, 0.36, 0.38), metallic=0.9, roughness=0.3)
    M["prop"] = material("Prop", (0.03, 0.03, 0.03), roughness=0.4)
    M["lens"] = material("Lens", (0.01, 0.01, 0.02), metallic=0.2, roughness=0.05)
    M["led_green"] = material("LedGreen", (0.1, 0.9, 0.2), emission=(0.1, 1.0, 0.2))
    M["led_red"] = material("LedRed", (0.9, 0.1, 0.05), emission=(1.0, 0.1, 0.05))
    M["warhead"] = material("Warhead", (0.20, 0.21, 0.12), roughness=0.7)
    M["tip_red"] = material("TipRed", (0.45, 0.06, 0.04), roughness=0.6)
    M["white"] = material("MissileWhite", (0.75, 0.75, 0.72), roughness=0.5)


def propeller(name, parent, hub, diameter, blades=2):
    """Blades with a little pitch twist; origin at the hub so the game can spin it."""
    parts = []
    hub_obj = cylinder(name, 0.018, 0.03, hub, M["motor"], None, verts=16)
    blade_len = diameter / 2
    for i in range(blades):
        a = 2 * math.pi * i / blades
        cx = hub[0] + math.cos(a) * blade_len / 2
        cy = hub[1] + math.sin(a) * blade_len / 2
        blade = box(f"{name}_b{i}", (blade_len, diameter * 0.075, 0.006), (cx, cy, hub[2] + 0.004),
                    M["prop"], None, rot=(math.radians(12), 0, a), bevel=0.002)
        parts.append(blade)
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    hub_obj.select_set(True)
    bpy.context.view_layer.objects.active = hub_obj
    bpy.ops.object.join()
    hub_obj.parent = parent
    return hub_obj


def arm_with_motor(parent, i, angle, inner, length, tube, motor_r, prop_d, z):
    """A carbon tube arm from the body out to a motor and propeller."""
    mid = inner + length / 2
    cylinder(f"Arm_{i}", tube, length, (math.cos(angle) * mid, math.sin(angle) * mid, z), M["carbon"], parent,
             rot=(0, math.radians(90), angle), verts=12)
    tip = inner + length
    mx, my = math.cos(angle) * tip, math.sin(angle) * tip
    cylinder(f"Motor_{i}", motor_r, motor_r * 1.3, (mx, my, z + motor_r * 0.6), M["motor"], parent)
    propeller(f"Prop_{i}", parent, (mx, my, z + motor_r * 1.4), prop_d)


# ---------- drones ----------

def build_scout():
    r = root("Drone_Scout")
    # Body: lower frame plate plus a rounded top shell, nose toward -Y.
    box("Frame", (0.30, 0.46, 0.04), (0, 0, 0), M["carbon"], r, bevel=0.008)
    box("Shell", (0.24, 0.38, 0.09), (0, 0.01, 0.06), M["shell_gray"], r, bevel=0.03)
    cylinder("GpsPuck", 0.045, 0.02, (0, 0.08, 0.115), M["shell_gray"], r)
    for i, a in enumerate([45, 135, 225, 315]):
        arm_with_motor(r, i, math.radians(a), 0.12, 0.30, 0.014, 0.035, 0.36, 0.0)
    # Camera gimbal under the nose.
    cylinder("GimbalMount", 0.02, 0.04, (0, -0.17, -0.04), M["motor"], r)
    sphere("CameraBall", 0.045, (0, -0.17, -0.08), M["shell_gray"], r)
    cylinder("CameraLens", 0.022, 0.02, (0, -0.21, -0.08), M["lens"], r, rot=(math.radians(90), 0, 0))
    # Landing skids.
    for side in (-1, 1):
        cylinder(f"SkidLeg_{side}", 0.006, 0.12, (side * 0.12, 0, -0.07), M["carbon"], r, verts=8)
        cylinder(f"Skid_{side}", 0.008, 0.36, (side * 0.12, 0, -0.13), M["carbon"], r,
                 rot=(math.radians(90), 0, 0), verts=8)
    # Nav lights: green at the nose, red at the tail.
    sphere("LedFront", 0.012, (0, -0.235, 0.0), M["led_green"], r)
    sphere("LedRear", 0.012, (0, 0.235, 0.0), M["led_red"], r)
    return r


def build_strike():
    r = root("Drone_Strike")
    box("Frame", (0.50, 0.70, 0.06), (0, 0, 0), M["carbon"], r, bevel=0.01)
    box("Shell", (0.42, 0.62, 0.16), (0, 0, 0.10), M["olive"], r, bevel=0.05)
    box("Battery", (0.24, 0.36, 0.08), (0, 0.06, 0.21), M["carbon"], r, bevel=0.015)
    cylinder("GpsMast", 0.008, 0.10, (0, 0.20, 0.28), M["carbon"], r, verts=8)
    cylinder("GpsPuck", 0.05, 0.022, (0, 0.20, 0.34), M["olive"], r)
    for i in range(6):
        a = math.radians(30 + 60 * i)
        arm_with_motor(r, i, a, 0.20, 0.46, 0.022, 0.05, 0.50, 0.02)
    # Twin missile rails under the body.
    for side in (-1, 1):
        x = side * 0.17
        box(f"Rail_{side}", (0.03, 0.55, 0.03), (x, 0, -0.06), M["carbon"], r, bevel=0.005)
        cylinder(f"Missile_{side}", 0.035, 0.55, (x, -0.02, -0.11), M["white"], r, rot=(math.radians(90), 0, 0))
        sphere(f"MissileSeeker_{side}", 0.035, (x, -0.295, -0.11), M["tip_red"], r, scale=(1, 1.6, 1))
        for k in range(4):
            fa = math.radians(45 + 90 * k)
            box(f"Fin_{side}_{k}", (0.004, 0.06, 0.05),
                (x + math.cos(fa) * 0.045, 0.23, -0.11 + math.sin(fa) * 0.045), M["white"], r,
                rot=(0, fa, 0), bevel=0)
    cylinder("TargetingPod", 0.05, 0.1, (0, -0.30, -0.06), M["olive"], r, rot=(math.radians(90), 0, 0))
    cylinder("PodLens", 0.035, 0.012, (0, -0.355, -0.06), M["lens"], r, rot=(math.radians(90), 0, 0))
    sphere("LedFront", 0.015, (0, -0.36, 0.02), M["led_green"], r)
    sphere("LedRear", 0.015, (0, 0.36, 0.02), M["led_red"], r)
    return r


def build_kamikaze():
    r = root("Drone_Kamikaze")
    # Bare FPV racing frame with a warhead strapped underneath, pointing forward.
    box("Frame", (0.10, 0.22, 0.02), (0, 0, 0), M["carbon"], r, bevel=0.004)
    box("Stack", (0.07, 0.08, 0.04), (0, 0.01, 0.03), M["carbon"], r, bevel=0.006)
    box("Battery", (0.06, 0.14, 0.035), (0, 0.02, 0.07), M["shell_gray"], r, bevel=0.008)
    box("FpvCam", (0.035, 0.03, 0.03), (0, -0.10, 0.03), M["shell_gray"], r, rot=(math.radians(-20), 0, 0), bevel=0.004)
    cylinder("FpvLens", 0.011, 0.012, (0, -0.118, 0.035), M["lens"], r, rot=(math.radians(70), 0, 0), verts=12)
    for i, a in enumerate([45, 135, 225, 315]):
        arm_with_motor(r, i, math.radians(a), 0.05, 0.13, 0.009, 0.025, 0.20, 0.0)
    cylinder("Antenna", 0.003, 0.09, (0, 0.11, 0.06), M["carbon"], r, rot=(math.radians(-35), 0, 0), verts=6)
    # Warhead: an RPG-style round with a stand-off probe.
    cylinder("Warhead", 0.04, 0.30, (0, -0.03, -0.06), M["warhead"], r, rot=(math.radians(90), 0, 0))
    sphere("WarheadNose", 0.04, (0, -0.18, -0.06), M["warhead"], r, scale=(1, 1.8, 1))
    cylinder("Probe", 0.006, 0.08, (0, -0.28, -0.06), M["motor"], r, rot=(math.radians(90), 0, 0), verts=8)
    cylinder("Band", 0.043, 0.02, (0, 0.05, -0.06), M["tip_red"], r, rot=(math.radians(90), 0, 0))
    for side in (-1, 1):
        box(f"Strap_{side}", (0.01, 0.012, 0.07), (side * 0.03, 0.0, -0.025), M["carbon"], r, bevel=0)
    return r


def join(name, parts, parent):
    """Joins parts into one object called name (so the game can show or hide it as a whole)."""
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.parent = parent
    return obj


def coax_arm(parent, i, angle, inner, length, tube, motor_r, prop_d, z):
    """A heavy-lift arm ending in a pair of motors, one propeller above and one below (coaxial X8)."""
    mid = inner + length / 2
    cylinder(f"Arm_{i}", tube, length, (math.cos(angle) * mid, math.sin(angle) * mid, z), M["carbon"], parent,
             rot=(0, math.radians(90), angle), verts=12)
    tip = inner + length
    mx, my = math.cos(angle) * tip, math.sin(angle) * tip
    cylinder(f"MotorTop_{i}", motor_r, motor_r * 1.2, (mx, my, z + motor_r * 0.75), M["motor"], parent)
    cylinder(f"MotorLow_{i}", motor_r, motor_r * 1.2, (mx, my, z - motor_r * 0.75), M["motor"], parent)
    propeller(f"Prop_{i}", parent, (mx, my, z + motor_r * 1.5), prop_d)
    propeller(f"Prop_{i}_low", parent, (mx, my, z - motor_r * 1.6), prop_d)


def build_nightjar():
    r = root("Drone_Nightjar")
    # Heavy night bomber: a long fuselage on four coaxial arms, twin batteries, a thermal turret under the nose
    # and a rack of three bombs. Each bomb is one object, "Bomb_N", so the game can hide it once dropped.
    box("Frame", (0.42, 0.82, 0.06), (0, 0, 0), M["carbon"], r, bevel=0.01)
    box("Shell", (0.34, 0.70, 0.17), (0, 0.01, 0.11), M["olive"], r, bevel=0.05)
    for side in (-1, 1):
        box(f"Battery_{side}", (0.13, 0.42, 0.09), (side * 0.075, 0.06, 0.24), M["carbon"], r, bevel=0.012)
    cylinder("GpsMast", 0.008, 0.12, (0, 0.30, 0.31), M["carbon"], r, verts=8)
    cylinder("GpsPuck", 0.055, 0.024, (0, 0.30, 0.38), M["olive"], r)
    for i, a in enumerate([45, 135, 225, 315]):
        coax_arm(r, i, math.radians(a), 0.20, 0.52, 0.026, 0.055, 0.62, 0.02)

    # Thermal turret: a ball with a big thermal lens and a small day camera.
    cylinder("TurretMount", 0.03, 0.05, (0, -0.33, -0.04), M["motor"], r)
    sphere("TurretBall", 0.075, (0, -0.33, -0.11), M["shell_gray"], r)
    cylinder("ThermalLens", 0.034, 0.02, (-0.022, -0.40, -0.11), M["lens"], r, rot=(math.radians(90), 0, 0))
    cylinder("DayLens", 0.016, 0.02, (0.035, -0.395, -0.10), M["lens"], r, rot=(math.radians(90), 0, 0))

    # Bomb rack and three finned bombs in a row, noses forward.
    box("Rack", (0.16, 0.62, 0.03), (0, 0.04, -0.05), M["carbon"], r, bevel=0.005)
    for k, (x, y) in enumerate([(0, -0.16), (0, 0.06), (0, 0.28)]):
        z = -0.115
        parts = [cylinder(f"BombBody_{k}", 0.042, 0.17, (x, y, z), M["warhead"], None, rot=(math.radians(90), 0, 0)),
                 sphere(f"BombNose_{k}", 0.042, (x, y - 0.085, z), M["warhead"], None, scale=(1, 1.7, 1)),
                 cylinder(f"BombBand_{k}", 0.044, 0.015, (x, y - 0.05, z), M["tip_red"], None, rot=(math.radians(90), 0, 0))]
        for f in range(4):
            fa = math.radians(45 + 90 * f)
            parts.append(box(f"BombFin_{k}_{f}", (0.004, 0.06, 0.05),
                             (x + math.cos(fa) * 0.04, y + 0.10, z + math.sin(fa) * 0.04), M["motor"], None,
                             rot=(0, fa, 0), bevel=0))
        join(f"Bomb_{k}", parts, r)

    # Tall landing legs, so the bombs clear the ground.
    for sx in (-1, 1):
        for sy in (-1, 1):
            strut(f"Leg_{sx}_{sy}", (sx * 0.15, sy * 0.26, -0.03), (sx * 0.25, sy * 0.30, -0.33), 0.012, M["carbon"], r)
        cylinder(f"Skid_{sx}", 0.014, 0.78, (sx * 0.25, 0, -0.33), M["carbon"], r, rot=(math.radians(90), 0, 0), verts=10)
    sphere("LedFront", 0.016, (0, -0.42, 0.03), M["led_green"], r)
    sphere("LedRear", 0.016, (0, 0.42, 0.03), M["led_red"], r)
    return r


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    builds = [("Drone_Scout", build_scout, 1.4), ("Drone_Strike", build_strike, 2.2), ("Drone_Kamikaze", build_kamikaze, 0.9),
              ("Drone_Nightjar", build_nightjar, 2.8)]
    only = set(args[2].split(",")) if len(args) > 2 else None
    for name, build, cam_distance in builds:
        if only and name not in only:
            continue
        reset_scene()
        M.clear()
        make_materials()
        r = build()
        box_uv_all(r)  # real-scale UVs for Unity's photo textures
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name}")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), cam_distance)
            print(f"PREVIEW {name}")


main()
