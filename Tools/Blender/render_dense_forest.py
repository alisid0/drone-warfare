"""Matched offline Deep Forest crops from production logical stems, canopy meshes and repository FBXs.
These are not Unity/HDRP screenshots and do not validate shader fades or GPU timing.
"""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Tools/DenseForest/Review'
OUT.mkdir(parents=True, exist_ok=True)
helper = ROOT / 'Tools/ForestHlod/render.py'
ns = {'__file__': str(helper), '__name__': 'forest_reader'}
exec(compile(helper.read_text().split('def render(name):')[0], str(helper), 'exec'), ns)
template, material, mesh_object = [ns[k] for k in ('template', 'material', 'mesh_object')]


def solid(name, color):
    """Simple shared terrain colors keep both revisions under identical offline lighting."""
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*[ns['linear'](c) for c in color], 1)
    shader.inputs['Roughness'].default_value = 1
    return m


def height(data, p):
    """Interpolate the production terrain heights exported at eight metres."""
    x, z = (p[0] + 1000) / 8, (p[2] + 1000) / 8
    i, j = max(0, min(249, int(x))), max(0, min(249, int(z)))
    u, v = min(1, max(0, x-i)), min(1, max(0, z-j))
    h = data['heights']
    return (h[j*251+i]*(1-u)+h[j*251+i+1]*u)*(1-v)+(h[(j+1)*251+i]*(1-u)+h[(j+1)*251+i+1]*u)*v


def terrain(data):
    """Render the same crop and its actual logical floor mask in both pictures."""
    n = 251
    verts = np.array([[x*8-1000, 1000-z*8, h] for z in range(n) for x in range(n) for h in [data['heights'][z*n+x]]])
    tris = np.array([[z*n+x,(z+1)*n+x,z*n+x+1] for z in range(n-1) for x in range(n-1)] +
                    [[z*n+x+1,(z+1)*n+x,(z+1)*n+x+1] for z in range(n-1) for x in range(n-1)])
    floor = data.get('floor', [0]*len(verts))
    ids = np.array([1 if floor[t[0]] > 2 else 0 for t in tris])
    mesh_object('Terrain', verts, tris, np.zeros((len(tris)*3,2)), [solid('Meadow',(.34,.40,.24)), solid('Litter',(.25,.28,.18))], ids)
    for line in data.get('roads', []):
        verts, faces = [], []
        for a,b in zip(line,line[1:]):
            d = np.array(b)-np.array(a); length = np.linalg.norm(d)
            if length < .01: continue
            side = np.array([-d[1],d[0]]) / length * 2
            k = len(verts)
            for p in [a-side,b-side,b+side,a+side]: verts.append([p[0],-p[1],height(data,[p[0],0,p[1]])+.15])
            faces.extend([[k,k+2,k+1],[k,k+3,k+2]])
        if verts: mesh_object('Forest track',np.array(verts),np.array(faces),np.zeros((len(faces)*3,2)),[solid('Track',(.45,.40,.31))])


def tree_models(data, view, phase):
    """Batch real FBX geometry with production heights, width, yaw and conservative camera budgets."""
    groups = {}
    eye = np.array(view['eye']); full,reduced,card = view.get('ranges',[45,125,350])
    names = ['Scenery_Tree','Scenery_Birch','Scenery_Spruce','Scenery_Pine']
    if phase == 'after':
        for t in data['logical']:
            distance = np.linalg.norm(np.array(t['p'])-eye)
            if distance > card: continue
            tier = 0 if distance < max(0,full-5) else 1 if distance < reduced-10 else 2
            name = 'Forest_'+names[t['species']][8:]+'_Reduced' if tier==1 else names[t['species']]
            groups.setdefault((name,0 if tier==1 else tier,.96+.04*t['shade']),[]).append(t)
    else:
        for t in data['trees']:
            distance = np.linalg.norm(np.array(t['p'])-eye)
            lod = 0 if distance < 65 else 1 if distance < (200 if 'Spruce' in t['model'] else 250) else 2
            groups.setdefault((t['model'],lod,1),[]).append(t)
    for (name,lod,tint),items in groups.items():
        v,tris,uv,slots,ids = template(name+'_LOD'+str(lod))
        model_height = max(1,float(v[:,1].max()-v[:,1].min()))
        transforms=[]
        for t in items:
            yaw = math.radians(t['yaw']) if phase=='after' else math.atan2(t['yaw'][0],t['yaw'][2])
            c,s=math.cos(yaw),math.sin(yaw)
            rotation=np.array([[c,0,-s],[0,1,0],[s,0,c]])
            if phase=='after':
                scale=t['height']/model_height
                size=np.array([scale*t['width'],scale,scale*t['width']])
            else: size=np.array([t['scale']]*3)
            transforms.append((v*size)@rotation+np.array(t['p']))
        vertices=np.concatenate(transforms)[:,[0,2,1]]*np.array([1,-1,1])
        indices=(tris[None,:,:]+np.arange(len(items))[:,None,None]*len(v)).reshape(-1,3)
        mesh_object(name,vertices,indices,np.tile(uv,(len(items),1)),[material(s,tint) for s in slots],np.tile(ids,len(items)))


