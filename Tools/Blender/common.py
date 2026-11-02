"""Shared helpers for the Blender model scripts: scene reset, materials, primitives, FBX export, preview renders.

Models face Blender's -Y, which becomes +Z (forward) in Unity after export.
"""
import math

import bpy
from mathutils import Vector


# ---------- scene helpers ----------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color, metallic=0.0, roughness=0.6, emission=None):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True  # always on (and deprecated) in Blender 5
    except AttributeError:
        pass
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = 4.0
    m.diffuse_color = (*color, 1.0)
    return m


def finish(obj, mat, parent, bevel=0.0, smooth=True):
    obj.data.materials.append(mat)
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
    if smooth:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
        except (AttributeError, RuntimeError):
            bpy.ops.object.shade_smooth()
    set_parent(obj, parent)
    return obj


def set_parent(obj, parent):
    """Parent without moving: the child keeps the world position it was created at."""
    obj.parent = parent
    if parent is not None:
        obj.matrix_parent_inverse = parent.matrix_world.inverted()


def box(name, size, loc, mat, parent, rot=(0, 0, 0), bevel=0.01):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, mat, parent, bevel)


def cylinder(name, radius, depth, loc, mat, parent, rot=(0, 0, 0), verts=24, bevel=0.004):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    return finish(obj, mat, parent, bevel)


def sphere(name, radius, loc, mat, parent, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=radius, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, mat, parent)


def root(name):
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = name
    return obj


# ---------- export and preview ----------

def export(root_obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    root_obj.select_set(True)
    for child in root_obj.children_recursive:
        child.select_set(True)
    bpy.context.view_layer.objects.active = root_obj
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        add_leaf_bones=False,
        path_mode="AUTO",
    )


def hexa(name, corners, mat, parent, bevel=0.02):
    """A six-sided block from 8 corners (bottom 4 then top 4, counter-clockwise), for sloped armour etc."""
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(c) for c in corners], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, mat, parent, bevel)


def gable_roof(name, w, d, z, rise, mat, parent, overhang=0.25, ridge_along_x=False):
    """A pitched roof over a w (x) by d (y) building. The ridge runs along Y, or along X if asked."""
    if ridge_along_x:
        w, d = d, w
    w2, d2 = w / 2 + overhang, d / 2 + overhang
    corners = [(-w2, -d2, z), (w2, -d2, z), (w2, d2, z), (-w2, d2, z),
               (-0.01, -d2, z + rise), (0.01, -d2, z + rise), (0.01, d2, z + rise), (-0.01, d2, z + rise)]
    obj = hexa(name, corners, mat, parent, bevel=0)
    if ridge_along_x:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        obj.rotation_euler = (0, 0, math.radians(90))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return obj


def block(cx, cy, z0, z1, front_w, front_l, back_w, back_l, top_inset_front=0.0, top_inset_back=0.0, top_narrow=0.0):
    """Corners for hexa(): a box from z0 to z1 whose top can be pulled in at the front/back/sides.
    Front is -Y. front_l / back_l are distances from (cx, cy) to the front / back faces."""
    yf, yb = cy - front_l, cy + back_l
    bottom = [(cx - front_w / 2, yf, z0), (cx + front_w / 2, yf, z0), (cx + back_w / 2, yb, z0), (cx - back_w / 2, yb, z0)]
    tf, tb = yf + top_inset_front, yb - top_inset_back
    top = [(cx - front_w / 2 + top_narrow, tf, z1), (cx + front_w / 2 - top_narrow, tf, z1),
           (cx + back_w / 2 - top_narrow, tb, z1), (cx - back_w / 2 + top_narrow, tb, z1)]
    return bottom + top


def pivot(name, loc, parent):
    """An empty used as a rotation pivot (turrets, radar heads)."""
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=loc)
    obj = bpy.context.active_object
    obj.name = name
    set_parent(obj, parent)
    return obj


def cone(name, r1, r2, depth, loc, mat, parent, verts=16):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    return finish(obj, mat, parent)


