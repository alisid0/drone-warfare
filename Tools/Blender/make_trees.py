"""Builds the trees (oaks, birches, Lombardy poplars, Norway spruces and Scots pines, several of each) in Blender
and exports them for Unity, replacing the simple ones from make_scenery.py.

Run from the project root (Blender 4.x or 5.x):
    blender --background --python Tools/Blender/make_trees.py -- Assets/DroneSandbox/Resources/Models Assets/DroneSandbox/Textures/PBR [preview_dir] [names]
Add --lod2-only to reuse the existing leaf/bark textures when changing only the far impostors.

How a tree is made:
1. Leaf atlas: each species' leaf (lobed oak, toothed birch, diamond poplar) is modelled, a few dozen of them are
   set along four twigs, and the twigs are rendered from above into a 2x2 atlas: colour with alpha
   (leaves_<species>.png) and a normal map from the leaves' fold and droop (leaves_<species>_nor.png).
   The birch's white bark is rendered the same way, from a tileable procedural shader (birch_bark_diff.png).
2. Branches grow by space colonisation: points scattered through the shape of the crown pull the branches
   towards them until the crown is filled, with a pull up or down per species (poplars reach up, birches droop).
   Spruces instead grow one straight leader with whorls of drooping branches, shorter towards the top, each with
   side shoots; pines are the same idea with a tall bare trunk and a rounded, irregular crown at the top.
   Thicknesses follow the pipe model (a branch is as thick as its children together).
3. The branches become tubes with real bark UVs (around and along, in metres); the trunk flares at the root.
4. Leaf cards (the atlas twigs) sit on every fine branch tip, with normals pointing out from the middle of the
   crown, so the crown shades softly like a real one instead of card by card.
5. Three levels of detail, named so Unity makes a LOD group: _LOD0 full, _LOD1 lighter, _LOD2 three crossed
   side cards and a horizontal crown card, sharing one side/top atlas (impostor_<species>.png).
Materials: Bark<Species>, Leaves<Species> and Impostor<Species> (photo-textured in Unity by ProjectSetup).
"""
import math
import os
import random
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import export, reset_scene, root  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector, kdtree, noise  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
LOD2_ONLY = "--lod2-only" in args
args = [arg for arg in args if arg != "--lod2-only"]
OUT_DIR = os.path.abspath(args[0] if args else ".")
TEX_DIR = os.path.abspath(args[1]) if len(args) > 1 else OUT_DIR
PREVIEW_DIR = os.path.abspath(args[2]) if len(args) > 2 and args[2] else None
ONLY = set(args[3].split(",")) if len(args) > 3 and args[3] else None
BAKED = set()

# ---------- species ----------

SPECIES = {
    "oak": dict(
        leaf="oak", leaves=9, leaf_len=0.2, dark=(0.06, 0.12, 0.032), light=(0.14, 0.22, 0.055), stem=(0.16, 0.11, 0.06),
        bark="BarkOak", trunk_base=0.45, tip_radius=0.012, segment=0.45, influence=3.4, kill=0.9, attractors=1000,
        trunk_top=3.2, tropism=-0.05, wander=0.3, card=1.7, card_radius=0.045, card_chance=0.8, density=1.0,
    ),
    "birch": dict(
        leaf="birch", leaves=15, leaf_len=0.11, dark=(0.11, 0.19, 0.045), light=(0.24, 0.33, 0.08), stem=(0.25, 0.14, 0.08),
        bark="BarkBirch", trunk_base=0.2, tip_radius=0.009, segment=0.35, influence=2.6, kill=0.7, attractors=950,
        trunk_top=4.5, tropism=-0.32, wander=0.18, card=1.25, card_radius=0.03, card_chance=0.9, density=1.35,
    ),
    "poplar": dict(
        leaf="poplar", leaves=11, leaf_len=0.15, dark=(0.07, 0.14, 0.04), light=(0.17, 0.26, 0.07), stem=(0.2, 0.15, 0.08),
        bark="BarkPoplar", trunk_base=0.32, tip_radius=0.01, segment=0.4, influence=2.6, kill=0.7, attractors=1500,
        trunk_top=1.6, tropism=0.4, wander=0.2, card=1.45, card_radius=0.035, card_chance=1.0, density=2.0,
    ),
    "spruce": dict(
        leaf="needle", twig="spruce", leaves=44, leaf_len=0.07, dark=(0.03, 0.075, 0.04), light=(0.07, 0.14, 0.06),
        stem=(0.2, 0.13, 0.08), bark="BarkPine", trunk_base=0.3, tip_radius=0.008, segment=0.35, card=1.45,
        card_chance=1.0, density=1.0, conifer=True, height=19.0, crown_base=1.6, reach=3.6, whorl=0.42, droop=0.32,
        flat_cards=True,
    ),
    "pine": dict(
        leaf="needle", twig="pine", leaves=46, leaf_len=0.23, dark=(0.06, 0.12, 0.05), light=(0.13, 0.2, 0.08),
        stem=(0.3, 0.18, 0.1), bark="BarkPine", trunk_base=0.3, tip_radius=0.008, segment=0.4, card=1.6,
        card_chance=1.0, density=2.4, conifer=True, height=18.0, crown_base=10.5, reach=3.4, whorl=0.75, droop=-0.18,
        flat_cards=False,
    ),
}


def shape(kind, variant):
    """How a variant's silhouette differs from its species' usual one: (width, height) scales and a sideways lean of
    the crown in metres. Variants 0 and 1 are the original trees; the later ones (a different seed too) are a
    little wider or slimmer, taller or shorter, and lopsided, so a wood of them doesn't repeat."""
    if variant < 2:
        return 1.0, 1.0, 0.0
    rng = random.Random(variant * 101 + len(kind))
    return rng.uniform(0.82, 1.12), rng.uniform(0.86, 1.1), rng.uniform(-0.9, 0.9)


def vary(q, kind, variant):
    """A crown point moved for the variant's shape (see shape)."""
    wide, tall, lean = shape(kind, variant)
    top = SPECIES[kind]["trunk_top"]
    up = max(0.0, q.z - top) * tall
    return Vector((q.x * wide + lean * up / 10.0, q.y * wide, top + up if q.z > top else q.z))


