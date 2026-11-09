"""Builds the player drones in Blender and exports each one as an FBX for Unity.

Run from the project root (Blender 4.x or 5.x):
    blender --background --factory-startup --python Tools/Blender/make_drones.py -- Assets/DroneSandbox/Resources/Models [preview_dir] [names]

Names, comma-separated (e.g. Drone_Nightjar), rebuild just those models. Real metres, Z up, noses down Blender's -Y,
which becomes +Z (forward) in Unity. The game draws them bigger than life (Drone.cs) so they read from the RTS camera.

Each drone is a root empty with:
- the airframe, a few meshes ("Body_<material>"), one per material (kit.Kit);
- one object per propeller, "Prop_N" (the Nightjar's lower ones "Prop_N_low"), with its origin at the hub so the
  game can spin it;
- over each propeller "Blur_N", a flat disc whose UVs run out from the hub along U: the game draws it as the faint
  blur of the spinning blades (material PropBlur);
- on the Nightjar, its bombs "Bomb_N", so the game can hide each one as it drops.
Lights: LedRed on the left arms and LedGreen on the right (as on aircraft), Strobe on top; the game makes them glow
and blinks the strobe. TealMark is the Norvane identification paint.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import box_uv_all, export, material, render_preview, reset_scene, root, set_parent  # noqa: E402
from kit import R90, TO_FWD, Kit, aim, rotm  # noqa: E402

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(args[0] if args else ".")
PREVIEW_DIR = os.path.abspath(args[1]) if len(args) > 1 and args[1] else None

M = {}
PROPS = []  # (name, hub, radius) of every propeller, for its blur disc


def make_materials():
    M["carbon"] = material("Carbon", (0.022, 0.022, 0.025), roughness=0.35)
    M["shell"] = material("ShellGray", (0.10, 0.105, 0.11), roughness=0.5)
    M["olive"] = material("Olive", (0.16, 0.18, 0.10), roughness=0.8)  # photo-textured in Unity
    M["motor"] = material("MotorMetal", (0.35, 0.36, 0.38), metallic=0.9, roughness=0.3)
    M["bell"] = material("MotorBell", (0.05, 0.05, 0.055), metallic=0.8, roughness=0.35)
    M["prop"] = material("Prop", (0.03, 0.03, 0.032), roughness=0.4)
    M["lens"] = material("Lens", (0.01, 0.01, 0.02), metallic=0.2, roughness=0.05)
    M["rubber"] = material("Rubber", (0.025, 0.025, 0.025), roughness=0.9)
    M["battery"] = material("Battery", (0.045, 0.05, 0.055), roughness=0.6)
    M["strap"] = material("Strap", (0.05, 0.05, 0.035), roughness=0.9)
    M["teal"] = material("TealMark", (0.012, 0.23, 0.29), roughness=0.5)
    M["led_green"] = material("LedGreen", (0.1, 0.9, 0.2), emission=(0.1, 1.0, 0.2))
    M["led_red"] = material("LedRed", (0.9, 0.1, 0.05), emission=(1.0, 0.1, 0.05))
    M["strobe"] = material("Strobe", (1.0, 1.0, 1.0), emission=(1.0, 1.0, 1.0))
    M["warhead"] = material("Warhead", (0.20, 0.21, 0.12), roughness=0.7)
    M["tip_red"] = material("TipRed", (0.45, 0.06, 0.04), roughness=0.6)
    M["white"] = material("MissileWhite", (0.62, 0.63, 0.6), roughness=0.5)
    M["blur"] = material("PropBlur", (0.04, 0.04, 0.04), roughness=0.5)


# ---------- shapes ----------

def squircle(w, h, n=16, e=3.0):
    """(x, z) points round a rounded rectangle w wide and h high (e = 2 is an ellipse; higher is squarer)."""
    pts = []
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n
        c, s = math.cos(a), math.sin(a)
        pts.append((math.copysign(abs(c) ** (2 / e), c) * w / 2, math.copysign(abs(s) ** (2 / e), s) * h / 2))
    return pts


def fuselage(kit, mat, sections, n=16, e=3.0):
    """A body lofted through sections (y, width, height, centre z), nose first."""
    kit.loft(mat, [[Vector((x, y, zc + z)) for x, z in squircle(w, h, n, e)] for y, w, h, zc in sections])


def sweep(kit, mat, stations, n=10, e=2.6):
    """A beam of rounded section through stations (point, width, height): arms, legs, booms. The section's height
    stays as near vertical as the beam allows."""
    pts = [Vector(p) for p, _, _ in stations]
    loops = []
    for i, (_, w, h) in enumerate(stations):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        side = d.cross(Vector((0, 0, 1)))
        side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
        up = side.cross(d).normalized()
        loops.append([pts[i] + side * x + up * z for x, z in squircle(w, h, n, e)])
    kit.loft(mat, loops)


def ball(kit, mat, c, r, n=16, rings=8, squash=1.0, rot=None):
    profile = [(-r * squash * math.cos(math.pi * i / rings), r * math.sin(math.pi * i / rings)) for i in range(rings + 1)]
    kit.lathe(mat, profile, c, rot, n=n)


def lens(kit, c, r, rot=TO_FWD, rim=True):
    """A camera lens looking along rot's +Z (forward by default) with a metal rim."""
    kit.cyl(M["lens"], r, r * 0.5, c, rot, n=14)
    if rim:
        kit.lathe(M["motor"], [(-r * 0.3, r * 1.25), (r * 0.3, r * 1.25), (r * 0.3, r * 1.02), (-r * 0.3, r * 1.02)],
                  c, rot, n=14, closed=True)


