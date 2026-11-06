"""Low-memory geometry reviews from production exports; these images do not validate HDRP or GPU timing."""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).resolve().parent
helper=ROOT/'Tools/WorldLandmarks/render.py'
scope={'__file__':str(helper),'__name__':'asset_reader'}
exec(compile(helper.read_text().split('def render(kind,')[0],str(helper),'exec'),scope)
template,mesh_object,solid=[scope[k] for k in ('template','mesh_object','solid')]


def render(name,altitude):
    """Use actual accepted buildings and the matching garden LOD, with shared production ground colours."""
    for obj in list(bpy.context.scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
    data=json.loads((ROOT/'Temp/lived-in/review'/f'{name}.json').read_text())
    heights=np.array(data['heights']).reshape(201,201)
    def height(x,z):
        u=np.clip((x+1000)/10,0,199.999);v=np.clip((z+1000)/10,0,199.999)
        i,j=int(u),int(v);u-=i;v-=j
        return (heights[j,i]*(1-u)+heights[j,i+1]*u)*(1-v)+(heights[j+1,i]*(1-u)+heights[j+1,i+1]*u)*v
    points=np.array([[i*10-1000,-(j*10-1000),heights[j,i]] for j in range(201) for i in range(201)])
    faces=[]
    for j in range(200):
        for i in range(200):
            a=j*201+i;faces.extend([(a,a+201,a+1),(a+1,a+201,a+202)])
    faces=np.array(faces);uv=points[faces.flatten()][:,:2]/2000+.5
    ground=solid('Production ground colour',(.3,.3,.3))
    tex=ground.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(ROOT/'Temp/lived-in/review'/f'{name}.png'))
    tex.image.colorspace_settings.name='sRGB';tex.interpolation='Linear'
    ground.node_tree.links.new(tex.outputs['Color'],ground.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
    mesh_object('Captured terrain',points,faces,uv,[ground],np.zeros(len(faces),int))
    vertices=[];faces=[]
    for edge in data.get('boundaries',[]):
        if edge['kind']!='Hedge':continue
        a,b,gate=np.array(edge['a']),np.array(edge['b']),np.array(edge['gate'])
        d=b-a;length=np.linalg.norm(d)
        if length<.01:continue
        count=max(1,math.ceil(length/4));side=np.array([-d[1],d[0]])/length*.5
        for k in range(count):
            x=a+d*k/count;y=a+d*(k+1)/count
            if np.linalg.norm((x+y)*.5-gate)<3:continue
            start=len(vertices)
            for p in (x-side,y-side,y+side,x+side):vertices.append([p[0],-p[1],height(*p)+1.5])
            faces.extend([(start,start+2,start+1),(start,start+3,start+2)])
    if faces:mesh_object('Parcel boundary silhouettes',np.array(vertices),np.array(faces),np.zeros((len(faces)*3,2)),[solid('Field hedges',(.16,.22,.1))],np.zeros(len(faces),int))
    mats=[solid('Lawn',(.24,.29,.13)),solid('Soil and wood',(.26,.20,.12)),solid('Gravel and wall',(.39,.36,.29)),solid('Hedge',(.16,.22,.10))]
    rough=np.array([.20043458,.20832994,.09801723])*.8+np.array([.06715037,.12965796,.06262422])*.2
    def srgb(linear):
        return tuple(np.where(linear<=.0031308,linear*12.92,1.055*linear**(1/2.4)-.055))
    margins=[solid('Uncultivated margin',srgb(rough)),solid('Rough damp ground',srgb(rough*.9))]
    for m in data['meshes']:
        field=m['name']=='Field use 128 m block'
        if (not field and not m['name'].endswith('0' if altitude<300 else '1')) or not m['vertices']:continue
        vertices=np.array(m['vertices'])[:,[0,2,1]]*np.array([1,-1,1]);faces=[];ids=[]
        for sub,indices in m['triangles'].items():
            tris=np.array(indices).reshape(-1,3);faces.extend(tris);ids.extend([int(sub)]*len(tris))
        if faces:mesh_object('Actual field use' if field else 'Actual garden mesh',vertices,np.array(faces),np.zeros((len(faces)*3,2)),margins if field else mats,np.array(ids))
    centre=np.array(data['target'])
    if name.startswith('Village') or name=='TownOutskirts':
        houses=[p['position'] for p in data['models'] if p['model'].startswith('Scenery_House')]
        if houses:centre=np.mean(houses,axis=0)[[0,2]]
    if name=='TownOutskirts':centre=np.clip(centre,-550,550)
    target=np.array([centre[0],height(*centre),centre[1]])
    eye=target+np.array([altitude*.48,altitude,altitude*.65]);groups={}
    for p in data['models']:
        if altitude>=300 and p['model'] in ('Scenery_LogPile','Scenery_Tree'):continue
        if np.linalg.norm(np.array(p['position'])-eye)>max(550,altitude*2):continue
        if not (ROOT/f"Assets/DroneSandbox/Resources/Models/{p['model']}.fbx").exists():continue
        lod=1 if altitude<300 else 2
        groups.setdefault((p['model'],lod,p['palette'],p['roof']),[]).append(p)
    for (model,lod,wall,roof),items in groups.items():
        v,t,uv,slots,ids=template(model+'_LOD'+str(lod));vertices=[]
        for p in items:
            angle=math.atan2(p['facing'][0],p['facing'][2]);c,s=math.cos(angle),math.sin(angle)
            matrix=np.array([[c,0,-s],[0,1,0],[s,0,c]])
            pts=(v*np.array(p['scale']))@matrix+np.array(p['position']);vertices.extend(pts[:,[0,2,1]]*np.array([1,-1,1]))
        faces=(t[None,:,:]+np.arange(len(items))[:,None,None]*len(v)).reshape(-1,3)
        walls=[(.68,.64,.53),(.64,.59,.45),(.58,.43,.26),(.62,.48,.42),(.46,.48,.45)]
        roofs=[(.38,.13,.065),(.26,.16,.12),(.43,.22,.12),(.19,.21,.22)]
        palette={'RoofTile':roofs[roof],'PlasterWhite':walls[wall],'RoofSlate':(.17,.19,.19),'RoofMetal':(.27,.29,.29),'Wood':(.27,.19,.10),'WindowGlass':(.06,.09,.1),'GardenGlass':(.22,.33,.32),'Brick':(.38,.21,.15)}
        materials=[]
        for slot in slots:
            color=palette.get(slot,(.34,.35,.28))
            if any(n in slot.lower() for n in ('leaf','leaves','foliage','needle','crown')):color=(.17,.26,.09)
            materials.append(solid(slot,color))
        mesh_object(model,np.array(vertices),faces,np.tile(uv,(len(items),1)),materials,np.tile(ids,len(items)))
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1280;scene.render.resolution_y=800;scene.render.resolution_percentage=100
    scene.render.threads_mode='FIXED';scene.render.threads=2
    scene.world=bpy.data.worlds.new('Review sky');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.38,.46,.55,1)
    sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN'));scene.collection.objects.link(sun);sun.data.energy=2;sun.rotation_euler=(.6,0,-.6)
    camera=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(camera)
    camera.location=Vector((eye[0],-eye[2],eye[1]));aim=Vector((target[0],-target[2],target[1]));camera.rotation_euler=(aim-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.lens=32 if name=='TownOutskirts' else 25
    camera.data.clip_end=5000;scene.camera=camera;scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
    (HERE/'Review').mkdir(exist_ok=True);scene.render.filepath=str(HERE/'Review'/f'{name}_{altitude}m.png')
    bpy.ops.render.render(write_still=True)

args=sys.argv[sys.argv.index('--')+1:]
render(args[0],int(args[1]))