def crown_point(kind, rng, variant):
    """A random point inside the species' crown (metres, z up)."""
    while True:
        p = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
        if p.length > 1:
            continue
        if kind == "oak":
            # A broad dome, wider than it is tall, a little lopsided per variant.
            wide = 6.8 if variant == 0 else 5.6
            q = Vector((p.x * wide, p.y * wide * 0.9, p.z * 4.6 + 9.0))
            if q.z < 4.5:
                continue
            q.x += 0.8 if variant == 0 else -0.6
            return q
        if kind == "birch":
            # A narrow oval with a pointed top.
            q = Vector((p.x * 2.9, p.y * 2.9, p.z * 5.8 + 10.0))
            q.x *= 1.0 - max(0.0, (q.z - 11.5) / 6.0)
            q.y *= 1.0 - max(0.0, (q.z - 11.5) / 6.0)
            if q.z < 5.0:
                continue
            return q
        # Lombardy poplar: a tall narrow column tapering to a point.
        z = rng.uniform(2.2, 20.5)
        radius = 2.1 * min(1.0, (20.8 - z) / 7.0) * (0.75 + 0.25 * min(1.0, (z - 2.0) / 3.0))
        a = rng.uniform(0, math.tau)
        rr = radius * math.sqrt(rng.random())
        return Vector((math.cos(a) * rr, math.sin(a) * rr, z))


# ---------- leaves and the atlas ----------

def leaf_width(kind, t):
    if kind == "needle":
        return 0.09 * (1.0 - t) ** 0.35
    if kind == "oak":
        base = math.sin(math.pi * min(1.0, t * 1.04)) ** 0.7 * 0.36
        lobes = 0.5 + 0.5 * abs(math.sin(t * math.pi * 4.5)) ** 0.55
        return base * lobes * (0.5 if t < 0.06 else 1.0)
    if kind == "birch":
        base = (t ** 0.45) * ((1.0 - t) ** 0.95) * 0.9
        return base * (1.0 + 0.07 * math.sin(t * 70.0))
    base = t / 0.35 if t < 0.35 else (1.0 - t) / 0.65
    return 0.5 * max(base, 0.0) ** 0.85 * (1.0 + 0.04 * math.sin(t * 55.0))