def motor(kit, c, r, h, flip=False):
    """A brushless outrunner on its mount at c, standing up (or hanging down when flip): the stator base, the dark
    bell with a bright band where the windings show through its slots, the shaft and prop nut. Returns the hub,
    where the propeller sits."""
    rot = rotm((math.pi, 0, 0)) if flip else None
    s = -1 if flip else 1
    c = Vector(c)
    kit.cyl(M["motor"], r * 0.82, h * 0.22, c + Vector((0, 0, s * h * 0.11)), rot, n=16)
    kit.lathe(M["bell"], [(h * 0.2, r * 0.94), (h * 0.26, r), (h * 0.86, r), (h * 0.97, r * 0.86), (h, r * 0.55), (h, 0)],
              c, rot, n=20)
    kit.cyl(M["motor"], r * 1.012, h * 0.16, c + Vector((0, 0, s * h * 0.5)), rot, n=20, caps=False)
    kit.cyl(M["motor"], r * 0.13, h * 0.32, c + Vector((0, 0, s * h * 1.12)), rot, n=8)
    kit.cyl(M["motor"], r * 0.3, h * 0.12, c + Vector((0, 0, s * h * 1.22)), rot, n=6)
    return c + Vector((0, 0, s * h * 1.12))


def prop(name, hub, diameter, chord, parent, blades=2, flip=False):
    """A propeller: lofted blades with an airfoil section, twisted from steep at the root to shallow at the tip,
    and a spinner. One object with its origin at the hub."""
    kit = Kit()
    R = diameter / 2
    up = Vector((0, 0, -1 if flip else 1))
    # (fraction of the radius, fraction of the chord, twist in degrees)
    stations = [(0.08, 0.5, 30), (0.2, 0.95, 26), (0.38, 1.0, 20), (0.58, 0.88, 15), (0.76, 0.72, 11),
                (0.9, 0.55, 9), (0.97, 0.38, 8), (1.0, 0.16, 8)]
    foil = [(0.5, 0.0), (0.33, 0.85), (0.05, 1.0), (-0.25, 0.6), (-0.5, 0.08), (-0.5, -0.04), (-0.2, -0.2), (0.3, -0.3)]
    for b in range(blades):
        a = 2 * math.pi * b / blades
        span = Vector((math.cos(a), math.sin(a), 0))
        travel = Vector((-math.sin(a), math.cos(a), 0))
        loops = []
        for f, cf, twist in stations:
            ch = chord * cf
            t = max(ch * 0.11, 0.0015)
            th = math.radians(twist)
            along = travel * math.cos(th) + up * math.sin(th)
            across = -travel * math.sin(th) + up * math.cos(th)
            centre = span * R * f - travel * ch * 0.08 * f  # tips swept back a little
            loops.append([centre + along * x * ch + across * y * t for x, y in foil])
        kit.loft(M["prop"], loops)
    hr, hh = max(R * 0.1, 0.012), max(R * 0.09, 0.01)
    spinner = [(-hh * 0.5, 0), (-hh * 0.5, hr), (hh * 0.5, hr), (hh * 0.9, hr * 0.6), (hh * 1.1, 0)]
    kit.lathe(M["prop"], spinner, (0, 0, 0), rotm((math.pi, 0, 0)) if flip else None, n=12)
    obj = build_object(kit, name, parent, hub)
    PROPS.append((name, Vector(hub), R, flip))
    return obj


