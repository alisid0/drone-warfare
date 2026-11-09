"""Turns the CC0 Poly Haven photo scans downloaded by Tools/fetch_models.py (Tools/scans/<id>/) into Unity models:
rocks, cliffs, stumps, a fallen trunk, ferns, shrubs and ground-cover plants.

Run from anywhere (Blender 5.x):
    blender --background --factory-startup --python Tools/Blender/import_scans.py -- [preview_dir] [names]
names: a comma-separated list of scan ids or model names to rebuild (default: all). With a preview folder it also
renders a tile per model (full detail on the left, the lighter LOD on the right) and a contact sheet of them all.
Keep the preview path short: Blender fails on long Windows paths.

What it makes, for each scan:
- Models in Assets/DroneSandbox/Resources/Models/Scan_<Name>.fbx, in real-world metres with the origin at the bottom
  centre (patches: at the ground they stand on). Each is a root empty <Name> with meshes <Name>_LOD0 and, for most,
  a decimated <Name>_LOD1, so Unity's importer gives it a LOD group (switch distances: Art.TuneLods). Every mesh has
  exactly one UV layer, UVMap (a second layer makes Unity read all-zero UVs), and one material, Scan_<id>, which
  Art.TryModel swaps for the photo material of that name (made by ProjectSetup from the textures below).
- Textures in Assets/DroneSandbox/Textures/PBR: scan_<id>_diff and scan_<id>_nor (OpenGL normal map). Poly Haven
  ships a plant's cut-out as a separate alpha map (its glTF colour map is a JPG), so for plants the two are
  merged into an RGBA PNG; rocks keep the JPG.
- Sets are split: a scan of several rocks or plants becomes one model per piece (_A, _B, ...), in the order of the
  scan's object names, or left to right when the pieces share one mesh.
- Plants too small to see from the game's camera (moss stems, nettle seedlings, celandine, dandelions, pebbles) are
  gathered into patches instead: a metre or so of ground cover in one mesh, so the map needs a handful of objects
  for what would otherwise be dozens. Young nettles are scaled up to adult size, and leafy twigs (shrub_04) are
  bunched into bushes.
- The rock faces are open shells (only the face was scanned), so they get a back: Solidify gives them thickness,
  and they're turned so the face looks along Blender's -Y, which is the model's forward (+Z) in Unity.
"""
import math
import os
import random
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import export, root, set_parent, single_uv_layer  # noqa: E402

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
SCANS = os.path.join(PROJECT, "Tools", "scans")
MODEL_DIR = os.path.join(PROJECT, "Assets", "DroneSandbox", "Resources", "Models")
TEXTURE_DIR = os.path.join(PROJECT, "Assets", "DroneSandbox", "Textures", "PBR")

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW_DIR = os.path.abspath(args[0]) if args and args[0] else None
ONLY = set(args[1].split(",")) if len(args) > 1 and args[1] else None


# ---------- what to make ----------

def single(name, lod0, lod1=0):
    """The whole scan as one model."""
    return dict(how="single", name=name, lod0=lod0, lod1=lod1)


def pieces(name, lod0, lod1=0, split="objects"):
    """One model per piece of a set: per object in the scan, or per group of loose parts along X ("loose")."""
    return dict(how="pieces", name=name, lod0=lod0, lod1=lod1, split=split)


def face(name, front, front_lod1, thickness):
    """A scanned rock face, turned to face forward and given a back (front triangles before the back is added)."""
    return dict(how="face", name=name, lod0=front, lod1=front_lod1, thickness=thickness)


def patch(name, seed, count, radius, scale, tris, lod1_tris=0, split="objects", **style):
    """A clump of the scan's pieces scattered over a rough disc: count pieces, each cut to `tris` triangles
    (and `lod1_tris` for the lighter level), scaled by a random factor in `scale`."""
    return dict(how="patch", name=name, seed=seed, count=count, radius=radius, scale=scale, tris=tris,
                lod1_tris=lod1_tris, split=split, style=style)


