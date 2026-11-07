"""Render production HLOD triangles and captured sector model roots; these are offline, not HDRP frames."""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np
from mathutils import Vector

root = Path(__file__).resolve().parents[2]
exports = root / 'Temp/world-landmarks/results'
output = root / 'Tools/WorldLandmarks/Review'
output.mkdir(parents=True, exist_ok=True)
# Reuse the already-reviewed FBX/Unity-material reader without running its forest-specific render loop.
helper = root / 'Tools/ForestHlod/render.py'
namespace = {'__file__': str(helper), '__name__': 'landmark_asset_reader'}
exec(compile(helper.read_text().split('def render(name):')[0], str(helper), 'exec'), namespace)
template, material, mesh_object = [namespace[k] for k in ('template', 'material', 'mesh_object')]


def solid(name, color):
    """Opaque lit material colours come directly from the production landmark mesh palette."""
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*[namespace['linear'](c) for c in color], 1)
    shader.inputs['Roughness'].default_value = .9
    return m


def captured(data, name):
    """Triangles are exported from LandmarkMesh.Build itself, including the exact slab overlaps."""
    vertices = np.array(data['vertices'], dtype=np.float32)[:, [0, 2, 1]] * np.array([1, -1, 1])
    triangles, ids = [], []
    for index, sub in enumerate(data['triangles']):
        triangles.extend(np.array(sub, dtype=np.int32).reshape(-1, 3))
        ids.extend([index] * (len(sub) // 3))
    triangles = np.array(triangles, dtype=np.int32)
    slots = [solid(name + str(i), color) for i, color in enumerate(data['colors'])]
    return mesh_object(name, vertices, triangles, np.zeros((len(triangles) * 3, 2)), slots, np.array(ids, dtype=np.int32))


def ground(data):
    """Height samples come from built Ground inside the crop and WorldPlan outside, on one aligned grid."""
    n, step = data['n'], data['step']
    vertices = np.array([[-2000 + x * step, 2000 - z * step, data['heights'][z * n + x]] for z in range(n) for x in range(n)], dtype=np.float32)
    triangles = np.array([[z*n+x, (z+1)*n+x, z*n+x+1] for z in range(n-1) for x in range(n-1)] + [[z*n+x+1, (z+1)*n+x, (z+1)*n+x+1] for z in range(n-1) for x in range(n-1)], dtype=np.int32)
    palette = [(0.38,.46,.28),(.61,.56,.35),(.21,.28,.16),(.31,.38,.28),(.19,.26,.29),(.47,.44,.40),(.45,.46,.33),(.48,.46,.42)]
    slots = [solid('Ground' + str(i), c) for i,c in enumerate(palette)]
    ids = np.array([data['cover'][tri[0]] for tri in triangles], dtype=np.int32)
    mesh_object('Sampled terrain', vertices, triangles, np.zeros((len(triangles)*3,2)), slots, ids)


def models(data, distant):
    """Batch the repository's real FBXs using the actual successful World.Place roots from the harness."""
    groups = {}
    feature_roots = {(b['model'], round(b['at'][0], 2), round(b['at'][1], 2)) for b in data['models']}
    for item in data['placements']:
        name = item['model']
        if distant and (name, round(item['position'][0], 2), round(item['position'][2], 2)) in feature_roots:
            continue
        if not (root / f'Assets/DroneSandbox/Resources/Models/{name}.fbx').exists():
            continue
        lod = 2 if any(t in name for t in ('Tree','Spruce','Pine','Birch','Poplar')) else 0
        groups.setdefault((name, lod), []).append(item)
    for (name, lod), items in groups.items():
        v, tris, uv, slots, ids = template(name + '_LOD' + str(lod))
        vertices = []
        for item in items:
            facing = item['facing']
            angle = math.atan2(facing[0], facing[2])
            ca, sa = math.cos(angle), math.sin(angle)
            rotation = np.array([[ca,0,-sa],[0,1,0],[sa,0,ca]])
            scale = np.array(item.get('scale', [1,1,1]))
            transformed = (v * scale) @ rotation + np.array(item['position'])
            vertices.extend(transformed[:,[0,2,1]] * np.array([1,-1,1]))
        indices = (tris[None,:,:] + np.arange(len(items))[:,None,None]*len(v)).reshape(-1,3)
        mesh_object(name, np.array(vertices), indices, np.tile(uv, (len(items),1)), [material(s) for s in slots], np.tile(ids,len(items)))


def routes(data):
    """Draw the exported prepared road/rail centre lines on the sampled terrain for geographic context."""
    n, step = data['n'], data['step']
    def height(p):
        x,z=(p+2000)/step
        i,j=max(0,min(n-2,int(x))),max(0,min(n-2,int(z)))
        u,v=x-i,z-j;h=data['heights']
        return (h[j*n+i]*(1-u)+h[j*n+i+1]*u)*(1-v)+(h[(j+1)*n+i]*(1-u)+h[(j+1)*n+i+1]*u)*v
    for name,width,color,lift in [('roads',6,(.48,.44,.36),.18),('rails',4.2,(.28,.27,.25),.22)]:
        vertices,triangles=[],[]
        for line in data[name]:
            points=np.array(line)
            for a,b in zip(points[:-1],points[1:]):
                delta=b-a;length=np.linalg.norm(delta)
                if length<.001:
                    continue
                side=np.array([-delta[1],delta[0]])/length*width/2
                base=len(vertices)
                for p in [a-side,b-side,b+side,a+side]:vertices.append([p[0],-p[1],height(p)+lift])
                triangles.extend([[base,base+2,base+1],[base,base+3,base+2]])
        if vertices:mesh_object(name,np.array(vertices),np.array(triangles),np.zeros((len(triangles)*3,2)),[solid(name,color)])


def render(kind, tier, distance):
    """Identical camera, ground and surroundings make the local/distant placement comparison reviewable."""
    for obj in list(bpy.context.scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    data = json.loads((exports / (kind + '.json')).read_text())
    ground(data)
    routes(data)
    captured(data['far' if tier == 'hlod' else 'surfaces'], 'Permanent landmark')
    models(data, tier == 'hlod')
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1600, 1000, 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world = bpy.data.worlds.new('World')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.38,.46,.55,1)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun','SUN'))
    scene.collection.objects.link(sun); sun.data.energy = 2; sun.data.use_shadow = True
    sun.rotation_euler = (math.radians(35),0,math.radians(-30))
    camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    scene.collection.objects.link(camera)
    a = np.array(data['along']); across = np.array([-a[1], a[0]])
    direction = (a * .25 - across); direction /= np.linalg.norm(direction)
    horizontal = direction * distance * math.cos(math.radians(32))
    camera.location = Vector((horizontal[0], -horizontal[1], data['elevation'] + distance * math.sin(math.radians(32))))
    target = Vector((0,0,data['elevation']))
    camera.rotation_euler = (target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type = 'PERSP'; camera.data.lens = 44; camera.data.clip_start = 100; camera.data.clip_end = 15000
    scene.camera = camera
    scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0; scene.view_settings.gamma = 1
    name = f'{kind}_{tier}_{distance}m'
    scene.render.filepath = str(output / (name + '.png'))
    bpy.ops.render.render(write_still=True)
    print('RENDERED', name, flush=True)


args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for kind in args or ['IndustrialZone','Airfield','RailJunction']:
    for tier, distance in [('hlod',2500),('sector',2500),('sector',1000)]:
        render(kind,tier,distance)