def build_object(kit, name, parent, loc=(0, 0, 0)):
    """Builds a kit (drawn round the origin) as one object called name, its materials as slots, placed at loc."""
    before = set(bpy.data.objects)
    kit.build(name, None)
    parts = [o for o in bpy.data.objects if o not in before]
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = obj.data.name = name
    obj.location = loc
    bpy.context.view_layer.update()
    set_parent(obj, parent)
    return obj


def blur_discs(parent):
    """A flat disc over each propeller for its blur, its UVs running from the hub (U = 0) to the tip (U = 1)."""
    for name, hub, R, flip in PROPS:
        rings, n = (0.1, 0.35, 0.6, 0.8, 0.93, 1.0), 40
        verts, faces, us = [], [], []
        for f in rings:
            for k in range(n):
                a = 2 * math.pi * k / n
                verts.append((math.cos(a) * R * f, math.sin(a) * R * f, 0.0))
                us.append(f)
        for i in range(len(rings) - 1):
            for k in range(n):
                a, b = i * n + k, i * n + (k + 1) % n
                faces.append((a, b, b + n, a + n))
        mesh = bpy.data.meshes.new("Blur")
        mesh.from_pydata(verts, [], faces)
        layer = mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:
            layer.data[loop.index].uv = (us[loop.vertex_index], 0.5)
        mesh.materials.append(M["blur"])
        obj = bpy.data.objects.new(name.replace("Prop", "Blur"), mesh)
        bpy.context.collection.objects.link(obj)
        obj.location = hub + Vector((0, 0, -0.012 if flip else 0.012))
        bpy.context.view_layer.update()
        set_parent(obj, parent)


def leds(kit, left, right, r=0.008):
    """Nav lights: red on the left (Blender +X, which is the drone's left), green on the right."""
    for c in left:
        ball(kit, M["led_red"], c, r, n=10, rings=5)
    for c in right:
        ball(kit, M["led_green"], c, r, n=10, rings=5)


def strobe(kit, c, r=0.012):
    kit.cyl(M["shell"], r * 1.3, r * 0.6, Vector(c) - Vector((0, 0, r * 0.3)), n=12)
    ball(kit, M["strobe"], c, r, n=12, rings=6, squash=0.8)


def gps_puck(kit, c, r):
    kit.lathe(M["shell"], [(0, 0), (0, r), (r * 0.25, r * 0.95), (r * 0.45, r * 0.6), (r * 0.5, 0)], c, n=20)


def antenna(kit, base, tip, r=0.005):
    """A rubber-duck telemetry antenna: a hinge block, then a tapered rubber whip."""
    base, tip = Vector(base), Vector(tip)
    kit.cyl(M["motor"], r * 1.4, r * 2.5, base, aim(tip - base), n=8)
    d = tip - base
    kit.cyl(M["rubber"], r, d.length * 0.92, base + d * 0.52, aim(d), n=8, r2=r * 0.6)


def straps(kit, centre, size, ys, colour="strap"):
    """Hook-and-loop straps round a box (a battery) at the given y offsets."""
    cx, cy, cz = centre
    w, _, h = size
    for y in ys:
        kit.box(M[colour], (w + 0.006, 0.014, h + 0.006), (cx, cy + y, cz))


def battery(kit, centre, size, strap_ys=(-0.25, 0.25)):
    kit.box(M["battery"], size, centre, ch=min(size) * 0.18)
    straps(kit, centre, size, [y * size[1] for y in strap_ys])


# ---------- drones ----------