# Poly Haven id -> the models made from it. Triangle targets: rocks about 3-6k at full detail and 600-1000 for the
# lighter level. Plants are left as they are when already light (ferns, moss); the plant patches get a thinned
# lighter level too, since from the game's camera a metre-wide clump is only a few percent of the screen.
SCAN_MODELS = {
    # Rocks and cliffs (2K textures).
    "rock_moss_set_01": [pieces("Scan_MossRock", 5000, 900)],          # six mossy boulders, 2-3.4 m
    "rock_moss_set_02": [pieces("Scan_MossStone", 4500, 800)],         # seven mossy rocks, 1-2 m
    "boulder_01": [single("Scan_Boulder", 6000, 1000)],                # a lichen-streaked boulder, 1.8 m
    "rock_07": [single("Scan_Stone", 3500, 700),                       # a 30 cm stone, scaled up in the game
                patch("Scan_Pebbles_A", 1, 18, 0.9, (0.6, 1.7), 200, 40, sink=0.3, tilt=25)],
    "rock_09": [single("Scan_FlatStone", 3000, 600),                   # a flat 15 cm stone
                patch("Scan_Pebbles_B", 2, 22, 0.9, (1.4, 3.4), 150, 30, sink=0.3, tilt=10)],
    "rock_face_01": [face("Scan_RockFace01", 2800, 440, 0.9)],         # a 5 m face of layered rock
    "rock_face_02": [face("Scan_RockFace02", 2800, 440, 0.6)],         # a 2.7 m face, mossy
    # Deadwood (1K).
    "tree_stump_01": [single("Scan_Stump01", 5000, 900)],
    "tree_stump_02": [single("Scan_Stump02", 5000, 900)],
    "dead_tree_trunk": [single("Scan_FallenTrunk", 5000, 900)],
    # Plants (1K, cut out by alpha).
    "fern_02": [pieces("Scan_Fern", 2500)],                            # four fern clumps, 0.6-1 m
    "shrub_02": [pieces("Scan_Shrub", 4000, 1000)],                    # four low, wiry shrubs, 1-1.7 m
    "shrub_04": [patch("Scan_Bush_A", 3, 9, 0.15, (4.5, 6.0), 600, 130, split="loose", bush=True),
                 patch("Scan_Bush_B", 4, 11, 0.2, (4.0, 5.5), 500, 110, split="loose", bush=True)],
    "nettle_plant": [patch("Scan_Nettles_A", 5, 11, 0.6, (2.8, 3.6), 550, 140, shrink=0.35),
                     patch("Scan_Nettles_B", 6, 8, 0.45, (2.6, 3.4), 550, 140, shrink=0.35)],
    "shrub_01": [patch("Scan_Herbs_A", 7, 7, 0.45, (1.4, 1.9), 700, 180, split="loose", shrink=0.3),
                 patch("Scan_Herbs_B", 8, 5, 0.35, (1.3, 1.8), 700, 180, split="loose", shrink=0.3)],
    "shrub_03": [patch("Scan_Weeds_A", 9, 9, 0.5, (1.1, 1.5), 600, 150, shrink=0.3),
                 patch("Scan_Weeds_B", 10, 6, 0.4, (1.0, 1.4), 600, 150, shrink=0.3)],
    "celandine_01": [patch("Scan_Celandine_A", 11, 12, 0.55, (0.9, 1.3), 500, 120),
                     patch("Scan_Celandine_B", 12, 9, 0.45, (0.9, 1.3), 500, 120)],
    "dandelion_01": [patch("Scan_Dandelions_A", 13, 7, 0.6, (1.0, 1.4), 800, 160),
                     patch("Scan_Dandelions_B", 14, 5, 0.45, (1.0, 1.4), 800, 160)],
    "moss_01": [patch("Scan_Moss_A", 15, 340, 0.36, (2.6, 3.6), 30, dome=0.09),
                patch("Scan_Moss_B", 16, 260, 0.3, (2.6, 3.4), 30, dome=0.07)],
}


