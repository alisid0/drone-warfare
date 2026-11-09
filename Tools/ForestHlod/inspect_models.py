"""Read FBX LOD sizes/material slots without importing the Unity project."""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[2]
out=root/'Tools/ForestHlod/Review'; out.mkdir(parents=True,exist_ok=True)
names=['Scenery_Tree','Scenery_Birch','Scenery_Spruce','Scenery_Pine','Scenery_House_A','Scenery_House_B','Scenery_House_C','Scenery_House_D','Scenery_Townhouse_Row','Scenery_Apartment','Scenery_Shop']
result={}
for name in names:
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(root/f'Assets/DroneSandbox/Resources/Models/{name}.fbx'))
    levels=[]
    for lod in range(3):
        objects=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.endswith('_LOD'+str(lod))]
        if not objects and lod==0 and not any(o.name.endswith('_LOD0') for o in bpy.context.scene.objects):
            objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
        if not objects: continue
        points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
        lo=[min(p[k] for p in points) for k in range(3)]; hi=[max(p[k] for p in points) for k in range(3)]
        materials=[]
        for o in objects:
            for m in o.data.materials:
                name_=m.name.split('.')[0]
                if name_ not in materials: materials.append(name_)
        levels.append(dict(lod=lod,lo=[lo[0],lo[2],lo[1]],hi=[hi[0],hi[2],hi[1]],materials=materials,triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)))
    result[name]=levels
(out/'models.json').write_text(json.dumps(result,indent=2))
print('MODEL AUDIT',result)