def build_scout():
    """Wren: a compact folding-arm recon quad with a stabilised day/thermal camera under the nose."""
    r = root("Drone_Scout")
    kit = Kit()
    fuselage(kit, M["shell"], [(-0.205, 0.03, 0.02, 0.022), (-0.19, 0.075, 0.05, 0.026), (-0.155, 0.115, 0.07, 0.032),
                               (-0.08, 0.135, 0.08, 0.036), (0.04, 0.14, 0.082, 0.038), (0.13, 0.125, 0.072, 0.04),
                               (0.18, 0.095, 0.055, 0.042), (0.205, 0.05, 0.03, 0.044)])
    fuselage(kit, M["teal"], [(-0.045, 0.142, 0.086, 0.037), (-0.022, 0.143, 0.087, 0.037)])
    kit.box(M["battery"], (0.092, 0.15, 0.014), (0, 0.06, 0.077))
    kit.box(M["carbon"], (0.06, 0.012, 0.006), (0, 0.13, 0.083))  # battery latch
    for x in (-0.03, 0.03):  # obstacle sensors in the nose
        lens(kit, (x, -0.19, 0.03), 0.006, rim=False)
    # Folding arms: the front pair high, the rear pair low, each with a motor pod and a landing foot.
    motors = []
    for sx in (-1, 1):
        for front in (True, False):
            hinge = Vector((sx * 0.055, -0.1 if front else 0.1, 0.035 if front else 0.012))
            tip = Vector((sx * 0.2, -0.17 if front else 0.17, 0.045 if front else 0.02))
            kit.cyl(M["motor"], 0.012, 0.03, hinge, n=12)
            sweep(kit, M["shell"], [(hinge, 0.026, 0.02), ((hinge + tip) / 2, 0.022, 0.017), (tip, 0.019, 0.015)])
            kit.cyl(M["shell"], 0.021, 0.03, tip, n=16)
            kit.box(M["teal"], (0.016, 0.03, 0.004), ((hinge + tip) / 2 + Vector((0, 0, 0.009))))
            leg_top = tip - Vector((0, 0, 0.015))
            foot = Vector((tip.x, tip.y, -0.075))
            kit.bar(M["shell"], leg_top, foot + Vector((0, 0, 0.006)), 0.004, n=8)
            ball(kit, M["rubber"], foot, 0.008, n=10, rings=5, squash=0.7)
            motors.append((motor(kit, tip + Vector((0, 0, 0.015)), 0.018, 0.02), sx, front, tip))
    leds(kit, [t + Vector((0, -0.02 if f else 0.02, -0.005)) for _, sx, f, t in motors if sx > 0],
         [t + Vector((0, -0.02 if f else 0.02, -0.005)) for _, sx, f, t in motors if sx < 0], r=0.006)
    gps_puck(kit, (0, 0.11, 0.078), 0.024)
    strobe(kit, (0, 0.165, 0.08), 0.008)
    # Gimbal under the nose: a yoke holding the camera ball, a wide day lens and a small thermal one.
    kit.box(M["shell"], (0.034, 0.024, 0.012), (0, -0.16, -0.006))
    for x in (-0.033, 0.033):
        kit.box(M["shell"], (0.006, 0.016, 0.04), (x, -0.165, -0.03))
    ball(kit, M["shell"], (0, -0.168, -0.045), 0.029)
    lens(kit, (-0.009, -0.195, -0.045), 0.011)
    lens(kit, (0.013, -0.193, -0.038), 0.006)
    kit.build("Body", r)
    for i, (hub, sx, front, _) in enumerate(motors):
        prop(f"Prop_{i}", hub, 0.24, 0.026, r)
    return r