# ---------- import ----------

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def import_scan(asset):
    """Imports the scan's glTF and returns its mesh objects with all transforms applied, sorted by name.
    Vertices split at UV seams are merged back, so decimation can work across the seams."""
    folder = os.path.join(SCANS, asset)
    gltf = next(f for f in sorted(os.listdir(folder)) if f.endswith(".gltf"))
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(folder, gltf), merge_vertices=True, import_shading="SMOOTH",
                              import_scene_as_collection=False)
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in meshes:
        if o.data.users > 1:
            o.data = o.data.copy()
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = world
        o.data.transform(world)
        o.matrix_world = Matrix.Identity(4)
    for o in [o for o in new if o.type != "MESH"]:
        bpy.data.objects.remove(o, do_unlink=True)
    return sorted(meshes, key=lambda o: o.name)


def split_loose(obj):
    """Splits a mesh holding several plants side by side into one object per plant: the loose parts (stems, leaves)
    are grouped by where they sit along X, and a gap along X separates two plants."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    part = [-1] * len(bm.faces)
    spans = []
    for start in bm.faces:
        if part[start.index] >= 0:
            continue
        stack, faces = [start], []
        part[start.index] = len(spans)
        while stack:
            f = stack.pop()
            faces.append(f.index)
            for e in f.edges:
                for g in e.link_faces:
                    if part[g.index] < 0:
                        part[g.index] = len(spans)
                        stack.append(g)
        xs = [v.co.x for i in faces for v in bm.faces[i].verts]
        spans.append((min(xs), max(xs)))
    order = sorted(range(len(spans)), key=lambda i: spans[i][0])
    group_of, groups, reach = {}, 0, None
    for i in order:
        lo, hi = spans[i]
        if reach is not None and lo > reach + 0.01:
            groups += 1
        reach = hi if reach is None or lo > reach + 0.01 else max(reach, hi)
        group_of[i] = groups
    result = []
    for g in range(groups + 1):
        copy = bm.copy()
        bmesh.ops.delete(copy, geom=[f for i, f in enumerate(copy.faces) if group_of[part[i]] != g], context="FACES")
        mesh = bpy.data.meshes.new(f"{obj.name}_{g}")
        copy.to_mesh(mesh)
        copy.free()
        o = bpy.data.objects.new(mesh.name, mesh)
        bpy.context.collection.objects.link(o)
        result.append(o)
    bm.free()
    bpy.data.objects.remove(obj, do_unlink=True)
    return result


def bottom_centre(obj):
    """Moves the mesh so its origin is in the middle of its footprint, at its lowest point."""
    co = [v.co for v in obj.data.vertices]
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(c.z for c in co)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    obj.data.transform(Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))


def size(obj):
    co = [v.co for v in obj.data.vertices]
    return Vector((max(c.x for c in co) - min(c.x for c in co), max(c.y for c in co) - min(c.y for c in co),
                   max(c.z for c in co) - min(c.z for c in co)))


def apply_modifiers(obj):
    """Bakes the object's modifiers into its mesh (without bpy.ops, so it works with nothing selected)."""
    graph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(graph))
    old = obj.data
    obj.modifiers.clear()
    obj.data = mesh
    if old.users == 0:
        bpy.data.meshes.remove(old)


def decimated(src, name, target, foliage=False):
    """A copy of src cut down to about `target` triangles with the Decimate modifier (collapse). Foliage is thinned
    first (see thin_leaves), since collapsing hundreds of small leaves shrinks them to nothing."""
    obj = src.copy()
    obj.data = src.data.copy()
    obj.name = name
    bpy.context.collection.objects.link(obj)
    if foliage and target:
        thin_leaves(obj, target, random.Random(name))
    tris = triangles(obj)
    if target and tris > target:
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = target / tris
        mod.use_collapse_triangulate = True
        apply_modifiers(obj)
    obj.data.name = name
    obj.data.shade_smooth()
    return obj