def leaf_mesh(kind, length, name):
    """A leaf along +Y from its stalk at the origin: folded along the midrib and drooping to the tip."""
    steps = 14
    verts, faces, uvs = [], [], []
    for i in range(steps + 1):
        t = i / steps
        w = leaf_width(kind, t) * length
        y = t * length
        droop = -0.14 * length * t * t
        verts += [(-w, y, droop), (0.0, y, droop + w * 0.28), (w, y, droop)]
        uvs += [(-1.0, t), (0.0, t), (1.0, t)]
    for i in range(steps):
        a = i * 3
        faces += [(a, a + 3, a + 4, a + 1), (a + 1, a + 4, a + 5, a + 2)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    layer = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            layer.data[li].uv = uvs[mesh.loops[li].vertex_index]
    mesh.update()
    return mesh


def emission_material(name, build):
    """An emission-only material (renders its colour exactly, unlit); build(nodes, links) returns the colour output."""
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except AttributeError:
        pass
    nodes, links = m.node_tree.nodes, m.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    emit = nodes.new("ShaderNodeEmission")
    links.new(build(nodes, links), emit.inputs["Color"])
    links.new(emit.outputs["Emission"], out.inputs["Surface"])
    return m


def leaf_colour_material(spec):
    def build(nodes, links):
        info = nodes.new("ShaderNodeObjectInfo")
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (*spec["dark"], 1)
        ramp.color_ramp.elements[1].color = (*spec["light"], 1)
        links.new(info.outputs["Random"], ramp.inputs["Fac"])
        # Midrib and a little mottling, from the leaf's UVs (u across -1..1, v along 0..1).
        uv = nodes.new("ShaderNodeUVMap")
        sep = nodes.new("ShaderNodeSeparateXYZ")
        links.new(uv.outputs["UV"], sep.inputs["Vector"])
        absx = nodes.new("ShaderNodeMath")
        absx.operation = "ABSOLUTE"
        links.new(sep.outputs["X"], absx.inputs[0])
        rib = nodes.new("ShaderNodeMath")
        rib.operation = "LESS_THAN"
        rib.inputs[1].default_value = 0.07
        links.new(absx.outputs[0], rib.inputs[0])
        lighter = nodes.new("ShaderNodeMix")
        lighter.data_type = "RGBA"
        lighter.inputs["Factor"].default_value = 0.45
        links.new(rib.outputs[0], lighter.inputs["Factor"])
        links.new(ramp.outputs["Color"], lighter.inputs["A"])
        lighter.inputs["B"].default_value = (0.55, 0.6, 0.3, 1)
        mottle = nodes.new("ShaderNodeTexNoise")
        mottle.inputs["Scale"].default_value = 9.0
        links.new(uv.outputs["UV"], mottle.inputs["Vector"])
        shade = nodes.new("ShaderNodeMapRange")
        shade.inputs["To Min"].default_value = 0.82
        shade.inputs["To Max"].default_value = 1.12
        links.new(mottle.outputs["Fac"], shade.inputs["Value"])
        mul = nodes.new("ShaderNodeMix")
        mul.data_type = "RGBA"
        mul.blend_type = "MULTIPLY"
        mul.inputs["Factor"].default_value = 1.0
        links.new(lighter.outputs["Result"], mul.inputs["A"])
        gray = nodes.new("ShaderNodeCombineXYZ")
        for c in ("X", "Y", "Z"):
            links.new(shade.outputs["Result"], gray.inputs[c])
        links.new(gray.outputs["Vector"], mul.inputs["B"])
        return mul.outputs["Result"]
    return emission_material("LeafColour", build)


def normal_material():
    """Renders the surface normal as a colour: a tangent-space normal map when seen from straight above."""
    def build(nodes, links):
        geo = nodes.new("ShaderNodeNewGeometry")
        fma = nodes.new("ShaderNodeVectorMath")
        fma.operation = "MULTIPLY_ADD"
        fma.inputs[1].default_value = (0.5, 0.5, 0.5)
        fma.inputs[2].default_value = (0.5, 0.5, 0.5)
        links.new(geo.outputs["Normal"], fma.inputs[0])
        return fma.outputs["Vector"]
    return emission_material("NormalColour", build)


def flat_colour(name, colour):
    def build(nodes, links):
        rgb = nodes.new("ShaderNodeRGB")
        rgb.outputs[0].default_value = (*colour, 1)
        return rgb.outputs[0]
    return emission_material(name, build)


def ribbon(name, points, width, mat):
    """A flat strip along points (a twig's stem, seen from above)."""
    verts, faces = [], []
    for i, p in enumerate(points):
        d = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        side = Vector((-d.y, d.x, 0)) * width * (1.0 - 0.6 * i / (len(points) - 1))
        verts += [p - side, p + side]
    for i in range(len(points) - 1):
        a = i * 2
        faces.append((a, a + 2, a + 3, a + 1))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def twig(spec, cx, cy, rng, leaf_mat, stem_mat):
    """One spray filling the atlas cell around (cx, cy): a stem up the middle and side shoots fanning out from its
    lower half, each with alternate leaves and one at its tip."""
    if spec.get("twig") == "pine":
        pine_tuft(spec, cx, cy, rng, leaf_mat, stem_mat)
        return
    base = Vector((cx + rng.uniform(-0.03, 0.03), cy - 0.46, 0))
    tip = Vector((cx + rng.uniform(-0.08, 0.08), cy + 0.28, 0))
    shoot(spec, base, tip, 0.007, spec["leaves"], rng, leaf_mat, stem_mat)
    sides = [-1, 1, -1, 1] if spec.get("twig") == "spruce" else [-1, 1] if rng.random() < 0.5 else [-1, 1, rng.choice([-1, 1])]
    for k, side in enumerate(sides):
        t = rng.uniform(0.15, 0.45) + 0.12 * k
        start = base.lerp(tip, t)
        angle = math.atan2(tip.y - base.y, tip.x - base.x) + side * math.radians(rng.uniform(28, 48))
        reach = rng.uniform(0.28, 0.38)
        end = start + Vector((math.cos(angle), math.sin(angle), 0)) * reach
        end.x = max(cx - 0.38, min(cx + 0.38, end.x))
        end.y = min(cy + 0.3, end.y)
        shoot(spec, start, end, 0.005, max(3, int(spec["leaves"] * 0.55)), rng, leaf_mat, stem_mat, small=0.85)


def pine_tuft(spec, cx, cy, rng, leaf_mat, stem_mat):
    """Pine: a short woody shoot with long needles fanning out from its upper half, and a second, smaller tuft."""
    for k, (dx, scale) in enumerate(((0.0, 1.0), (-0.24, 0.75), (0.24, 0.75))):
        base = Vector((cx + dx * 0.5, cy - 0.45 + 0.12 * k, 0))
        tip = Vector((cx + dx, cy + 0.05 - 0.15 * k, 0))
        ribbon("Stem", [base.lerp(tip, i / 9) for i in range(10)], 0.012 * scale, stem_mat)
        for i in range(int(spec["leaves"] * scale)):
            t = rng.uniform(0.35, 1.0)
            p = base.lerp(tip, t)
            heading = math.pi / 2 + rng.uniform(-1.25, 1.25) * (0.4 + 0.6 * t)
            needle(spec, p, heading, spec["leaf_len"] * scale * rng.uniform(0.75, 1.1), rng, leaf_mat)


def needle(spec, p, heading, length, rng, leaf_mat):
    obj = bpy.data.objects.new("Leaf", leaf_mesh(spec["leaf"], length, "Leaf"))
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(leaf_mat)
    obj.location = p + Vector((0, 0, rng.uniform(0.0, 0.03)))
    obj.rotation_euler = (rng.uniform(-0.2, 0.2), 0, heading - math.pi / 2)


def shoot(spec, base, tip, width, n, rng, leaf_mat, stem_mat, small=1.0):
    """A stem from base to tip with n alternate leaves along it and one at its end."""
    bend = Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.02, 0.02), 0))
    stem = [base.lerp(tip, k / 9) + bend * math.sin(math.pi * k / 9) for k in range(10)]
    ribbon("Stem", stem, width, stem_mat)
    for i in range(n + 1):
        last = i == n
        t = 1.0 if last else 0.1 + 0.82 * i / (n - 1)
        k = min(int(t * 9), 8)
        p = stem[k].lerp(stem[k + 1], t * 9 - k)
        along = (stem[k + 1] - stem[k]).normalized()
        heading = math.atan2(along.y, along.x)
        if spec.get("twig") == "spruce" and not last:
            for side in (-1, 1):
                needle(spec, p, heading + side * math.radians(rng.uniform(45, 80)), spec["leaf_len"] * small * rng.uniform(0.8, 1.15), rng, leaf_mat)
            continue
        if not last:
            heading += (1 if i % 2 == 0 else -1) * math.radians(rng.uniform(40, 80))
        size = spec["leaf_len"] * small * rng.uniform(0.8, 1.15) * (0.7 + 0.3 * math.sin(math.pi * min(t, 0.95)))
        mesh = leaf_mesh(spec["leaf"], size, "Leaf")
        obj = bpy.data.objects.new("Leaf", mesh)
        bpy.context.collection.objects.link(obj)
        obj.data.materials.append(leaf_mat)
        direction = Vector((math.cos(heading), math.sin(heading), 0))
        obj.location = p + direction * 0.012 + Vector((0, 0, rng.uniform(0.0, 0.03)))
        obj.rotation_euler = (rng.uniform(-0.25, 0.25), 0, heading - math.pi / 2)


def setup_ortho_render(width, height, scale, location, rotation, transparent=True):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = transparent
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = scale
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = location
    cam.rotation_euler = rotation
    scene.camera = cam
    return scene


