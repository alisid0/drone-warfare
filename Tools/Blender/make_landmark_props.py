"""Three small civilian yard assets, shared colours, three LODs and a shallow FBX hierarchy."""
from pathlib import Path
import json
import math
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from mathutils import Vector
from common import material, reset_scene, root
from kit import Kit, TO_X
from world_asset_common import export_asset, mesh_from_kit


def freight(k, m, lod):
    """A used intermodal container and a pallet stack fit inside a twelve metre storage bay."""
    k.box(m[0], (2.44, 6.06, 2.59), (-1.5, 0, 1.295))
    k.box(m[1], (2.1, 2.6, 1.15), (2.1, 1.1, .675))
    if lod < 2:
        for z in (.08, 2.51):
            k.box(m[2], (2.48, 6.1, .12), (-1.5, 0, z))
        for x in (-2.69, -.31):
            k.box(m[2], (.12, 6.1, 2.6), (x, 0, 1.3)) if lod == 0 else None
        for x in (-1.96, -1.04):
            k.box(m[2], (.045, .08, 2.3), (x, -3.08, 1.3))
        for z in (.13, .55, .98):
            k.box(m[2], (2.2, 2.7, .08), (2.1, 1.1, z))
    if lod == 0:
        for y in range(-14, 15):
            for x in (-2.73, -.27):
                k.box(m[1], (.045, .05, 2.38), (x, y * .2, 1.3))


def vehicles(k, m, lod):
    """An unmarked delivery van and flatbed are civilian scenery, without weapons or unit components."""
    for x, length in ((-2.5, 5.1), (2.6, 7.3)):
        k.box(m[2], (2, length, .3), (x, 0, .65))
        k.box(m[1], (2.05, 1.8, 1.7), (x, -length / 2 + 1.05, 1.65))
        k.box(m[2], (1.8, .06, .72), (x, -length / 2 + .12, 2.0))
        if x < 0:
            k.box(m[1], (2.13, 3.1, 2.05), (x, .85, 1.72))
        else:
            k.box(m[0], (2.25, 4.8, .24), (x, 1.0, 1.0))
            if lod < 2:
                k.box(m[0], (.12, 4.8, .5), (x - 1.1, 1, 1.3))
                k.box(m[0], (.12, 4.8, .5), (x + 1.1, 1, 1.3))
        for side in (-1, 1):
            for y in (-length * .31, length * .31):
                k.cyl(m[2], .43, .23, (x + side * 1.02, y, .43), rot=TO_X, n=(12, 8, 4)[lod])
                if lod == 0:
                    k.cyl(m[1], .21, .25, (x + side * 1.035, y, .43), rot=TO_X, n=8)
        if lod == 0:
            for side in (-1, 1):
                k.box(m[2], (.05, 1.25, .66), (x + side * 1.04, -length / 2 + 1.05, 2.0))
                k.box(m[0], (.25, .1, .15), (x + side * .8, -length / 2 + .06, 1.2))


def aggregate(k, m, lod):
    """An asymmetric 10 m radius, 3.5 m high stockpile with a low scraped toe and offset crest."""
    n = (24, 14, 8)[lod]
    rings = []
    for radius, z, shift in ((10, .02, 0), (7.4, 1.6, 1.2), (2.8, 3.5, 2.2)):
        rings.append([(shift + radius * (1 + .09 * math.sin(i * 2.3)) * math.cos(2 * math.pi * i / n),
                       radius * .85 * (1 + .08 * math.cos(i * 1.9)) * math.sin(2 * math.pi * i / n),
                       z + (0 if z < .1 else .14 * math.sin(i * 2.1))) for i in range(n)])
    k.loft(m[0], rings)
    if lod == 0:
        for i in range(8):
            a = i * 2.4
            k.box(m[1], (.5, .7, .25), (math.cos(a) * 8.5, math.sin(a) * 7, .16))


def turntable(name, folder, meshes):
    """Eight fixed sunlit views expose every side of each delivered model, using only its first LOD."""
    for mesh in meshes:
        mesh.hide_render = not mesh.name.endswith('_LOD0')
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.background_type = 'WORLD'
    scene.world = bpy.data.worlds.new('Preview world'); scene.world.color = (.16, .18, .20)
    from world_asset_common import bounds
    lo, hi = bounds([o for o in meshes if o.name.endswith('_LOD0')]); centre = (lo + hi) * .5
    radius = max(hi - lo) * 1.8
    bpy.ops.object.camera_add()
    camera = bpy.context.object; scene.camera = camera; camera.data.type = 'ORTHO'; camera.data.ortho_scale = radius
    for i in range(8):
        angle = i * math.pi / 4
        camera.location = centre + Vector((radius * math.cos(angle), radius * math.sin(angle), radius * .65))
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(folder / f'{name}-{i}.png'); bpy.ops.render.render(write_still=True)


def main():
    """Export stable metadata with CPU readability only for these tiny assets used by the yard batcher."""
    args = sys.argv[sys.argv.index('--') + 1:]
    out = Path(args[0]).resolve(); preview = Path(args[1]).resolve() if len(args) > 1 else None
    out.mkdir(parents=True, exist_ok=True)
    if preview: preview.mkdir(parents=True, exist_ok=True)
    report = []
    for name, build in (('Scenery_FreightGroup', freight), ('Scenery_UtilityVehicles', vehicles), ('Scenery_AggregatePile', aggregate)):
        reset_scene(); rig = root(name)
        mats = [material('YardRust', (.26, .19, .115)), material('YardGrey', (.38, .39, .36)), material('YardDark', (.055, .06, .06))]
        for lod in range(3):
            kit = Kit(); build(kit, mats, lod); mesh_from_kit(kit, f'{name}_LOD{lod}', rig)
        info = export_asset(rig, out, prop=True); report.append(info)
        meta = out / (name + '.fbx.meta')
        meta.write_bytes(meta.read_text().replace('isReadable: 0', 'isReadable: 1').replace('\n', '\r\n').encode())
        if preview: turntable(name, preview, list(rig.children))
    if preview: (preview / 'models.json').write_text(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
