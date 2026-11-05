"""Shared geometry and delivery helpers for the world-asset pack; existing model builders are unchanged."""
import math
import os
import re
import uuid
from pathlib import Path

import bpy
from mathutils import Vector

from common import box_uv_all, export_model, join_meshes, material, root
from kit import Kit, TO_BACK, TO_FWD, TO_X, aim, yprism

PROJECT = Path(__file__).resolve().parents[2]
R90 = math.pi / 2


# These names deliberately match the project's material replacement table.
def palette():
    """Creates the shared photo-material slots and the existing flat glass/marking slots."""
    colors = {
        'Concrete': (.30, .29, .27), 'ConcreteStained': (.24, .23, .21),
        'Brick': (.24, .07, .035), 'RoofMetal': (.22, .24, .25),
        'RoofTile': (.32, .075, .025), 'RoofSlate': (.09, .105, .12),
        'Galvanized': (.40, .42, .43), 'RustySteel': (.20, .11, .06),
        'SteelDark': (.10, .095, .09), 'Asphalt': (.05, .05, .05),
        'Gravel': (.20, .19, .18), 'PlasterWhite': (.62, .60, .55),
        'PlasterCream': (.58, .49, .34), 'PlasterGrey': (.36, .36, .34),
        'Wood': (.16, .10, .05), 'Olive': (.16, .18, .10), 'OliveDark': (.10, .11, .06),
        'WindowGlass': (.02, .035, .045), 'WindowFrame': (.65, .64, .60),
        'Interior': (.012, .014, .016), 'TealMark': (.012, .23, .29),
        'ShellGray': (.28, .32, .27), 'Prop': (.03, .03, .032),
        'Lens': (.01, .016, .023), 'SignalOrange': (.8, .18, .025),
    }
    metal = {'Galvanized', 'RustySteel', 'SteelDark', 'RoofMetal'}
    return {n: material(n, c, metallic=.55 if n in metal else .0,
                        roughness=.14 if n in ('WindowGlass', 'Lens') else .72) for n, c in colors.items()}


def roof(k, m, cx, cy, w, d, z, rise, axis='y', over=.35):
    """A solid gable with eaves; the ridge can follow either footprint axis."""
    profile = [(-w / 2 - over, z), (w / 2 + over, z), (0, z + rise)]
    loops = yprism(profile, -d / 2 - over, d / 2 + over)
    if axis == 'x':
        loops = [[Vector((p.y, p.x, p.z)) for p in loop] for loop in loops]
    k.loft(m, [[p + Vector((cx, cy, 0)) for p in loop] for loop in loops])


def window(k, m, p, w, h, lod, side='y'):
    """A recessed-looking glass panel with optional sill and mullions, cheap at distance."""
    x, y, z = p
    dims = (w, .08, h) if side == 'y' else (.08, w, h)
    k.box(m['WindowGlass'], dims, p)
    if lod == 0:
        for offset in (-w / 2, 0, w / 2):
            pos = (x + offset, y, z) if side == 'y' else (x, y + offset, z)
            k.box(m['WindowFrame'], (.065, .12, h + .14) if side == 'y' else (.12, .065, h + .14), pos)
        for dz in (-h / 2, h / 2):
            k.box(m['WindowFrame'], (w + .14, .15, .08) if side == 'y' else (.15, w + .14, .08), (x, y, z + dz))
        k.box(m['Concrete'], (w + .3, .3, .12) if side == 'y' else (.3, w + .3, .12), (x, y, z - h / 2 - .1))


def door(k, m, x, y, w, h, lod, z0=0, color='SteelDark'):
    """A loading or entrance door with a projecting frame and horizontal slats."""
    k.box(m[color], (w, .14, h), (x, y, z0 + h / 2))
    if lod < 2:
        for xx in (x - w / 2, x + w / 2):
            k.box(m['Concrete'], (.18, .3, h + .2), (xx, y, z0 + h / 2))
        k.box(m['Concrete'], (w + .3, .3, .22), (x, y, z0 + h))
    if lod == 0:
        for i in range(1, max(2, int(h / .32))):
            k.box(m['Galvanized'], (w - .14, .04, .025), (x, y - .09, z0 + i * .32))