def render_to(scene, path, view_transform):
    scene.view_settings.view_transform = view_transform
    scene.view_settings.look = "None"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def make_atlas(kind):
    """Renders the species' 2x2 twig atlas (colour with alpha, then normals) into TEX_DIR."""
    spec = SPECIES[kind]
    reset_scene()
    rng = random.Random({"oak": 1, "birch": 2, "poplar": 3, "spruce": 4, "pine": 5}[kind])
    leaf_mat = leaf_colour_material(spec)
    stem_mat = flat_colour("Stem", spec["stem"])
    for cx, cy in ((-0.5, -0.5), (0.5, -0.5), (-0.5, 0.5), (0.5, 0.5)):
        twig(spec, cx, cy, rng, leaf_mat, stem_mat)
    scene = setup_ortho_render(2048, 2048, 2.0, (0, 0, 5), (0, 0, 0))
    render_to(scene, os.path.join(TEX_DIR, f"leaves_{kind}.png"), "Standard")
    normal = normal_material()
    for obj in scene.objects:
        if obj.type == "MESH":
            obj.data.materials.clear()
            obj.data.materials.append(normal)
    render_to(scene, os.path.join(TEX_DIR, f"leaves_{kind}_nor.png"), "Raw")
    print(f"ATLAS {kind}")


def make_birch_bark():
    """White birch bark with dark horizontal lenticels and blotches, tileable both ways (4D noise on a torus)."""
    reset_scene()
    def build(nodes, links):
        tex = nodes.new("ShaderNodeTexCoord")
        sep = nodes.new("ShaderNodeSeparateXYZ")
        links.new(tex.outputs["Generated"], sep.inputs["Vector"])
        def angle(axis):
            m = nodes.new("ShaderNodeMath")
            m.operation = "MULTIPLY"
            m.inputs[1].default_value = math.tau
            links.new(sep.outputs[axis], m.inputs[0])
            return m.outputs[0]
        def trig(value, op):
            m = nodes.new("ShaderNodeMath")
            m.operation = op
            links.new(value, m.inputs[0])
            return m.outputs[0]
        au, av = angle("X"), angle("Y")
        torus = nodes.new("ShaderNodeCombineXYZ")
        links.new(trig(au, "COSINE"), torus.inputs["X"])
        links.new(trig(au, "SINE"), torus.inputs["Y"])
        links.new(trig(av, "COSINE"), torus.inputs["Z"])
        w = trig(av, "SINE")
        def noise4(scale, detail, stretch):
            mapping = nodes.new("ShaderNodeVectorMath")
            mapping.operation = "MULTIPLY"
            mapping.inputs[1].default_value = stretch
            links.new(torus.outputs["Vector"], mapping.inputs[0])
            n = nodes.new("ShaderNodeTexNoise")
            n.noise_dimensions = "4D"
            n.inputs["Scale"].default_value = scale
            n.inputs["Detail"].default_value = detail
            links.new(mapping.outputs["Vector"], n.inputs["Vector"])
            ww = nodes.new("ShaderNodeMath")
            ww.operation = "MULTIPLY"
            ww.inputs[1].default_value = stretch[2]
            links.new(w, ww.inputs[0])
            links.new(ww.outputs[0], n.inputs["W"])
            return n.outputs["Fac"]
        # Lenticels: thin dark dashes running around the trunk (stretched across u).
        dash = noise4(2.0, 3.0, (0.6, 0.6, 7.0))
        dashes = nodes.new("ShaderNodeMapRange")
        dashes.inputs["From Min"].default_value = 0.6
        dashes.inputs["From Max"].default_value = 0.66
        links.new(dash, dashes.inputs["Value"])
        blotch = noise4(1.2, 4.0, (1.0, 1.0, 1.4))
        blotches = nodes.new("ShaderNodeMapRange")
        blotches.inputs["From Min"].default_value = 0.6
        blotches.inputs["From Max"].default_value = 0.7
        links.new(blotch, blotches.inputs["Value"])
        dark = nodes.new("ShaderNodeMath")
        dark.operation = "MAXIMUM"
        links.new(dashes.outputs["Result"], dark.inputs[0])
        links.new(blotches.outputs["Result"], dark.inputs[1])
        grain = noise4(6.0, 6.0, (1.0, 1.0, 3.0))
        base = nodes.new("ShaderNodeMix")
        base.data_type = "RGBA"
        base.inputs["A"].default_value = (0.62, 0.6, 0.55, 1)
        base.inputs["B"].default_value = (0.86, 0.84, 0.8, 1)
        links.new(grain, base.inputs["Factor"])
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        links.new(dark.outputs[0], mix.inputs["Factor"])
        links.new(base.outputs["Result"], mix.inputs["A"])
        mix.inputs["B"].default_value = (0.06, 0.055, 0.05, 1)
        return mix.outputs["Result"]
    mat = emission_material("BirchBark", build)
    bpy.ops.mesh.primitive_plane_add(size=2, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(mat)
    scene = setup_ortho_render(1024, 1024, 2.0, (0, 0, 5), (0, 0, 0), transparent=False)
    render_to(scene, os.path.join(TEX_DIR, "birch_bark_diff.png"), "Standard")
    print("BARK birch")


# ---------- the tree ----------

class Node:
    __slots__ = ("pos", "parent", "children", "radius", "depth")

    def __init__(self, pos, parent):
        self.pos = pos
        self.parent = parent
        self.children = []
        self.radius = 0.0
        self.depth = 0


def grow(kind, rng, variant):
    """Space colonisation: returns the list of nodes, the root first."""
    spec = SPECIES[kind]
    seg, influence, kill = spec["segment"], spec["influence"], spec["kill"]
    nodes = [Node(Vector((0, 0, 0)), None)]
    # The bare trunk up to where the crown starts, leaning and wandering a little.
    lean = Vector((rng.uniform(-0.06, 0.06), rng.uniform(-0.06, 0.06), 1)).normalized()
    while nodes[-1].pos.z < spec["trunk_top"]:
        d = (lean + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * 0.05).normalized()
        n = Node(nodes[-1].pos + d * seg, len(nodes) - 1)
        nodes[n.parent].children.append(len(nodes))
        nodes.append(n)
    attractors = [vary(crown_point(kind, rng, variant), kind, variant) for _ in range(spec["attractors"])]
    tropism = Vector((0, 0, spec["tropism"]))
    for _ in range(140):
        if not attractors:
            break
        tree = kdtree.KDTree(len(nodes))
        for i, n in enumerate(nodes):
            tree.insert(n.pos, i)
        tree.balance()
        pulls = {}
        keep = []
        for a in attractors:
            co, index, dist = tree.find(a)
            if dist < kill:
                continue  # reached: this space is filled
            keep.append(a)
            if dist < influence:
                pulls[index] = pulls.get(index, Vector((0.0, 0.0, 0.0))) + (a - co).normalized()
        attractors = keep
        if not pulls:
            break
        for index, pull in pulls.items():
            jitter = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * spec["wander"]
            d = (pull.normalized() + tropism + jitter).normalized()
            parent = nodes[index]
            new_pos = parent.pos + d * seg
            if any((nodes[c].pos - new_pos).length < seg * 0.3 for c in parent.children):
                continue
            nodes[index].children.append(len(nodes))
            nodes.append(Node(new_pos, index))
    # Pipe model thicknesses, from the tips inwards, then scaled to the trunk's size.
    for i in range(len(nodes) - 1, -1, -1):
        n = nodes[i]
        if not n.children:
            n.radius = spec["tip_radius"]
        else:
            n.radius = sum(nodes[c].radius ** 2.4 for c in n.children) ** (1 / 2.4)
    raw_root, raw_tip = nodes[0].radius, spec["tip_radius"]
    k = math.log(spec["tip_radius"] / spec["trunk_base"]) / math.log(raw_tip / raw_root)
    for n in nodes:
        n.radius = spec["trunk_base"] * (n.radius / raw_root) ** k
    # How many steps each node is from the furthest tip beyond it (tips are 0): leaf cards go on the outer twigs.
    for n in nodes:
        n.depth = 0
    for i in range(len(nodes) - 1, -1, -1):
        n = nodes[i]
        n.depth = 0 if not n.children else 1 + max(nodes[c].depth for c in n.children)
    return nodes


def grow_conifer(kind, rng, variant):
    """A straight leader with whorls of branches. Spruce: from low down, long and drooping at the bottom, short at
    the top (a cone). Pine: only up top, in a rounded, irregular crown, the branches reaching up and out."""
    spec = SPECIES[kind]
    wide, tall, _ = shape(kind, variant)
    seg = spec["segment"]
    height = spec["height"] * (1.0 + 0.08 * (variant - 0.5) if variant < 2 else tall)
    crown_base = spec["crown_base"] * tall
    nodes = [Node(Vector((0, 0, 0)), None)]

    def add(pos, parent):
        nodes[parent].children.append(len(nodes))
        nodes.append(Node(pos, parent))
        return len(nodes) - 1

    lean = Vector((rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03), 1)).normalized()
    trunk = [0]
    while nodes[trunk[-1]].pos.z < height:
        wobble = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * (0.04 if kind == "spruce" else 0.12)
        trunk.append(add(nodes[trunk[-1]].pos + (lean + wobble).normalized() * seg, trunk[-1]))
    next_whorl = crown_base
    for t in trunk:
        z = nodes[t].pos.z
        if z < next_whorl or z > height - 0.4:
            continue
        next_whorl = z + spec["whorl"] * rng.uniform(0.85, 1.15)
        up = (z - crown_base) / (height - crown_base)
        if kind == "spruce":
            reach = wide * spec["reach"] * (1.0 - up) ** 0.95 + 0.35
            count = rng.randint(4, 6)
        else:
            reach = wide * spec["reach"] * math.sin(math.pi * min(1.0, 0.15 + up * 0.85)) ** 0.7 + 0.4
            count = rng.randint(2, 4)
        start = rng.uniform(0, math.tau)
        for b in range(count):
            a = start + b * math.tau / count + rng.uniform(-0.3, 0.3)
            branch(nodes, add, t, a, reach * rng.uniform(0.8, 1.1), spec, rng, depth=0)
    # Thickness: pipe model, then a taper from the trunk's size to the tips (as for the broadleaf trees).
    for i in range(len(nodes) - 1, -1, -1):
        n = nodes[i]
        n.radius = spec["tip_radius"] if not n.children else sum(nodes[c].radius ** 2.4 for c in n.children) ** (1 / 2.4)
    raw_root, raw_tip = nodes[0].radius, spec["tip_radius"]
    k = math.log(spec["tip_radius"] / spec["trunk_base"]) / math.log(raw_tip / raw_root)
    for n in nodes:
        n.radius = spec["trunk_base"] * (n.radius / raw_root) ** k
    for i in range(len(nodes) - 1, -1, -1):
        n = nodes[i]
        n.depth = 0 if not n.children else 1 + max(nodes[c].depth for c in n.children)
    # The leader stays thick all the way up: a conifer's trunk tapers evenly, not by the pipe model's branching.
    for t in trunk:
        z = nodes[t].pos.z
        nodes[t].radius = max(nodes[t].radius, spec["trunk_base"] * (1.0 - z / (height * 1.05)) + 0.01)
        nodes[t].depth = max(nodes[t].depth, 8)
    return nodes