def lumpy(name, radius, loc, mat, parent, scale=(1, 1, 1), strength=0.25, subdiv=2, smooth=False):
    """An icosphere roughed up with a noise displacement: foliage clumps and rocks.
    The noise uses world coordinates, so clumps at different spots come out different."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=radius, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    tex = bpy.data.textures.new(f"{name}_noise", "CLOUDS")
    tex.noise_scale = radius * 0.6
    mod = obj.modifiers.new("Displace", "DISPLACE")
    mod.texture = tex
    mod.strength = strength
    mod.texture_coords = "GLOBAL"
    finish(obj, mat, parent, smooth=False)
    if smooth:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.shade_smooth()
    return obj


def box_uvs(obj):
    """Box-projected UVs in world metres (1 UV unit = 1 m), so Unity can lay tiling photo textures over the
    model at a real-world scale. Each face takes the two axes across its normal."""
    import bmesh
    single_uv_layer(obj)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv = bm.loops.layers.uv["UVMap"]
    world = obj.matrix_world
    turn = world.to_3x3()
    for face in bm.faces:
        n = turn @ face.normal
        axis = max(range(3), key=lambda i: abs(n[i]))
        for loop in face.loops:
            co = world @ loop.vert.co
            loop[uv].uv = (co.y, co.z) if axis == 0 else (co.x, co.z) if axis == 1 else (co.x, co.y)
    bm.to_mesh(obj.data)
    bm.free()


SECOND_UV = "UVFall"  # an optional second UV set (Unity's uv2), kept through joins; see make_dam.water_sheet


def single_uv_layer(obj, second=False):
    """Gives a mesh exactly one UV layer, named UVMap (plus UVFall, second, when asked). Meshes built from raw
    vertices start with none, and bmesh would name a new one Float2; when parts with different layer names are
    joined, the model ends up with two layers and Unity reads only the first, so half the parts got all-zero UVs
    (one flat colour)."""
    layers = obj.data.uv_layers
    if "UVMap" not in layers:
        if len(layers) > 0 and layers[0].name != SECOND_UV:
            layers[0].name = "UVMap"
        else:
            layers.new(name="UVMap")
    for layer in [l for l in layers if l.name != "UVMap" and not (second and l.name == SECOND_UV)]:
        layers.remove(layer)
    if second and SECOND_UV not in layers:
        layers.new(name=SECOND_UV)
    layers.active = layers["UVMap"]


def box_uv_all(root_obj):
    """box_uvs for every mesh under root_obj, with modifiers applied first (for models that keep their parts)."""
    meshes = [o for o in root_obj.children_recursive if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    if meshes:
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.convert(target="MESH")
    for o in meshes:
        box_uvs(o)


def join_meshes(root_obj):
    """Applies modifiers and merges every mesh under root_obj into one object, so Unity draws the model with
    one renderer (one sub-mesh per material) instead of dozens, with real-scale UVs.
    Only for models without moving parts."""
    meshes = [o for o in root_obj.children_recursive if o.type == "MESH"]
    if len(meshes) < 2:
        box_uv_all(root_obj)
        return
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.convert(target="MESH")
    # If any part has a second UV set, every part gets one (unused, a copy of its first), so they line up.
    second = any(SECOND_UV in o.data.uv_layers for o in meshes)
    for o in meshes:
        if not o.get("keep_uv"):  # leaf cards keep their own 0-1 UVs
            box_uvs(o)
        single_uv_layer(o, second)
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = root_obj.name + "_Mesh"
    world = joined.matrix_world.copy()
    joined.parent = root_obj
    joined.matrix_world = world


def leaf_cards(name, clumps, per_clump, size, mat, parent, rng, center, up=0.35):
    """Leaf cards for a tree crown: small square quads showing a cluster of leaves (cut out by alpha in Unity),
    scattered through each clump (x, y, z, radius) at random angles. Their normals all point out from the
    crown's centre (tipped up by `up`), so the crown lights like one soft, leafy volume."""
    c = Vector(center)
    verts, faces, uvs, normals = [], [], [], []
    for (px, py, pz, rad) in clumps:
        for _ in range(per_clump):
            while True:
                d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
                if d.length <= 1.0:
                    break
            p = Vector((px, py, pz)) + d * rad
            out = p - c
            out = out.normalized() if out.length > 1e-3 else Vector((0, 0, 1))
            n = (out + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.9).normalized()
            t = n.cross(Vector((0, 0, 1)))
            t = t.normalized() if t.length > 1e-3 else Vector((1, 0, 0))
            b = n.cross(t)
            a = rng.uniform(0, 2 * math.pi)
            t, b = t * math.cos(a) + b * math.sin(a), b * math.cos(a) - t * math.sin(a)
            half = size * rng.uniform(0.75, 1.25) / 2
            shade = (out + Vector((0, 0, up))).normalized()
            i0 = len(verts)
            for (u, v) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                verts.append(tuple(p + t * u * half + b * v * half))
                uvs.append(((u + 1) / 2, (v + 1) / 2))
                normals.append(tuple(shade))
            faces.append((i0, i0 + 1, i0 + 2, i0 + 3))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        layer.data[loop.index].uv = uvs[loop.vertex_index]
    mesh.normals_split_custom_set_from_vertices(normals)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    obj["keep_uv"] = True
    set_parent(obj, parent)
    return obj