def thin_leaves(obj, target, rng):
    """Plant scans are hundreds of separate leaves (loose parts of a few dozen triangles) on a few stems. Collapse
    keeps a leaf recognisable down to about six triangles; if that still leaves more than `target`, drop leaves at
    random instead (never stems, the parts much bigger than a typical leaf) and enlarge the ones kept a little so
    the plant stays about as leafy from a distance."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()
    seen = [False] * len(bm.faces)
    parts = []
    for start in bm.faces:
        if seen[start.index]:
            continue
        stack, faces = [start], []
        seen[start.index] = True
        while stack:
            f = stack.pop()
            faces.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if not seen[g.index]:
                        seen[g.index] = True
                        stack.append(g)
        parts.append(faces)
    typical = sorted(len(p) for p in parts)[len(parts) // 2]
    floor = len(bm.faces) * min(1.0, 6.0 / typical)  # triangles left once every leaf is down to ~6
    if floor <= target:
        bm.free()
        return
    keep = target / floor
    grow = min(1.35, keep ** -0.5)
    drop = []
    for faces in parts:
        if len(faces) > 3 * typical:
            continue  # a stem
        if rng.random() > keep:
            drop += faces
            continue
        verts = {v for f in faces for v in f.verts}
        centre = sum((v.co for v in verts), Vector()) / len(verts)
        for v in verts:
            v.co = centre + (v.co - centre) * grow
    bmesh.ops.delete(bm, geom=drop, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()


# ---------- textures and material ----------

def find(folder, key):
    hits = [f for f in os.listdir(folder) if key in f]
    return os.path.join(folder, sorted(hits)[0]) if hits else None


def replace(path):
    """Removes an older copy of a texture saved with the other extension (and its Unity .meta)."""
    stem, ext = os.path.splitext(path)
    for other in (".jpg", ".png"):
        if other != ext:
            for p in (stem + other, stem + other + ".meta"):
                if os.path.exists(p):
                    os.remove(p)


def copy_textures(asset):
    """Copies the colour and normal maps into Textures/PBR as scan_<id>_diff / _nor. A plant's alpha map is merged
    into its colour map, giving an RGBA PNG. Returns (colour path, normal path, has alpha)."""
    folder = os.path.join(SCANS, asset, "textures")
    diff, nor, alpha = find(folder, "_diff_"), find(folder, "_nor_gl_"), find(folder, "_alpha_")
    os.makedirs(TEXTURE_DIR, exist_ok=True)
    nor_out = os.path.join(TEXTURE_DIR, f"scan_{asset}_nor" + os.path.splitext(nor)[1])
    replace(nor_out)
    shutil.copyfile(nor, nor_out)
    if not alpha:
        diff_out = os.path.join(TEXTURE_DIR, f"scan_{asset}_diff" + os.path.splitext(diff)[1])
        replace(diff_out)
        shutil.copyfile(diff, diff_out)
        return diff_out, nor_out, False
    diff_out = os.path.join(TEXTURE_DIR, f"scan_{asset}_diff.png")
    replace(diff_out)
    colour = bpy.data.images.load(diff)
    cut = bpy.data.images.load(alpha)
    w, h = colour.size
    if tuple(cut.size) != (w, h):
        cut.scale(w, h)
    rgba = np.empty(w * h * 4, dtype=np.float32)
    colour.pixels.foreach_get(rgba)
    mask = np.empty(w * h * 4, dtype=np.float32)
    cut.pixels.foreach_get(mask)
    rgba[3::4] = mask[0::4]
    merged = bpy.data.images.new(f"scan_{asset}_diff", w, h, alpha=True)
    merged.pixels.foreach_set(rgba)
    merged.filepath_raw = diff_out
    merged.file_format = "PNG"
    merged.save()
    for img in (colour, cut, merged):
        bpy.data.images.remove(img)
    return diff_out, nor_out, True


def scan_material(asset, diff, nor, cutout):
    """The model's one material, Scan_<id>. Unity swaps it for the photo material of the same name; the textures
    here only make the preview (and the FBX on its own) look right."""
    m = bpy.data.materials.new(f"Scan_{asset}")
    m["cutout"] = cutout
    try:
        m.use_nodes = True  # always on (and deprecated) in Blender 5
    except AttributeError:
        pass
    nodes, links = m.node_tree.nodes, m.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.85
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(diff)
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if cutout:
        links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    ntex = nodes.new("ShaderNodeTexImage")
    ntex.image = bpy.data.images.load(nor)
    ntex.image.colorspace_settings.name = "Non-Color"
    nmap = nodes.new("ShaderNodeNormalMap")
    links.new(ntex.outputs["Color"], nmap.inputs["Color"])
    links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return m


def is_plant(mat):
    return mat.get("cutout", False)


def finish_mesh(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.material_index = 0
    single_uv_layer(obj)
    for attr in list(obj.data.color_attributes):
        obj.data.color_attributes.remove(attr)


def make_model(name, lods, mat):
    """A root empty <name> with the given meshes as <name>_LOD0, _LOD1."""
    r = root(name)
    for i, obj in enumerate(lods):
        obj.name = f"{name}_LOD{i}"
        obj.data.name = obj.name
        finish_mesh(obj, mat)
        set_parent(obj, r)
    return r


# ---------- the kinds of model ----------

def build_single(spec, parts, mat):
    src = join(parts) if len(parts) > 1 else parts[0]
    bottom_centre(src)
    model = lods_of(src, spec, mat)
    bpy.data.objects.remove(src, do_unlink=True)
    return [(spec["name"], model)]


def build_pieces(spec, parts, mat):
    if spec["split"] == "loose":
        parts = [p for part in parts for p in split_loose(part)]
    models = []
    for i, src in enumerate(parts):
        bottom_centre(src)
        name = f"{spec['name']}_{chr(ord('A') + i)}"
        models.append((name, lods_of(src, spec, mat, name)))
        bpy.data.objects.remove(src, do_unlink=True)
    return models


def lods_of(src, spec, mat, name=None):
    name = name or spec["name"]
    foliage = is_plant(mat)
    lods = [decimated(src, name + "_LOD0", spec["lod0"], foliage)]
    if spec["lod1"]:
        lods.append(decimated(src, name + "_LOD1", spec["lod1"], foliage))
    return make_model(name, lods, mat)


def facing(obj):
    """The way a scanned face looks: its area-weighted normal."""
    normal = Vector()
    for p in obj.data.polygons:
        normal += p.normal * p.area
    return normal.normalized()


def build_face(spec, parts, mat):
    """Turns the face so it looks along -Y, then gives it a solid back."""
    src = join(parts) if len(parts) > 1 else parts[0]
    normal = facing(src)
    src.data.transform(Matrix.Rotation(math.atan2(normal.x, -normal.y), 4, "Z"))
    lods = []
    for i, target in enumerate((spec["lod0"], spec["lod1"])):
        obj = decimated(src, f"{spec['name']}_LOD{i}", target)
        add_back(obj, spec["thickness"])
        lods.append(obj)
    bpy.data.objects.remove(src, do_unlink=True)
    # Bottom centre, measured on the full-detail mesh so the LODs move together.
    co = [v.co for v in lods[0].data.vertices]
    shift = Matrix.Translation((-(min(c.x for c in co) + max(c.x for c in co)) / 2,
                                -(min(c.y for c in co) + max(c.y for c in co)) / 2, -min(c.z for c in co)))
    for obj in lods:
        obj.data.transform(shift)
    return [(spec["name"], make_model(spec["name"], lods, mat))]


def add_back(obj, thickness):
    """Closes a scanned face's open back with a smoothed copy of the face, pushed `thickness` metres straight back
    and stitched to it round the edge. (Solidify offsets each point along its own normal, and on a surface this
    bumpy the back pokes out through the front.) The back shares the face's UVs, so it's rock too."""
    shift = -facing(obj) * thickness
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv.active
    front = list(bm.faces)
    verts = list(bm.verts)
    smooth = {v: v.co.copy() for v in verts}
    for _ in range(15):
        smooth = {v: smooth[v] * 0.5 + sum((smooth[e.other_vert(v)] for e in v.link_edges), Vector()) * 0.5 / len(v.link_edges)
                  if v.link_edges else smooth[v] for v in verts}
    back = {v: bm.verts.new(smooth[v] + shift) for v in verts}
    rims = []
    for f in front:
        for loop in f.loops:
            if loop.edge.is_boundary:
                a, b = loop.vert, loop.link_loop_next.vert
                rims.append(((b, a, back[a], back[b]), (loop.link_loop_next[uv].uv.copy(), loop[uv].uv.copy())))
        loops = list(reversed(f.loops))
        new = bm.faces.new([back[l.vert] for l in loops])
        for nl, l in zip(new.loops, loops):
            nl[uv].uv = l[uv].uv
    for quad, (uv_b, uv_a) in rims:
        try:
            new = bm.faces.new(quad)
        except ValueError:
            continue  # the scan's edge touches itself here
        for nl, value in zip(new.loops, (uv_b, uv_a, uv_a, uv_b)):
            nl[uv].uv = value
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.shade_smooth()


def join(parts):
    bm = bmesh.new()
    for p in parts:
        bm.from_mesh(p.data)
    mesh = bpy.data.meshes.new(parts[0].name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(mesh.name, mesh)
    bpy.context.collection.objects.link(obj)
    for p in parts:
        bpy.data.objects.remove(p, do_unlink=True)
    return obj


def build_patch(spec, parts, mat):
    """Scatters the scan's pieces over a rough disc and merges them into one mesh (and a lighter copy)."""
    if spec["split"] == "loose":
        parts = [p for part in parts for p in split_loose(part)]
    for p in parts:
        bottom_centre(p)
    style = spec["style"]
    rng = random.Random(spec["seed"])
    foliage = is_plant(mat)
    hi = [decimated(p, p.name + "_hi", spec["tris"], foliage) for p in parts]
    lo = [decimated(p, p.name + "_lo", spec["lod1_tris"], foliage) for p in parts] if spec["lod1_tris"] else None
    layout = []
    radius = spec["radius"]
    wobble = [rng.uniform(0, 2 * math.pi) for _ in range(3)]
    spots = []
    gap = 0.0 if style.get("dome") else radius * 0.9 / math.sqrt(spec["count"])
    tries = 0
    while len(spots) < spec["count"] and tries < spec["count"] * 80:
        tries += 1
        a = rng.uniform(0, 2 * math.pi)
        r = radius * math.sqrt(rng.random())
        # A lumpy outline, not a perfect circle.
        edge = radius * (0.78 + 0.12 * math.sin(2 * a + wobble[0]) + 0.1 * math.sin(3 * a + wobble[1]))
        if r > edge:
            continue
        p = Vector((r * math.cos(a), r * math.sin(a), 0))
        if any((p - q).length < gap for q in spots):
            continue
        spots.append(p)
    for p in spots:
        k = rng.randrange(len(parts))
        s = rng.uniform(*spec["scale"])
        if style.get("shrink"):
            s *= 1 - style["shrink"] * min(1.0, p.length / radius)  # smaller towards the edge of the clump
        spin = Matrix.Rotation(rng.uniform(0, 2 * math.pi), 4, "Z")
        if style.get("bush"):
            # Branches leaning out from the middle, like the stems of a shrub.
            lean = Matrix.Rotation(math.radians(rng.uniform(8, 35)), 4, "X")
            tilt = Matrix.Rotation(math.atan2(p.x, -p.y), 4, "Z") @ lean
        elif style.get("dome"):
            # Moss: stems standing out of a low cushion.
            d = style["dome"]
            u = p.length / radius
            p.z = d * (1 - u * u) - 0.01
            n = Vector((2 * d * p.x / radius ** 2, 2 * d * p.y / radius ** 2, 1)).normalized()
            tilt = Vector((0, 0, 1)).rotation_difference(n).to_matrix().to_4x4()
        else:
            t = math.radians(rng.uniform(0, style.get("tilt", 8)))
            tilt = Matrix.Rotation(t, 4, Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)).normalized())
        sink = style.get("sink", 0.0) * size(parts[k]).z * s
        layout.append((k, Matrix.Translation(p - Vector((0, 0, sink))) @ tilt @ spin @ Matrix.Scale(s, 4)))
    lods = [assemble(spec["name"] + "_LOD0", hi, layout)]
    if lo:
        lods.append(assemble(spec["name"] + "_LOD1", lo, layout))
    # Centred on the clump; z = 0 stays the ground it grows from.
    co = [v.co for v in lods[0].data.vertices]
    shift = Matrix.Translation((-(min(c.x for c in co) + max(c.x for c in co)) / 2,
                                -(min(c.y for c in co) + max(c.y for c in co)) / 2, 0))
    for obj in lods:
        obj.data.transform(shift)
    for obj in hi + (lo or []) + parts:
        bpy.data.objects.remove(obj, do_unlink=True)
    return [(spec["name"], make_model(spec["name"], lods, mat))]


