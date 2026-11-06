"""Same-camera Blender comparison of actual planner exports and FBXs, not Unity GPU evidence."""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
helper = ROOT / 'Tools/WorldLandmarks/render.py'
source = helper.read_text().split('def render(kind,')[0]
scope = {'__file__': str(helper), '__name__': 'landmark_reader'}
exec(compile(source, str(helper), 'exec'), scope)
ground, routes, captured = [scope[k] for k in ('ground', 'routes', 'captured')]
template, mesh_object, solid = [scope[k] for k in ('template', 'mesh_object', 'solid')]
output = Path(__file__).resolve().parent / 'Review'


def models(data, distant, camera):
    """Use real model roots and their runtime tier; absent distant clutter never enters the image."""
    feature_roots = {(b['model'], round(b['at'][0], 2), round(b['at'][1], 2)) for b in data['models']}
    groups = {}
    for item in data['placements']:
        name = item['model']; point = item['position']
        if (name, round(point[0], 2), round(point[2], 2)) not in feature_roots and not item.get('clutter'): continue
        if distant: continue
        distance = np.linalg.norm(np.array(point) - camera)
        if (item.get('clutter') or item.get('nearOnly')) and distance > 275: continue
        lod = (1 if distance < 150 else 2) if item.get('clutter') else (0 if distance < 65 else 1 if distance < 250 else 2)
        if not (ROOT / f'Assets/DroneSandbox/Resources/Models/{name}.fbx').exists(): continue
        groups.setdefault((name, lod), []).append(item)
    for (name, lod), items in groups.items():
        v, tri, uv, slots, ids = template(name + '_LOD' + str(lod)); vertices = []
        for item in items:
            a = math.atan2(item['facing'][0], item['facing'][2]); c, s = math.cos(a), math.sin(a)
            matrix = np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]])
            points = (v * np.array(item['scale'])) @ matrix + np.array(item['position'])
            vertices.extend(points[:, [0, 2, 1]] * np.array([1, -1, 1]))
        mats = []
        colours = {'YardRust': (.26, .19, .115), 'YardGrey': (.38, .39, .36), 'YardDark': (.055, .06, .06)}
        for slot in slots:
            mats.append(solid(slot, colours[slot]) if slot in colours else scope['material'](slot))
        indices = (tri[None, :, :] + np.arange(len(items))[:, None, None] * len(v)).reshape(-1, 3)
        mesh_object(name, np.array(vertices), indices, np.tile(uv, (len(items), 1)), mats, np.tile(ids, len(items)))


def render(phase, kind, distance):
    """Camera distance is measured to site centre; lighting, FOV and target are fixed across phases."""
    for obj in list(bpy.context.scene.objects): bpy.data.objects.remove(obj, do_unlink=True)
    data = json.loads((ROOT / f'Temp/landmark-realism/{phase}/results/{kind}.json').read_text())
    ground(data); routes(data); captured(data['far' if distance == 2500 else 'surfaces'], 'Landmark')
    if distance != 2500:
        for mesh in data.get('fenceMeshes', []): captured(mesh, 'Terrain-following fence')
    a = np.array(data['along']); across = np.array([-a[1], a[0]])
    direction = a * .25 - across; direction /= np.linalg.norm(direction)
    pitch = math.radians(52 if distance != 2500 else 32)
    horizontal = direction * distance * math.cos(pitch)
    position = np.array([horizontal[0], data['elevation'] + distance * math.sin(pitch), horizontal[1]])
    models(data, distance == 2500, position)
    scene = bpy.context.scene; scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1600, 1000, 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world = bpy.data.worlds.new('World'); scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.38, .46, .55, 1)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 2; sun.rotation_euler = (math.radians(35), 0, math.radians(-30))
    camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera')); scene.collection.objects.link(camera)
    camera.location = Vector((position[0], -position[2], position[1]))
    target = Vector((0, 0, data['elevation'])); camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.lens = 20 if distance != 2500 else 44
    camera.data.clip_start = 1; camera.data.clip_end = 15000; scene.camera = camera
    scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'None'
    scene.render.filepath = str(output / f'{kind}_{distance}m_{phase}.png')
    bpy.ops.render.render(write_still=True)


args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
for phase in (args[:1] or ['before', 'after']):
    for kind in (args[1:2] or ['IndustrialZone', 'Airfield', 'RailJunction']):
        for distance in ((int(args[2]),) if len(args) > 2 else (300, 2500)): render(phase, kind, distance)
