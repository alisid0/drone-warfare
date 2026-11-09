"""Matched Blender structure review. Production meshes/colours/normals, fixed sun/exposure; no HDRP or fog claim."""
from pathlib import Path
import json
import math
import re
import sys
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
helper = ROOT / 'Tools/Blender/render_dense_forest.py'
source = helper.read_text().split("args=sys.argv[")[0]
source = source.replace("solid('Meadow',(.34,.40,.24))", "solid('Meadow',(105/255,126/255,86/255))")
exec(compile(source, str(helper), 'exec'))
OUT = ROOT / 'Tools/DistantForest/Review'
OUT.mkdir(exist_ok=True)


def photo_tints():
    """Apply the production species correction to the existing photo cards in the supplementary 300 m view."""
    code = (ROOT/'Assets/DroneSandbox/Scripts/ForestPalette.cs').read_text()
    colors = [tuple(map(int,m)) for m in re.findall(r'new Color32\((\d+), (\d+), (\d+), 255\)',code)[:4]]
    original = [(77,98,51),(99,116,61),(64,87,58),(80,99,64)]
    for m in bpy.data.materials:
        if not m.name.startswith(('Leaves','Impostor')): continue
        species = next((i for i,name in enumerate(['Oak','Birch','Spruce','Pine']) if name in m.name),None)
        if species is None: continue
        for node in m.node_tree.nodes:
            if node.type != 'MIX_RGB' or node.blend_type != 'MULTIPLY': continue
            color = list(node.inputs[2].default_value)
            for i in range(3):
                srgb = color[i]*12.92 if color[i]<=.0031308 else 1.055*color[i]**(1/2.4)-.055
                color[i] = ns['linear'](srgb*colors[species][i]/original[species][i])
            node.inputs[2].default_value=color


def landscape(data):
    """Surround the sector with the sampled world terrain so the 9 km view contains broad forest districts."""
    n, step, half = data['n'], data['step'], data['half']
    verts = np.array([[x*step-half, half-z*step, h] for z in range(n) for x in range(n) for h in [data['heights'][z*n+x]]])
    tris = np.array([[z*n+x,(z+1)*n+x,z*n+x+1] for z in range(n-1) for x in range(n-1)] +
                    [[z*n+x+1,(z+1)*n+x,(z+1)*n+x+1] for z in range(n-1) for x in range(n-1)])
    colors = [(105/255,126/255,86/255),(.50,.46,.30),(49/255,70/255,45/255),(.31,.38,.29),(.12,.24,.28),(.39,.39,.36),(.40,.40,.34),(.45,.43,.38)]
    mats = [solid('Land '+str(i), c) for i,c in enumerate(colors)]
    ids = np.array([data['covers'][t[0]] for t in tris])
    mesh_object('Landscape',verts,tris,np.zeros((len(tris)*3,2)),mats,ids)


def forest(export, altitude, view):
    """Render the exact exported production surface, including its normals and texture gutters."""
    pattern = 'distant-*.json' if altitude == 9000 else 'close-*.json' if altitude < 1800 and export.name == 'after' else 'canopy-*.json'
    for path in sorted(export.glob(pattern)):
        if path.name.endswith('budget.json'): continue
        data = json.loads(path.read_text())
        v = np.array(data['vertices']); tris = np.array(data['indices']).reshape(-1,3)
        if not len(tris): continue
        if altitude != 9000:
            centers = v[tris].mean(axis=1)
            tris = tris[(np.abs(centers[:,0])<=1000)&(np.abs(centers[:,2])<=1000)]
        uv = np.array(data['uv']); uv[:,1] = 1-uv[:,1]
        normals = np.array(data['normals'])[:,[0,2,1]]*np.array([1,-1,1])
        m = material(str(path),path=path.with_suffix('.png'))
        if altitude == 300:
            nodes, links = m.node_tree.nodes, m.node_tree.links
            geometry=nodes.new('ShaderNodeNewGeometry'); distance=nodes.new('ShaderNodeVectorMath'); distance.operation='DISTANCE'
            distance.inputs[1].default_value=(view['eye'][0],-view['eye'][2],view['eye'][1]); links.new(geometry.outputs['Position'],distance.inputs[0])
            threshold=nodes.new('ShaderNodeMath'); threshold.operation='GREATER_THAN'; threshold.inputs[1].default_value=view['ranges'][2]-17.5
            links.new(distance.outputs['Value'],threshold.inputs[0])
            bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED'); links.new(threshold.outputs[0],bsdf.inputs['Alpha']); m.surface_render_method='DITHERED'
        obj = mesh_object('Canopy',v[:,[0,2,1]]*np.array([1,-1,1]),tris,uv[tris.ravel()],[m],normals=normals)


