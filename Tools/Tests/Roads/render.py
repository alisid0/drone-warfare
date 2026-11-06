"""Fixed 150/300 m cameras over captured road meshes and FBX placements; not Unity/HDRP timing evidence."""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np
from mathutils import Vector

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
helper=ROOT/'Tools/WorldLandmarks/render.py'
scope={'__file__':str(helper),'__name__':'road_asset_reader'}
exec(compile(helper.read_text().split('def render(kind,')[0],str(helper),'exec'),scope)
template,mesh_object,solid=[scope[k] for k in ('template','mesh_object','solid')]
scene_name,phase=sys.argv[sys.argv.index('--')+1:][:2]
data=json.loads((ROOT/'Temp/roads/review'/f'{scene_name}.json').read_text())
heights=np.array(data['heights']).reshape(401,401)


def height(x,z):
    """Sample the exported production terrain grid only for camera aiming and cheap boundary context."""
    u=np.clip((x+1000)/5,0,399.999);v=np.clip((z+1000)/5,0,399.999)
    i,j=int(u),int(v);u-=i;v-=j
    return (heights[j,i]*(1-u)+heights[j,i+1]*u)*(1-v)+(heights[j+1,i]*(1-u)+heights[j+1,i+1]*u)*v


def texture_material(name,path):
    """Use the same baked sRGB atlas sampled by the game's ordinary HDRP Lit material."""
    mat=solid(name,(1,1,1));node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(path),check_existing=True)
    node.image.colorspace_settings.name='sRGB';node.extension='REPEAT';node.interpolation='Linear'
    mat.node_tree.links.new(node.outputs['Color'],mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    return mat


def render(label,centre,altitude):
    """Only the road geometry, paint and road furniture change between the two identically framed views."""
    for obj in list(bpy.context.scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
    centre=np.array(centre);radius=altitude*2.4
    i0=max(0,int((centre[0]-radius+1000)/5));i1=min(400,int((centre[0]+radius+1000)/5)+1)
    j0=max(0,int((centre[1]-radius+1000)/5));j1=min(400,int((centre[1]+radius+1000)/5)+1)
    n=i1-i0+1
    points=np.array([[i*5-1000,-(j*5-1000),heights[j,i]-.08] for j in range(j0,j1+1) for i in range(i0,i1+1)])
    faces=[]
    for j in range(j1-j0):
        for i in range(i1-i0):
            a=j*n+i;faces.extend([(a,a+n,a+1),(a+1,a+n,a+n+1)])
    faces=np.array(faces);uv=points[faces.flatten()][:,:2]/2000+.5
    ground=texture_material('Captured ground',ROOT/'Temp/roads/review'/f'{scene_name}-{phase}.png')
    mesh_object('Captured terrain',points,faces,uv,[ground],np.zeros(len(faces),int))
    roadmats={name:texture_material(name,ROOT/'Assets/DroneSandbox/Resources/Textures'/f'{name}.png') for name in ('RoadHighway','RoadLane','RoadTrack')}
    for name,color in {'RoadJunction':(92/255,91/255,87/255),'RoadGravel':(132/255,128/255,113/255),'RoadDitch':(64/255,70/255,49/255),'RoadPuddle':(.23,.26,.25)}.items():roadmats[name]=solid(name,color)
    garden=[solid('Lawn',(.24,.29,.13)),solid('Soil',(.26,.20,.12)),solid('Gravel',(.39,.36,.29)),solid('Hedge',(.16,.22,.10)),solid('Chalk',(.82,.82,.76))]
    for m in data['meshes']:
        road=m['name'].startswith('Road')
        if road and (phase=='before' or m['name']=='RoadPuddle'):continue
        if not road and not m['name'].endswith('0' if altitude<300 else '1'):continue
        if not m['vertices']:continue
        vertices=np.array(m['vertices'])[:,[0,2,1]]*np.array([1,-1,1]);tris=[];ids=[]
        for sub,indices in m['triangles'].items():
            faces=np.array(indices).reshape(-1,3);tris.extend(faces);ids.extend([0 if road else int(sub)]*len(faces))
        if not tris:continue
        tris=np.array(tris);uv=np.array(m['uv'])[tris.flatten()] if m['uv'] else np.zeros((len(tris)*3,2))
        mesh_object(m['name'],vertices,tris,uv,[roadmats[m['name']]] if road else garden,np.array(ids))
    vertices=[];faces=[]
    for edge in data['boundaries']:
        if edge['kind']!='Hedge':continue
        a,b,gate=np.array(edge['a']),np.array(edge['b']),np.array(edge['gate']);d=b-a;length=np.linalg.norm(d)
        if length<.1 or np.linalg.norm((a+b)*.5-centre)>radius+length*.5:continue
        side=np.array([-d[1],d[0]])/length*.65;count=max(1,math.ceil(length/3))
        for k in range(count):
            x=a+d*k/count;y=a+d*(k+1)/count
            if np.linalg.norm((x+y)*.5-gate)<2.5:continue
            start=len(vertices)
            for p in (x-side,y-side,y+side,x+side):vertices.append([p[0],-p[1],height(*p)+1.5])
            faces.extend([(start,start+2,start+1),(start,start+3,start+2)])
    if faces:mesh_object('Existing hedge context',np.array(vertices),np.array(faces),np.zeros((len(faces)*3,2)),[garden[3]],np.zeros(len(faces),int))
    groups={}
    for item in data['models']+(data['roadside'] if phase=='after' else []):
        p=np.array(item['position'])
        if np.linalg.norm(p[[0,2]]-centre)>radius:continue
        name=item['model']
        if not (ROOT/f'Assets/DroneSandbox/Resources/Models/{name}.fbx').exists():continue
        lod=1 if altitude<300 else 2
        groups.setdefault((name,lod),[]).append(item)
    for (name,lod),items in groups.items():
        v,t,uv,slots,ids=template(name+'_LOD'+str(lod));vertices=[]
        for p in items:
            angle=math.atan2(p['facing'][0],p['facing'][2]);c,s=math.cos(angle),math.sin(angle)
            pts=(v*np.array(p['scale']))@np.array([[c,0,-s],[0,1,0],[s,0,c]])+np.array(p['position'])
            vertices.extend(pts[:,[0,2,1]]*np.array([1,-1,1]))
        faces=(t[None,:,:]+np.arange(len(items))[:,None,None]*len(v)).reshape(-1,3)
        mats=[]
        for slot in slots:
            if name=='Scenery_Poplar':mat=scope['material'](slot)
            else:mat=solid(slot,{'Wood':(.23,.16,.09),'RoofTile':(.38,.13,.065),'PlasterWhite':(.68,.64,.53),'WindowGlass':(.06,.09,.1),'RoofSlate':(.17,.19,.19),'RoofMetal':(.27,.29,.29)}.get(slot,(.34,.35,.28)))
            mats.append(mat)
        mesh_object(name,np.array(vertices),faces,np.tile(uv,(len(items),1)),mats,np.tile(ids,len(items)))
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1280;scene.render.resolution_y=800;scene.render.resolution_percentage=100
    scene.render.threads_mode='FIXED';scene.render.threads=2
    scene.world=bpy.data.worlds.new('Review sky');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.38,.46,.55,1)
    sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN'));scene.collection.objects.link(sun);sun.data.energy=2;sun.rotation_euler=(.6,0,-.6)
    camera=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(camera)
    target=Vector((centre[0],-centre[1],height(*centre)));camera.location=target+Vector((altitude*.48,-altitude*.65,altitude))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.lens=27;camera.data.clip_end=5000;scene.camera=camera
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(HERE/'Review'/f'{label}_{altitude}m_{phase}.png');bpy.ops.render.render(write_still=True)
    print('RENDERED',label,altitude,phase,flush=True)
    # Release unused review data before the next view in this single Blender process.
    for obj in list(bpy.context.scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users==0:bpy.data.meshes.remove(mesh)


for label,centre in data['targets'].items():
    for altitude in (150,300):render(label,centre,altitude)
