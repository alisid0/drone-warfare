"""One compact nine-metre timber telegraph pole, with direct-child LOD meshes and no transparent materials."""
from pathlib import Path
import json
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import reset_scene, root, material
from kit import Kit
from world_asset_common import mesh_from_kit, export_asset


def main():
    """Export three small tiers; the insulators and crossarm preserve the silhouette at strategy height."""
    args = sys.argv[sys.argv.index('--') + 1:]
    reset_scene()
    rig = root('Scenery_TelegraphPole')
    wood = material('Wood', (.23, .16, .09))
    for lod in range(3):
        k = Kit()
        k.box(wood, (.24, .24, 9), (0, 0, 4.5))
        k.box(wood, (2.2, .15, .18), (0, 0, 8.3))
        if lod < 2:
            for x in (-.85, -.35, .35, .85):
                k.box(wood, (.14, .14, .24), (x, 0, 8.5))
        if lod == 0:
            k.box(wood, (.4, .32, .5), (0, 0, 1.4))
        mesh_from_kit(k, f'Scenery_TelegraphPole_LOD{lod}', rig)
    report = export_asset(rig, Path(args[0]).resolve(), prop=True)
    Path(args[1]).write_text(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
