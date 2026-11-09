"""Weathers the enemy vehicles in the field: mud, dust, rain streaks, grime and chipped paint, baked into each
vehicle's own textures and exported as Weathered_<type>.fbx.

Run from the project root, after make_world.py and make_sectors.py (it starts from the FBX files they export):
    blender --background --factory-startup --python Tools/Blender/make_weathered.py -- Assets/DroneSandbox/Resources/Models Assets/DroneSandbox/Textures/Weathered [preview_dir] [names]

names is an optional comma-separated filter such as Tank,APC. For each vehicle:
1. Import Enemy_<type>.fbx. The parts stay as they are (the game still turns the turret and kicks the barrel back),
   and so do the glass, lamps, lenses, beacons and camouflage nets, which keep their own materials.
2. Unwrap everything else into one shared texture, keeping the old real-metre UVs to read the photo textures
   (Textures/PBR) through, so the bake starts from the paint the game showed before.
3. Bake it with Cycles into Weathered_<type>_diff.jpg (colour, with soft ambient occlusion), _nor.jpg (the photo
   textures' relief plus caked mud and chipped paint, OpenGL tangent space) and _mask.png (HDRP's mask map:
   metallic, ambient occlusion, unused, smoothness). The weathering: mud splashed up from the ground (dry and pale,
   darker and wet low down, thickest on the wheels and tracks and under the hull), dust kicked up onto the sides and
   settled on the top surfaces and in the corners, rain streaks down the sides, grime in the crevices, and paint
   chipped off the exposed edges to bare steel. All of it comes from noise in 3D, so it runs across the unwrap's
   seams without a break, and it's sized in the game's metres (some models are drawn at half size).
4. Export it with the painted materials swapped for one, "Weathered_<type>". Drone Sandbox > Configure Project makes
   that material from the three textures, and Art.TryModel swaps it in by name.
"""
import glob
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from bpy_extras.bmesh_utils import bmesh_linked_uv_islands
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from baking import Graph, base_colour, base_name, use_gpu  # noqa: E402
from common import export_model, material, render_preview, reset_scene  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODELS = os.path.abspath(args[0] if args else ".")
TEXTURES = os.path.abspath(args[1] if len(args) > 1 else ".")
PHOTOS = os.path.join(os.path.dirname(TEXTURES), "PBR")
PREVIEW_DIR = os.path.abspath(args[2]) if len(args) > 2 and args[2] else None
ONLY = set(args[3].split(",")) if len(args) > 3 and args[3] else None

# mud: how high the mud splashes up, in the game's metres. scale: what the game draws the model at
# (EnemyUnit.ModelScale). view: preview camera distance and target height, in model metres.
VEHICLES = {
    "Tank": dict(mud=0.55, scale=1.0, view=(7.5, 0.9)),
    "Truck": dict(mud=0.4, scale=1.0, view=(7.0, 0.9)),
    "AAGun": dict(mud=0.3, scale=1.0, view=(6.0, 0.6)),
    "Radar": dict(mud=0.35, scale=1.0, view=(8.5, 1.5)),
    "Artillery": dict(mud=0.4, scale=1.0, view=(10.0, 1.0)),
    "APC": dict(mud=0.35, scale=0.5, view=(9.0, 1.0)),
    "Rocket": dict(mud=0.35, scale=0.5, view=(10.0, 1.2)),
    "Tanker": dict(mud=0.35, scale=0.5, view=(10.0, 1.2)),
    "Jammer": dict(mud=0.35, scale=0.5, view=(10.0, 1.2)),
}
SIZE = 2048  # colour; the normal map is half, the mask a quarter

