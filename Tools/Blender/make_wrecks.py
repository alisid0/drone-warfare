"""Turns the enemy vehicles into burnt-out wrecks and exports each as Wreck_<type>.fbx with its own baked texture.

Run from the project root, after make_world.py and make_sectors.py (it starts from the FBX files they export):
    blender --background --factory-startup --python Tools/Blender/make_wrecks.py -- Assets/DroneSandbox/Resources/Models Assets/DroneSandbox/Textures/Wrecks [preview_dir] [names]

names is an optional comma-separated filter such as Tank,APC. For each vehicle:
1. Import Enemy_<type>.fbx, so the wreck lines up with the model the game drove (same origin and size).
2. Knock it about: the turret blown off or slewed, the barrel drooping, the mast fallen; glass, lamps, canvas, seat
   pads, tyres and camouflage nets burnt away (a wheeled wreck settles onto its rims); most antennas and some small
   fittings gone.
3. Crumple it: long edges are split so the panels can bend, then everything is dented and warped with noise.
4. Join it into one mesh, unwrap it, and bake its look with Cycles into Wreck_<type>_diff.jpg (colour, with
   ambient occlusion) and Wreck_<type>_nor.jpg (pitting and blistering, OpenGL tangent space). The colour layers
   soot over the old paint low down, rust and burnt-off paint higher up and pale ash on the top surfaces, from the
   heat of the fire (higher is hotter, broken up by noise); wood and fabric go to charcoal.
5. Export it with one material, "Wreck_<type>". Drone Sandbox > Configure Project makes that material from the
   two textures, and Art.TryModel swaps it in by name.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from baking import Graph, base_colour, base_name, use_gpu  # noqa: E402
from common import export, material, render_preview, reset_scene, root  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODELS = os.path.abspath(args[0] if args else ".")
TEXTURES = os.path.abspath(args[1] if len(args) > 1 else ".")
PREVIEW_DIR = os.path.abspath(args[2]) if len(args) > 2 and args[2] else None
ONLY = set(args[3].split(",")) if len(args) > 3 and args[3] else None

# How each vehicle died. turret: degrees of yaw, pitch (nose down, about X) and roll (about Y, +X side down) about
# its pivot, and toss to throw it off beside the hull (to +X). barrel: degrees the gun droops. tyres: it rode on tyres and now sits on its
# rims. dents: how many big dents. size: colour texture size (the normal map is half).
WRECKS = {
    "Tank": dict(turret=dict(yaw=52, roll=24, toss=True), dents=7, size=2048, view=(8.0, 0.8)),
    "Truck": dict(tyres=True, dents=6, size=2048, view=(7.0, 0.8)),
    "AAGun": dict(turret=dict(yaw=40), barrel=14, dents=4, size=1024, view=(6.0, 0.6)),
    "Radar": dict(turret=dict(yaw=25, pitch=32), dents=4, size=1024, view=(8.5, 1.5)),
    "Artillery": dict(barrel=10, dents=5, size=2048, view=(10.0, 1.0)),
    "APC": dict(turret=dict(yaw=70, roll=7), barrel=12, tyres=True, dents=6, size=2048, view=(9.0, 1.0)),
    "Rocket": dict(turret=dict(yaw=22, roll=16), tyres=True, dents=6, size=2048, view=(10.0, 1.2)),
    "Tanker": dict(tyres=True, dents=8, size=2048, view=(10.0, 1.2)),
    "Jammer": dict(turret=dict(pitch=-68), tyres=True, dents=6, size=2048, view=(10.0, 1.2)),
    "Depot": dict(dents=5, size=2048, view=(11.0, 0.8)),
}

BURNT_AWAY = ("Glass", "WindowGlass", "Lamp", "AmberLens", "RedLens", "Beacon", "Canvas", "SeatPad", "Strap", "Rubber",
              "CamoNet", "CamoNetA", "CamoNetB")
CHARRING = ("Wood", "WoodLight", "Sandbag", "Dirt", "Interior")  # burn to charcoal rather than rust


# ---------- 1-2: import and knock about ----------

def import_vehicle(kind):
    bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, f"Enemy_{kind}.fbx"),
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True)
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o.type == "MESH"]


def turn_about(obj, pivot, rot):
    """Turns obj (and everything parented to it) by rot about the world point pivot."""
    obj.matrix_world = Matrix.Translation(pivot) @ rot.to_4x4() @ Matrix.Translation(-pivot) @ obj.matrix_world


def world_bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    return (Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)]))


def knock_about(meshes, spec, rng):
    by_name = {o.name.split(".")[0]: o for o in meshes}
    turret, barrel = by_name.get("Turret"), by_name.get("Barrel")
    if barrel is not None and spec.get("barrel"):
        pivot = barrel.matrix_world.translation.copy()
        turn_about(barrel, pivot, Matrix.Rotation(math.radians(spec["barrel"]), 3, "X"))
        bpy.context.view_layer.update()
    t = spec.get("turret")
    if turret is not None and t:
        pivot = turret.matrix_world.translation.copy()
        rot = (Matrix.Rotation(math.radians(t.get("yaw", 0)), 3, "Z")
               @ Matrix.Rotation(math.radians(t.get("pitch", 0)), 3, "X")
               @ Matrix.Rotation(math.radians(t.get("roll", 0)), 3, "Y"))
        turn_about(turret, pivot, rot)
        bpy.context.view_layer.update()
        if t.get("toss"):
            # Blown off by its own ammunition: it lands beside the hull, propped against it.
            hull = [o for o in meshes if o is not turret and o.parent is not turret and o is not barrel]
            moving = [turret] + [o for o in meshes if o.parent is turret]
            hlo, hhi = world_bounds(hull)
            tlo, thi = world_bounds(moving)
            dx = hhi.x + (thi.x - tlo.x) * 0.15 - (tlo.x + thi.x) / 2
            shift = Vector((dx, (hhi.y - hlo.y) * 0.12, 0.25 - tlo.z))
            turret.matrix_world = Matrix.Translation(shift) @ turret.matrix_world
            bpy.context.view_layer.update()


def to_world(meshes):
    """Bakes every mesh's transform into its vertices and unparents it, so all of them live in one space."""
    for o in meshes:
        o.data.transform(o.matrix_world)
    for o in meshes:
        o.parent = None
        o.matrix_world = Matrix.Identity(4)
    for o in [o for o in bpy.data.objects if o.type == "EMPTY"]:
        bpy.data.objects.remove(o, do_unlink=True)


