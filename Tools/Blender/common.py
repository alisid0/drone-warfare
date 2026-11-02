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


def lumpy(name, radius, loc, mat, parent, scale=(1, 1, 1), strength=0.25, subdiv=2):
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
    return finish(obj, mat, parent, smooth=False)


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

