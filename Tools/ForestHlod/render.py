"""Render captured production submissions in Blender, with the repository FBXs and photo materials.
This is an offline visual comparison, not an HDRP screenshot or a GPU benchmark.
"""
import bpy, json, math, re, sys
from pathlib import Path
from collections import defaultdict
import numpy as np
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
exports=root/'Temp/forest-hlod/exports'; output=root/'Tools/ForestHlod/Review';output.mkdir(parents=True,exist_ok=True)
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if args and args[0]=='--calibration':
    exports=root/'Temp/forest-hlod/calibration';args=args[1:]
requested=args or ['forest_2500m_before','forest_2500m_after']
templates={}; materials={}; images={};guid_paths={}
for meta in (root/'Assets/DroneSandbox/Textures').rglob('*.meta'):
    match=re.search(r'guid: ([0-9a-f]+)',meta.read_text())
    if match:guid_paths[match[1]]=meta.with_suffix('')
def linear(c): return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
def image(path):
    path=str(path)
    if path.endswith('.ppm'): path=path[:-4]+'.png'
    if path not in images: images[path]=bpy.data.images.load(path,check_existing=True)
    return images[path]
def material(name,tint=1,path=None):
    key=(name,round(tint,3),str(path))
    if key in materials:return materials[key]
    m=bpy.data.materials.new(name);m.use_nodes=True
    nodes,links=m.node_tree.nodes,m.node_tree.links;nodes.clear()
    color=(1,1,1);alpha=False
    if path is None:
        file=root/f'Assets/DroneSandbox/Resources/Materials/PBR/{name}.mat'
        if file.exists():
            source=file.read_text();match=re.search(r'_BaseColor: \{r: ([^,]+), g: ([^,]+), b: ([^,]+)',source)
            if match: color=tuple(float(match[i]) for i in range(1,4))
            match=re.search(r'_BaseColorMap:\s+m_Texture: \{[^}]*guid: ([0-9a-f]+)',source)
            if match:path=guid_paths.get(match[1])
        alpha=name.startswith(('Impostor','Leaves'))
    bsdf=nodes.new('ShaderNodeBsdfPrincipled');bsdf.inputs['Roughness'].default_value=1
    bsdf.inputs['Specular IOR Level'].default_value=0
    bsdf.inputs['Emission Strength'].default_value=.35
    colour=tuple(linear(x*tint) for x in color)+(1,)
    bsdf.inputs['Base Color'].default_value=colour;bsdf.inputs['Emission Color'].default_value=colour
    if path and Path(path).exists():
        tex=nodes.new('ShaderNodeTexImage');tex.image=image(path)
        mult=nodes.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=1;mult.inputs[2].default_value=colour
        links.new(tex.outputs['Color'],mult.inputs[1]);links.new(mult.outputs[0],bsdf.inputs['Base Color']);links.new(mult.outputs[0],bsdf.inputs['Emission Color'])
        if alpha:
            threshold=nodes.new('ShaderNodeMath');threshold.operation='GREATER_THAN';threshold.inputs[1].default_value=.45
            links.new(tex.outputs['Alpha'],threshold.inputs[0]);links.new(threshold.outputs[0],bsdf.inputs['Alpha'])
            m.surface_render_method='DITHERED'
    out=nodes.new('ShaderNodeOutputMaterial');links.new(bsdf.outputs[0],out.inputs['Surface'])
    materials[key]=m;return m

def template(name):
    if name in templates:return templates[name]
    model,lod=name.rsplit('_LOD',1)
    prior=set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=str(root/f'Assets/DroneSandbox/Resources/Models/{model}.fbx'))
    added=[o for o in bpy.context.scene.objects if o not in prior]
    objects=[o for o in added if o.type=='MESH' and o.name.split('.')[0].endswith('_LOD'+lod)]
    if not objects:objects=[o for o in added if o.type=='MESH']
    vertices=[];triangles=[];uv=[];slots=[];ids=[];base=0
    for obj in objects:
        mesh=obj.data;mesh.calc_loop_triangles()
        verts=np.array([obj.matrix_world@v.co for v in mesh.vertices],dtype=np.float32)
        vertices.extend(verts[:,[0,2,1]]*np.array([1,1,-1]))
        slot_offset=len(slots);slots.extend([m.name.split('.')[0] for m in mesh.materials])
        for tri in mesh.loop_triangles:
            triangles.append([base+v for v in tri.vertices]);ids.append(slot_offset+tri.material_index)
            for loop in tri.loops:uv.append(tuple(mesh.uv_layers.active.data[loop].uv) if mesh.uv_layers.active else (0,0))
        base+=len(verts)
    for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
    result=(np.array(vertices,dtype=np.float32),np.array(triangles,dtype=np.int32),np.array(uv,dtype=np.float32),slots,np.array(ids,dtype=np.int32))
    templates[name]=result;return result

