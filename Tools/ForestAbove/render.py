"""One Blender view per process, using exported production selections and meshes; this is not an HDRP screenshot."""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
helper = ROOT / 'Tools/Blender/render_dense_forest.py'
helper_source = helper.read_text().split('args=sys.argv[')[0]
helper_source = helper_source.replace("solid('Meadow',(.34,.40,.24))", "solid('Meadow',(105/255,126/255,86/255))")
exec(compile(helper_source, str(helper), 'exec'))
OUT = ROOT / 'Tools/ForestAbove/Review'
OUT.mkdir(exist_ok=True)


def trees(data):
    """Batch only roots submitted by the production scheduler, using repository models and the real species dimensions."""
    groups = {}
    names = ['Tree', 'Birch', 'Spruce', 'Pine']
    for t in data['logical']:
        if t['tier'] < 0:
            continue
        tier = t['tier']
        name = ('Forest_' + names[t['species']] + '_Reduced') if tier == 1 else 'Scenery_' + names[t['species']]
        groups.setdefault((name, 0 if tier == 1 else tier, .96 + .04*t['shade']), []).append(t)
    for (name, lod, tint), items in groups.items():
        v, tris, uv, slots, ids = template(name + '_LOD' + str(lod))
        h = max(1, float(v[:, 1].max() - v[:, 1].min()))
        transforms = []
        for t in items:
            yaw = math.radians(t['yaw']); c, s = math.cos(yaw), math.sin(yaw)
            rotation = np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]])
            scale = t['height']/h
            transforms.append((v*np.array([scale*t['width'], scale, scale*t['width']])) @ rotation + np.array(t['p']))
        vertices = np.concatenate(transforms)[:, [0, 2, 1]] * np.array([1, -1, 1])
        indices = (tris[None, :, :] + np.arange(len(items))[:, None, None]*len(v)).reshape(-1, 3)
        mesh_object(name, vertices, indices, np.tile(uv, (len(items), 1)), [material(s.split('.')[0], tint) for s in slots], np.tile(ids, len(items)))


def surface(export, data, candidate):
    """Replay the exported canopy and the shared shader texture under fixed lighting, with a midpoint hand-over mask."""
    eye = Vector((data['eye'][0], -data['eye'][2], data['eye'][1]))
    direction = (Vector((data['target'][0], -data['target'][2], data['target'][1]))-eye).normalized()
    detail_image = bpy.data.images.load(str(ROOT/'Assets/DroneSandbox/Resources/Textures/ForestDetail.png'))
    detail_image.colorspace_settings.name = 'Non-Color'
    for file in sorted(export.glob('canopy-*.json')):
        d = json.loads(file.read_text()); v = np.array(d['vertices']); tris = np.array(d['indices']).reshape(-1, 3)
        if not len(tris): continue
        centers = v[tris].mean(axis=1)
        tris = tris[(np.abs(centers[:, 0]) <= 1000) & (np.abs(centers[:, 2]) <= 1000)]
        uv = np.array(d['uv']); uv[:, 1] = 1-uv[:, 1]
        # Blender reads PPM directly, keeping generated evidence out of Resources.
        m = material(file.stem, path=file.with_suffix('.ppm'))
        nodes, links = m.node_tree.nodes, m.node_tree.links
        bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
        geometry = nodes.new('ShaderNodeNewGeometry')

        def mathnode(op, a, b):
            n = nodes.new('ShaderNodeMath'); n.operation = op
            for i, value in enumerate((a, b)):
                if isinstance(value, (float, int)): n.inputs[i].default_value = value
                else: links.new(value, n.inputs[i])
            return n.outputs[0]

        delta = nodes.new('ShaderNodeVectorMath'); delta.operation = 'SUBTRACT'; delta.inputs[1].default_value = eye
        links.new(geometry.outputs['Position'], delta.inputs[0])
        distance = nodes.new('ShaderNodeVectorMath'); distance.operation = 'LENGTH'; links.new(delta.outputs[0], distance.inputs[0])
        if candidate:
            depth = nodes.new('ShaderNodeVectorMath'); depth.operation = 'DOT_PRODUCT'; depth.inputs[1].default_value = direction; links.new(delta.outputs[0], depth.inputs[0])
            score = mathnode('MULTIPLY', mathnode('ADD', depth.outputs['Value'], 32), 1000/(4*max(1, data['focal'])))
            inside = mathnode('LESS_THAN', score, data['thresholds'][2]*.95)
            sep = nodes.new('ShaderNodeSeparateXYZ'); links.new(delta.outputs[0], sep.inputs[0])
            horizontal = mathnode('SQRT', mathnode('ADD', mathnode('MULTIPLY', sep.outputs[0], sep.outputs[0]), mathnode('MULTIPLY', sep.outputs[1], sep.outputs[1])), 0)
            inside = mathnode('MULTIPLY', inside, mathnode('LESS_THAN', horizontal, max(0, data['ready']-16)))
            links.new(mathnode('SUBTRACT', 1, inside), bsdf.inputs['Alpha'])
            coords = nodes.new('ShaderNodeSeparateXYZ'); links.new(geometry.outputs['Position'], coords.inputs[0])
            worlduv = nodes.new('ShaderNodeCombineXYZ')
            links.new(mathnode('MULTIPLY', mathnode('ADD', coords.outputs[0], data['origin'][0]), 1/128), worlduv.inputs[0])
            links.new(mathnode('MULTIPLY', mathnode('SUBTRACT', data['origin'][1], coords.outputs[1]), 1/128), worlduv.inputs[1])
            tex = nodes.new('ShaderNodeTexImage'); tex.image = detail_image; links.new(worlduv.outputs[0], tex.inputs[0])
            split = nodes.new('ShaderNodeSeparateColor'); links.new(tex.outputs['Color'], split.inputs[0])
            clusteruv = nodes.new('ShaderNodeVectorMath'); clusteruv.operation = 'MULTIPLY_ADD'
            clusteruv.inputs[1].default_value = (.381, .381, .381); clusteruv.inputs[2].default_value = (.173, .619, 0)
            links.new(worlduv.outputs[0], clusteruv.inputs[0]); tex2 = nodes.new('ShaderNodeTexImage'); tex2.image = detail_image; links.new(clusteruv.outputs[0], tex2.inputs[0])
            split2 = nodes.new('ShaderNodeSeparateColor'); links.new(tex2.outputs['Color'], split2.inputs[0])
            factor = mathnode('MULTIPLY', mathnode('ADD', .5, split.outputs[0]), mathnode('ADD', .82, mathnode('MULTIPLY', .36, split2.outputs[0])))
            base = bsdf.inputs['Base Color'].links[0].from_socket
            tint = nodes.new('ShaderNodeMixRGB'); tint.blend_type = 'MULTIPLY'; tint.inputs[0].default_value = 1
            links.new(base, tint.inputs[1]); links.new(factor, tint.inputs[2]); links.new(tint.outputs[0], bsdf.inputs['Base Color'])
            slope = nodes.new('ShaderNodeCombineXYZ')
            for axis, sign in ((0, 1), (1, -1)):
                channel = axis + 1
                value = mathnode('ADD', mathnode('MULTIPLY', mathnode('SUBTRACT', mathnode('MULTIPLY', split.outputs[channel], 2), 1), .50), mathnode('MULTIPLY', mathnode('SUBTRACT', mathnode('MULTIPLY', split2.outputs[channel], 2), 1), .22))
                links.new(mathnode('MULTIPLY', value, sign), slope.inputs[axis])
            normal = nodes.new('ShaderNodeVectorMath'); normal.operation = 'ADD'; links.new(geometry.outputs['Normal'], normal.inputs[0]); links.new(slope.outputs[0], normal.inputs[1])
            normalized = nodes.new('ShaderNodeVectorMath'); normalized.operation = 'NORMALIZE'; links.new(normal.outputs[0], normalized.inputs[0]); links.new(normalized.outputs[0], bsdf.inputs['Normal'])
        else:
            links.new(mathnode('GREATER_THAN', distance.outputs['Value'], max(0, data['thresholds'][2]-17.5)), bsdf.inputs['Alpha'])
        m.surface_render_method = 'DITHERED'
        normals = np.array(d['normals'])[:, [0, 2, 1]] * np.array([1, -1, 1])
        mesh_object(file.stem, v[:, [0, 2, 1]]*np.array([1, -1, 1]), tris, uv[tris.ravel()], [m], normals=normals)