def islands(bm):
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, group = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            group.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        out.append(group)
    return out


def axis(pts):
    """A part's long axis: ((its lower end, unit direction), length, width across it)."""
    centre = sum(pts, Vector()) / len(pts)
    p0 = max(pts, key=lambda p: (p - centre).length)
    p1 = max(pts, key=lambda p: (p - p0).length)
    d = p1 - p0
    length = d.length
    if length < 1e-6:
        return (p0, Vector((0, 0, 1))), 0.0, 0.0
    d /= length
    width = 2 * max(((p - p0) - d * (p - p0).dot(d)).length for p in pts)
    return (p0 if p0.z <= p1.z else p1, d), length, width


def strip(obj, rng):
    """Deletes what burnt away or broke off, and records each face's old paint for the bake (Paint attribute:
    colour, alpha 0 where it chars to charcoal)."""
    mesh = obj.data
    names = [base_name(m) for m in mesh.materials]
    colours = [base_colour(m) for m in mesh.materials]
    paint = mesh.color_attributes.new("Paint", "FLOAT_COLOR", "CORNER")
    for poly in mesh.polygons:
        i = poly.material_index
        c = colours[i] if i < len(colours) else (0.1, 0.1, 0.1)
        a = 0.0 if i < len(names) and names[i] in CHARRING else 1.0
        for li in poly.loop_indices:
            paint.data[li].color = (*c, a)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    doomed = set()
    for f in bm.faces:
        n = names[f.material_index] if f.material_index < len(names) else ""
        if n in BURNT_AWAY:
            doomed.add(f)
    fate = {}  # antennas come in segments: every segment rising from one spot shares its fate
    for group in islands(bm):
        if group[0] in doomed:
            continue
        pts = [v.co for f in group for v in f.verts]
        foot, length, width = axis(pts)
        upright = length > 0.25 and width < 0.06 and length > width * 8 and abs(foot[1].z) > 0.6
        if upright:  # an antenna or whip (tilted along with its turret, perhaps)
            spot = (round(foot[0].x / 0.2), round(foot[0].y / 0.2))
            gone = fate.setdefault(spot, rng.random() < 0.8)
        else:
            gone = length < 0.15 and rng.random() < 0.2  # small fittings
        if gone:
            doomed.update(group)
    bmesh.ops.delete(bm, geom=list(doomed), context="FACES")
    bm.to_mesh(mesh)
    bm.free()


