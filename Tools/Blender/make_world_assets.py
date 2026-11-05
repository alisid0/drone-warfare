"""Builds the recon and sector-identity asset pack without changing existing generators.

blender --background --factory-startup --python Tools/Blender/make_world_assets.py -- Models [previews] [names]
Names may be comma-separated model names or group names: airfield, industrial, rail, town, recon.
Each FBX contains direct-child _LOD0/1/2 meshes; metre UVs and the existing PBR material names.
"""
import importlib
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import reset_scene, root
from kit import Kit
from world_asset_common import PROJECT, export_asset, mesh_from_kit, palette, previews


def main():
    """Exports selected models, records measured statistics and renders optional review images."""
    args = sys.argv[sys.argv.index('--')+1:]
    out = Path(args[0]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    preview = str(Path(args[1]).resolve()) if len(args) > 1 and args[1] not in ('', '-') else None
    only = set(args[2].split(',')) if len(args) > 2 and args[2] else set()
    report_path = PROJECT / 'Tools/Blender/world_assets_manifest.json'
    reports = json.loads(report_path.read_text()) if report_path.exists() else {}
    for group in ('airfield', 'industrial', 'rail', 'town', 'recon'):
        module_path = Path(__file__).with_name('world_' + group + '.py')
        if not module_path.exists():
            continue
        module = importlib.import_module('world_' + group)
        for name, build in module.BUILDS:
            if only and group not in only and name not in only:
                continue
            reset_scene()
            m, r = palette(), root(name)
            for lod in range(3):
                k = Kit()
                build(k, m, lod)
                mesh_from_kit(k, name + f'_LOD{lod}', r)
                if group == 'recon':
                    module.propeller(m, lod, r)
            reports[name] = export_asset(r, out, prop=group == 'recon')
            report_path.write_bytes((json.dumps(reports, indent=2, sort_keys=True)+'\n').replace('\n','\r\n').encode())
            if preview:
                previews(r, preview, turntable=group == 'recon')


if __name__ == '__main__':
    main()