def rail(k, m, a, b, lod, height=1.05):
    """A guardrail with posts at human-scale spacing; omit fine rails in the last LOD."""
    if lod == 2:
        return
    a, b = Vector(a), Vector(b)
    count = max(1, math.ceil((b - a).length / 2.2))
    for i in range(count + 1):
        p = a.lerp(b, i / count)
        k.bar(m, p, p + Vector((0, 0, height)), .035, n=4)
    for z in (.5, height) if lod == 0 else (height,):
        k.bar(m, a + Vector((0, 0, z)), b + Vector((0, 0, z)), .035, n=4)


def stairs(k, m, x, y, width, run, rise, lod, z0=0):
    """A staircase climbing along +Y; lower detail keeps a solid sloping flight."""
    if lod == 0:
        n = max(2, math.ceil(rise / .2))
        for i in range(n):
            h = rise * (i + 1) / n
            k.box(m['Concrete'], (width, run / n, h), (x, y + run * (i + .5) / n, z0 + h / 2))
    else:
        k.loft(m['Concrete'], [[(xx, y, z0), (xx, y + run, z0), (xx, y + run, z0 + rise)]
                               for xx in (x - width / 2, x + width / 2)])
    for xx in (x - width / 2, x + width / 2):
        rail(k, m['Galvanized'], (xx, y, z0), (xx, y + run, z0 + rise), lod)


def ring_rail(k, m, radius, z, lod, n=24):
    """A circular walkway handrail, retained only in near and middle views."""
    if lod == 2:
        return
    pts = [(radius * math.cos(2 * math.pi * i / n), radius * math.sin(2 * math.pi * i / n), z) for i in range(n + 1)]
    for a, b in zip(pts, pts[1:]):
        k.bar(m, (*a[:2], z + 1.05), (*b[:2], z + 1.05), .045, n=4)
        k.bar(m, a, (*a[:2], z + 1.05), .04, n=4)
        if lod == 0:
            k.bar(m, (*a[:2], z + .5), (*b[:2], z + .5), .035, n=4)


def mesh_from_kit(k, name, parent):
    """Combines the kit into one direct child with metre UVs and material submeshes."""
    temp = root('Assembly')
    k.build('Part', temp)
    join_meshes(temp)
    mesh = next(o for o in temp.children if o.type == 'MESH')
    mesh.parent = parent
    mesh.name = mesh.data.name = name
    bpy.data.objects.remove(temp, do_unlink=True)
    return mesh


def bounds(meshes):
    """World bounds from actual vertices, used for both reports and camera framing."""
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    return Vector(tuple(min(p[i] for p in pts) for i in range(3))), Vector(tuple(max(p[i] for p in pts) for i in range(3)))


def tris(o):
    """Counts the triangulated mesh exactly as the exporter will deliver it."""
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