def branch(nodes, add, parent, angle, reach, spec, rng, depth):
    """A branch out from parent at angle around the trunk: sagging (spruce) or rising (pine) along its length, its
    tip turning up, with side shoots on its outer two thirds."""
    seg = spec["segment"] * (0.8 if depth else 1.0)
    out = Vector((math.cos(angle), math.sin(angle), 0))
    steps = max(2, int(reach / seg))
    current = parent
    for i in range(steps):
        f = (i + 1) / steps
        droop = spec["droop"] * (1.0 - 1.6 * f * f) if spec["droop"] > 0 else spec["droop"] * (1.0 - f)
        d = (out + Vector((0, 0, -droop)) + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.12).normalized()
        current = add(nodes[current].pos + d * seg, current)
        if depth == 0 and f > 0.3 and i % 2 == 1:
            remaining = reach * (1.0 - f)
            for side in (-1, 1):
                a = angle + side * math.radians(rng.uniform(40, 65))
                branch(nodes, add, current, a, max(0.35, remaining * 0.55), spec, rng, depth=1)


def chains(nodes):
    """Splits the tree into branches: each follows its thickest child; the others start branches of their own,
    beginning at the fork so the tubes overlap there."""
    result = []
    stack = [(0, None)]
    while stack:
        start, fork = stack.pop()
        chain = ([fork] if fork is not None else []) + [start]
        i = start
        while nodes[i].children:
            kids = sorted(nodes[i].children, key=lambda c: -nodes[c].radius)
            for k in kids[1:]:
                stack.append((k, i))
            i = kids[0]
            chain.append(i)
        result.append(chain)
    return result