def join(meshes, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = obj.data.name = name
    return obj


# ---------- 3: crumple ----------

def crumple(obj, spec, rng, settle):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for _ in range(3):  # split long edges so big panels have vertices to bend
        long_edges = [e for e in bm.edges if e.calc_length() > 0.3]
        if not long_edges:
            break
        bmesh.ops.subdivide_edges(bm, edges=long_edges, cuts=1, use_grid_fill=True)
    bm.normal_update()
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    tops = [f for f in bm.faces if f.normal.z > 0.6 and f.calc_center_median().z > lo.z + (hi.z - lo.z) * 0.35]
    sides = [f for f in bm.faces if abs(f.normal.z) < 0.3 and f.calc_area() > 0.02]
    dents = []
    for k in range(spec.get("dents", 5)):
        pool = tops if (k % 3 != 2 and tops) else sides or tops
        if not pool:
            break
        f = rng.choice(pool)
        dents.append((f.calc_center_median(), f.normal.copy(), rng.uniform(0.35, 0.9), rng.uniform(0.05, 0.14)))
    seed = Vector((rng.uniform(0, 100), rng.uniform(0, 100), rng.uniform(0, 100)))
    for v in bm.verts:
        p = v.co.copy()
        v.co += noise.noise_vector(p * 1.6 + seed) * 0.02 + noise.noise_vector(p * 5.5 + seed) * 0.005
        for c, n, radius, depth in dents:
            d = (p - c).length
            if d < radius:
                v.co -= n * depth * (1 - (d / radius) ** 2) ** 2
    if settle:  # on its rims now: drop it until the lowest part touches the ground
        drop = min(v.co.z for v in bm.verts)
        for v in bm.verts:
            v.co.z -= drop
    bm.to_mesh(obj.data)
    bm.free()


# ---------- 4: unwrap and bake ----------

def unwrap(obj):
    uvs = obj.data.uv_layers
    for layer in list(uvs):  # the model's real-metre UVs (for tiling photo textures) don't fit a baked texture
        uvs.remove(layer)
    uvs.active = uvs.new(name="UVMap")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_material(name, lo, hi):
    """The burn shader, wired for both bakes: colour into Emission, relief into a Principled BSDF's Normal.
    Returns the material and the two output sockets to switch between."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    g = Graph(mat)
    geo = g.node("ShaderNodeNewGeometry")
    pos, nrm = geo.outputs["Position"], geo.outputs["Normal"]
    paint = g.node("ShaderNodeAttribute")
    paint.attribute_name = "Paint"
    sep = g.node("ShaderNodeSeparateXYZ", Vector=pos)
    sepn = g.node("ShaderNodeSeparateXYZ", Vector=nrm)
    height = g.node("ShaderNodeMapRange", Value=sep.outputs["Z"])
    height.inputs["From Min"].default_value = lo.z
    height.inputs["From Max"].default_value = hi.z
    h, up = height.outputs["Result"], sepn.outputs["Z"]

    n1 = g.noise(pos, 0.45, 3.0)
    n2 = g.noise(pos, 2.2, 8.0, 0.62)
    n3 = g.noise(pos, 13.0, 6.0, 0.62)
    n4 = g.noise(pos, 60.0, 3.0, 0.5)
    n5 = g.noise(pos, 1.3, 8.0, 0.62)  # rust patches

    # Heat: higher is hotter, in blotches. Low down the old paint survives under soot; above it the paint has burnt
    # off to dark, mottled metal, rust blooms in patches, and pale ash lies on the top surfaces. All of it comes from
    # noise in 3D, so it runs across the unwrap's seams without a break.
    burn = g.math("ADD", g.math("MULTIPLY", h, 0.75), 0.25)
    burn = g.math("ADD", burn, g.math("MULTIPLY", g.math("SUBTRACT", n1, 0.5), 0.6))
    burn = g.math("ADD", burn, g.math("MULTIPLY", g.math("SUBTRACT", n2, 0.5), 0.5), clamp=True)
    soot_amt = g.smooth(burn, 0.08, 0.22)
    metal_amt = g.smooth(g.math("ADD", burn, g.math("MULTIPLY", g.math("SUBTRACT", n3, 0.5), 0.3)), 0.3, 0.45)
    rust_amt = g.math("ADD", g.math("MULTIPLY", n5, 0.7), g.math("ADD", g.math("MULTIPLY", n3, 0.3), g.math("MULTIPLY", burn, 0.2)))
    rust_amt = g.math("MULTIPLY", g.smooth(rust_amt, 0.62, 0.74), g.smooth(burn, 0.3, 0.5))
    rust_amt = g.math("MULTIPLY", rust_amt, 0.75)
    ash_amt = g.math("MULTIPLY", g.smooth(burn, 0.6, 0.85), g.smooth(up, 0.5, 0.9))
    ash_amt = g.math("MULTIPLY", ash_amt, g.smooth(g.math("ADD", g.math("MULTIPLY", n2, 0.6), g.math("MULTIPLY", n3, 0.4)), 0.54, 0.66))
    char_amt = g.math("SUBTRACT", 1.0, paint.outputs["Alpha"])

    remnant = g.multiply(paint.outputs["Color"], (0.5, 0.5, 0.48, 1))
    metal = g.mix((0.045, 0.04, 0.037, 1), (0.13, 0.12, 0.11, 1), g.math("MULTIPLY", g.math("MULTIPLY", n3, n4), 1.6, clamp=True))
    rust_col = g.mix((0.09, 0.045, 0.025, 1), (0.25, 0.115, 0.05, 1), n4)
    ash_col = g.mix((0.17, 0.165, 0.16, 1), (0.29, 0.28, 0.265, 1), n4)

    c = g.mix(remnant, (0.028, 0.025, 0.023, 1), soot_amt)
    c = g.mix(c, metal, metal_amt)
    c = g.mix(c, rust_col, rust_amt)
    c = g.mix(c, ash_col, ash_amt)
    c = g.mix(c, g.mix((0.012, 0.011, 0.01, 1), (0.2, 0.19, 0.18, 1), g.math("MULTIPLY", g.smooth(up, 0.4, 0.9), n3)), char_amt)
    grain = g.node("ShaderNodeCombineColor")
    for i in range(3):
        g.set(grain.inputs[i], g.math("ADD", g.math("MULTIPLY", n4, 0.4), 0.8))
    c = g.multiply(c, grain.outputs[0])
    ao = g.node("ShaderNodeAmbientOcclusion", Distance=0.45)
    ao.samples = 16
    shade = g.math("ADD", g.math("MULTIPLY", ao.outputs["AO"], 0.7), 0.3)
    sv = g.node("ShaderNodeCombineColor")
    for i in range(3):
        g.set(sv.inputs[i], shade)
    shaded = g.multiply(c, sv.outputs[0])
    emit = g.node("ShaderNodeEmission", Color=shaded, Strength=1.0)

    # Relief: blistered, pitted metal where the paint burnt off; flaking rust.
    relief = g.math("ADD", g.math("MULTIPLY", n4, 0.5), g.math("MULTIPLY", g.math("MULTIPLY", n4, rust_amt), 0.8))
    relief = g.math("ADD", relief, g.math("MULTIPLY", n3, 0.35))
    bump = g.node("ShaderNodeBump", Strength=0.45, Distance=0.015, Height=relief)
    bsdf = g.node("ShaderNodeBsdfPrincipled", Normal=bump.outputs["Normal"])
    out = g.node("ShaderNodeOutputMaterial")
    return mat, out, emit.outputs[0], bsdf.outputs[0]


def bake(obj, kind, size):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    device = use_gpu()
    # Heat runs from the ground to the top of the body; the 92nd percentile of height ignores masts and whips.
    zs = sorted(v.co.z for v in obj.data.vertices)
    lo, hi = Vector((0, 0, zs[0])), Vector((0, 0, zs[int(len(zs) * 0.92)]))
    mat, out, colour, relief = bake_material(f"Bake_{kind}", lo, hi)
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    nt = mat.node_tree
    target = nt.nodes.new("ShaderNodeTexImage")
    nt.nodes.active = target
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    scene.render.bake.margin = 6
    files = {}
    for label, socket, pass_type, px, colourspace, samples in (
            ("diff", colour, "EMIT", size, "sRGB", 32), ("nor", relief, "NORMAL", size // 2, "Non-Color", 4)):
        image = bpy.data.images.new(f"Wreck_{kind}_{label}", px, px)
        image.colorspace_settings.name = colourspace
        target.image = image
        nt.links.new(socket, out.inputs["Surface"])
        scene.cycles.samples = samples
        if pass_type == "NORMAL":
            scene.render.bake.normal_space = "TANGENT"
        bpy.ops.object.bake(type=pass_type, margin=6, use_clear=True)
        path = os.path.join(TEXTURES, f"Wreck_{kind}_{label}.jpg")
        image.file_format = "JPEG"
        scene.render.image_settings.file_format = "JPEG"
        scene.render.image_settings.quality = 88 if label == "diff" else 95
        image.save(filepath=path, quality=scene.render.image_settings.quality)
        files[label] = path
    print(f"  BAKED {kind} on {device}")
    return files


# ---------- 5: export ----------

def finish_material(obj, kind, files):
    """Swaps the bake graph for a plain material named Wreck_<kind> showing the baked textures (it's what the FBX
    carries, and what the preview renders)."""
    mat = material(f"Wreck_{kind}", (0.2, 0.2, 0.2), roughness=0.85)
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(files["diff"], check_existing=True)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nor_tex = nt.nodes.new("ShaderNodeTexImage")
    nor_tex.image = bpy.data.images.load(files["nor"], check_existing=True)
    nor_tex.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nor_tex.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    if "Paint" in obj.data.color_attributes:
        obj.data.color_attributes.remove(obj.data.color_attributes["Paint"])


def build(kind, spec):
    reset_scene()
    rng = random.Random(kind)
    meshes = import_vehicle(kind)
    knock_about(meshes, spec, rng)
    to_world(meshes)
    for o in meshes:
        strip(o, rng)
    meshes = [o for o in meshes if len(o.data.polygons)]
    obj = join(meshes, f"Wreck_{kind}_Mesh")
    crumple(obj, spec, rng, settle=spec.get("tyres", False))
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    unwrap(obj)
    files = bake(obj, kind, spec["size"])
    finish_material(obj, kind, files)
    r = root(f"Wreck_{kind}")
    obj.parent = r
    obj.data.calc_loop_triangles()
    tris = len(obj.data.loop_triangles)
    export(r, os.path.join(MODELS, f"Wreck_{kind}.fbx"))
    print(f"EXPORTED Wreck_{kind} ({tris} triangles)")
    if PREVIEW_DIR:
        os.makedirs(PREVIEW_DIR, exist_ok=True)
        distance, target_z = spec["view"]
        render_preview(os.path.join(PREVIEW_DIR, f"Wreck_{kind}.png"), distance, target_z)
        cam = bpy.context.scene.camera  # and from above, as the game's camera sees it
        cam.location = (distance * 0.3, -distance * 0.55, distance * 1.05 + target_z)
        cam.rotation_euler = (Vector((0, 0, target_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        bpy.context.scene.render.filepath = os.path.join(PREVIEW_DIR, f"Wreck_{kind}_top.png")
        bpy.ops.render.render(write_still=True)
        print(f"PREVIEW Wreck_{kind}")


def main():
    os.makedirs(TEXTURES, exist_ok=True)
    for kind, spec in WRECKS.items():
        if ONLY and kind not in ONLY:
            continue
        build(kind, spec)


if __name__ == "__main__":
    main()