def build_strike():
    """A hexacopter gunship: canopy over a twin-plate hub, six folding arms, two missiles under the hub, a targeting
    ball under the nose and tall landing legs."""
    r = root("Drone_Strike")
    kit = Kit()
    for z in (0.0, 0.06):  # top and bottom carbon plates
        kit.lathe(M["carbon"], [(-0.003, 0), (-0.003, 0.17), (0.003, 0.17), (0.003, 0)], (0, 0, z), n=6, phase=0.5)
    for a in range(6):  # standoffs between them
        ang = math.radians(60 * a + 30)
        kit.cyl(M["motor"], 0.006, 0.06, (math.cos(ang) * 0.13, math.sin(ang) * 0.13, 0.03), n=8)
    fuselage(kit, M["olive"], [(-0.25, 0.05, 0.03, 0.075), (-0.215, 0.18, 0.085, 0.095), (-0.12, 0.25, 0.12, 0.11),
                               (0.1, 0.25, 0.12, 0.11), (0.2, 0.18, 0.09, 0.1), (0.245, 0.06, 0.035, 0.09)])
    fuselage(kit, M["teal"], [(-0.06, 0.254, 0.124, 0.11), (-0.03, 0.254, 0.124, 0.11)])
    for sx in (-1, 1):
        battery(kit, (sx * 0.055, 0.07, 0.195), (0.095, 0.2, 0.06))
    kit.box(M["carbon"], (0.21, 0.24, 0.006), (0, 0.07, 0.163))  # battery tray
    # Six arms in a V-nosed hexa, folding a quarter of the way out, each with a motor and LED.
    motors, left, right = [], [], []
    for i in range(6):
        ang = math.radians(60 * i)
        d = Vector((math.cos(ang), math.sin(ang), 0))
        inner, tip = d * 0.12 + Vector((0, 0, 0.03)), d * 0.62 + Vector((0, 0, 0.03))
        kit.box(M["motor"], (0.05, 0.05, 0.05), d * 0.15 + Vector((0, 0, 0.03)), rotm((0, 0, ang)))  # hub clamp
        kit.bar(M["carbon"], inner, tip, 0.016, n=12)
        kit.cyl(M["motor"], 0.022, 0.05, d * 0.3 + Vector((0, 0, 0.03)), aim(d), n=12)  # fold joint
        kit.cyl(M["teal"], 0.0172, 0.05, d * 0.5 + Vector((0, 0, 0.03)), aim(d), n=12, caps=False)
        kit.box(M["carbon"], (0.07, 0.07, 0.006), tip + Vector((0, 0, 0.018)), rotm((0, 0, ang)))  # motor plate
        kit.box(M["motor"], (0.04, 0.04, 0.035), tip, rotm((0, 0, ang)))  # tube clamp
        motors.append(motor(kit, tip + Vector((0, 0, 0.021)), 0.04, 0.04))
        (left if d.x > 0.1 else right if d.x < -0.1 else []).append(tip + d * 0.03 - Vector((0, 0, 0.02)))
    leds(kit, left, right, r=0.01)
    # Missiles on rails either side under the hub, noses forward.
    for sx in (-1, 1):
        x = sx * 0.14
        kit.box(M["carbon"], (0.03, 0.5, 0.025), (x, 0.0, -0.035))
        for y in (-0.15, 0.15):
            kit.box(M["motor"], (0.012, 0.02, 0.04), (x, y, -0.015))
        missile(kit, (x, 0.3, -0.083), 0.6, 0.033)
    # Targeting ball under the nose.
    kit.cyl(M["motor"], 0.025, 0.04, (0, -0.2, -0.02), n=12)
    ball(kit, M["shell"], (0, -0.2, -0.075), 0.052)
    lens(kit, (-0.016, -0.248, -0.075), 0.017)
    lens(kit, (0.02, -0.247, -0.066), 0.009)
    # GPS mast, strobe and telemetry antennas at the back.
    kit.bar(M["carbon"], (0, 0.19, 0.12), (0, 0.19, 0.27), 0.006, n=8)
    gps_puck(kit, (0, 0.19, 0.27), 0.045)
    strobe(kit, (0, 0.19, 0.3), 0.012)
    for sx in (-1, 1):
        antenna(kit, (sx * 0.1, 0.2, 0.13), (sx * 0.16, 0.27, 0.3))
    # Landing legs: a strut down from each side of the hub to a skid with rubber ends.
    for sx in (-1, 1):
        for sy in (-1, 1):
            kit.bar(M["carbon"], (sx * 0.1, sy * 0.1, -0.003), (sx * 0.24, sy * 0.15, -0.3), 0.011, n=10)
        kit.bar(M["carbon"], (sx * 0.24, -0.27, -0.3), (sx * 0.24, 0.27, -0.3), 0.012, n=10)
        for sy in (-1, 1):
            ball(kit, M["rubber"], (sx * 0.24, sy * 0.27, -0.3), 0.016, n=10, rings=5)
    kit.build("Body", r)
    for i, hub in enumerate(motors):
        prop(f"Prop_{i}", hub, 0.5, 0.045, r)
    return r