def tube(nodes, chain, sides, flare, rng, verts, faces, uvs, mat_index, mats):
    """A tube along a chain of nodes, with parallel-transport frames (no twisting) and bark UVs in metres."""
    pts = [nodes[i].pos for i in chain]
    radii = [nodes[i].radius for i in chain]
    tangents = []
    for k in range(len(pts)):
        a = pts[max(k - 1, 0)]
        b = pts[min(k + 1, len(pts) - 1)]
        tangents.append((b - a).normalized())
    normal = tangents[0].orthogonal().normalized()
    avg = sum(radii) / len(radii)
    circumference = math.tau * max(avg, 0.02)
    length = 0.0
    base = len(verts)
    for k, p in enumerate(pts):
        if k > 0:
            length += (p - pts[k - 1]).length
            axis = tangents[k - 1].cross(tangents[k])
            if axis.length > 1e-6:
                angle = tangents[k - 1].angle(tangents[k])
                normal = Matrix.Rotation(angle, 3, axis.normalized()) @ normal
        binormal = tangents[k].cross(normal).normalized()
        r = radii[k]
        if flare and p.z < 1.2:
            r *= 1.0 + 0.55 * math.exp(-p.z / 0.35)
        for s in range(sides + 1):
            a = s / sides * math.tau
            bump = 1.0
            if flare:
                bump += 0.12 * math.sin(a * 5 + p.z * 0.7) * math.exp(-p.z / 1.5) + 0.04 * noise.noise(p * 2.0 + Vector((a, 0, 0)))
            offset = (normal * math.cos(a) + binormal * math.sin(a)) * r * bump
            verts.append(tuple(p + offset))
            uvs.append((s / sides * circumference, length))
    for k in range(len(pts) - 1):
        for s in range(sides):
            a = base + k * (sides + 1) + s
            b = a + sides + 1
            faces.append((a, a + 1, b + 1, b))
            mats.append(mat_index)


def leaf_cards(nodes, kind, rng, crown_centre, keep, enlarge, verts, faces, uvs, normals, mat_index, mats):
    """Twig cards on the fine branch tips. Each card is a quad from the twig's base outwards, showing one of the four
    atlas twigs, with its normals pointing out from the crown's middle (soft, rounded crown shading)."""
    spec = SPECIES[kind]
    size = spec["card"] * enlarge
    for i, n in enumerate(nodes):
        if i == 0 or n.depth > 6:
            continue
        # Outer twigs carry two sprays each; further in, the crown is filled more thinly.
        count = (3 if n.depth <= 1 else 2 if n.depth <= 3 else 0.5) * spec["density"]
        count = int(count) + (1 if rng.random() < count - int(count) else 0)
        for _ in range(count):
            if rng.random() > spec["card_chance"] * keep:
                continue
            card(nodes, n, size, rng, crown_centre, verts, faces, uvs, normals, mat_index, mats, spec.get("flat_cards", False))