def mesh_object(name, verts, faces, mat, parent, smooth=True):
    """A mesh made from raw vertices and faces."""
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, mat, parent, smooth=smooth)


def strut(name, p1, p2, radius, mat, parent, verts=6):
    """A thin cylinder from p1 to p2 (lattice towers, braces, rails)."""
    a, b = Vector(p1), Vector(p2)
    d = b - a
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_euler()
    return cylinder(name, radius, d.length, tuple((a + b) / 2), mat, parent, rot=tuple(rot), verts=verts, bevel=0)


def ring(name, r_in, r_out, z, mat, parent, segments=48):
    """A flat ring lying at height z (painted circles)."""
    verts, faces = [], []
    for k in range(segments):
        a = 2 * math.pi * k / segments
        verts.append((math.cos(a) * r_in, math.sin(a) * r_in, z))
        verts.append((math.cos(a) * r_out, math.sin(a) * r_out, z))
    for k in range(segments):
        i, j = 2 * k, 2 * ((k + 1) % segments)
        faces.append((i, i + 1, j + 1, j))  # counter-clockwise from above, so it faces up
    return mesh_object(name, verts, faces, mat, parent, smooth=False)


def sandbag(name, loc, yaw, mat, parent, size=(0.6, 0.32, 0.2)):
    """One plump sandbag."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=(0, 0, yaw))
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = min(size) * 0.45
    mod.segments = 2
    obj.data.materials.append(mat)
    set_parent(obj, parent)
    return obj


def sandbag_wall(prefix, start, end, layers, mat, parent, bag=0.6):
    """Staggered courses of sandbags from start to end (x, y), standing on z = 0."""
    a, b = Vector((*start, 0)), Vector((*end, 0))
    d = b - a
    yaw = math.atan2(d.y, d.x)
    count = max(1, round(d.length / bag))
    for layer in range(layers):
        shift = 0.5 if layer % 2 else 0.0
        for k in range(count):
            t = (k + 0.5 + shift) / count
            if t > 1.0:
                continue
            p = a + d * t
            sandbag(f"{prefix}_{layer}_{k}", (p.x, p.y, 0.1 + layer * 0.18), yaw, mat, parent)


def net(name, size, z, mats, parent, sag=0.35, seed=1):
    """A camouflage net: a lumpy sheet drooping at the edges, in patches of two colours."""
    import random
    rng = random.Random(seed)
    sx, sy = size
    n = 10
    for idx, mat in enumerate(mats):
        verts, faces = [], []
        for j in range(n + 1):
            for i in range(n + 1):
                u, v = i / n - 0.5, j / n - 0.5
                edge = max(abs(u), abs(v)) * 2
                h = z - sag * edge ** 3 + rng.uniform(-0.06, 0.06) + idx * 0.03
                verts.append((u * sx * (1 - idx * 0.12), v * sy * (1 - idx * 0.12), h))
        for j in range(n):
            for i in range(n):
                if idx and rng.random() < 0.45:
                    continue  # holes in the top layer show the colour underneath
                a = j * (n + 1) + i
                faces.append((a, a + 1, a + n + 2, a + n + 1))
        mesh_object(f"{name}_{idx}", verts, faces, mat, parent, smooth=False)


def render_preview(path, distance, target_z=0.0):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = 900
    scene.render.resolution_y = 600
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("World")
    scene.world = world
    world.color = (0.55, 0.6, 0.62)
    try:
        world.use_nodes = True
    except AttributeError:
        pass
    bg = next((n for n in world.node_tree.nodes if n.type == "BACKGROUND"), None) if world.node_tree else None
    if bg:
        bg.inputs["Color"].default_value = (0.55, 0.6, 0.62, 1)
        bg.inputs["Strength"].default_value = 0.8

    ground_z = -0.25 if target_z == 0.0 else 0.0
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, ground_z))
    ground = bpy.context.active_object
    ground.data.materials.append(material("PreviewGround", (0.25, 0.27, 0.2), roughness=0.9))

    bpy.ops.object.light_add(type="SUN", location=(2, -2, 4), rotation=(math.radians(50), 0, math.radians(30)))
    bpy.context.active_object.data.energy = 3.5

    cam_loc = (distance * 0.75, -distance, distance * 0.55 + target_z)
    bpy.ops.object.camera_add(location=cam_loc)
    cam = bpy.context.active_object
    direction = Vector((0, 0, target_z)) - Vector(cam_loc)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