def missile(kit, tail, length, radius):
    """A small guided missile from tail forward (-Y): nozzle, body, warhead band, glass seeker and two sets of fins."""
    tail = Vector(tail)
    L, R = length, radius
    kit.lathe(M["white"], [(0, 0), (0, R * 0.65), (L * 0.03, R * 0.85), (L * 0.06, R), (L * 0.84, R),
                           (L * 0.9, R * 0.92), (L * 0.95, R * 0.7)], tail, TO_FWD, n=16)
    kit.lathe(M["lens"], [(L * 0.95, R * 0.7), (L * 0.98, R * 0.48), (L, 0)], tail, TO_FWD, n=16)
    kit.cyl(M["tip_red"], R * 1.01, L * 0.04, tail + Vector((0, -L * 0.8, 0)), TO_FWD, n=16, caps=False)
    kit.cyl(M["carbon"], R * 0.55, L * 0.02, tail + Vector((0, 0.004, 0)), TO_FWD, n=12)  # nozzle
    for k in range(4):
        fa = math.radians(45 + 90 * k)
        out = Vector((math.cos(fa), 0, math.sin(fa)))
        kit.box(M["white"], (0.003, L * 0.12, R * 1.5), tail + Vector((0, -L * 0.08, 0)) + out * R * 1.4,
                rotm((0, -fa + R90, 0)))
        kit.box(M["white"], (0.002, L * 0.14, R * 0.7), tail + Vector((0, -L * 0.5, 0)) + out * R * 1.2,
                rotm((0, -fa + R90, 0)))


def build_kamikaze():
    """A 7-inch FPV racer carrying a shaped-charge warhead under its belly."""
    r = root("Drone_Kamikaze")
    kit = Kit()
    # True-X frame: a centre plate and four arms, a top plate on standoffs, the flight stack between.
    kit.box(M["carbon"], (0.07, 0.12, 0.005), (0, 0, 0))
    motors, left, right = [], [], []
    for sx in (-1, 1):
        for sy in (-1, 1):
            tip = Vector((sx * 0.112, sy * 0.112, 0.0))
            d = tip.normalized()
            kit.box(M["carbon"], (0.026, tip.length + 0.012, 0.006), tip / 2, rotm((0, 0, math.atan2(d.y, d.x) - R90)))
            kit.box(M["teal"], (0.02, 0.03, 0.002), tip * 0.62 + Vector((0, 0, 0.004)), rotm((0, 0, math.atan2(d.y, d.x) - R90)))
            motors.append(motor(kit, tip + Vector((0, 0, 0.003)), 0.014, 0.017))
            (left if sx > 0 else right).append(tip + d * 0.02 - Vector((0, 0, 0.006)))
    leds(kit, left, right, r=0.004)
    kit.box(M["carbon"], (0.06, 0.1, 0.003), (0, 0.0, 0.032))
    for sx in (-1, 1):
        for sy in (-1, 1):
            kit.cyl(M["motor"], 0.0028, 0.03, (sx * 0.024, sy * 0.042, 0.017), n=6)
    kit.box(M["shell"], (0.032, 0.032, 0.018), (0, 0.01, 0.014))  # flight controller and ESC stack
    # FPV camera between side plates, tilted up for fast forward flight.
    for sx in (-1, 1):
        kit.box(M["carbon"], (0.003, 0.03, 0.028), (sx * 0.016, -0.055, 0.017))
    tilt = rotm((math.radians(-25), 0, 0))
    kit.box(M["shell"], (0.024, 0.022, 0.022), (0, -0.056, 0.018), tilt)
    lens(kit, (0, -0.069, 0.023), 0.008, rot=tilt @ TO_FWD)
    # Battery strapped on top, with teal tape.
    battery(kit, (0, 0.005, 0.054), (0.044, 0.105, 0.036), strap_ys=(-0.2, 0.2))
    kit.box(M["teal"], (0.0445, 0.02, 0.0365), (0, -0.03, 0.054))
    # Video and control antennas at the back.
    kit.bar(M["rubber"], (0, 0.05, 0.035), (0, 0.085, 0.085), 0.003, n=6)
    ball(kit, M["rubber"], (0, 0.087, 0.088), 0.009, n=10, rings=5, squash=0.5)
    for sx in (-1, 1):
        kit.bar(M["rubber"], (sx * 0.01, 0.05, 0.034), (sx * 0.05, 0.1, 0.06), 0.0015, n=4)
    # Warhead: an anti-armour round with a stand-off probe, strapped and zip-tied under the frame.
    tail = Vector((0, 0.15, -0.05))
    kit.lathe(M["warhead"], [(0, 0), (0, 0.012), (0.08, 0.012), (0.1, 0.03), (0.125, 0.042), (0.225, 0.042),
                             (0.27, 0.033), (0.3, 0.02), (0.318, 0.007), (0.32, 0)], tail, TO_FWD, n=18)
    kit.cyl(M["tip_red"], 0.0425, 0.014, tail + Vector((0, -0.2, 0)), TO_FWD, n=18, caps=False)
    kit.bar(M["motor"], tail + Vector((0, -0.31, 0)), tail + Vector((0, -0.39, 0)), 0.005, n=8)
    ball(kit, M["motor"], tail + Vector((0, -0.392, 0)), 0.007, n=8, rings=4)
    for k in range(4):
        fa = math.radians(45 + 90 * k)
        out = Vector((math.cos(fa), 0, math.sin(fa)))
        kit.box(M["warhead"], (0.0025, 0.06, 0.03), tail + Vector((0, -0.035, 0)) + out * 0.024,
                rotm((0, -fa + R90, 0)))
    for y in (-0.06, 0.04):  # straps up round the frame
        kit.box(M["strap"], (0.09, 0.012, 0.004), (0, y, -0.004))
        for sx in (-1, 1):
            kit.box(M["strap"], (0.004, 0.012, 0.05), (sx * 0.044, y, -0.028))
        kit.lathe(M["strap"], [(-0.006, 0.0435), (0.006, 0.0435), (0.006, 0.047), (-0.006, 0.047)],
                  tail + Vector((0, y - 0.15, 0)), TO_FWD, n=16, closed=True)
    kit.build("Body", r)
    for i, hub in enumerate(motors):
        prop(f"Prop_{i}", hub, 0.178, 0.02, r, blades=3)
    return r