def assemble(name, sources, layout):
    bm = bmesh.new()
    for k, matrix in layout:
        mesh = sources[k].data.copy()
        mesh.transform(matrix)
        bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.shade_smooth()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


BUILDERS = {"single": build_single, "pieces": build_pieces, "face": build_face, "patch": build_patch}


# ---------- preview ----------

def render_tile(model_root, path, text):
    """Renders one model: full detail on the left and the lighter LOD on the right, with a label."""
    scene = bpy.context.scene
    lods = sorted([o for o in model_root.children if o.type == "MESH"], key=lambda o: o.name)
    others = [o for o in scene.objects if o.type == "MESH" and o not in lods and o.name != "PreviewGround"]
    for o in others:
        o.hide_render = True
    width = max(size(o).x for o in lods)
    for i, o in enumerate(lods):
        o.location.x = (i - (len(lods) - 1) / 2) * width * 1.15
    bpy.context.view_layer.update()
    corners = [o.matrix_world @ Vector(c) for o in lods for c in o.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    centre, radius = (lo + hi) / 2, max((hi - lo).length / 2, 0.15)
    cam = scene.camera
    direction = Vector((0.45, -1.0, 0.55)).normalized()
    cam.location = centre + direction * radius / math.sin(math.atan(13.5 / 50)) * 1.05
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.objects["PreviewGround"].scale = (radius * 6, radius * 6, 1)
    label = scene.objects["PreviewLabel"]
    label.data.body = text
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in lods:
        o.location.x = 0
    for o in others:
        o.hide_render = False


def preview_stage():
    """Sun, sky, ground, camera and label for render_tile."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 480, 360
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("PreviewSky")
    scene.world = world
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    bg = next((n for n in world.node_tree.nodes if n.type == "BACKGROUND"), None)
    if bg:
        bg.inputs["Color"].default_value = (0.5, 0.58, 0.68, 1)
        bg.inputs["Strength"].default_value = 0.9
    sun = bpy.data.objects.new("PreviewSun", bpy.data.lights.new("PreviewSun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(40), 0, math.radians(35))
    scene.collection.objects.link(sun)
    plane = bpy.data.meshes.new("PreviewGround")
    plane.from_pydata([(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new("PreviewGround", plane)
    gm = bpy.data.materials.new("PreviewGround")
    try:
        gm.use_nodes = True
    except AttributeError:
        pass
    next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Base Color"].default_value = (0.13, 0.15, 0.08, 1)
    plane.materials.append(gm)
    scene.collection.objects.link(ground)
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    cam.data.lens = 50
    scene.collection.objects.link(cam)
    scene.camera = cam
    curve = bpy.data.curves.new("PreviewLabel", "FONT")
    curve.size = 0.026 * 0.1
    label = bpy.data.objects.new("PreviewLabel", curve)
    lm = bpy.data.materials.new("PreviewLabel")
    try:
        lm.use_nodes = True
    except AttributeError:
        pass
    b = next(n for n in lm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (0, 0, 0, 1)
    b.inputs["Emission Color"].default_value = (1, 1, 1, 1)
    b.inputs["Emission Strength"].default_value = 1.0
    curve.materials.append(lm)
    scene.collection.objects.link(label)
    label.parent = cam
    label.location = (-0.0345, -0.0255, -0.1)
    cam.data.clip_start = 0.01


def contact_sheet(tiles, path, columns=5):
    """Stitches the tiles (all the same size) into one picture."""
    images = [bpy.data.images.load(t) for t in tiles]
    w, h = images[0].size
    rows = (len(images) + columns - 1) // columns
    sheet = np.zeros((rows * h, columns * w, 4), dtype=np.float32)
    sheet[..., 3] = 1
    for i, img in enumerate(images):
        px = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        r, c = i // columns, i % columns
        y0 = (rows - 1 - r) * h  # Blender's rows run bottom to top
        sheet[y0:y0 + h, c * w:(c + 1) * w] = px.reshape(h, w, 4)
    out = bpy.data.images.new("ScanSheet", columns * w, rows * h, alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()


# ---------- main ----------

def wanted(asset, specs):
    if not ONLY:
        return specs
    if asset in ONLY:
        return specs
    return [s for s in specs if s["name"] in ONLY or any(n.startswith(s["name"] + "_") for n in ONLY)]


def main():
    os.makedirs(MODEL_DIR, exist_ok=True)
    tiles, report = [], []
    for asset, specs in SCAN_MODELS.items():
        specs = wanted(asset, specs)
        if not specs:
            continue
        if not os.path.isdir(os.path.join(SCANS, asset)):
            print(f"SKIPPED {asset}: not downloaded (python Tools/fetch_models.py {asset})")
            continue
        reset()
        diff, nor, cutout = copy_textures(asset)
        mat = scan_material(asset, diff, nor, cutout)
        models = []
        for spec in specs:
            parts = import_scan(asset)
            models += BUILDERS[spec["how"]](spec, parts, mat)
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            preview_stage()
        for name, model_root in models:
            export(model_root, os.path.join(MODEL_DIR, name + ".fbx"))
            lods = sorted([o for o in model_root.children if o.type == "MESH"], key=lambda o: o.name)
            counts = [triangles(o) for o in lods]
            dims = size(lods[0])
            line = f"{name}: {' / '.join(str(c) for c in counts)} triangles, {dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m ({asset})"
            print("EXPORTED " + line)
            report.append(line)
            if PREVIEW_DIR:
                tile = os.path.join(PREVIEW_DIR, name + ".png")
                render_tile(model_root, tile, f"{name}  {' / '.join(str(c) for c in counts)}")
                tiles.append(tile)
    if PREVIEW_DIR and tiles:
        reset()
        for n in range(0, len(tiles), 20):
            contact_sheet(tiles[n:n + 20], os.path.join(PREVIEW_DIR, f"sheet_{n // 20 + 1}.png"))
        with open(os.path.join(PREVIEW_DIR, "report.txt"), "w") as f:
            f.write("\n".join(report) + "\n")
        print("SHEETS in " + PREVIEW_DIR)


main()