def masks():
    """Render visible-surface labels with identical camera and geometry for reproducible luminance masks."""
    for m in bpy.data.materials:
        if not m.use_nodes: continue
        name = m.name
        if 'canopy-' in name or 'close-' in name or 'distant-' in name or name.startswith(('Leaves','Impostor')): color = (1,0,0,1)
        elif name.startswith('Meadow') or name.startswith('Land 0'): color = (0,1,0,1)
        elif name.startswith('Land 1'): color = (1,1,0,1)
        else: color = (0,0,1,1)
        nodes, links = m.node_tree.nodes, m.node_tree.links
        out = next((n for n in nodes if n.type=='OUTPUT_MATERIAL'), None)
        if out is None: continue
        emission = nodes.new('ShaderNodeEmission'); emission.inputs[0].default_value = color
        bsdf=next((n for n in nodes if n.type=='BSDF_PRINCIPLED'),None)
        if bsdf is not None and bsdf.inputs['Alpha'].is_linked:
            alpha=bsdf.inputs['Alpha'].links[0].from_socket
            transparent=nodes.new('ShaderNodeBsdfTransparent'); mix=nodes.new('ShaderNodeMixShader')
            links.new(alpha,mix.inputs[0]); links.new(transparent.outputs[0],mix.inputs[1]); links.new(emission.outputs[0],mix.inputs[2]); links.new(mix.outputs[0],out.inputs['Surface'])
        else: links.new(emission.outputs[0],out.inputs['Surface'])


def review(phase, altitude):
    """Use a locked camera, 35-degree sun and zero exposure offset in both revisions."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ns['materials'].clear(); ns['images'].clear()
    export = ROOT/'Temp/distant-forest'/phase
    data = json.loads((export/'scene.json').read_text())
    view = next(v for v in data['views'] if v['altitude']==altitude)
    if altitude == 9000: landscape(json.loads((export/'landscape.json').read_text()))
    else: terrain(data)
    forest(export,altitude,view)
    # None of these cameras submits full or reduced meshes. At 300 m retain any visible crown cards.
    if altitude == 300:
        tree_models(data,view,'after')
        if phase == 'after': photo_tints()
    for m in bpy.data.materials:
        if m.use_nodes:
            for node in m.node_tree.nodes:
                if node.type=='BSDF_PRINCIPLED': node.inputs['Emission Strength'].default_value=0
    scene = bpy.context.scene
    scene.render.engine='BLENDER_EEVEE'; scene.render.resolution_x=1600; scene.render.resolution_y=900; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Fixed review sky'); scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.40,1)
    sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN')); scene.collection.objects.link(sun)
    sun.data.energy=2; sun.data.use_shadow=False; sun.rotation_euler=(math.radians(35),0,math.radians(-30))
    cam=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera')); scene.collection.objects.link(cam)
    cam.location=Vector((view['eye'][0],-view['eye'][2],view['eye'][1])); target=Vector((view['target'][0],-view['target'][2],view['target'][1]))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.sensor_fit='VERTICAL'; cam.data.angle_y=math.radians(55); cam.data.clip_end=30000; scene.camera=cam
    scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'; scene.view_settings.exposure=0
    scene.render.filepath=str(OUT/f'forest_{altitude}m_{phase}.png'); bpy.ops.render.render(write_still=True)
    masks()
    scene.render.filepath=str(export/f'mask_{altitude}.png'); bpy.ops.render.render(write_still=True)


args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for phase in args[:1] or ['before','after']:
    for altitude in list(map(int,args[1:])) or [300,1000,2500,9000]: review(phase,altitude)
