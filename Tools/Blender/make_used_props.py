"""Three small village and farm assets for the used-look pass: a muck heap, a transformer kiosk and a pump house.

Three LODs each, one mesh per LOD directly under the root (never deeper, so the FBX export keeps them upright).
Material names match the photo materials where one exists (Concrete, Galvanized, DoorWood, Brick, RoofTile),
so Art.TryModel gives them real textures; the heap's muck and straw are plain colours.

  blender --background --factory-startup --python Tools/Blender/make_used_props.py -- <out dir> [preview dir]
"""
from pathlib import Path
import json
import math
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import material, reset_scene, root
from kit import Kit, rotm
from world_asset_common import export_asset, mesh_from_kit, roof


def muck_heap(k, m, lod):
    """A 6 by 4 m manure heap on a concrete pad, held by a low three-sided wall, with straw on its flanks."""
    k.box(m['Concrete'], (7.2, 5.2, .15), (0, 0, .075))
    k.box(m['Concrete'], (7.2, .3, 1.2), (0, 2.45, .6))
    if lod < 2:
        for x in (-3.45, 3.45):
            k.box(m['Concrete'], (.3, 4.6, 1.0), (x, .2, .5))
    n = (20, 12, 6)[lod]
    rings = []
    for rx, ry, z, shift in ((3.2, 2.2, .15, 0), (2.7, 1.8, .85, .2), (1.6, 1.1, 1.45, .45), (.5, .35, 1.7, .6)):
        rings.append([(rx * (1 + .1 * math.sin(i * 2.7)) * math.cos(2 * math.pi * i / n),
                       shift + ry * (1 + .08 * math.cos(i * 1.7)) * math.sin(2 * math.pi * i / n),
                       z + (0 if z < .2 else .08 * math.sin(i * 3.1))) for i in range(n)])
    k.loft(m['Muck'], rings[:3] if lod == 2 else rings)
    if lod == 0:
        for i in range(7):
            a = i * 2.1
            k.box(m['Straw'], (.9, .6, .18), (math.cos(a) * 2.4, .2 + math.sin(a) * 1.5, .5 + .1 * (i % 3)), rot=rotm((0, 0, a)))


def transformer_kiosk(k, m, lod):
    """A 2.6 m wide prefabricated substation kiosk with two steel doors, louvres and an overhanging lid."""
    k.box(m['Concrete'], (3.0, 2.2, .2), (0, 0, .1))
    k.box(m['Concrete'], (2.6, 1.8, 2.3), (0, 0, 1.35))
    k.box(m['Concrete'], (2.9, 2.1, .14), (0, 0, 2.57))
    for x in (-.62, .62):
        k.box(m['Galvanized'], (1.1, .06, 1.9), (x, -.92, 1.2))
    if lod < 2:
        for y in (-.5, .5):
            k.box(m['SteelDark'], (.06, .7, .5), (1.32, y, 1.9))
            k.box(m['SteelDark'], (.06, .7, .5), (-1.32, y, 1.9))
    if lod == 0:
        for x in (-.62, .62):
            for z in (.6, 1.2, 1.8):
                k.box(m['SteelDark'], (.9, .03, .04), (x, -.96, z))
        k.box(m['Warning'], (.3, .02, .26), (.62, -.97, 1.65))


def pump_house(k, m, lod):
    """A 3.6 by 4.4 m brick pump house with a tiled gable, a plank door and the main entering through the floor."""
    k.box(m['Concrete'], (4.6, 5.4, .2), (0, 0, .1))
    k.box(m['Brick'], (3.6, 4.4, 2.6), (0, 0, 1.5))
    roof(k, m['RoofTile'], 0, 0, 3.6, 4.4, 2.8, 1.3, axis='y', over=.3)
    if lod < 2:
        k.loft(m['Brick'], [[(x, y, z) for x, z in ((-1.8, 2.8), (1.8, 2.8), (0, 4.1))] for y in (-2.2, 2.2)])
        k.box(m['DoorWood'], (1.0, .08, 2.0), (.6, -2.22, 1.2))
    if lod == 0:
        k.box(m['WindowGlass'], (.08, .7, .5), (1.82, .6, 1.9))
        k.bar(m['Galvanized'], (-1.4, -2.7, .08), (-1.4, -2.24, .55), .09, n=8)
        k.bar(m['Galvanized'], (-1.4, -2.7, .08), (-1.4, -3.3, .08), .09, n=8)


BUILDS = [('Scenery_MuckHeap', muck_heap), ('Scenery_TransformerKiosk', transformer_kiosk), ('Scenery_PumpHouse', pump_house)]


def main():
    """Export each asset with stable metadata, then optional eight-view previews of LOD0."""
    args = sys.argv[sys.argv.index('--') + 1:]
    out = Path(args[0]).resolve(); preview = Path(args[1]).resolve() if len(args) > 1 else None
    out.mkdir(parents=True, exist_ok=True)
    if preview: preview.mkdir(parents=True, exist_ok=True)
    report = []
    for name, build in BUILDS:
        reset_scene(); rig = root(name)
        mats = {'Concrete': material('Concrete', (.30, .29, .27), roughness=.95), 'Galvanized': material('Galvanized', (.40, .42, .43), metallic=.75, roughness=.42),
                'SteelDark': material('SteelDark', (.10, .095, .09), metallic=.6, roughness=.55), 'DoorWood': material('DoorWood', (.10, .05, .025), roughness=.8),
                'Brick': material('Brick', (.24, .07, .035), roughness=.9), 'RoofTile': material('RoofTile', (.32, .075, .025), roughness=.85),
                'WindowGlass': material('WindowGlass', (.02, .025, .03), metallic=.3, roughness=.1), 'Warning': material('YardWarning', (.75, .55, .05), roughness=.6),
                'Muck': material('Muck', (.075, .055, .035), roughness=1.0), 'Straw': material('Straw', (.42, .34, .16), roughness=1.0)}
        for lod in range(3):
            kit = Kit(); build(kit, mats, lod); mesh_from_kit(kit, f'{name}_LOD{lod}', rig)
        report.append(export_asset(rig, out, prop=True))
        if preview:
            from make_landmark_props import turntable
            turntable(name, preview, list(rig.children))
    if preview: (preview / 'used-props.json').write_text(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