def build_nightjar():
    """Nightjar: a heavy coaxial X8 night bomber with a long fuselage, twin batteries, a thermal turret under the
    nose, a rack of three bombs and tall landing legs. Each bomb is one object, "Bomb_N"."""
    r = root("Drone_Nightjar")
    kit = Kit()
    fuselage(kit, M["olive"], [(-0.44, 0.05, 0.035, 0.05), (-0.41, 0.17, 0.09, 0.065), (-0.32, 0.26, 0.135, 0.08),
                               (0.1, 0.28, 0.15, 0.085), (0.32, 0.24, 0.12, 0.082), (0.42, 0.12, 0.065, 0.075),
                               (0.45, 0.04, 0.025, 0.07)])
    fuselage(kit, M["teal"], [(-0.13, 0.284, 0.153, 0.085), (-0.1, 0.284, 0.153, 0.085)])
    kit.box(M["carbon"], (0.32, 0.6, 0.012), (0, 0.04, 0.0))  # belly plate
    for sx in (-1, 1):
        battery(kit, (sx * 0.068, 0.07, 0.205), (0.12, 0.38, 0.08))
    kit.box(M["carbon"], (0.27, 0.42, 0.008), (0, 0.07, 0.162))
    # Four thick folding arms, each ending in a pair of motors: one up, one down, a propeller on each.
    motors, left, right = [], [], []
    for i, a in enumerate([45, 135, 225, 315]):
        ang = math.radians(a)
        d = Vector((math.cos(ang), math.sin(ang), 0))
        root_pt, fold, tip = d * 0.14 + Vector((0, 0, 0.05)), d * 0.32 + Vector((0, 0, 0.05)), d * 0.72 + Vector((0, 0, 0.05))
        sweep(kit, M["olive"], [(root_pt, 0.06, 0.055), (fold, 0.052, 0.048)])
        kit.cyl(M["motor"], 0.03, 0.065, fold, aim(d), n=14)
        sweep(kit, M["carbon"], [(fold, 0.046, 0.042), (tip - d * 0.04, 0.038, 0.034)], n=12, e=2.2)
        kit.cyl(M["teal"], 0.0215, 0.06, fold + d * 0.13, aim(d), n=12, caps=False)
        kit.cyl(M["motor"], 0.05, 0.08, tip, n=16)  # motor pod between the two motors
        top = motor(kit, tip + Vector((0, 0, 0.04)), 0.05, 0.05)
        low = motor(kit, tip - Vector((0, 0, 0.04)), 0.05, 0.05, flip=True)
        motors.append((top, low))
        (left if d.x > 0 else right).append(tip + d * 0.055)
    leds(kit, left, right, r=0.012)
    # Thermal turret: a ball with a big thermal lens and a small day camera.
    kit.cyl(M["motor"], 0.03, 0.05, (0, -0.34, -0.02), n=12)
    ball(kit, M["shell"], (0, -0.34, -0.09), 0.075)
    lens(kit, (-0.024, -0.408, -0.09), 0.03)
    lens(kit, (0.035, -0.404, -0.078), 0.013)
    # Bomb rack: a beam with release hooks over each bomb.
    kit.box(M["carbon"], (0.06, 0.66, 0.03), (0, 0.06, -0.022))
    for y in (-0.18, 0.06, 0.3):
        for dy in (-0.05, 0.05):
            kit.box(M["motor"], (0.014, 0.012, 0.035), (0, y + dy, -0.05))
    # GPS mast, strobe and antennas.
    kit.bar(M["carbon"], (0, 0.33, 0.12), (0, 0.33, 0.3), 0.007, n=8)
    gps_puck(kit, (0, 0.33, 0.3), 0.05)
    strobe(kit, (0, 0.33, 0.33), 0.014)
    for sx in (-1, 1):
        antenna(kit, (sx * 0.12, 0.36, 0.1), (sx * 0.2, 0.44, 0.3), r=0.006)
    # Tall landing legs, so the bombs clear the ground.
    for sx in (-1, 1):
        for sy in (-1, 1):
            kit.bar(M["carbon"], (sx * 0.13, sy * 0.24, -0.005), (sx * 0.27, sy * 0.3, -0.38), 0.014, n=10)
        kit.bar(M["carbon"], (sx * 0.27, -0.4, -0.38), (sx * 0.27, 0.4, -0.38), 0.016, n=10)
        for sy in (-1, 1):
            ball(kit, M["rubber"], (sx * 0.27, sy * 0.4, -0.38), 0.02, n=10, rings=5)
    kit.build("Body", r)
    for k, y in enumerate((-0.18, 0.06, 0.3)):
        b = Kit()
        bomb(b, (0, 0.14, 0), 0.27, 0.045)
        build_object(b, f"Bomb_{k}", r, (0, y, -0.115))
    for i, (top, low) in enumerate(motors):
        prop(f"Prop_{i}", top, 0.62, 0.055, r)
        prop(f"Prop_{i}_low", low, 0.62, 0.055, r, flip=True)
    return r