def canopy(export,view):
    """Use exported production HLOD triangles and textures, clipped only to this review's 2 km crop."""
    eye=Vector((view['eye'][0],-view['eye'][2],view['eye'][1]))
    for file in export.glob('canopy-*.json'):
        if file.name=='canopy-budget.json': continue
        data=json.loads(file.read_text()); v=np.array(data['vertices']); tris=np.array(data['indices']).reshape(-1,3)
        centers=v[tris].mean(axis=1)
        keep=(np.abs(centers[:,0])<=1000)&(np.abs(centers[:,2])<=1000)
        tris=tris[keep]; uv=np.array(data['uv']);uv[:,1]=1-uv[:,1]
        texture=file.with_suffix('.png')
        m=material(file.stem,path=texture)
        nodes,links=m.node_tree.nodes,m.node_tree.links
        geometry=nodes.new('ShaderNodeNewGeometry'); distance=nodes.new('ShaderNodeVectorMath');distance.operation='DISTANCE'
        distance.inputs[1].default_value=eye;links.new(geometry.outputs['Position'],distance.inputs[0])
        threshold=nodes.new('ShaderNodeMath');threshold.operation='GREATER_THAN';threshold.inputs[1].default_value=view['ranges'][2]-17.5
        links.new(distance.outputs['Value'],threshold.inputs[0])
        bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');links.new(threshold.outputs[0],bsdf.inputs['Alpha']);m.surface_render_method='DITHERED'
        normals=np.array(data['normals'])[:,[0,2,1]]*np.array([1,-1,1]) if 'normals' in data else None
        mesh_object(file.stem,v[:,[0,2,1]]*np.array([1,-1,1]),tris,uv[tris.ravel()],[m],normals=normals)


def render(phase, altitude):
    """Lock camera, illumination and output size; compare density rather than unrelated presentation changes."""
    for obj in list(bpy.context.scene.objects): bpy.data.objects.remove(obj,do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if not mesh.users:bpy.data.meshes.remove(mesh)
    export=ROOT/'Temp/dense-forest'/phase;data=json.loads((export/'scene.json').read_text())
    view=next(v for v in data['views'] if v['altitude']==altitude)
    terrain(data);tree_models(data,view,phase)
    if phase=='after':canopy(export,view)
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1600;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Review world');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.40,1)
    sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN'));scene.collection.objects.link(sun);sun.data.energy=2;sun.data.use_shadow=False;sun.rotation_euler=(math.radians(35),0,math.radians(-30))
    cam=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(cam)
    cam.location=Vector((view['eye'][0],-view['eye'][2],view['eye'][1]));target=Vector((view['target'][0],-view['target'][2],view['target'][1]))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.sensor_fit='VERTICAL';cam.data.angle_y=math.radians(55);cam.data.clip_end=20000;scene.camera=cam
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0
    scene.render.filepath=str(OUT/f'deepforest_{altitude}m_{phase}.png');bpy.ops.render.render(write_still=True)


args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for phase in (args[:1] or ['before','after']):
    for altitude in (list(map(int,args[1:])) or [100,300,2500]):render(phase,altitude)