def mesh_object(name,verts,tris,uv,slots,ids=None,normals=None):
    mesh=bpy.data.meshes.new(name)
    verts=np.ascontiguousarray(verts,dtype=np.float32);tris=np.ascontiguousarray(tris,dtype=np.int32)
    mesh.vertices.add(len(verts));mesh.vertices.foreach_set('co',verts.ravel())
    mesh.loops.add(tris.size);mesh.loops.foreach_set('vertex_index',tris.ravel())
    mesh.polygons.add(len(tris));mesh.polygons.foreach_set('loop_start',np.arange(len(tris),dtype=np.int32)*3);mesh.polygons.foreach_set('loop_total',np.full(len(tris),3,dtype=np.int32))
    layer=mesh.uv_layers.new();layer.data.foreach_set('uv',np.ascontiguousarray(uv,dtype=np.float32).ravel())
    for m in slots:mesh.materials.append(m)
    if ids is not None:mesh.polygons.foreach_set('material_index',ids)
    mesh.update()
    if normals is not None:
        mesh.polygons.foreach_set('use_smooth',np.ones(len(tris),dtype=bool))
        mesh.normals_split_custom_set_from_vertices(normals)
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj);return obj

def captured_mesh(data,texture):
    v=np.array(data['vertices'],dtype=np.float32)[:,[0,2,1]]*np.array([1,-1,1]);tri=np.array(data['indices'],dtype=np.int32).reshape(-1,3)
    uv=np.array(data['uv'],dtype=np.float32);uv[:,1]=1-uv[:,1]
    normals=None
    if 'normals' in data:normals=np.array(data['normals'],dtype=np.float32)[:,[0,2,1]]*np.array([1,-1,1])
    return mesh_object(data.get('name','Ground'),v,tri,uv[tri.ravel()],[material(texture,path=exports/texture)],normals=normals)

def render(name):
    for obj in list(bpy.context.scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users==0:bpy.data.meshes.remove(mesh)
    data=json.loads((exports/(name+'.json')).read_text())
    captured_mesh(data['ground'],data['worldTexture'])
    for mesh in data['meshes']:captured_mesh(mesh,mesh['texture'])
    groups=defaultdict(list)
    for instance in data['instances']:groups[(instance['model'],round(instance['tint'],3))].append(instance['matrix'])
    for (model,tint),matrices in groups.items():
        v,tri,uv,slots,ids=template(model)
        # Flatten repeated geometry into one mesh per model/tint; no hundred-thousand-object Blender scene.
        matrices=np.array(matrices,dtype=np.float32).reshape(-1,4,4);n=len(matrices)
        verts=(np.einsum('vi,kij->kvj',v,matrices[:,:3,:3])+matrices[:,None,3,:3]).reshape(-1,3)[:,[0,2,1]]*np.array([1,-1,1])
        indices=(tri[None,:,:]+np.arange(n,dtype=np.int32)[:,None,None]*len(v)).reshape(-1,3)
        normals=np.tile(np.array([0,0,1],dtype=np.float32),(len(verts),1)) if '_LOD2' in model and model.startswith(('Scenery_Tree','Scenery_Birch','Scenery_Spruce','Scenery_Pine')) else None
        mesh_object(model,verts,indices,np.tile(uv,(n,1)),[material(s,tint) for s in slots],np.tile(ids,n),normals)
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1600;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('World');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.38,.46,.55,1)
    sun=bpy.data.objects.new('Sun',bpy.data.lights.new('Sun','SUN'));scene.collection.objects.link(sun);sun.data.energy=2;sun.data.use_shadow=False;sun.rotation_euler=(math.radians(35),0,math.radians(-30))
    cam=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(cam)
    def xyz(p):return Vector((p[0],-p[2],p[1]))
    cam.location=xyz(data['camera']);target=xyz(data['target']);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='PERSP';cam.data.lens=43.455844;cam.data.sensor_width=36;cam.data.sensor_fit='VERTICAL';cam.data.clip_start=.5;cam.data.clip_end=50000;scene.camera=cam
    # A 45-degree vertical FOV at the same 16:9 aspect used by the capture frustum.
    cam.data.angle_y=math.radians(45)
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
    scene.render.filepath=str(output/(name+'.png'))
    bpy.ops.render.render(write_still=True)
    print('RENDERED',name,flush=True)
for name in requested:render(name)
