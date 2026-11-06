"""One low-memory Blender review of exported geometry/paint; this is not an HDRP validation render."""
from pathlib import Path
import math
import struct
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
args = sys.argv[sys.argv.index('--') + 1:]
phase, scene_name = args[:2]
source = ROOT / 'Temp/water/review' / phase / scene_name
output = ROOT / 'Tools/Tests/Water/Review'
output.mkdir(parents=True, exist_ok=True)
for default in list(bpy.data.objects):
    bpy.data.objects.remove(default, do_unlink=True)


def material(name, colour, roughness=1):
    """Make a simple review material, with no claim of reproducing the HDRP lighting pipeline."""
    result = bpy.data.materials.new(name)
    result.use_nodes = True
    bsdf = result.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*colour, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    return result


def mesh(name, vertices, faces, mat):
    """Release export lists immediately after Blender has copied a single mesh."""
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.materials.append(mat)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    return obj


with (source / 'terrain.bin').open('rb') as stream:
    n, size, cx, cz = struct.unpack('<ifff', stream.read(16))
    heights = struct.unpack('<' + 'f' * (n * n), stream.read(n * n * 4))
vertices = [(i / (n - 1) * size - size / 2, j / (n - 1) * size - size / 2, heights[j * n + i])
            for j in range(n) for i in range(n)]
faces = [(j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i)
         for j in range(n - 1) for i in range(n - 1)]
ground = material('Production terrain layer means', (.3, .3, .3))
obj = mesh('Production terrain', vertices, faces, ground)
del vertices, faces, heights
uv = obj.data.uv_layers.new(name='Ground metres')
for polygon in obj.data.polygons:
    for loop in polygon.loop_indices:
        v = obj.data.vertices[obj.data.loops[loop].vertex_index].co
        uv.data[loop].uv = (v.x / size + .5, v.y / size + .5)
texture = ground.node_tree.nodes.new('ShaderNodeTexImage')
texture.image = bpy.data.images.load(str(source / 'colour.ppm'))
texture.image.colorspace_settings.name = 'sRGB'
texture.interpolation = 'Linear'
ground.node_tree.links.new(texture.outputs['Color'], ground.node_tree.nodes['Principled BSDF'].inputs['Base Color'])

water_path = source / 'water.bin'
if water_path.exists():
    r, g, b, ambient, smooth = map(float, (source / 'optics.txt').read_text().split())
    river = material('HDRP scattering approximation ONLY', (r * ambient, g * ambient, b * ambient), 1 - smooth)
    river.node_tree.nodes['Principled BSDF'].inputs['IOR'].default_value = 1.333
    lake = material('Unchanged lake approximation ONLY', (.035, .095, .12), .04)
    lake.node_tree.nodes['Principled BSDF'].inputs['IOR'].default_value = 1.333
    with water_path.open('rb') as stream:
        parts = struct.unpack('<i', stream.read(4))[0]
        for part in range(parts):
            nv, nt = struct.unpack('<ii', stream.read(8))
            values = struct.unpack('<' + 'f' * (nv * 3), stream.read(nv * 12))
            vertices = [(values[k] - cx, values[k + 2] - cz, values[k + 1]) for k in range(0, len(values), 3)]
            indices = struct.unpack('<' + 'i' * nt, stream.read(nt * 4))
            faces = [(indices[k], indices[k + 2], indices[k + 1]) for k in range(0, nt, 3)]
            mesh('Water ' + str(part), vertices, faces, lake if part == 2 else river)
            del values, vertices, indices, faces

camera_values = list(map(float, (source / 'camera.txt').read_text().split()))
_, level, _, altitude, angle = camera_values
target = Vector((0, 0, level))
camera_data = bpy.data.cameras.new('Review at 45 degrees')
camera = bpy.data.objects.new('Review camera', camera_data)
bpy.context.collection.objects.link(camera)
camera.location = target + Vector((0, -altitude, altitude))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.lens = 38
camera_data.clip_end = 15000
bpy.context.scene.camera = camera
sun_data = bpy.data.lights.new('Review sunlight', 'SUN')
sun_data.energy = 2.0
sun_data.angle = math.radians(3)
sun = bpy.data.objects.new('Review sunlight', sun_data)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(35), math.radians(-20), math.radians(-25))
world = bpy.data.worlds.new('Review sky')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.30, .42, .57, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .6
scene = bpy.context.scene
scene.world = world
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 12
scene.cycles.max_bounces = 3
scene.render.threads_mode = 'FIXED'
scene.render.threads = 2
scene.render.resolution_x = 1000
scene.render.resolution_y = 700
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = str(output / (scene_name + '_' + phase + '.png'))
# A permanent caption travels with every exported image so it cannot be mistaken for a game screenshot.
scene.render.use_stamp = True
scene.render.use_stamp_note = True
scene.render.stamp_note_text = phase.upper() + ' | ' + scene_name + ' | production geometry/paint; Blender optics approximation, NOT Unity/HDRP'
scene.render.use_stamp_date = False
scene.render.use_stamp_time = False
scene.render.use_stamp_render_time = False
scene.render.use_stamp_frame = False
scene.render.use_stamp_filename = False
scene.render.use_stamp_camera = False
scene.render.use_stamp_scene = False
scene.render.stamp_font_size = 12
bpy.ops.render.render(write_still=True)