def card(nodes, n, size, rng, crown_centre, verts, faces, uvs, normals, mat_index, mats, flat=False):
    """One twig card at node n, from the twig's base outwards."""
    parent = nodes[n.parent].pos
    along = (n.pos - parent).normalized()
    outward = (n.pos - crown_centre)
    outward = outward.normalized() if outward.length > 1e-6 else Vector((0, 0, 1))
    if flat:
        # Conifer sprays: along the branch, lying nearly flat, tilted a little at random; smaller near the top.
        size *= 1.0 - 0.55 * min(1.0, max(0.0, n.pos.z / max(crown_centre.z * 2.0, 1.0)))
        up = (along + Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 0.05))).normalized()
        side = up.cross(Vector((0, 0, 1)))
        side = side.normalized() if side.length > 1e-6 else up.orthogonal().normalized()
        side = Matrix.Rotation(rng.uniform(-0.45, 0.45), 3, up) @ side
    else:
        up = (along * 0.5 + outward * 0.5 + Vector((0, 0, 0.25))).normalized()
        side = up.orthogonal().normalized()
        side = Matrix.Rotation(rng.uniform(0, math.tau), 3, up) @ side
    s = size * rng.uniform(0.8, 1.2)
    base = n.pos - up * s * 0.08
    corners = [base - side * s / 2, base + side * s / 2, base + side * s / 2 + up * s, base - side * s / 2 + up * s]
    q = rng.randrange(4)
    u0, v0 = (q % 2) * 0.5, (q // 2) * 0.5
    quad_uv = [(u0, v0), (u0 + 0.5, v0), (u0 + 0.5, v0 + 0.5), (u0, v0 + 0.5)]
    start = len(verts)
    for c, uv in zip(corners, quad_uv):
        verts.append(tuple(c))
        uvs.append(uv)
        nrm = (c - crown_centre)
        nrm = (nrm.normalized() if nrm.length > 1e-6 else Vector((0, 0, 1))) + Vector((0, 0, 0.35))
        normals.append(nrm.normalized())
    faces.append((start, start + 1, start + 2, start + 3))
    mats.append(mat_index)


def build_mesh(name, verts, faces, uvs, normals, mats, materials, parent):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    layer = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        poly.material_index = mats[poly.index]
        poly.use_smooth = True
        for li in poly.loop_indices:
            layer.data[li].uv = uvs[mesh.loops[li].vertex_index]
    for m in materials:
        mesh.materials.append(m)
    mesh.update()
    if normals is not None:
        mesh.normals_split_custom_set_from_vertices(normals)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    return obj


def tree_materials(kind):
    """Preview materials with the real textures (Unity swaps in its own by these names)."""
    spec = SPECIES[kind]
    cap = kind.capitalize()
    bark_file = {"oak": "oak_bark_diff.jpg", "birch": "birch_bark_diff.png", "poplar": "willow_bark_diff.jpg",
                 "spruce": "pine_bark_diff.jpg", "pine": "pine_bark_diff.jpg"}[kind]

    def textured(name, file, alpha=False, scale=None):
        m = bpy.data.materials.new(name)
        try:
            m.use_nodes = True
        except AttributeError:
            pass
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Roughness"].default_value = 0.8
        path = os.path.join(TEX_DIR, file)
        if os.path.exists(path):
            img = m.node_tree.nodes.new("ShaderNodeTexImage")
            img.image = bpy.data.images.load(path, check_existing=True)
            if scale:
                tc = m.node_tree.nodes.new("ShaderNodeTexCoord")
                mp = m.node_tree.nodes.new("ShaderNodeMapping")
                mp.inputs["Scale"].default_value = (1 / scale, 1 / scale, 1)
                m.node_tree.links.new(tc.outputs["UV"], mp.inputs["Vector"])
                m.node_tree.links.new(mp.outputs["Vector"], img.inputs["Vector"])
            m.node_tree.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
            if alpha:
                m.node_tree.links.new(img.outputs["Alpha"], bsdf.inputs["Alpha"])
                try:
                    m.surface_render_method = "DITHERED"
                except AttributeError:
                    m.blend_method = "CLIP"
        return m

    return (textured(spec["bark"], bark_file, scale=1.6),
            textured("Leaves" + cap, f"leaves_{kind}.png", alpha=True),
            textured("Impostor" + cap, f"impostor_{kind}.png", alpha=True))


def build_tree(name, kind, seed, variant):
    """Grows the unchanged near meshes, then adds an eight-triangle RTS impostor for distant crowns."""
    rng = random.Random(seed)
    spec = SPECIES[kind]
    nodes = grow_conifer(kind, rng, variant) if spec.get("conifer") else grow(kind, rng, variant)
    bark, leaves, impostor = tree_materials(kind)
    r = root(name)
    ys = [n.pos.z for n in nodes]
    crown_bottom = spec["crown_base"] if spec.get("conifer") else min(z for z in ys if z > spec["trunk_top"])
    crown_centre = Vector((0, 0, (crown_bottom + max(ys)) / 2))
    height = max(ys) + SPECIES[kind]["card"] * 0.6
    width = max(max(abs(n.pos.x), abs(n.pos.y)) for n in nodes) + SPECIES[kind]["card"] * 0.6

    for lod, (min_radius, keep, enlarge) in enumerate([(0.0, 1.0, 1.0), (0.06, 0.45, 1.45)]):
        verts, faces, uvs, mats = [], [], [], []
        for chain in chains(nodes):
            top = max(nodes[i].radius for i in chain)
            if top < max(min_radius, 0.03) or len(chain) < 2:  # the finest twigs are in the leaf cards
                continue
            sides = 10 if top > 0.25 else 7 if top > 0.08 else 5 if top > 0.03 else 4
            if lod == 1:
                sides = max(4, sides // 2 + 1)
            tube(nodes, chain, sides, chain[0] == 0, rng, verts, faces, uvs, 0, mats)
        normals = [None] * len(verts)
        bark_count = len(verts)
        card_rng = random.Random(seed + 7 + lod)
        leaf_cards(nodes, kind, card_rng, crown_centre, keep, enlarge, verts, faces, uvs, normals, 1, mats)
        # Bark keeps its own smooth normals; only the cards get the crown normals.
        obj = build_mesh(f"{name}_LOD{lod}", verts, faces, uvs, None, mats, [bark, leaves], r)
        obj.data.update()
        computed = [Vector(v.normal) for v in obj.data.vertices]
        custom = [computed[i] if i < bark_count else normals[i] for i in range(len(verts))]
        obj.data.normals_split_custom_set_from_vertices(custom)
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"  {name}_LOD{lod}: {tris} triangles")

    # Bake once per species, even for a filtered export. The top and side always come from the same tree.
    if kind not in BAKED:
        render_impostor(kind, r, height, width)
        BAKED.add(kind)
    verts, faces, uvs, mats = [], [], [], []
    for k in range(3):
        a = k * math.pi / 3
        d = Vector((math.cos(a), math.sin(a), 0)) * width
        start = len(verts)
        for corner, uv in ((-d, (0, 0)), (d, (1, 0)), (d + Vector((0, 0, height)), (1, 1)), (-d + Vector((0, 0, height)), (0, 1))):
            verts.append(tuple(corner))
            uvs.append(impostor_uv(uv, False))
        faces.append((start, start + 1, start + 2, start + 3))
        mats.append(0)
    # A crown seen from above fills the gaps between vertical cards without adding another renderer/material.
    # Conifers keep their leader above the cap; broad crowns put it near their upper shoulder.
    cap = height * {"oak": 0.72, "birch": 0.72, "poplar": 0.65, "spruce": 0.3, "pine": 0.8}[kind]
    start = len(verts)
    for x, y, uv in ((-1, -1, (0, 0)), (1, -1, (1, 0)), (1, 1, (1, 1)), (-1, 1, (0, 1))):
        verts.append((x * width, y * width, cap))
        uvs.append(impostor_uv(uv, True))
    faces.append((start, start + 1, start + 2, start + 3))
    mats.append(0)
    lod2 = build_mesh(f"{name}_LOD2", verts, faces, uvs, None, mats, [impostor], r)
    lod2.data.normals_split_custom_set_from_vertices([Vector((0, 0, 1))] * len(verts))
    return r, height


def impostor_uv(uv, top):
    """Maps a full picture into one padded atlas tile, keeping the other view out of its mip edges."""
    return ((512 * int(top) + 8 + uv[0] * 496) / 1024, (8 + uv[1] * 496) / 512)


def render_impostor(kind, tree_root, height, width):
    """Bakes side and overhead views into one transparent atlas for the existing alpha-clipped material."""
    path = os.path.join(TEX_DIR, f"impostor_{kind}.png")
    scene = bpy.context.scene
    lod0 = next(o for o in tree_root.children if o.name.endswith("_LOD0"))
    hidden = [o for o in tree_root.children if o is not lod0]
    for o in hidden:
        o.hide_render = True
    world = bpy.data.worlds.new("ImpostorWorld")
    scene.world = world
    world.color = (0.8, 0.82, 0.85)
    try:
        world.use_nodes = True
        bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
        bg.inputs["Color"].default_value = (0.75, 0.8, 0.88, 1)
        bg.inputs["Strength"].default_value = 0.9
    except (AttributeError, StopIteration):
        pass
    sun = bpy.data.lights.new("ImpostorSun", "SUN")
    sun.energy = 3.0
    sun_obj = bpy.data.objects.new("ImpostorSun", sun)
    bpy.context.collection.objects.link(sun_obj)
    sun_obj.rotation_euler = (math.radians(50), 0, math.radians(30))
    size = max(height, width * 2) * 1.02
    setup_ortho_render(512, 512, size, (0, -60, height / 2), (math.radians(90), 0, 0))
    scene.camera.data.clip_end = 200
    # The card spans the tree's width either side of the trunk and its full height; centre the shot on that.
    scene.camera.data.shift_y = 0.0
    scene.camera.data.ortho_scale = size
    scene.render.resolution_x = int(512 * (width * 2) / size + 0.5)
    scene.render.resolution_y = int(512 * height / size + 0.5)
    scene.camera.data.ortho_scale = max(width * 2, height)
    # Stretch the side picture into its square tile; the side card's aspect restores the original proportions.
    # Temporary render files stay outside Assets, so Unity sees only the finished shared atlas.
    pixels = np.zeros((512, 1024, 4), dtype=np.float32)
    with tempfile.TemporaryDirectory(prefix="dw-impostor-") as temp:
        for top in (False, True):
            if top:
                scene.camera.location = (0, 0, height + 60)
                scene.camera.rotation_euler = (0, 0, 0)
                scene.camera.data.ortho_scale = width * 2
                scene.render.resolution_x = scene.render.resolution_y = 496
            shot = os.path.join(temp, "top.png" if top else "side.png")
            render_to(scene, shot, "Standard")
            img = bpy.data.images.load(shot, check_existing=False)
            img.scale(496, 496)
            tile = np.empty(496 * 496 * 4, dtype=np.float32)
            img.pixels.foreach_get(tile)
            x = 512 * int(top) + 8
            pixels[8:504, x:x + 496] = tile.reshape(496, 496, 4)
            bpy.data.images.remove(img)
    atlas = bpy.data.images.new("ImpostorAtlas", width=1024, height=512, alpha=True)
    atlas.pixels.foreach_set(pixels.ravel())
    atlas.filepath_raw = path
    atlas.file_format = "PNG"
    atlas.save()
    bpy.data.images.remove(atlas)
    bpy.data.objects.remove(sun_obj, do_unlink=True)
    for o in hidden:
        o.hide_render = False
    # Reload so the impostor material shows the new picture.
    for img in bpy.data.images:
        if img.filepath and os.path.basename(img.filepath) == os.path.basename(path):
            img.reload()


def preview(path, height):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.film_transparent = False
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    world = bpy.data.worlds.new("PreviewWorld")
    scene.world = world
    try:
        world.use_nodes = True
        bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
        bg.inputs["Color"].default_value = (0.55, 0.65, 0.8, 1)
        bg.inputs["Strength"].default_value = 0.8
    except (AttributeError, StopIteration):
        pass
    for o in bpy.context.scene.objects:
        if o.name.endswith("_LOD1") or o.name.endswith("_LOD2"):
            o.hide_render = True
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("PreviewGround")
    gm.diffuse_color = (0.2, 0.25, 0.12, 1)
    try:
        gm.use_nodes = True
        b = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        b.inputs["Base Color"].default_value = (0.16, 0.22, 0.09, 1)
    except StopIteration:
        pass
    ground.data.materials.append(gm)
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 4.0
    sun_obj = bpy.data.objects.new("Sun", sun)
    bpy.context.collection.objects.link(sun_obj)
    sun_obj.rotation_euler = (math.radians(45), 0, math.radians(40))
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 40
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam)
    d = height * 1.45
    cam.location = (d * 0.8, -d, height * 0.45)
    direction = Vector((0, 0, height * 0.5)) - Vector(cam.location)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.view_settings.view_transform = "AgX" if "AgX" in [t.identifier for t in scene.view_settings.bl_rna.properties["view_transform"].enum_items] else "Standard"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