def export_asset(r, out_dir, prop=False):
    """Checks LOD budgets, UVs and shallow hierarchy, then writes an FBX and stable Unity metadata."""
    meshes = [o for o in r.children_recursive if o.type == 'MESH']
    counts = [sum(tris(o) for o in meshes if o.name.endswith(f'_LOD{i}')) for i in range(3)]
    assert all(counts), (r.name, counts)
    assert counts[0] > counts[1] > counts[2], (r.name, counts)
    assert counts[2] < 300, (r.name, counts)
    assert counts[0] < (6000 if prop else 1500 if r.name in ('Scenery_PipeRack', 'Scenery_Windsock') else 8000), (r.name, counts)
    for o in meshes:
        assert o.parent is r, o.name
        assert len(o.data.uv_layers) == 1 and o.data.uv_layers[0].name == 'UVMap', o.name
        assert all(math.isfinite(c) for uv in o.data.uv_layers[0].data for c in uv.uv), o.name
        assert all(p.area > 1e-10 for p in o.data.polygons), o.name
    lo, hi = bounds([o for o in meshes if o.name.endswith('_LOD0')])
    path = Path(out_dir) / (r.name + '.fbx')
    export_model(r, str(path))
    meta = path.with_suffix('.fbx.meta')
    if not meta.exists():
        template = (PROJECT / 'Assets/DroneSandbox/Resources/Models/Scenery_Tree.fbx.meta').read_text()
        template = re.sub(r'guid: [0-9a-f]{32}', 'guid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'dw/world-assets/' + r.name).hex, template, count=1)
        meta.write_bytes(('\r\n'.join(line.rstrip() for line in template.splitlines())+'\r\n').encode())
    result = dict(name=r.name, dimensions=[round(v, 3) for v in hi-lo], bounds=[list(lo), list(hi)],
                  triangles=counts, materials=sorted({m.name for o in meshes for m in o.data.materials}),
                  path=path.relative_to(PROJECT).as_posix(), max_mesh_depth=1,
                  lod_bounds=[[list(a), list(b)] for a, b in [bounds([o for o in meshes if o.name.endswith(f'_LOD{i}')]) for i in range(3)]])
    print('ASSET', r.name, counts, result['dimensions'], flush=True)
    return result


def preview_materials():
    """Loads the existing Unity diffuse textures at their actual tiling scale, for previews only."""
    folder = PROJECT / 'Assets/DroneSandbox/Textures/PBR'
    guids = {}
    for p in folder.glob('*.meta'):
        match = re.search(r'^guid: (\w+)', p.read_text(), re.M)
        if match:
            guids[match[1]] = p.with_suffix('')
    for mat in bpy.data.materials:
        mat.use_backface_culling = True
        path = PROJECT / 'Assets/DroneSandbox/Resources/Materials/PBR' / (mat.name + '.mat')
        if not path.exists():
            continue
        data = path.read_text()
        match = re.search(r'- _BaseColorMap:\s+m_Texture: \{fileID: \d+, guid: (\w+).*?m_Scale: \{x: ([\d.e-]+), y: ([\d.e-]+)', data, re.S)
        if not match or match[1] not in guids:
            continue
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        uv = nodes.new('ShaderNodeTexCoord')
        scale = nodes.new('ShaderNodeVectorMath')
        scale.operation = 'MULTIPLY'
        scale.inputs[1].default_value = (float(match[2]), float(match[3]), 1)
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images.load(str(guids[match[1]]), check_existing=True)
        links.new(uv.outputs['UV'], scale.inputs[0])
        links.new(scale.outputs[0], tex.inputs['Vector'])
        bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
        tint = re.search(r'- _BaseColor: \{r: ([\d.e-]+), g: ([\d.e-]+), b: ([\d.e-]+)', data)
        mult = nodes.new('ShaderNodeMixRGB')
        mult.blend_type = 'MULTIPLY'
        mult.inputs[0].default_value = 1
        mult.inputs[2].default_value = tuple(float(tint[i]) for i in (1, 2, 3)) + (1,) if tint else (1, 1, 1, 1)
        links.new(tex.outputs['Color'], mult.inputs[1])
        links.new(mult.outputs[0], bsdf.inputs['Base Color'])


def figure(loc):
    """A simple standing 1.8 m human scale reference, never exported."""
    k = Kit()
    m = material('PreviewFigure', (.9, .25, .025))
    for x in (-.12, .12):
        k.box(m, (.18, .28, .12), (x, -.035, .06))
        k.bar(m, (x, 0, .1), (x, 0, .87), .085, n=6)
    k.box(m, (.42, .25, .55), (0, 0, 1.13))
    for s in (-1, 1):
        k.bar(m, (s*.25, 0, 1.35), (s*.33, -.03, .85), .065, n=6)
    k.lathe(m, [(1.42, .09), (1.49, .13), (1.70, .12), (1.8, 0)], (0, 0, 0), n=10)
    r = root('PreviewScaleFigure_1.8m')
    mesh_from_kit(k, 'ScaleFigure', r)
    r.location = loc
    lo, hi = bounds(list(r.children))
    assert abs(hi.z-lo.z-1.8) < .0001, 'Scale figure must be exactly 1.8 m tall'
    return r


def camera_at(cam, point, target):
    """Aims the preview camera without changing the asset's coordinate system."""
    cam.location = point
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()


def previews(r, directory, turntable=False):
    """Renders a 45-degree view, exact 1.5 px/m RTS plan, LOD2 check and optional orbit frames."""
    folder = Path(directory)
    folder.mkdir(parents=True, exist_ok=True)
    meshes = [o for o in r.children_recursive if o.type == 'MESH']
    for o in meshes:
        o.hide_render = not o.name.endswith('_LOD0')
    lo, hi = bounds([o for o in meshes if not o.hide_render])
    size, centre = hi-lo, (lo+hi)/2
    scale_figure = figure((hi.x + max(.8, size.x * .035), lo.y, min(-.02, lo.z-.05)))
    preview_materials()
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 720, 540
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world = bpy.data.worlds.new('PreviewWorld')
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get('Background')
    bg.inputs['Color'].default_value = (.6, .67, .75, 1)
    bg.inputs['Strength'].default_value = .65
    bpy.ops.mesh.primitive_plane_add(size=1500, location=(0, 0, min(-.02, lo.z-.05)))
    bpy.context.object.data.materials.append(material('PreviewGround', (.16, .19, .16)))
    bpy.ops.object.light_add(type='SUN', rotation=(.48, -.5, -.65))
    bpy.context.object.data.energy = 2.4
    bpy.context.object.data.angle = .12
    bpy.ops.object.camera_add()
    cam = bpy.context.object
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.clip_end = 3000
    width = max((size.x + size.y) * .78, size.z * 1.45, 5) * 1.3
    cam.data.ortho_scale = width
    distance = width * 2
    camera_at(cam, centre + Vector((distance/2, -distance/2, distance/math.sqrt(2))), centre)
    # Include the whole figure, even beside small cottages or tall tanks; never crop it at the frame edge.
    bpy.context.view_layer.update()
    visible = [o for o in meshes if not o.hide_render] + list(scale_figure.children)
    points = [o.matrix_world @ v.co for o in visible for v in o.data.vertices]
    view = cam.rotation_euler.to_matrix().transposed()
    projected = [view @ (p-centre) for p in points]
    left, right = min(p.x for p in projected), max(p.x for p in projected)
    bottom, top = min(p.y for p in projected), max(p.y for p in projected)
    shift = cam.rotation_euler.to_matrix() @ Vector(((left+right)/2, (bottom+top)/2, 0))
    cam.location += shift
    width = max(right-left, (top-bottom)*720/540)*1.12
    cam.data.ortho_scale = width
    scene.render.filepath = str(folder / (r.name + '.png'))
    bpy.ops.render.render(write_still=True)
    for o in meshes:
        o.hide_render = not o.name.endswith('_LOD2')
    scene.render.filepath = str(folder / (r.name + '_LOD2.png'))
    bpy.ops.render.render(write_still=True)
    for o in meshes:
        o.hide_render = not o.name.endswith('_LOD0')
    camera_at(cam, (centre.x, centre.y, 600), (centre.x, centre.y, 0))
    cam.data.ortho_scale = 480  # 720 / 480 = 1.5 px/m: a 40 m building is exactly 60 px.
    scene.render.filepath = str(folder / (r.name + '_RTS.png'))
    bpy.ops.render.render(write_still=True)
    if turntable:
        # One fixed framing for the full orbit prevents zooming or clipping as the long wing turns.
        scene_lo, scene_hi = bounds(visible)
        orbit_centre = (scene_lo+scene_hi)/2
        width = max(width, max((p-orbit_centre).length for p in points)*2*720/540*1.12)
        cam.data.ortho_scale = width
        orbit = folder / 'Drone_Recon_turntable'
        orbit.mkdir(exist_ok=True)
        for i in range(24):
            a = 2 * math.pi * i / 24
            camera_at(cam, orbit_centre + Vector((math.sin(a)*distance, -math.cos(a)*distance, distance*.6)), orbit_centre)
            scene.render.filepath = str(orbit / f'{i:03}.png')
            bpy.ops.render.render(write_still=True)
    print('PREVIEWS', r.name, flush=True)