def render_one(phase, sector):
    """Fixed 300 m/45-degree camera, default 45-degree vertical FOV, identical sun and exposure for both revisions."""
    export = ROOT/'Temp/forest-above'/(phase+'-'+sector)
    data = json.loads((export/'scene.json').read_text())
    bpy.ops.wm.read_factory_settings(use_empty=True)
    terrain(data); surface(export, data, phase == 'after'); trees(data)
    colors = [(84,108,70),(89,112,73),(62,88,62),(66,92,63)] if phase == 'after' else [(73,99,61),(78,103,64),(48,77,54),(53,81,55)]
    original = [(77,98,51),(99,116,61),(64,87,58),(80,99,64)]
    for m in bpy.data.materials:
        if not m.name.startswith(('Leaves','Impostor')) or not m.use_nodes: continue
        species = next((i for i,name in enumerate(['Oak','Birch','Spruce','Pine']) if name in m.name),None)
        if species is None: continue
        for node in m.node_tree.nodes:
            if node.type != 'MIX_RGB' or node.blend_type != 'MULTIPLY': continue
            color = list(node.inputs[2].default_value)
            for i in range(3):
                srgb = color[i]*12.92 if color[i]<=.0031308 else 1.055*color[i]**(1/2.4)-.055
                color[i] = ns['linear'](srgb*colors[species][i]/original[species][i])
            node.inputs[2].default_value = color
    scene = bpy.context.scene; scene.render.engine = 'BLENDER_EEVEE'; scene.render.threads_mode = 'FIXED'; scene.render.threads = 2
    if hasattr(scene, 'eevee') and hasattr(scene.eevee, 'taa_render_samples'):
        scene.eevee.taa_render_samples = 16
    scene.render.resolution_x = 1600; scene.render.resolution_y = 900; scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'; scene.world = bpy.data.worlds.new('Review world'); scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.30, .35, .40, 1)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 2; sun.data.use_shadow = False; sun.rotation_euler = (math.radians(35), 0, math.radians(-30))
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera')); scene.collection.objects.link(cam)
    cam.location = Vector((data['eye'][0], -data['eye'][2], data['eye'][1])); target = Vector((data['target'][0], -data['target'][2], data['target'][1]))
    cam.rotation_euler = (target-cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.sensor_fit = 'VERTICAL'; cam.data.angle_y = math.radians(45); cam.data.clip_end = 15000; scene.camera = cam
    scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'None'; scene.view_settings.exposure = 0
    scene.render.filepath = str(OUT/(sector+'_300m_'+phase+'.png')); bpy.ops.render.render(write_still=True)


args = sys.argv[sys.argv.index('--')+1:]
render_one(args[0], args[1])