TREES = [
    ("Scenery_Tree", "oak", 4, 0),
    ("Scenery_Tree_B", "oak", 9, 1),
    ("Scenery_Birch", "birch", 5, 0),
    ("Scenery_Poplar", "poplar", 6, 0),
    ("Scenery_Spruce", "spruce", 3, 0),
    ("Scenery_Pine", "pine", 1, 0),
    ("Scenery_Pine_B", "pine", 7, 1),
    # More of each, so woods don't repeat: Art.TryModel picks one of "<name>" and "<name>_V2".. by position.
    ("Scenery_Tree_V2", "oak", 21, 2),
    ("Scenery_Tree_V3", "oak", 33, 3),
    ("Scenery_Birch_V2", "birch", 14, 2),
    ("Scenery_Birch_V3", "birch", 27, 3),
    ("Scenery_Poplar_V2", "poplar", 18, 2),
    ("Scenery_Spruce_V2", "spruce", 12, 2),
    ("Scenery_Spruce_V3", "spruce", 25, 3),
    ("Scenery_Spruce_V4", "spruce", 38, 4),
    ("Scenery_Pine_V2", "pine", 16, 2),
    ("Scenery_Pine_V3", "pine", 29, 3),
]


def main():
    """Exports the tree variants; --lod2-only keeps the existing bark and leaf textures untouched."""
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(TEX_DIR, exist_ok=True)
    wanted = {kind for name, kind, _, _ in TREES if not ONLY or name in ONLY}
    if not LOD2_ONLY:
        for kind in sorted(wanted):
            make_atlas(kind)
        if "birch" in wanted:
            make_birch_bark()
    for name, kind, seed, variant in TREES:
        if ONLY and name not in ONLY:
            continue
        # Filtered exports still bake the same representative, not whichever variant happened to be selected.
        source = next(t for t in TREES if t[1] == kind)
        if kind not in BAKED and source[0] != name:
            reset_scene()
            build_tree(*source)
        reset_scene()
        r, height = build_tree(name, kind, seed, variant)
        export(r, os.path.join(OUT_DIR, name + ".fbx"))
        print(f"EXPORTED {name} ({height:.1f} m)")
        if PREVIEW_DIR:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            preview(os.path.join(PREVIEW_DIR, name + ".png"), height)


if __name__ == "__main__":
    main()