# The painted materials the bake starts from. Photo-textured (as ProjectSetup.PbrMaterials makes them): texture,
# tint, metres per repeat, smoothness, metallic. Flat (the colour comes from the model): smoothness, metallic. And
# what each surface is: "paint" chips to bare steel at the edges, "metal" is bare already and only wears shiny,
# "soft" (canvas, rubber, straps, wood) takes the mud and dust but doesn't chip.
PHOTO = {
    "Olive": ("green_metal", (0.85, 0.9, 0.7), 2.0, 0.3, 0.2, "paint"),
    "OliveDark": ("green_metal", (0.55, 0.58, 0.45), 2.0, 0.3, 0.2, "paint"),
    "Gunmetal": ("metal_plate", (0.4, 0.4, 0.42), 1.2, 0.5, 0.6, "metal"),
    "SteelDark": ("metal_plate", (0.45, 0.45, 0.45), 1.5, 0.4, 0.5, "metal"),
    "RustySteel": ("rust_paint", (0.55, 0.45, 0.4), 1.5, 0.25, 0.2, "metal"),
    "Canvas": ("hessian", (0.55, 0.55, 0.38), 1.6, 0.05, 0.0, "soft"),
    "Sandbag": ("hessian", (0.88, 0.83, 0.72), 0.7, 0.05, 0.0, "soft"),
    "Wood": ("rough_wood", (0.65, 0.6, 0.55), 2.0, 0.1, 0.0, "soft"),
    "WoodLight": ("wood", (1.1, 1.0, 0.9), 2.0, 0.1, 0.0, "soft"),
}
FLAT = {
    "Rubber": (0.15, 0.0, "soft"),
    "MarkingOrange": (0.35, 0.0, "paint"),
    "Brass": (0.6, 1.0, "metal"),
    "SeatPad": (0.1, 0.0, "soft"),
    "Strap": (0.1, 0.0, "soft"),
    "Dirt": (0.05, 0.0, "soft"),
}
KEPT = ("Glass", "WindowGlass", "Lamp", "AmberLens", "RedLens", "Beacon", "CamoNet", "CamoNetA", "CamoNetB")


def srgb_to_linear(c):
    """Unity reads material colours as sRGB, so this is the colour the game drew."""
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def photo(name):
    found = glob.glob(os.path.join(PHOTOS, name + ".*"))
    found = [f for f in found if not f.endswith(".meta")]
    return bpy.data.images.load(found[0], check_existing=True) if found else None


# ---------- 1-2: import and unwrap ----------

def import_vehicle(kind):
    bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, f"Enemy_{kind}.fbx"),
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True)
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o.type == "MESH"]


def kept(mesh, poly):
    return poly.material_index >= len(mesh.materials) or base_name(mesh.materials[poly.material_index]) in KEPT