def bomb(kit, tail, length, radius):
    """A finned drop bomb from tail forward (-Y), drawn round the origin."""
    tail = Vector(tail)
    L, R = length, radius
    kit.lathe(M["warhead"], [(0, 0), (0, R * 0.35), (L * 0.18, R * 0.42), (L * 0.32, R), (L * 0.72, R),
                             (L * 0.86, R * 0.82), (L * 0.95, R * 0.45), (L, 0)], tail, TO_FWD, n=18)
    kit.cyl(M["tip_red"], R * 1.01, L * 0.05, tail + Vector((0, -L * 0.66, 0)), TO_FWD, n=18, caps=False)
    for k in range(4):
        fa = math.radians(45 + 90 * k)
        out = Vector((math.cos(fa), 0, math.sin(fa)))
        kit.box(M["motor"], (0.003, L * 0.22, R * 0.95), tail + Vector((0, -L * 0.11, 0)) + out * R * 0.75,
                rotm((0, -fa + R90, 0)))
    kit.lathe(M["motor"], [(-0.004, R * 0.95), (0.004, R * 0.95), (0.004, R * 1.02), (-0.004, R * 1.02)],
              tail + Vector((0, -0.01, 0)), TO_FWD, n=16, closed=True)  # fin ring


BUILDS = [("Drone_Scout", build_scout, 1.1), ("Drone_Strike", build_strike, 2.4), ("Drone_Kamikaze", build_kamikaze, 0.85),
          ("Drone_Nightjar", build_nightjar, 3.0)]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    only = set(args[2].split(",")) if len(args) > 2 and args[2] else None
    for name, build, cam_distance in BUILDS:
        if only and name not in only:
            continue
        reset_scene()
        M.clear()
        PROPS.clear()
        make_materials()
        r = build()
        box_uv_all(r)  # real-scale UVs for Unity's photo textures
        blur_discs(r)
        tris = 0
        for o in r.children_recursive:
            if o.type == "MESH":
                o.data.calc_loop_triangles()
                tris += len(o.data.loop_triangles)
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name} ({tris} triangles)")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            for o in r.children_recursive:
                if o.name.startswith("Blur"):
                    o.hide_render = True
            render_preview(os.path.join(PREVIEW_DIR, name + ".png"), cam_distance)
            print(f"PREVIEW {name}")


if __name__ == "__main__":
    main()
