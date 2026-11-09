"""Small opaque garden and farm props with three shallow LOD meshes, using the shared Blender helpers."""
from pathlib import Path
import json
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import reset_scene, root, material
from kit import Kit
from world_asset_common import mesh_from_kit, export_asset, roof


def shed(k, mats, lod):
    """An open-front machinery bay fits a small edge farm's yard without becoming a military building."""
    wood, metal, glass = mats
    roof(k, metal, 0, 0, 8, 6, 3.5, .8)
    for x in (-3.8, 3.8):
        for y in (-2.8, 2.8):
            k.box(wood, (.22, .22, 3.5), (x, y, 1.75))
    k.box(wood, (8, .16, 3.3), (0, 2.8, 1.65))
    if lod < 2:
        for x in (-3.9, 3.9):
            k.box(wood, (.16, 5.8, 2.6), (x, 0, 1.3))
        k.box(wood, (8, .22, .25), (0, -2.8, 3.25))
    if lod == 0:
        for x in range(-3, 4):
            k.box(wood, (.06, .04, 3.2), (x, 2.69, 1.6))
        for y in range(-2, 3):
            for x in (-4.0, 4.0):
                k.box(metal, (.05, .05, 2.5), (x, y, 1.3))


def greenhouse(k, mats, lod):
    """Opaque dusty glazing avoids transparency sorting and retains the greenhouse silhouette at distance."""
    wood, metal, glass = mats
    k.box(wood, (3.6, 5.4, .22), (0, 0, .11))
    k.box(glass, (3.4, 5.2, 1.7), (0, 0, 1.05))
    roof(k, glass, 0, 0, 3.4, 5.2, 1.9, .9, over=.05)
    if lod < 2:
        for x in (-1.75, 1.75):
            for y in (-2.65, 0, 2.65):
                k.box(metal, (.065, .065, 1.9), (x, y, 1.0))
        k.box(metal, (.09, 5.4, .09), (0, 0, 2.83))
    if lod == 0:
        for z in (.3, 1.1, 1.9):
            for x in (-1.76, 1.76):
                k.box(metal, (.065, 5.4, .065), (x, 0, z))
        for x in (-.5, .5):
            k.box(metal, (.06, .09, 1.8), (x, -2.67, 1.0))


def main():
    """Validate exact geometry and export only the two missing models, keeping all meshes direct root children."""
    args = sys.argv[sys.argv.index('--') + 1:]
    out = Path(args[0]).resolve(); report = []
    for name, build in (('Scenery_MachineryShed', shed), ('Scenery_Greenhouse', greenhouse)):
        reset_scene(); rig = root(name)
        mats = [material('Wood', (.22, .15, .08)), material('RoofMetal', (.25, .28, .28)), material('GardenGlass', (.22, .33, .32))]
        for lod in range(3):
            kit = Kit(); build(kit, mats, lod); mesh_from_kit(kit, f'{name}_LOD{lod}', rig)
        report.append(export_asset(rig, out, prop=True))
    Path(args[1]).write_text(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