def unwrap(meshes, scale):
    """A new UV layer, Atlas, with every painted face packed into one square; the old one is kept as Tile."""
    for o in meshes:
        mesh = o.data
        uvs = mesh.uv_layers
        uvs[0].name = "Tile"
        atlas = uvs.new(name="Atlas")
        uvs.active = atlas
        atlas.active_render = True
        for v in mesh.vertices:
            v.select = False
        for e in mesh.edges:
            e.select = False
        for poly in mesh.polygons:
            poly.select = not kept(mesh, poly)
            if poly.select:
                for li in poly.loop_indices:
                    mesh.vertices[mesh.loops[li].vertex_index].select = True
                    mesh.edges[mesh.loops[li].edge_index].select = True
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.context.tool_settings.mesh_select_mode = (False, False, True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    shrink_small_parts(meshes, scale)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=0.0015, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    covered = 0.0
    for o in meshes:  # the glass and lamps don't use the texture: shrink them to a point so the bake skips them
        mesh = o.data
        atlas = mesh.uv_layers["Atlas"]
        for poly in mesh.polygons:
            if kept(mesh, poly):
                for li in poly.loop_indices:
                    atlas.data[li].uv = (0.9995, 0.9995)
            else:
                uv = [atlas.data[li].uv for li in poly.loop_indices]
                covered += abs(sum(uv[i].x * uv[i - 1].y - uv[i - 1].x * uv[i].y for i in range(len(uv)))) / 2
    print(f"  atlas {covered:.0%} covered")


def shrink_small_parts(meshes, scale):
    """Small parts (track links, bolts, handles) get half the texture density of the big panels: there are
    hundreds of them, and the bake only needs to tell them apart from far off."""
    for o in meshes:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        uv = bm.loops.layers.uv["Atlas"]
        for island in bmesh_linked_uv_islands(bm, uv):
            if sum(f.calc_area() for f in island) * scale * scale > 0.03:
                continue
            loops = [loop for f in island for loop in f.loops]
            centre = sum((loop[uv].uv for loop in loops), Vector((0, 0))) / len(loops)
            for loop in loops:
                loop[uv].uv = centre + (loop[uv].uv - centre) * 0.5
        bm.to_mesh(o.data)
        bm.free()


# ---------- 3: bake ----------

def weather_material(mat, spec, ground, seed):
    """The weathering shader for one of the model's materials, wired for every bake: colour, metallic, smoothness
    and occlusion into Emission, relief into a Principled BSDF's Normal. Returns the material, its output node, the
    sockets to switch between and its image node (the bake target)."""
    name = base_name(mat)
    s = spec["scale"]
    out_mat = bpy.data.materials.new(f"Bake_{name}")
    g = Graph(out_mat)
    add = lambda a, b: g.math("ADD", a, b)  # noqa: E731
    mul = lambda a, b: g.math("MULTIPLY", a, b)  # noqa: E731
    inv = lambda a: g.math("SUBTRACT", 1.0, a)  # noqa: E731

    geo = g.node("ShaderNodeNewGeometry")
    nrm = geo.outputs["Normal"]
    p = g.vec("ADD", g.vec("SCALE", geo.outputs["Position"], scale=s), seed)  # in the game's metres
    z = mul(g.math("SUBTRACT", g.node("ShaderNodeSeparateXYZ", Vector=geo.outputs["Position"]).outputs["Z"], ground), s)
    up = g.node("ShaderNodeSeparateXYZ", Vector=nrm).outputs["Z"]

    if name in PHOTO:
        texture, tint, tile, smoothness, metallic, kind = PHOTO[name]
        uv = g.node("ShaderNodeUVMap")
        uv.uv_map = "Tile"
        mapping = g.node("ShaderNodeMapping", Vector=uv.outputs["UV"], Scale=(1 / tile, 1 / tile, 1))
        diff = g.node("ShaderNodeTexImage", Vector=mapping.outputs["Vector"])
        diff.image = photo(texture + "_diff")
        base = g.multiply(diff.outputs["Color"], (*srgb_to_linear(tint), 1))
        relief = g.node("ShaderNodeTexImage", Vector=mapping.outputs["Vector"])
        relief.image = photo(texture + "_nor")
        relief.image.colorspace_settings.name = "Non-Color"
        nmap = g.node("ShaderNodeNormalMap", Color=relief.outputs["Color"])
        nmap.uv_map = "Tile"
        base_normal = nmap.outputs["Normal"]
    else:
        smoothness, metallic, kind = FLAT.get(name, (0.2, 0.0, "soft"))
        base = (*srgb_to_linear(base_colour(mat)), 1)
        base_normal = nrm

    nb = g.noise(p, 0.6, 3.0)  # big patches
    nm = g.noise(p, 2.6, 6.0, 0.6)
    nf = g.noise(p, 12.0, 6.0, 0.62)
    ng = g.noise(p, 48.0, 3.0, 0.5)  # grain
    stretched = g.node("ShaderNodeMapping", Vector=p, Scale=(7.0, 7.0, 0.45)).outputs["Vector"]
    nstreak = g.noise(stretched, 1.4, 4.0, 0.55)
    ao = g.node("ShaderNodeAmbientOcclusion", Distance=0.35 / s)
    ao.samples = 16
    occl = ao.outputs["AO"]
    bevel = g.node("ShaderNodeBevel", Radius=0.012 / s)
    bevel.samples = 8
    edge = g.smooth(inv(g.vec("DOT_PRODUCT", bevel.outputs["Normal"], nrm)), 0.015, 0.1)
    edge = mul(edge, g.smooth(occl, 0.7, 0.95))  # outside corners only

    # Mud: up to a wavy line, with spatter above it, thick under the hull; tyres and tracks pick up more.
    line = add(mul(add(mul(nb, 0.6), 0.7), spec["mud"] + (0.05 if name == "Rubber" else 0.0)), mul(g.math("SUBTRACT", nm, 0.5), 0.25))
    core = g.smooth(g.math("SUBTRACT", line, z), -0.04, 0.1)
    near = inv(g.smooth(z, g.math("SUBTRACT", line, 0.05), add(line, 0.4)))
    drops = g.noise(p, 22.0, 4.0, 0.6)
    spatter = mul(g.smooth(add(add(mul(drops, 0.65), mul(ng, 0.35)), mul(near, 0.18)), 0.72, 0.75), near)
    under = mul(g.smooth(g.math("MULTIPLY", up, -1.0), 0.35, 0.8), inv(g.smooth(z, line, add(mul(line, 2.5), 0.3))))
    mud = g.math("MAXIMUM", g.math("MAXIMUM", core, spatter), under)
    wet = mul(inv(g.smooth(z, 0.0, mul(line, 0.7))), g.smooth(nm, 0.3, 0.6))
    dry = g.mix((0.08, 0.063, 0.045, 1), g.mix((0.13, 0.1, 0.072, 1), (0.21, 0.17, 0.12, 1), ng), g.smooth(nf, 0.35, 0.6))
    mud_col = g.mix(dry, (0.05, 0.04, 0.03, 1), wet)  # dry and cracked in clods, wet and dark low down

    # Dust: a film kicked up onto the sides (thicker low down), settled on the top surfaces and in their corners.
    top = g.smooth(up, 0.4, 0.9)
    film = add(0.06, mul(inv(g.smooth(z, 0.2, 1.4)), 0.22))
    settled = mul(mul(top, g.smooth(add(mul(nm, 0.6), mul(nb, 0.4)), 0.4, 0.7)), 0.5)
    corners = mul(mul(inv(occl), g.smooth(up, 0.2, 0.7)), 0.6)
    dust = mul(g.math("ADD", add(film, settled), corners, clamp=True), 0.85)
    dust_col = g.mix((0.27, 0.24, 0.19, 1), (0.37, 0.33, 0.26, 1), ng)

    # Rain streaks down the sides, grime in the crevices.
    sides = inv(g.smooth(g.math("ABSOLUTE", up), 0.25, 0.6))
    streak = mul(mul(g.smooth(nstreak, 0.56, 0.68), sides), g.smooth(nb, 0.25, 0.55))
    crevice = g.smooth(inv(occl), 0.1, 0.55)

    c = base
    smooth_v, metal_v = smoothness, metallic
    chip = 0.0
    if kind == "paint":
        c = g.multiply(c, g.node("ShaderNodeCombineColor", Red=add(0.88, mul(nb, 0.24)), Green=add(0.88, mul(nb, 0.24)),
                                 Blue=add(0.88, mul(nb, 0.24))).outputs[0])
        faded = g.node("ShaderNodeRGBToBW", Color=c).outputs[0]
        c = g.mix(c, g.multiply(g.node("ShaderNodeCombineColor", Red=faded, Green=faded, Blue=faded).outputs[0], (1.15, 1.15, 1.12, 1)),
                  mul(top, 0.18))  # sun-faded on top
        chip = g.smooth(add(add(mul(edge, 0.8), mul(nf, 0.45)), mul(ng, 0.25)), 0.92, 0.96)
        rusty = g.smooth(nm, 0.45, 0.65)
        c = g.mix(c, g.mix((0.22, 0.21, 0.2, 1), (0.14, 0.07, 0.035, 1), rusty), chip)
        smooth_v = g.mix((smoothness,) * 3 + (1,), g.mix((0.5, 0.5, 0.5, 1), (0.15, 0.15, 0.15, 1), rusty), chip)
        metal_v = g.mix((metallic,) * 3 + (1,), g.mix((0.85, 0.85, 0.85, 1), (0.2, 0.2, 0.2, 1), rusty), chip)
    elif kind == "metal":
        c = g.mix(c, g.multiply(c, (1.7, 1.7, 1.7, 1)), mul(edge, 0.5))  # rubbed shiny at the edges
        smooth_v = add(smoothness, mul(edge, 0.15))

    c = g.mix(c, (0.05, 0.045, 0.035, 1), mul(crevice, 0.55))
    c = g.mix(c, (0.07, 0.06, 0.045, 1), mul(streak, 0.45))
    c = g.mix(c, dust_col, dust)
    c = g.mix(c, mud_col, mud)
    c = g.multiply(c, g.node("ShaderNodeCombineColor", Red=add(0.93, mul(ng, 0.14)), Green=add(0.93, mul(ng, 0.14)),
                                Blue=add(0.93, mul(ng, 0.14))).outputs[0])
    shade = add(0.55, mul(occl, 0.45))
    colour = g.multiply(c, g.node("ShaderNodeCombineColor", Red=shade, Green=shade, Blue=shade).outputs[0])

    smooth_v = g.mix(smooth_v if not isinstance(smooth_v, float) else (smooth_v,) * 3 + (1,),
                     (0.0, 0.0, 0.0, 1), mul(crevice, 0.4))
    smooth_v = g.mix(smooth_v, (0.05, 0.05, 0.05, 1), dust)
    smooth_v = g.mix(smooth_v, g.mix((0.08, 0.08, 0.08, 1), (0.45, 0.45, 0.45, 1), wet), mud)
    metal_v = metal_v if not isinstance(metal_v, float) else (metal_v,) * 3 + (1,)
    metal_v = g.mix(g.mix(metal_v, (0.0, 0.0, 0.0, 1), dust), (0.0, 0.0, 0.0, 1), mud)
    occl_v = add(0.3, mul(occl, 0.7))

    height = add(mul(mud, add(0.55, mul(nf, 0.45))), mul(mul(dust, ng), 0.05))
    if kind == "paint":
        height = g.math("SUBTRACT", height, mul(chip, 0.25))  # chips sit below the paint
    bump = g.node("ShaderNodeBump", Strength=0.6, Distance=0.006 / s, Height=height, Normal=base_normal)
    bsdf = g.node("ShaderNodeBsdfPrincipled", Normal=bump.outputs["Normal"])

    sockets = {
        "diff": g.node("ShaderNodeEmission", Color=colour).outputs[0],
        "metal": g.node("ShaderNodeEmission", Color=metal_v).outputs[0],
        "smooth": g.node("ShaderNodeEmission", Color=smooth_v).outputs[0],
        "ao": g.node("ShaderNodeEmission", Color=occl_v).outputs[0],
        "nor": bsdf.outputs[0],
    }
    target = g.node("ShaderNodeTexImage")
    g.nt.nodes.active = target
    return out_mat, g.node("ShaderNodeOutputMaterial"), sockets, target


def blank_material():
    """For the glass and lamps during the bake (their faces are shrunk to a point, so it never shows)."""
    mat = bpy.data.materials.new("Bake_Kept")
    g = Graph(mat)
    sockets = {k: g.node("ShaderNodeEmission").outputs[0] for k in ("diff", "metal", "smooth", "ao")}
    sockets["nor"] = g.node("ShaderNodeBsdfPrincipled").outputs[0]
    target = g.node("ShaderNodeTexImage")
    g.nt.nodes.active = target
    return mat, g.node("ShaderNodeOutputMaterial"), sockets, target


PASSES = (  # name, bake type, size, colour space, samples
    ("diff", "EMIT", SIZE, "sRGB", 32),
    ("nor", "NORMAL", SIZE // 2, "Non-Color", 8),
    ("metal", "EMIT", SIZE // 4, "Non-Color", 8),
    ("smooth", "EMIT", SIZE // 4, "Non-Color", 8),
    ("ao", "EMIT", SIZE // 4, "Non-Color", 16),
)


def bake(meshes, kind, spec):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    device = use_gpu()
    ground = min((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices)
    rng = random.Random(kind)
    seed = Vector((rng.uniform(0, 100), rng.uniform(0, 100), rng.uniform(0, 100)))
    originals = {o.name: list(o.data.materials) for o in meshes}
    graphs = {}
    blank = blank_material()
    for o in meshes:
        for i, mat in enumerate(o.data.materials):
            name = base_name(mat)
            if name in KEPT:
                o.data.materials[i] = blank[0]
                continue
            if name not in graphs:
                graphs[name] = weather_material(mat, spec, ground, seed)
            o.data.materials[i] = graphs[name][0]
    graphs["_kept"] = blank
    painted = [o for o in meshes if any(not kept(o.data, p) for p in o.data.polygons)]
    bpy.ops.object.select_all(action="DESELECT")
    for o in painted:
        o.select_set(True)
    bpy.context.view_layer.objects.active = painted[0]
    scene.render.bake.normal_space = "TANGENT"
    images = {}
    for label, pass_type, px, colourspace, samples in PASSES:
        image = bpy.data.images.new(f"Weathered_{kind}_{label}", px, px)
        image.colorspace_settings.name = colourspace
        for mat, out, sockets, target in graphs.values():
            target.image = image
            mat.node_tree.links.new(sockets[label], out.inputs["Surface"])
        scene.cycles.samples = samples
        margin = {2048: 6, 1024: 4}.get(px, 2)
        scene.render.bake.margin = margin
        bpy.ops.object.bake(type=pass_type, margin=margin, use_clear=True)
        images[label] = image
    files = {}
    for label, quality in (("diff", 88), ("nor", 95)):
        path = os.path.join(TEXTURES, f"Weathered_{kind}_{label}.jpg")
        images[label].file_format = "JPEG"
        scene.render.image_settings.file_format = "JPEG"
        scene.render.image_settings.quality = quality
        images[label].save(filepath=path, quality=quality)
        files[label] = path
    files["mask"] = save_mask(kind, images)
    print(f"  BAKED {kind} on {device}")
    return originals, files


def save_mask(kind, images):
    """HDRP's mask map: metallic in red, occlusion in green, smoothness in alpha."""
    px = images["metal"].size[0]

    def grey(image):
        a = np.empty(px * px * 4, dtype=np.float32)
        image.pixels.foreach_get(a)
        return a[0::4]

    rgba = np.zeros(px * px * 4, dtype=np.float32)
    rgba[0::4] = grey(images["metal"])
    rgba[1::4] = grey(images["ao"])
    rgba[3::4] = grey(images["smooth"])
    mask = bpy.data.images.new(f"Weathered_{kind}_mask", px, px, alpha=True)
    mask.colorspace_settings.name = "Non-Color"
    mask.alpha_mode = "STRAIGHT"
    mask.pixels.foreach_set(rgba)
    path = os.path.join(TEXTURES, f"Weathered_{kind}_mask.png")
    mask.filepath_raw = path
    mask.file_format = "PNG"
    mask.save()
    return path


# ---------- 4: export ----------

def finish_materials(meshes, kind, originals, files):
    """Gives each part the Weathered_<kind> material in place of its painted ones (and its glass and lamps back),
    with the baked textures, and leaves the atlas as its only UV layer."""
    mat = material(f"Weathered_{kind}", (0.2, 0.2, 0.2), roughness=0.6)
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
    mask_tex = nt.nodes.new("ShaderNodeTexImage")
    mask_tex.image = bpy.data.images.load(files["mask"], check_existing=True)
    mask_tex.image.colorspace_settings.name = "Non-Color"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(mask_tex.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    rough = nt.nodes.new("ShaderNodeMath")
    rough.operation = "SUBTRACT"
    rough.inputs[0].default_value = 1.0
    nt.links.new(mask_tex.outputs["Alpha"], rough.inputs[1])
    nt.links.new(rough.outputs[0], bsdf.inputs["Roughness"])

    for o in meshes:
        mesh = o.data
        old = originals[o.name]
        slots, remap = [], []
        for m in old:
            keep = m if base_name(m) in KEPT else mat
            if keep not in slots:
                slots.append(keep)
            remap.append(slots.index(keep))
        indices = [remap[p.material_index] if p.material_index < len(remap) else 0 for p in mesh.polygons]
        mesh.materials.clear()
        for m in slots:
            mesh.materials.append(m)
        mesh.polygons.foreach_set("material_index", indices)
        uvs = mesh.uv_layers
        uvs.remove(uvs["Tile"])
        uvs["Atlas"].name = "UVMap"
        mesh.update()


def build(kind, spec):
    reset_scene()
    meshes = import_vehicle(kind)
    unwrap(meshes, spec["scale"])
    originals, files = bake(meshes, kind, spec)
    finish_materials(meshes, kind, originals, files)
    r = next(o for o in bpy.data.objects if o.type == "EMPTY" and o.parent is None)
    r.name = f"Weathered_{kind}"
    tris = 0
    for o in meshes:
        o.data.calc_loop_triangles()
        tris += len(o.data.loop_triangles)
    export_model(r, os.path.join(MODELS, f"Weathered_{kind}.fbx"))
    print(f"EXPORTED Weathered_{kind} ({tris} triangles)")
    if PREVIEW_DIR:
        os.makedirs(PREVIEW_DIR, exist_ok=True)
        distance, target_z = spec["view"]
        render_preview(os.path.join(PREVIEW_DIR, f"Weathered_{kind}.png"), distance, target_z)
        cam = bpy.context.scene.camera  # and from above, as the game's camera sees it
        cam.location = (distance * 0.3, -distance * 0.55, distance * 1.05 + target_z)
        cam.rotation_euler = (Vector((0, 0, target_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        bpy.context.scene.render.filepath = os.path.join(PREVIEW_DIR, f"Weathered_{kind}_top.png")
        bpy.ops.render.render(write_still=True)
        cam.location = (distance * 0.5, -distance * 0.3, distance * 0.12 + target_z * 0.5)  # close, low, from the side
        cam.rotation_euler = (Vector((0, 0, target_z * 0.6)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        bpy.context.scene.render.filepath = os.path.join(PREVIEW_DIR, f"Weathered_{kind}_side.png")
        bpy.ops.render.render(write_still=True)
        print(f"PREVIEW Weathered_{kind}")


def main():
    os.makedirs(TEXTURES, exist_ok=True)
    for kind, spec in VEHICLES.items():
        if ONLY and kind not in ONLY:
            continue
        build(kind, spec)


if __name__ == "__main__":
    main()
