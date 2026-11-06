"""Derive 3k-triangle close forest LODs from the existing FBXs, preserving UVs and material names."""
import bpy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MODELS = ROOT / 'Assets/DroneSandbox/Resources/Models'
report = {}
for species in ('Tree', 'Birch', 'Spruce', 'Pine'):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(MODELS / f'Scenery_{species}.fbx'))
    keep = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.endswith('_LOD1')]
    assert keep, species
    for obj in keep:
        matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = matrix
    for obj in list(bpy.context.scene.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)
    before = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in keep)
    for obj in keep:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        mod = obj.modifiers.new('Forest reduced', 'DECIMATE')
        mod.ratio = min(1.0, 3000 / before)
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.name = f'Forest_{species}_Reduced'
        for material in obj.data.materials:
            if material:
                material.name = material.name.split('.')[0]
    after = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in keep)
    bpy.ops.export_scene.fbx(filepath=str(MODELS / f'Forest_{species}_Reduced.fbx'),
        use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True, bake_anim=False, add_leaf_bones=False)
    report[species] = dict(source_triangles=before, reduced_triangles=after)
out = ROOT / 'Tools/DenseForest'
out.mkdir(parents=True, exist_ok=True)
(out / 'reduced-models.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report))
